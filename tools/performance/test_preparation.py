"""Regression: settle every concurrent start before closing a failed pool batch."""
import asyncio
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import AsyncMock, patch
from reuse_matrix import measure


class PreparationTests(unittest.IsolatedAsyncioTestCase):
    async def test_failed_parallel_start_cannot_outlive_cleanup(self):
        lanes = []
        class Lane:
            def __init__(self, *args, **kwargs):
                self.index = len(lanes)
                self.starts = self.retirements = 0
                self.process = None
                self.completed = self.closed = False
                lanes.append(self)

            async def start(self):
                if self.index == 0:
                    raise TimeoutError('injected create timeout')
                await asyncio.sleep(.02)
                self.completed = True
                self.starts += 1

            async def close(self):
                if self.index:
                    assert self.completed, 'cleanup raced with unfinished startup'
                self.closed = True
                self.retirements += 1

        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory) / 'stage'
            with patch('reuse_matrix.EngineLane', Lane), \
                 patch('reuse_matrix.pressure', return_value={'freePercent': 80, 'swapUsedMiB': 0}), \
                 patch('reuse_matrix.command', new=AsyncMock(return_value='')):
                with self.assertRaisesRegex(RuntimeError, 'Stage failed'):
                    await measure(folder, {'mode': 'trusted', 'transport': 'engine', 'workers': 2,
                                          'memory': 64, 'seconds': 1}, 'unused', None)
            self.assertTrue(lanes[1].completed)
            self.assertTrue(all(l.closed for l in lanes))
            result = json.loads((folder / 'summary.json').read_text())
            self.assertIn('injected create timeout', result['failure'])
            self.assertEqual(result['remaining'], [])
