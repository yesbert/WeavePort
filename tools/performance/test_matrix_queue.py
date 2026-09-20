import asyncio
import unittest
from matrix_queue import SerialReadyQueue, grouped_capacity


class ReadyQueueTests(unittest.IsolatedAsyncioTestCase):
    async def test_same_key_serializes_without_blocking_other_plugins(self):
        q = SerialReadyQueue(8, lambda x: x[0])
        for item in [('a', 1), ('a', 2), ('b', 1)]:
            q.put_nowait(item)
        first, other = await q.get(), await q.get()
        self.assertEqual(('a', 1), first[1])
        self.assertEqual(('b', 1), other[1])
        pending = asyncio.create_task(q.get())
        await asyncio.sleep(0)
        self.assertFalse(pending.done())
        q.task_done(first[0])
        second = await pending
        self.assertEqual(('a', 2), second[1])
        q.task_done(other[0])
        q.task_done(second[0])
        await q.join()
        self.assertFalse(q.pending)

    async def test_capacity_includes_waiting_jobs_for_busy_keys(self):
        q = SerialReadyQueue(2, lambda x: x[0])
        q.put_nowait(('a', 1))
        first = await q.get()
        q.put_nowait(('a', 2))
        q.put_nowait(('a', 3))
        with self.assertRaises(asyncio.QueueFull):
            q.put_nowait(('b', 1))
        q.task_done(first[0])
        for expected in [2, 3]:
            key, item = await q.get()
            self.assertEqual(expected, item[1])
            q.task_done(key)
        await q.join()

    async def test_join_waits_for_inflight_and_blocked_jobs(self):
        q = SerialReadyQueue(2, lambda x: x)
        q.put_nowait('a')
        q.put_nowait('a')
        first = await q.get()
        joined = asyncio.create_task(q.join())
        q.task_done(first[0])
        await asyncio.sleep(0)
        self.assertFalse(joined.done())
        second = await q.get()
        q.task_done(second[0])
        await joined

    async def test_ten_call_customers_leave_eight_independent_lanes_ready(self):
        capacity = grouped_capacity(8, 10)
        q = SerialReadyQueue(capacity, lambda i: i // 10)
        for i in range(capacity):
            q.put_nowait(i)
        active = [await asyncio.wait_for(q.get(), .1) for _ in range(8)]
        self.assertEqual(set(range(8)), {key for key, item in active})
        for key, item in active:
            q.task_done(key)
        self.assertLessEqual(grouped_capacity(128, 100), 10000)

    async def test_sentinels_are_independent(self):
        q = SerialReadyQueue(2, lambda x: x)
        await q.put(None)
        await q.put(None)
        first, second = await q.get(), await q.get()
        self.assertIsNone(first[1])
        self.assertIsNone(second[1])
        self.assertIsNot(first[0], second[0])
        q.task_done(first[0])
        q.task_done(second[0])
        await q.join()


if __name__ == '__main__':
    unittest.main()
