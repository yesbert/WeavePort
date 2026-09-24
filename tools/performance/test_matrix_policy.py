import asyncio
import unittest
from matrix_policy import PolicyPool


class FakeLane:
    def __init__(self):
        self.name = None
        self.starts = 0
        self.active = 0

    async def close(self):
        self.name = None

    async def call(self, request, intended=None):
        self.active += 1
        assert self.active == 1
        if self.name is None:
            self.starts += 1
            self.name = str(id(self)) + "-" + str(self.starts)
        await asyncio.sleep(0.001)
        self.active -= 1
        return {"container": self.name}


class PolicyTests(unittest.IsolatedAsyncioTestCase):
    async def test_bound_affinity_and_replacement(self):
        lane = FakeLane()
        pool = PolicyPool([lane], "bound")
        a = {"tenant": "a", "plugin": "p"}
        first = await pool.call(a)
        self.assertEqual(first, await pool.call(a))
        second = await pool.call(a | {"tenant": "b"})
        self.assertNotEqual(first, second)
        third = await pool.call(a | {"tenant": "b", "plugin": "q"})
        self.assertNotEqual(second, third)
        self.assertNotEqual(
            third, await pool.call(a | {"tenant": "b", "plugin": "q", "version": "v2"})
        )
        self.assertEqual(pool.switches, 3)

    async def test_approved_reuses_across_boundaries(self):
        pool = PolicyPool([FakeLane()], "approved")
        self.assertEqual(
            await pool.call({"tenant": "a", "plugin": "p"}),
            await pool.call({"tenant": "b", "plugin": "q"}),
        )

    async def test_bound_concurrency_keeps_one_binding(self):
        lanes = [FakeLane(), FakeLane()]
        pool = PolicyPool(lanes, "bound")
        rows = await asyncio.gather(
            *(pool.call({"tenant": "a", "plugin": "p"}) for _ in range(10))
        )
        self.assertEqual(len({r["container"] for r in rows}), 1)
        self.assertEqual(sum(l.starts for l in lanes), 1)

    async def test_affinity_survives_interleaved_customers(self):
        pool = PolicyPool([FakeLane(), FakeLane()], "bound")
        a = {"tenant": "a", "plugin": "p"}
        b = {"tenant": "b", "plugin": "p"}
        first = await pool.call(a)
        await pool.call(b)
        self.assertEqual(first, await pool.call(a))
