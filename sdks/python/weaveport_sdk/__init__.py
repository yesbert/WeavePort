"""Provider API: register async functions/generators; transport is SDK-owned."""
import asyncio
import contextvars
import json
import os
import socket
import sys
import uuid
from dataclasses import dataclass

_SCOPE = contextvars.ContextVar("weaveport_invocation", default=None)

def _encode(value):
    return json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode("utf-8")

@dataclass(frozen=True)
class PluginContext:
    tenant: str
    configuration: object
    _runtime: object

    async def call_host(self, operation, value):
        return await self._runtime.callback(operation, value)

class PluginApplication:
    def __init__(self, plugin_version="1"):
        if not isinstance(plugin_version, str) or not plugin_version.strip():
            raise ValueError("Invalid plugin version")
        self._plugin_version = plugin_version
        self._functions = {}
        self._streams = {}

    def _register(self, name, target):
        if not name or name.startswith("$") or name in self._functions or name in self._streams:
            raise ValueError("Invalid or duplicate operation")
        def register(handler):
            target[name] = handler
            return handler
        return register

    def function(self, name):
        return self._register(name, self._functions)

    def stream(self, name):
        return self._register(name, self._streams)

    def run(self):
        asyncio.run(_Runtime(self).run())

class _Runtime:
    def __init__(self, app):
        self.app = app
        self.iterator = None
        self.stream_id = None
        self.pending = None
        self.has_pending = False
        self.bytes = 0
        self.callback_id = 0
        self.callback_lock = asyncio.Lock()
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
            return reply["value"]

    async def close(self):
        iterator, self.iterator = self.iterator, None
        self.stream_id = None
        self.pending = None
        self.has_pending = False
        if iterator is not None:
            await iterator.aclose()

    async def dispatch(self, request):
        operation, payload = request["operation"], request["payload"]
        if operation in ("$sdk.next", "$sdk.close"):
            if self.stream_id is None or payload["stream"] != self.stream_id:
                raise ValueError("Stream ownership")
            if operation == "$sdk.close":
                await self.close()
                return {}
            items, batch_bytes, done = [], 2, False
            while len(items) < 16:
                if self.has_pending:
                    item, self.has_pending = self.pending, False
                    self.pending = None
                else:
                    try:
                        item = await anext(self.iterator)
                    except StopAsyncIteration:
                        done = True
                        break
                size = len(_encode(item))
                if size > 128 << 10:
                    raise ValueError("Item limit")
                if batch_bytes + size + 1 > 256 << 10:
                    self.pending, self.has_pending = item, True
                    break
                batch_bytes += size + 1
                self.bytes += size
                if self.bytes > 64 << 20:
                    raise ValueError("Stream limit")
                items.append(item)
            if done:
                await self.close()
            return dict(items=items, done=done)
        if self.iterator is not None:
            raise RuntimeError("Stream already active")
        context = PluginContext(request["context"]["tenant"], request["context"]["configuration"], self)
        if operation == "$sdk.call":
            return await self.app._functions[payload["operation"]](payload["input"], context)
        if operation != "$sdk.start":
            raise ValueError("Unknown SDK operation")
        self.iterator = self.app._streams[payload["operation"]](payload["input"], context).__aiter__()
        self.stream_id, self.bytes = uuid.uuid4().hex, 0
        return dict(stream=self.stream_id)

    async def run(self):
        await self.send(dict(type="ready", protocol=1, pluginVersion=self.app._plugin_version))
        try:
            while (request := await self.read()) is not None:
                scope = dict(request=request, active=True)
                handle = _SCOPE.set(scope)
                try:
                    value = await self.dispatch(request)
                    await self.send(dict(type="result", id=request["id"], value=value))
                except Exception:
                    await self.close()
                    await self.send(dict(type="error", id=request["id"], code="sdk-error"))
                finally:
                    scope["active"] = False
                    _SCOPE.reset(handle)
        finally:
            await self.close()
            if self.channel:
                self.reader.close()
                self.writer.close()
                self.channel.close()
