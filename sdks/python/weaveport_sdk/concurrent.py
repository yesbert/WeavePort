"""Protocol 2 demultiplexing. Only the reader owns inbound channel access."""

MAXIMUM_RETIRED_IDENTITIES = 4096

from .protocol import FailureCodes, FrameKinds, ProtocolLimits, SdkOperations

import asyncio
import contextvars
import inspect
from concurrent.futures import ThreadPoolExecutor
from collections import deque


async def run_concurrent(runtime, context_type, scope_variable):
    config = await runtime.read()
    degree = config.get("degree") if isinstance(config, dict) else None
    if (
        config is None
        or config.get("type") != "configure"
        or type(degree) is not int
        or not 1 <= degree <= ProtocolLimits.MaximumConcurrentCalls
    ):
        raise ValueError("Expected bounded concurrency configuration")
    await ConcurrentRuntime(runtime, context_type, scope_variable, degree).run()


class ConcurrentRuntime:
    """Own routing state until every admitted invocation finishes cleanup."""

    def __init__(self, runtime, context_type, scope_variable, degree):
        self.runtime = runtime
        self.context_type = context_type
        self.scope_variable = scope_variable
        self.degree = degree
        self.calls = {}
        self.callbacks = {}
        self.completed = set()
        self.completed_order = deque()
        self.cancelled_callbacks = set()
        self.cancelled_callback_order = deque()
        self.loop = asyncio.get_running_loop()
        self.executor = ThreadPoolExecutor(
            max_workers=degree, thread_name_prefix="weaveport"
        )
        runtime.callback = self.callback
        runtime.loop = self.loop

    async def callback(self, operation, value):
        scope = self.scope_variable.get()
        if not scope or not scope["active"] or scope["cancelled"]:
            raise RuntimeError("Expired invocation")
        self.runtime.callback_id += 1
        callback_id, request_id = str(self.runtime.callback_id), scope["request"]["id"]
        future = self.loop.create_future()
        self.callbacks[(request_id, callback_id)] = future
        scope["callbacks"].append(future)
        await self.runtime.send(
            dict(
                type=FrameKinds.Callback,
                id=request_id,
                callbackId=callback_id,
                operation=operation,
                payload=value,
            )
        )
        return await future

    async def execute(self, request, scope):
        token = self.scope_variable.set(scope)
        context = self.context_type(
            request["context"]["tenant"],
            request["context"]["configuration"],
            self.runtime,
        )
        if scope["cancelled"]:
            context._cancelled.set()
        scope["context"] = context
        response = None
        try:
            if request.get("operation") != SdkOperations.Call:
                raise ValueError("Concurrent workers support unary functions only")
            payload = request["payload"]
            handler = self.runtime.app._functions[payload["operation"]]
            value = await self.invoke_handler(handler, payload["input"], context)
            response = dict(
                type=FrameKinds.Result, id=request["id"], value=value, reusable=True
            )
        except asyncio.CancelledError:
            scope["cancelled"] = True
        except Exception:
            response = dict(
                type=FrameKinds.Error, id=request["id"], code=FailureCodes.SdkError
            )
        finally:
            scope["active"] = False
            try:
                await context._close()
            except BaseException:
                response = dict(
                    type=FrameKinds.Error,
                    id=request["id"],
                    code=FailureCodes.CleanupError,
                    primaryCode=(response or {}).get("code", FailureCodes.CleanupError),
                    cleanupFailed=True,
                )
            if scope["cancelled"] and (
                response is None or response.get("code") != FailureCodes.CleanupError
            ):
                response = dict(type=FailureCodes.Cancelled, id=request["id"])
            callback_results = await asyncio.gather(
                *scope["callbacks"], return_exceptions=True
            )
            if (
                not scope["cancelled"]
                and any(
                    isinstance(result, BaseException) for result in callback_results
                )
                and response.get("code") != FailureCodes.CleanupError
            ):
                response = dict(
                    type=FrameKinds.Error, id=request["id"], code=FailureCodes.SdkError
                )
            # Host callbacks may outlive cancellation and may never reply. Release their
            # futures at terminal completion; retain only bounded identities for late replies.
            for key in [key for key in self.callbacks if key[0] == request["id"]]:
                self.callbacks.pop(key)
                self.cancelled_callbacks.add(key)
                self.cancelled_callback_order.append(key)
                if len(self.cancelled_callback_order) <= MAXIMUM_RETIRED_IDENTITIES:
                    continue
                self.cancelled_callbacks.discard(
                    self.cancelled_callback_order.popleft()
                )
            try:
                await self.runtime.send(response)
            except (ValueError, TypeError, OverflowError):
                await self.runtime.send(
                    dict(
                        type=FrameKinds.Error,
                        id=request["id"],
                        code=FailureCodes.SdkError,
                    )
                )
            self.calls.pop(request["id"], None)
            self.completed.add(request["id"])
            self.completed_order.append(request["id"])
            if len(self.completed_order) > MAXIMUM_RETIRED_IDENTITIES:
                self.completed.remove(self.completed_order.popleft())
            self.scope_variable.reset(token)

    async def run(self):
        try:
            while (frame := await self.runtime.read()) is not None:
                self.route(frame)
        finally:
            for scope in self.calls.values():
                scope["cancelled"] = True
                if not (context := scope.get("context")):
                    continue
                context._cancelled.set()
            for future in self.callbacks.values():
                if future.done():
                    continue
                future.set_exception(RuntimeError("Channel closed"))
            await asyncio.gather(
                *(scope["task"] for scope in list(self.calls.values())),
                return_exceptions=True,
            )
            self.executor.shutdown(wait=True)

    def admit(self, frame, request_id):
        if (
            not isinstance(request_id, str)
            or request_id in self.calls
            or request_id in self.completed
            or len(self.calls) >= self.degree
        ):
            raise ValueError("Invalid invocation admission")
        if not isinstance(frame.get("operation"), str) or not isinstance(
            frame.get("payload"), dict
        ):
            raise ValueError("Malformed invocation")
        context = frame.get("context")
        if (
            not isinstance(context, dict)
            or not isinstance(context.get("tenant"), str)
            or "configuration" not in context
        ):
            raise ValueError("Malformed context")
        scope = dict(request=frame, active=True, cancelled=False, callbacks=[])
        self.calls[request_id] = scope
        scope["task"] = asyncio.create_task(self.execute(frame, scope))

    def complete_callback(self, frame, request_id):
        key = (request_id, frame.get("callbackId"))
        future = self.callbacks.pop(key, None)
        if future is None and key in self.cancelled_callbacks:
            self.cancelled_callbacks.remove(key)
            return
        if future is None:
            raise ValueError("Unknown callback identity")
        if future.done():
            return
        if frame.get("error") is not None or frame.get("success") is False:
            future.set_exception(RuntimeError("Host callback failed"))
            return
        future.set_result(frame.get("value"))

    def cancel(self, frame, request_id):
        scope = self.calls.get(request_id)
        if scope is None and request_id in self.completed:
            return
        if scope is None:
            raise ValueError("Unknown cancellation identity")
        scope["cancelled"] = True
        if context := scope.get("context"):
            context._cancelled.set()
        for (owner, _), future in self.callbacks.items():
            if owner != request_id or future.done():
                continue
            future.set_exception(RuntimeError("Invocation cancelled"))

    def route(self, frame):
        kind, request_id = frame.get("type"), frame.get("id")
        if kind == "invoke":
            self.admit(frame, request_id)
        elif kind == "callback-result":
            self.complete_callback(frame, request_id)
        elif kind == "cancel":
            self.cancel(frame, request_id)
        else:
            raise ValueError("Unknown protocol frame")

    async def invoke_handler(self, handler, value, context):
        if inspect.iscoroutinefunction(handler):
            return await handler(value, context)
        copied = contextvars.copy_context()
        result = await self.loop.run_in_executor(
            self.executor, copied.run, handler, value, context
        )
        if inspect.isawaitable(result):
            return await result
        return result
