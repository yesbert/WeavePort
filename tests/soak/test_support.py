"""Regression checks for supervisor guards and real owned-process cleanup."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/soak'))
from support import read_line, stop, process_table, summarize_resources, verify_package, descriptor_counts, cleanup_uncertain, startup_limit


class SupervisorTests(unittest.TestCase):
    def test_startup_policy_matches_clients_without_unbounded_limits(self):
        for clients in (3, 24, 48):
            self.assertEqual(startup_limit(clients), clients)
        self.assertEqual(startup_limit(24, 8), 8)
        for invalid in (0, -1, 49, True, float('inf'), '24'):
            with self.assertRaises(ValueError):
                startup_limit(24, invalid)

    def test_report_does_not_round_rare_errors_to_zero(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'result.json').write_text(json.dumps(dict(status='failed', sourceCommit='test', scope='fixture', settings=dict(clients=1))))
            metric = dict(values=dict(rate=0.0000005, passes=1, fails=1999999))
            (path / 'k6-summary.json').write_text(json.dumps(dict(metrics={'unexpected_errors': metric, 'unexpected_errors{tenant:tenant-0}': metric})))
            subprocess.run([sys.executable, '-B', str(Path(__file__).resolve().parents[2] / 'tools/soak/analyze.py'), directory], check=True, capture_output=True)
            report = (path / 'report.md').read_text()
            self.assertIn('unexpected error samples | 1', report)
            self.assertEqual(report.count('0.00000050'), 2)

    def test_rotating_short_cleanup_never_accumulates_age(self):
        for _ in range(100):
            self.assertFalse(cleanup_uncertain(dict(Quarantined=3, OldestQuarantineSeconds=1, MaintenanceFailure=None)))

    def test_stuck_cleanup_and_maintenance_failure_abort(self):
        for age, expected in ((0, False), (30, False), (30.001, True), (90, True)):
            self.assertEqual(cleanup_uncertain(dict(Quarantined=1, OldestQuarantineSeconds=age, MaintenanceFailure=None)), expected)
        self.assertTrue(cleanup_uncertain(dict(Quarantined=0, OldestQuarantineSeconds=0, MaintenanceFailure='IOException')))
        self.assertFalse(cleanup_uncertain(dict(Quarantined=0, OldestQuarantineSeconds=0, MaintenanceFailure=None)))

    def test_invalid_cleanup_diagnostics_fail_closed(self):
        for age in (None, -1, float('nan'), float('inf'), '1', True):
            self.assertTrue(cleanup_uncertain(dict(Quarantined=1, OldestQuarantineSeconds=age, MaintenanceFailure=None)))
        self.assertTrue(cleanup_uncertain(dict(Quarantined=0, OldestQuarantineSeconds=1, MaintenanceFailure=None)))
        self.assertTrue(cleanup_uncertain(dict(Quarantined=-1, OldestQuarantineSeconds=0, MaintenanceFailure=None)))
        self.assertTrue(cleanup_uncertain({}))

    def test_descriptor_sample_does_not_invent_zero(self):
        import os
        counts = descriptor_counts([os.getpid()])
        self.assertIn(os.getpid(), counts)
        if counts[os.getpid()] is not None:
            self.assertGreater(counts[os.getpid()], 0)

    def test_package_identity_rejects_changed_loaded_bytes(self):
        import hashlib
        import zipfile
        with tempfile.TemporaryDirectory() as directory:
            feed = Path(directory)
            with zipfile.ZipFile(feed / 'WeavePort.Sdk.0.5.0.nupkg', 'w') as archive:
                archive.writestr('lib/net10.0/WeavePort.Sdk.dll', b'expected')
            verify_package(feed, 'WeavePort.Sdk', hashlib.sha256(b'expected').hexdigest())
            with self.assertRaises(RuntimeError):
                verify_package(feed, 'WeavePort.Sdk', hashlib.sha256(b'changed').hexdigest())

    def test_control_timeout_is_bounded(self):
        child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], stdout=subprocess.PIPE,
                                 stderr=subprocess.DEVNULL, text=True, start_new_session=True)
        try:
            with self.assertRaises(TimeoutError):
                read_line(child, 0.05)
        finally:
            stop(child)
        self.assertIsNotNone(child.poll())
        child.stdout.close()

    def test_cleanup_reaches_child_after_root_exits(self):
        child = subprocess.Popen([sys.executable, '-c',
            'import subprocess,sys; subprocess.Popen([sys.executable,"-c","import time; time.sleep(60)"]); print("{}",flush=True)'],
            stdout=subprocess.PIPE, text=True, start_new_session=True)
        read_line(child)
        child.wait(timeout=5)
        self.assertTrue(stop(child))
        import time
        time.sleep(0.1)
        self.assertFalse(any(row['group'] == child.pid for row in process_table()))
        child.stdout.close()

    def test_resource_accounting_retains_first_last_and_peak(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'resources.jsonl'
            path.write_text('\n'.join(json.dumps({'processes': [{'rssKiB': value}]}) for value in (1024, 4096, 2048)))
            result = summarize_resources(path)
            self.assertEqual(result['samples'], 3)
            self.assertEqual(result['peakSummedRssMiB'], 4)
            self.assertEqual(result['last']['processes'][0]['rssKiB'], 2048)


if __name__ == '__main__':
    unittest.main()
