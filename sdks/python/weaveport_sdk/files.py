"""Function file inputs and outputs; bytes travel only through the host's readers and writers."""

import asyncio
import base64
import io
import re
from collections import OrderedDict, namedtuple

from .protocol import FileErrors, ProtocolLimits, SdkOperations

FileInfo = namedtuple("FileInfo", "name length media_type")
# fullmatch: a trailing newline must not pass as it would with "$".
_NAME = re.compile(r"[A-Za-z0-9_\-][A-Za-z0-9._\-]{0,127}")
_MEDIA_TYPE = re.compile(r"[!-~]{1,255}")


class PluginFileError(OSError):
    """A refused file operation; the invocation continues unless the handler re-raises."""

    def __init__(self, code):
        super().__init__(f"Function file operation refused: {code}")
        self.code = code


def validate_block_bytes(value):
    if (
        not isinstance(value, int)
        or not ProtocolLimits.FileBlockMinimumBytes
        <= value
        <= ProtocolLimits.FileBlockMaximumBytes
    ):
        raise ValueError("File block size must be 4096-262144 bytes")
    return value


class FileSession:
    """Invocation-owned descriptors and the reserved file operations."""

    def __init__(self, request, runtime, block_bytes):
        files = request.get("files") if isinstance(request, dict) else None
        files = files if isinstance(files, dict) else {}
        self.inputs = tuple(
            FileInfo(item["name"], item["length"], item["mediaType"])
            for item in files.get("inputs", ())
        )
        self.outputs_permitted = isinstance(files.get("outputs"), dict)
        self.block_bytes = block_bytes
        self._runtime = runtime
        self._outputs = set()
        self._closed = False

    def input(self, name):
        for info in self.inputs:
            if info.name == name:
                return info
        raise PluginFileError(FileErrors.InputNotFound)

    def reserve_output(self, name, media_type):
        if not isinstance(name, str) or not _NAME.fullmatch(name):
            raise ValueError("Invalid output name")
        if not isinstance(media_type, str) or not _MEDIA_TYPE.fullmatch(media_type):
            raise ValueError("Invalid media type")
        self._check()
        if not self.outputs_permitted:
            raise PluginFileError(FileErrors.OutputNotPermitted)
        if name in self._outputs:
            raise PluginFileError(FileErrors.OutputExists)
        self._outputs.add(name)

    async def read(self, name, offset):
        value = await self._send(
            SdkOperations.InputRead,
            dict(name=name, offset=offset, length=self.block_bytes),
        )
        return base64.b64decode(value["data"], validate=True)

    async def create(self, name, media_type):
        await self._send(SdkOperations.OutputCreate, dict(name=name, mediaType=media_type))

    async def write(self, name, data):
        await self._send(
            SdkOperations.OutputWrite,
            dict(name=name, data=base64.b64encode(data).decode("ascii")),
        )

    async def complete(self, name):
        value = await self._send(SdkOperations.OutputComplete, dict(name=name))
        return value["length"]

    def close(self):
        self._closed = True
        self._runtime = None

    def _check(self):
        if self._closed:
            raise RuntimeError("Session completed")

    async def _send(self, operation, payload):
        self._check()
        value, error = await self._runtime.exchange_callback(operation, payload)
        if error is not None:
            raise PluginFileError(error)
        return value


class InputFile:
    """A host input readable at any offset; recently read blocks are cached."""

    def __init__(self, files, info, loop):
        self._files = files
        self._info = info
        self._loop = loop
        self._cache = OrderedDict()

    @property
    def name(self):
        return self._info.name

    @property
    def size(self):
        return self._info.length

    @property
    def media_type(self):
        return self._info.media_type

    async def read_at(self, offset, length):
        """Return up to length bytes from offset; fewer only at the end of the input."""
        if offset < 0 or length < 0:
            raise ValueError("Invalid range")
        # Checked on every read, including cache hits, so a retained input cannot outlive its call.
        self._files._check()
        end = min(offset + length, self.size)
        parts = []
        while offset < end:
            block = await self._block(offset // self._files.block_bytes)
            start = offset % self._files.block_bytes
            chunk = block[start : start + end - offset]
            if not chunk:
                break
            parts.append(chunk)
            offset += len(chunk)
        return b"".join(parts)

    def as_file(self):
        """A synchronous seekable binary file for libraries such as zipfile; use it off the event-loop thread."""
        return io.BufferedReader(_SyncInput(self, self._loop), self._files.block_bytes)

    async def _block(self, index):
        if index in self._cache:
            self._cache.move_to_end(index)
            return self._cache[index]
        data = await self._files.read(self.name, index * self._files.block_bytes)
        self._cache[index] = data
        if len(self._cache) > ProtocolLimits.FileBlockCacheBlocks:
            self._cache.popitem(last=False)
        return data


class OutputFile:
    """A named output; close() completes it. The host commits it only if the call succeeds."""

    def __init__(self, files, name, loop):
        self._files = files
        self._loop = loop
        self.name = name
        self._buffer = bytearray()
        self._written = 0
        self._completed = False

    @property
    def written(self):
        return self._written

    async def write(self, data):
        """Buffer at most one block; large writes are sent block by block without copying them whole."""
        if self._completed:
            raise PluginFileError(FileErrors.OutputClosed)
        view = memoryview(data).cast("B")
        total = len(view)
        self._written += total
        while view:
            take = min(self._files.block_bytes - len(self._buffer), len(view))
            self._buffer += view[:take]
            view = view[take:]
            await self._send_full_block()
        return total

    async def _send_full_block(self):
        if len(self._buffer) < self._files.block_bytes:
            return
        block = bytes(self._buffer)
        self._buffer.clear()
        await self._files.write(self.name, block)

    async def close(self):
        """Send the remaining bytes and complete the output; repeated calls do nothing."""
        if self._completed:
            return
        self._completed = True
        if self._buffer:
            await self._files.write(self.name, bytes(self._buffer))
            self._buffer.clear()
        await self._files.complete(self.name)

    async def __aenter__(self):
        return self

    async def __aexit__(self, *_):
        await self.close()

    def as_file(self):
        """A synchronous writable binary file; closing it completes the output. Use it off the event-loop thread."""
        return _SyncOutput(self, self._loop)


def _run(loop, coroutine):
    try:
        running = asyncio.get_running_loop()
    except RuntimeError:
        running = None
    if running is loop:
        coroutine.close()
        raise RuntimeError(
            "Synchronous file access must run off the event loop, e.g. with asyncio.to_thread"
        )
    return asyncio.run_coroutine_threadsafe(coroutine, loop).result()


class _SyncInput(io.RawIOBase):
    def __init__(self, source, loop):
        self._source = source
        self._loop = loop
        self._position = 0

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self._position

    def seek(self, offset, whence=io.SEEK_SET):
        base = {io.SEEK_SET: 0, io.SEEK_CUR: self._position, io.SEEK_END: self._source.size}[whence]
        if base + offset < 0:
            raise ValueError("Negative seek position")
        self._position = base + offset
        return self._position

    def readinto(self, buffer):
        data = _run(self._loop, self._source.read_at(self._position, len(buffer)))
        buffer[: len(data)] = data
        self._position += len(data)
        return len(data)


class _SyncOutput(io.RawIOBase):
    def __init__(self, target, loop):
        self._target = target
        self._loop = loop

    def writable(self):
        return True

    def write(self, data):
        return _run(self._loop, self._target.write(bytes(data)))

    def __del__(self):
        # Never complete an output from garbage collection; an unclosed output fails the call as output-incomplete.
        pass

    def close(self):
        if not self.closed:
            _run(self._loop, self._target.close())
        super().close()
