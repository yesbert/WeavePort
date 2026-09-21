"""Protocol 2 demultiplexing. Only the reader owns inbound channel access."""
import asyncio
import contextvars
import inspect
from concurrent.futures import ThreadPoolExecutor
from collections import deque


async def run_concurrent(runtime, context_type, scope_variable):
    config = await runtime.read()
    degree = config.get("degree") if isinstance(config, dict) else None
    if config is None or config.get("type") != "configure" or type(degree) is not int or not 1 <= degree <= 1024:
        raise ValueError("Expected bounded concurrency configuration")
    calls, callbacks = {}, {}
    completed, completed_order = set(), deque()
    cancelled_callbacks, cancelled_callback_order = set(), deque()
    loop = asyncio.get_running_loop()
    executor = ThreadPoolExecutor(max_workers=degree, thread_name_prefix="weaveport")

    async def callback(operation, value):
        scope = scope_variable.get()
        if not scope or not scope["active"] or scope["cancelled"]:
            raise RuntimeError("Expired invocation")
        runtime.callback_id += 1
        cid, rid = str(runtime.callback_id), scope["request"]["id"]
        future = loop.create_future()
        callbacks[(rid, cid)] = future
        scope["callbacks"].append(future)
        await runtime.send(dict(type="callback", id=rid, callbackId=cid, operation=operation, payload=value))
        return await future

    runtime.callback = callback
    runtime.loop = loop

    async def execute(request, scope):
        token = scope_variable.set(scope)
        context = context_type(request["context"]["tenant"], request["context"]["configuration"], runtime)
        if scope["cancelled"]:
            context._cancelled.set()
        scope["context"] = context
        response = None
        try:
            if request.get("operation") != "$sdk.call":
                raise ValueError("Concurrent workers support unary functions only")
            payload = request["payload"]
            handler = runtime.app._functions[payload["operation"]]
            if inspect.iscoroutinefunction(handler):
                value = await handler(payload["input"], context)
            else:
                copied = contextvars.copy_context()
                value = await loop.run_in_executor(executor, copied.run, handler, payload["input"], context)
                if inspect.isawaitable(value):
                    value = await value
            response = dict(type="result", id=request["id"], value=value, reusable=True)
        except asyncio.CancelledError:
            scope["cancelled"] = True
        except Exception:
            response = dict(type="error", id=request["id"], code="sdk-error")
        finally:
            scope["active"] = False
            try:
                await context._close()
            except BaseException:
                response = dict(type="error", id=request["id"], code="cleanup-error")
            if scope["cancelled"] and (response is None or response.get("code") != "cleanup-error"):
                response = dict(type="cancelled", id=request["id"])
            callback_results = await asyncio.gather(*scope["callbacks"], return_exceptions=True)
            if not scope["cancelled"] and any(isinstance(result, BaseException) for result in callback_results):
                if response.get("code") != "cleanup-error":
                    response = dict(type="error", id=request["id"], code="sdk-error")
            # Host callbacks may outlive cancellation and may never reply. Release their
            # futures at terminal completion; retain only bounded identities for late replies.
            for key in [key for key in callbacks if key[0] == request["id"]]:
                callbacks.pop(key)
                cancelled_callbacks.add(key)
                cancelled_callback_order.append(key)
                if len(cancelled_callback_order) > 4096:
                    cancelled_callbacks.discard(cancelled_callback_order.popleft())
            try:
                await runtime.send(response)
            except (ValueError, TypeError, OverflowError):
                await runtime.send(dict(type="error", id=request["id"], code="sdk-error"))
            calls.pop(request["id"], None)
            completed.add(request["id"])
            completed_order.append(request["id"])
            if len(completed_order) > 4096:
                completed.remove(completed_order.popleft())
            scope_variable.reset(token)

    try:
        while (frame := await runtime.read()) is not None:
            kind, rid = frame.get("type"), frame.get("id")
            if kind == "invoke":
                if not isinstance(rid, str) or rid in calls or rid in completed or len(calls) >= degree:
                    raise ValueError("Invalid invocation admission")
                if not isinstance(frame.get("operation"), str) or not isinstance(frame.get("payload"), dict):
                    raise ValueError("Malformed invocation")
                context = frame.get("context")
                if not isinstance(context, dict) or not isinstance(context.get("tenant"), str) or "configuration" not in context:
                    raise ValueError("Malformed context")
                scope = dict(request=frame, active=True, cancelled=False, callbacks=[])
                calls[rid] = scope
                scope["task"] = asyncio.create_task(execute(frame, scope))
            elif kind == "callback-result":
                key = (rid, frame.get("callbackId"))
                future = callbacks.pop(key, None)
                if future is None:
                    if key in cancelled_callbacks:
                        cancelled_callbacks.remove(key)
                        continue
                    raise ValueError("Unknown callback identity")
                if not future.done():
                    if frame.get("error") is not None or frame.get("success") is False:
                        future.set_exception(RuntimeError("Host callback failed"))
                    else:
                        future.set_result(frame.get("value"))
            elif kind == "cancel":
                scope = calls.get(rid)
                if scope is None:
                    if rid in completed:
                        continue
                    raise ValueError("Unknown cancellation identity")
                scope["cancelled"] = True
                if context := scope.get("context"):
                    context._cancelled.set()
                for (owner, _), future in callbacks.items():
                    if owner == rid and not future.done():
                        future.set_exception(RuntimeError("Invocation cancelled"))
                # Do not cancel the task: a thread or author cleanup may still own resources.
            else:
                raise ValueError("Unknown protocol frame")
    finally:
        for scope in calls.values():
            scope["cancelled"] = True
            if context := scope.get("context"):
                context._cancelled.set()
        for future in callbacks.values():
            if not future.done():
                future.set_exception(RuntimeError("Channel closed"))
        await asyncio.gather(*(scope["task"] for scope in list(calls.values())), return_exceptions=True)
        executor.shutdown(wait=True)
