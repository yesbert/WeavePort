"""Provider API: register async functions/generators; transport is SDK-owned."""
import asyncio
import contextvars
import json
import os
import socket
import sys
import uuid
import inspect
import threading
from .source import SourceSession
from .stream import LiveStream

_SCOPE = contextvars.ContextVar("weaveport_invocation", default=None)

def _encode(value):
    return json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(",", ":")).encode("utf-8")

class SessionCleanupError(RuntimeError):
    """Registered cleanup failed; the host must retire the worker."""

class PluginContext:
    """Resources belong to one function call or complete result stream."""
    def __init__(self, tenant, configuration, runtime):
        self._tenant, self._configuration, self._runtime = tenant, configuration, runtime
        self._active, self._cleanup = True, []
        self._cancelled = threading.Event()

    def _check(self):
        if not self._active:
            raise RuntimeError("Session completed")

    @property
    def tenant(self):
        self._check()
        return self._tenant

    @property
    def configuration(self):
        self._check()
        return self._configuration

    def on_close(self, action):
        """Register a sync/async zero-argument cleanup action, in ownership order."""
        self._check()
        if not callable(action):
            raise TypeError("Cleanup must be callable")
        self._cleanup.append(action)

    def own(self, resource):
        """Transfer a resource exposing close() or aclose() to this session."""
        self.on_close(getattr(resource, "aclose", None) or resource.close)
        return resource

    @property
    def cancelled(self):
        """Cooperative cancellation; capacity remains occupied until the handler exits."""
        return self._cancelled.is_set()

    def call_host_sync(self, operation, value):
        """Call a host callback from a synchronous concurrent worker handler."""
        self._check()
        loop = getattr(self._runtime, "loop", None)
        if loop is None:
            raise RuntimeError("Synchronous callbacks require concurrent mode")
        try:
            if asyncio.get_running_loop() is loop:
                raise RuntimeError("Use await call_host from async handlers")
        except RuntimeError as error:
            if str(error) != "no running event loop":
                raise
        return asyncio.run_coroutine_threadsafe(self.call_host(operation, value), loop).result()

    async def call_host(self, operation, value):
        self._check()
        return await self._runtime.callback(operation, value)

    async def _close(self):
        self._active = False
        self._tenant = self._configuration = self._runtime = None
        actions, self._cleanup = self._cleanup, []
        errors = []
        for action in reversed(actions):
            try:
                result = action()
                if inspect.isawaitable(result):
                    await result
            except BaseException as error:
                errors.append(error)
        if errors:
            raise SessionCleanupError("Registered session cleanup failed") from errors[0]

class PluginApplication:
    def __init__(self, plugin_version="1", *, concurrent_calls=False):
        if not isinstance(plugin_version, str) or not plugin_version.strip():
            raise ValueError("Invalid plugin version")
        self._concurrent_calls = concurrent_calls
        self._plugin_version = plugin_version
        self._functions = {}
        self._streams = {}
        self._sources = {}

    def _register(self, name, target):
        if not name or name.startswith("$") or name in self._functions or name in self._streams or name in self._sources:
            raise ValueError("Invalid or duplicate operation")
        def register(handler):
            target[name] = handler
            return handler
        return register

    def function(self, name):
        return self._register(name, self._functions)

    def stream(self, name):
        return self._register(name, self._streams)

    def source(self, name):
        """Register a handler returning a binary readable with owned close/aclose."""
        return self._register(name, self._sources)

    def run(self):
        asyncio.run(_Runtime(self).run())

