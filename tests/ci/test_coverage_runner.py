"""Real subprocess checks for bounded coverage-suite execution and retained failures."""
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('coverage_runner', ROOT / 'scripts/ci/sonar/coverage.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class CoverageRunnerTests(unittest.TestCase):
    def run_process(self, code, timeout=5):
        with tempfile.TemporaryFile(mode='w+') as log:
            result = runner.run_suite([sys.executable, '-c', code], log, timeout)
            log.seek(0)
            return result, log.read()

    def test_success_keeps_output(self):
        code, output = self.run_process("print('completed')")
        self.assertEqual(code, 0)
        self.assertIn('completed', output)

    def test_failure_is_not_reclassified(self):
        code, _ = self.run_process('raise SystemExit(3)')
        self.assertEqual(code, 3)

    def test_hang_becomes_explicit_bounded_failure(self):
        code, output = self.run_process('import time; time.sleep(60)', .1)
        self.assertEqual(code, 124)
        self.assertIn('watchdog', output)


if __name__ == '__main__':
    unittest.main()
