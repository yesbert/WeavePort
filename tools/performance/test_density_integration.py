"""Negative controls for a previously built density harness; never run beside timed measurements."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
HARNESS = ROOT / "benchmarks/WeavePort.Density/bin/Release/net10.0/WeavePort.Density.dll"


class HarnessControls(unittest.TestCase):
    def run_case(self, **overrides):
        with tempfile.TemporaryDirectory(prefix="wp-density-control-") as directory:
            root = Path(directory)
            config = dict(Root=str(ROOT), Output=str(root), Adapter="native", Mode="scheduled", Language="python",
                          Executable=shutil.which("python3"), Clients=1, Workers=1, Seconds=1, **overrides)
            path = root / "config.json"
            path.write_text(json.dumps(config))
            process = subprocess.run(["dotnet", str(HARNESS), str(path)], cwd=ROOT, capture_output=True, text=True, timeout=30)
            result = json.loads((root / "result.json").read_text())
            self.assertNotEqual(process.returncode, 0)
            self.assertFalse(result["passed"])
            self.assertEqual(result["cleanup"]["Workers"], 0)
            self.assertEqual(result["cleanup"]["Bindings"], 0)
            self.assertFalse(any((root / "workers").iterdir()))
            return result

    def test_resource_guard_cleans_up(self):
        result = self.run_case(MaxHostRssMiB=1)
        self.assertEqual(result["failure"], "resource-or-operator-stop")

    def test_generator_drops_invalidate_capacity(self):
        result = self.run_case(Rate=50000, MaxPending=1)
        self.assertGreater(result["totals"]["Dropped"], 0)
        self.assertEqual(result["totals"]["Success"] + result["totals"]["Failed"] + result["totals"]["Dropped"], 50000)


if __name__ == "__main__":
    unittest.main()