class _Runtime:
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
            self.writer = self.channel.makefile("wb", buffering=65536)
        else:
            self.reader = sys.stdin.buffer
            self.writer = sys.stdout.buffer
        sys.stdout = sys.stderr

    async def read(self):
        line = await asyncio.to_thread(self.reader.readline, (1 << 20) + 2)
        if not line:
            return None
        if len(line) > (1 << 20) + 1 or not line.endswith(b"\n"):
            raise ValueError("Frame limit")
        return json.loads(line)

    async def send(self, value):
        data = _encode(value)
        if len(data) > 1 << 20:
            raise ValueError("Frame limit")
        self.writer.write(data + b"\n")
        self.writer.flush()

    async def callback(self, operation, value):
        scope = _SCOPE.get()
        if scope is self.stream_scope:
            if self.live_stream is not None:
                await self.live_stream.wait_for_delivery()
            await self.exchange_ready.wait()
        async with self.callback_lock:
            if scope is None or not scope["active"]:
                raise RuntimeError("Expired invocation")
            self.callback_id += 1
            cid = str(self.callback_id)
            rid = scope["request"]["id"]
            await self.send(dict(type="callback", id=rid, callbackId=cid, operation=operation, payload=value))
            reply = await self.read()
            if reply is None or reply.get("type") != "callback-result" or reply.get("id") != rid or reply.get("callbackId") != cid:
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
        try:
            if live_stream is not None:
                try:
                    await live_stream.close()
                except BaseException as error:
                    raise SessionCleanupError("Stream advancement cleanup failed") from error
            if iterator is not None:
                try:
                    await iterator.aclose()
                except BaseException as error:
                    raise SessionCleanupError("Stream cleanup failed") from error
        finally:
            await self.complete_context()

    async def complete_context(self):
        context, self.context = self.context, None
        if context is not None:
            await context._close()

    async def dispatch(self, request):
        operation, payload = request["operation"], request["payload"]
        if operation in ("$sdk.source.read", "$sdk.source.close"):
            if self.source.identity is None or payload["source"] != self.source.identity:
                raise ValueError("Source ownership")
            if operation == "$sdk.source.close":
                await self.close()
                return {}
            result = await self.source.read(payload)
            if result["done"]:
                await self.close()
            return result
        if operation in ("$sdk.next", "$sdk.close"):
            if self.stream_id is None or payload["stream"] != self.stream_id:
                raise ValueError("Stream ownership")
            if operation == "$sdk.close":
                await self.close()
                return {}
            result = await self.live_stream.next_batch()
            if result["done"]:
                await self.close()
            return result
        if self.iterator is not None or self.source.identity is not None:
            raise RuntimeError("Session already active")
        context = self.context = PluginContext(request["context"]["tenant"], request["context"]["configuration"], self)
        if operation == "$sdk.source.open":
            return await self.source.open(self.app._sources[payload["operation"]], payload["input"], context)
        if operation == "$sdk.call":
            try:
                return await self.app._functions[payload["operation"]](payload["input"], context)
            finally:
                await self.complete_context()
        if operation != "$sdk.start":
            raise ValueError("Unknown SDK operation")
        self.stream_scope = _SCOPE.get()
        self.iterator = self.app._streams[payload["operation"]](payload["input"], context).__aiter__()
        self.live_stream = LiveStream(self.iterator, _encode)
        self.stream_id = uuid.uuid4().hex
        return dict(stream=self.stream_id)

    async def run(self):
        ready = dict(type="ready", protocol=2 if self.app._concurrent_calls else 1, pluginVersion=self.app._plugin_version, sessionCleanup=1)
        if self.app._concurrent_calls:
            ready["concurrentCalls"] = 1
        await self.send(ready)
        if self.app._concurrent_calls:
            from .concurrent import run_concurrent
            try:
                await run_concurrent(self, PluginContext, _SCOPE)
            finally:
                if self.channel:
                    self.channel.close()
            return
        try:
            while (request := await self.read()) is not None:
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
                    await self.send(dict(type="result", id=request["id"], value=value, reusable=self.context is None and self.iterator is None))
                except Exception as error:
                    cleanup_failed = isinstance(error, SessionCleanupError)
                    try:
                        await self.close()
                    except Exception:
                        cleanup_failed = True
                    await self.send(dict(type="error", id=request["id"], code="cleanup-error" if cleanup_failed else "sdk-error"))
                finally:
                    self.exchange_ready.clear()
                    scope["active"] = False
                    _SCOPE.reset(handle)
                    scope["request"] = None
                    request = None
                    value = None
        finally:
            await self.close()
            if self.channel:
                self.reader.close()
                self.writer.close()
                self.channel.close()
