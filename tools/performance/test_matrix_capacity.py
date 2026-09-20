import json
from pathlib import Path
import tempfile
import unittest
from matrix_capacity import stability


class CapacityTests(unittest.TestCase):
    def evaluate(self, backlogs, peaks=None, passed=True):
        with tempfile.TemporaryDirectory() as folder:
            rows = [{'elapsed': 1 + i * 3, 'counts': {'offered': 1000 + i * 1000,
                     'completed': 1000 + i * 1000 - pending, 'dropped': 0},
                     'windowResponseMs': {'count': 1000, 'p99': peaks[i] if peaks else 10}}
                    for i, pending in enumerate(backlogs)]
            path = Path(folder)
            (path / 'samples.jsonl').write_text(''.join(json.dumps(row) + '\n' for row in rows))
            return stability(path, {'config': {'seconds': 60, 'workers': 8, 'rate': 500}, 'meetsOneSecondSlo': passed})

    def test_stable_operating_point(self):
        self.assertTrue(self.evaluate([5, 8, 7, 6, 9, 7, 8, 6, 10])['qualified'])

    def test_queue_growth_rejects_good_aggregate_latency(self):
        self.assertFalse(self.evaluate([0, 0, 0, 30, 60, 100, 200, 300, 400])['qualified'])

    def test_bad_window_cannot_hide_in_aggregate(self):
        self.assertFalse(self.evaluate([0] * 9, [10] * 8 + [1200])['qualified'])

    def test_dropped_calls_reject_capacity(self):
        self.assertFalse(self.evaluate([0] * 9, passed=False)['qualified'])

    def test_too_few_windows_are_not_evidence(self):
        self.assertFalse(self.evaluate([0, 0])['qualified'])


if __name__ == '__main__':
    unittest.main()
