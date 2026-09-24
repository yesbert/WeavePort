"""Bounded producer and live-delivery regression checks."""

import asyncio
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from weaveport_sdk.stream import LiveStream


def encode(value):
    return json.dumps(value, separators=(",", ":")).encode()


class LiveStreamTests(unittest.IsolatedAsyncioTestCase):
    async def test_paused_consumer_bounds_small_items(self):
        await self.assert_bounded_read_ahead("small")

    async def test_paused_consumer_bounds_large_items(self):
        await self.assert_bounded_read_ahead("x" * 65536)

    async def assert_bounded_read_ahead(self, item):
        produced = 0
        closed = False

        async def generate():
            nonlocal produced, closed
            try:
                for _ in range(100):
                    produced += 1
                    yield item
            finally:
                closed = True

        stream = LiveStream(generate(), encode)
        first = await stream.next_batch()
        self.assertTrue(first["items"])
        await asyncio.sleep(0.01)
        # A paused consumer cannot permit unbounded generator read-ahead.
        unconsumed = produced - len(first["items"])
        self.assertLessEqual(unconsumed, 17)
        self.assertLessEqual(unconsumed * len(encode(item)), (256 + 128) << 10)
        received = first["items"]
        while True:
            batch = await stream.next_batch()
            self.assertLessEqual(len(encode(batch["items"])), 256 << 10)
            received.extend(batch["items"])
            if batch["done"]:
                break
        self.assertEqual(received, [item] * 100)
        await stream.close()
        self.assertTrue(closed)

    async def test_empty_heartbeats_keep_one_advancement_alive(self):
        release = asyncio.Event()
        advances = 0
        closed = False

        async def generate():
            nonlocal advances, closed
            try:
                advances += 1
                await release.wait()
                yield "ready"
            finally:
                closed = True

        stream = LiveStream(generate(), encode)
        for _ in range(3):
            self.assertEqual(await stream.next_batch(), dict(items=[], done=False))
            self.assertEqual(advances, 1)
        release.set()
        self.assertEqual(await stream.next_batch(), dict(items=["ready"], done=True))
        await stream.close()
        self.assertTrue(closed)


if __name__ == "__main__":
    unittest.main()
