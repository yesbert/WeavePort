"""Expanded probes must fail before launching containers when headroom is absent."""

import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, patch
from reuse_matrix import measure


class HeadroomControls(unittest.IsolatedAsyncioTestCase):
    async def rejected(self, workers, free_percent, expected):
        with tempfile.TemporaryDirectory(prefix="wp-matrix-headroom-") as directory:
            folder = Path(directory) / "stage"
            engine = SimpleNamespace(get=lambda route: {"MemTotal": 1024 * 1048576})
            with patch(
                "reuse_matrix.pressure",
                return_value={"freePercent": free_percent, "swapUsedMiB": 0},
            ), patch(
                "reuse_matrix.docker_memory.snapshot",
                return_value={"allContainersRawMiB": 0},
            ), patch(
                "reuse_matrix.command", new=AsyncMock(return_value="")
            ), patch(
                "reuse_matrix.EngineLane.start", new_callable=AsyncMock
            ) as start:
                with self.assertRaisesRegex(RuntimeError, "Stage failed"):
                    await measure(
                        folder,
                        {
                            "mode": "trusted",
                            "transport": "engine",
                            "workers": workers,
                            "memory": 64,
                            "seconds": 1,
                        },
                        "unused-image",
                        engine,
                    )
                start.assert_not_awaited()
            result = json.loads((folder / "summary.json").read_text())
            self.assertFalse(result["passed"])
            self.assertIn(expected, result["failure"])
            self.assertEqual([], result["known"])
            self.assertEqual([], result["remaining"])

    async def test_expanded_reservation_requires_docker_headroom(self):
        await self.rejected(128, 70, "Docker headroom")

    async def test_initial_host_pressure_prevents_pool_preparation(self):
        await self.rejected(16, 5, "host memory headroom")


if __name__ == "__main__":
    unittest.main()
