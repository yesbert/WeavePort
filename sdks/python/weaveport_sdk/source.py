"""Exclusive, bounded binary source ownership."""

from .protocol import ProtocolLimits

import base64
import inspect
import uuid


class SourceSession:
    def __init__(self):
        self.source = None
        self.identity = None

    async def open(self, handler, value, context):
        source = handler(value, context)
        if inspect.isawaitable(source):
            source = await source
        if not callable(getattr(source, "read", None)):
            raise TypeError("Source must expose read(size) and close() or aclose()")
        context.own(source)
        self.source, self.identity = source, uuid.uuid4().hex
        return dict(source=self.identity)

    async def read(self, payload):
        size = payload.get("chunkBytes")
        if (
            type(size) is not int
            or not ProtocolLimits.SourceChunkMinimumBytes
            <= size
            <= ProtocolLimits.SourceChunkMaximumBytes
        ):
            raise ValueError("Source chunk limit")
        data = self.source.read(size)
        if inspect.isawaitable(data):
            data = await data
        if not isinstance(data, (bytes, bytearray)) or len(data) > size:
            raise ValueError("Source must return bounded bytes")
        return dict(data=base64.b64encode(data).decode("ascii"), done=len(data) == 0)

    def release(self):
        self.source = self.identity = None
