"""A single retained producer batches ready items without per-item task scheduling."""

from .protocol import ProtocolLimits

import asyncio
from collections import deque

_BATCH_WAIT_SECONDS = 0.025


class LiveStream:
    def __init__(self, iterator, encode):
        self.iterator = iterator
        self.encode = encode
        self.items = deque()
        self.buffer_bytes = 2
        self.total_bytes = 0
        self.ready = asyncio.Event()
        self.space = asyncio.Event()
        self.delivered = asyncio.Event()
        self.task = None
        self.done = False
        self.error = None

    async def _produce(self):
        try:
            while True:
                await self._wait_for_item_capacity()
                item = await anext(self.iterator)
                size = len(self.encode(item))
                if size > ProtocolLimits.StreamItemBytes:
                    raise ValueError("Item limit")
                await self._wait_for_byte_capacity(size)
                self.total_bytes += size
                if self.total_bytes > ProtocolLimits.StreamTotalBytes:
                    raise ValueError("Stream limit")
                self.items.append(item)
                self.buffer_bytes += size + 1
                self.ready.set()
        except StopAsyncIteration:
            self.done = True
        except asyncio.CancelledError:
            raise
        except Exception as error:
            self.error = error
        finally:
            self.ready.set()

    async def next_batch(self):
        if self.task is None:
            self.task = asyncio.create_task(self._produce())
        if not self.items and not self.done and self.error is None:
            self.ready.clear()
            try:
                async with asyncio.timeout(_BATCH_WAIT_SECONDS):
                    await self.ready.wait()
            except TimeoutError:
                pass
        if self.error is not None:
            error, self.error = self.error, None
            raise error
        items = list(self.items)
        self.items.clear()
        self.buffer_bytes = 2
        self.space.set()
        self.delivered.set()
        return dict(items=items, done=self.done)

    async def wait_for_delivery(self):
        while self.items:
            self.delivered.clear()
            await self.delivered.wait()

    async def close(self):
        if self.task is not None and not self.task.done():
            self.task.cancel()
            await asyncio.gather(self.task, return_exceptions=True)
        self.items.clear()
        if self.error is not None:
            error, self.error = self.error, None
            raise error

    async def _wait_for_item_capacity(self):
        while len(self.items) >= ProtocolLimits.StreamBatchItems:
            self.space.clear()
            await self.space.wait()

    async def _wait_for_byte_capacity(self, size):
        while self.buffer_bytes + size + 1 > ProtocolLimits.StreamBatchBytes:
            self.space.clear()
            await self.space.wait()
