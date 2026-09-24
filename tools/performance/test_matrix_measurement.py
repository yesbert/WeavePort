"""Exercise real matrix accounting and scheduling with bounded in-memory lanes."""

import asyncio
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import AsyncMock, patch
from reuse_matrix import measure


class CooperativeLane:
    def __init__(self, mode, known, image, *args, **kwargs):
        self.name = "fixture"
        self.starts = self.retirements = 0
        self.process = None

    async def start(self):
        self.starts += 1

    async def close(self):
        self.retirements += 1

    async def call(self, request, intended=None):
        await asyncio.sleep(0.001)
        payload = request["payload"]
        value = payload.upper() if request["plugin"] == "a" else payload[::-1]
        return {
            "request": request,
            "container": self.name,
            "outcome": {
                "status": "ok",
                "reusable": True,
                "value": {
                    "tenant": request["tenant"],
                    "plugin": request["plugin"],
                    "value": value,
                },
            },
            "responseMs": 1.0,
            "turnoverMs": 1.0,
            "queueMs": 0.0,
        }


class MeasurementTests(unittest.IsolatedAsyncioTestCase):
    async def run_stage(self, settings):
        memory = {"ownedMiB": 0, "ownedRawMiB": 0, "owned": []}

        async def command(*args):
            return f"{os.getpid()} 1 1024" if args[0] == "/bin/ps" else ""

        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory) / "stage"
            with patch("reuse_matrix.MatrixLane", CooperativeLane), patch(
                "reuse_matrix.pressure",
                return_value={"freePercent": 80, "swapUsedMiB": 0},
            ), patch("reuse_matrix.command", new=command), patch(
                "reuse_matrix.docker_memory.snapshot", return_value=memory
            ):
                result = await measure(
                    folder,
                    {"mode": "trusted", "workers": 2, "seconds": 0.1, **settings},
                    "fixture",
                    None,
                )
            self.assertTrue(result["passed"])
            self.assertGreater(result["counts"]["completed"], 0)
            self.assertEqual(result["counts"]["offered"], result["counts"]["completed"])
            self.assertEqual(2, result["retirements"])
            self.assertEqual([], result["remaining"])
            self.assertEqual(result, json.loads((folder / "summary.json").read_text()))
            return result

    async def test_saturated_lanes_account_for_every_completion(self):
        await self.run_stage({"arrival": "saturated"})

    async def test_grouped_arrivals_finish_complete_customers(self):
        result = await self.run_stage({"arrival": "saturated", "callsPerCustomer": 3})
        self.assertEqual(0, result["counts"]["offered"] % 3)
        self.assertEqual(
            result["counts"]["offered"] // 3, result["counts"]["customersCompleted"]
        )

    async def test_poisson_arrivals_drain_admitted_requests(self):
        await self.run_stage({"arrival": "poisson", "rate": 100, "seed": 1729})

    async def test_periodic_customers_keep_their_schedule(self):
        await self.run_stage(
            {"arrival": "periodic", "population": 4, "periodSeconds": 0.02}
        )


if __name__ == "__main__":
    unittest.main()
