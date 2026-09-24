"""Bounded ready queue: same customer/plugin jobs never occupy two worker lanes."""

import asyncio
from collections import deque


def grouped_capacity(workers, calls_per_customer):
    """Keep enough independent customer groups ready, with a bounded call count."""
    return min(10000, workers * calls_per_customer * 2)


class SerialReadyQueue:
    def __init__(self, maxsize, key_for):
        if maxsize < 1:
            raise ValueError("Queue capacity must be positive")
        self.maximum, self.key_for = maxsize, key_for
        self.ready, self.pending = asyncio.Queue(), {}
        self.waiting = self.unfinished = 0
        self.space, self.drained = asyncio.Event(), asyncio.Event()
        self.space.set()
        self.drained.set()

    def put_nowait(self, item):
        if self.waiting >= self.maximum:
            raise asyncio.QueueFull
        key = self.key_for(item) if item is not None else object()
        if key not in self.pending:
            self.pending[key] = deque()
            self.ready.put_nowait(key)
        self.pending[key].append(item)
        self.waiting += 1
        self.unfinished += 1
        self.drained.clear()
        if self.waiting >= self.maximum:
            self.space.clear()

    async def put(self, item):
        while True:
            try:
                self.put_nowait(item)
                return
            except asyncio.QueueFull:
                await self.space.wait()

    async def get(self):
        key = await self.ready.get()
        item = self.pending[key].popleft()
        self.waiting -= 1
        self.space.set()
        return key, item

    def task_done(self, key):
        self.ready.task_done()
        if self.pending[key]:
            self.ready.put_nowait(key)
        else:
            del self.pending[key]
        self.unfinished -= 1
        if self.unfinished == 0:
            self.drained.set()

    async def join(self):
        await self.drained.wait()
