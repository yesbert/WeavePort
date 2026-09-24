"""Exclusive protocol runtime; one invocation scope owns callback authority."""

import asyncio
import contextvars
import json
import os
import socket
import sys
import uuid

from .protocol import (
    ProtocolVersions,
    FailureCodes,
    FrameKinds,
    ProtocolLimits,
    SdkOperations,
)

from .cleanup import cleanup_all, execute_with_cleanup
from .context import PluginContext, SessionCleanupError
from .source import SourceSession
from .stream import LiveStream

_CHANNEL_BUFFER_BYTES = 65536

_SCOPE = contextvars.ContextVar("weaveport_invocation", default=None)


def _encode(value):
    return json.dumps(
        value, ensure_ascii=True, allow_nan=False, separators=(",", ":")
    ).encode("utf-8")


class Runtime:
    def __init__(self, app):
        self.app = app
        self.source = SourceSession()
        self.context = None
        self.iterator = None
        self.stream_id = None
        self.callback_id = 0
        self.callback_lock = asyncio.Lock()
        self.exchange_ready = asyncio.Event()
        self.stream_scope = None
        self.live_stream = None
        self.channel = None
        if endpoint := os.getenv("WEAVEPORT_SOCKET"):
            self.channel = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            self.channel.connect(endpoint)
            self.reader = self.channel.makefile("rb")
            self.writer = self.channel.makefile("wb", buffering=_CHANNEL_BUFFER_BYTES)
        else:
            self.reader = sys.stdin.buffer
            self.writer = sys.stdout.buffer
        sys.stdout = sys.stderr

    async def read(self):
        line = await asyncio.to_thread(
            self.reader.readline, ProtocolLimits.FrameBytes + 2
        )
        if not line:
            return None
        if len(line) > ProtocolLimits.FrameBytes + 1 or not line.endswith(b"\n"):
            raise ValueError("Frame limit")
        return json.loads(line)

    async def send(self, value):
        data = _encode(value)
        if len(data) > ProtocolLimits.FrameBytes:
            raise ValueError("Frame limit")
        self.writer.write(data + b"\n")
        self.writer.flush()

    async def callback(self, operation, value):
        scope = _SCOPE.get()
        if scope is self.stream_scope:
            await self.wait_for_stream_exchange()
        async with self.callback_lock:
            if scope is None or not scope["active"]:
                raise RuntimeError("Expired invocation")
            self.callback_id += 1
            callback_id = str(self.callback_id)
            request_id = scope["request"]["id"]
            await self.send(
                dict(
                    type=FrameKinds.Callback,
                    id=request_id,
                    callbackId=callback_id,
                    operation=operation,
                    payload=value,
                )
            )
            reply = await self.read()
            if (
                reply is None
                or reply.get("type") != FrameKinds.CallbackResult
                or reply.get("id") != request_id
                or reply.get("callbackId") != callback_id
            ):
                raise ValueError("Callback identity")
            if reply.get("error") is not None or reply.get("success") is False:
                raise RuntimeError("Host callback failed")
            return reply["value"]

    async def close(self):
        iterator, self.iterator = self.iterator, None
        live_stream, self.live_stream = self.live_stream, None
        self.stream_scope = None
        self.stream_id = None
        self.source.release()
        actions = []
        if live_stream is not None:
            actions.append(live_stream.close)
        if iterator is not None:
            actions.append(iterator.aclose)
        actions.append(self.complete_context)
        await cleanup_all(*actions)

    async def complete_context(self):
        context, self.context = self.context, None
        if context is not None:
            await context._close()

    async def dispatch(self, request):
        operation, payload = request["operation"], request["payload"]
        if operation in (SdkOperations.SourceRead, SdkOperations.SourceClose):
            return await self.dispatch_source(operation, payload)
        if operation in (SdkOperations.Next, SdkOperations.Close):
            return await self.dispatch_stream(operation, payload)
        if self.iterator is not None or self.source.identity is not None:
            raise RuntimeError("Session already active")
        context = self.context = PluginContext(
            request["context"]["tenant"], request["context"]["configuration"], self
        )
        if operation == SdkOperations.SourceOpen:
            return await self.source.open(
                self.app._sources[payload["operation"]], payload["input"], context
            )
        if operation == SdkOperations.Call:
            return await execute_with_cleanup(
                lambda: self.app._functions[payload["operation"]](
                    payload["input"], context
                ),
                self.complete_context,
            )
        if operation != SdkOperations.Start:
            raise ValueError("Unknown SDK operation")
        self.stream_scope = _SCOPE.get()
        self.iterator = self.app._streams[payload["operation"]](
            payload["input"], context
        ).__aiter__()
        self.live_stream = LiveStream(self.iterator, _encode)
        self.stream_id = uuid.uuid4().hex
        return dict(stream=self.stream_id)

    async def run(self):
        ready = dict(
            type=FrameKinds.Ready,
            protocol=(
                ProtocolVersions.Concurrent
                if self.app._concurrent_calls
                else ProtocolVersions.Exclusive
            ),
            pluginVersion=self.app._plugin_version,
            sessionCleanup=ProtocolVersions.SessionCleanup,
        )
        if self.app._concurrent_calls:
            ready["concurrentCalls"] = ProtocolVersions.ConcurrentCalls
        await self.send(ready)
        if self.app._concurrent_calls:
            from .concurrent import run_concurrent

            try:
                await run_concurrent(self, PluginContext, _SCOPE)
            finally:
                self.close_channel()
            return
        try:
            while (request := await self.read()) is not None:
                await self.exchange(request)
                request = None
        finally:
            await self.close()
            if self.channel:
                self.reader.close()
                self.writer.close()
                self.channel.close()

    async def exchange(self, request):
        """Keep callback authority active only for this host exchange."""
        scope = self.stream_scope or dict(request=request, active=True)
        scope.update(request=request, active=True)
        self.exchange_ready.set()
        handle = _SCOPE.set(scope)
        try:
            value = await self.dispatch(request)
            self.exchange_ready.clear()
            scope["active"] = False
            async with self.callback_lock:
                pass
            await self.send(
                dict(
                    type=FrameKinds.Result,
                    id=request["id"],
                    value=value,
                    reusable=self.context is None and self.iterator is None,
                )
            )
        except Exception as error:
            cleanup_failed = isinstance(error, SessionCleanupError)
            try:
                await self.close()
            except Exception:
                cleanup_failed = True
            await self.send(
                dict(
                    type=FrameKinds.Error,
                    id=request["id"],
                    code=(
                        FailureCodes.CleanupError
                        if cleanup_failed
                        else FailureCodes.SdkError
                    ),
                    primaryCode=(
                        FailureCodes.CleanupError
                        if isinstance(error, SessionCleanupError)
                        and not getattr(error, "has_execution_failure", False)
                        else FailureCodes.SdkError
                    ),
                    cleanupFailed=cleanup_failed,
                )
            )
        finally:
            self.exchange_ready.clear()
            scope["active"] = False
            _SCOPE.reset(handle)
            scope["request"] = None
            value = None

    async def dispatch_source(self, operation, payload):
        if self.source.identity is None or payload["source"] != self.source.identity:
            raise ValueError("Source ownership")
        if operation == SdkOperations.SourceClose:
            await self.close()
            return {}
        result = await self.source.read(payload)
        if result["done"]:
            await self.close()
        return result

    async def dispatch_stream(self, operation, payload):
        if self.stream_id is None or payload["stream"] != self.stream_id:
            raise ValueError("Stream ownership")
        if operation == SdkOperations.Close:
            await self.close()
            return {}
        result = await self.live_stream.next_batch()
        if result["done"]:
            await self.close()
        return result

    async def wait_for_stream_exchange(self):
        if self.live_stream is not None:
            await self.live_stream.wait_for_delivery()
        await self.exchange_ready.wait()

    def close_channel(self):
        if self.channel:
            self.channel.close()
