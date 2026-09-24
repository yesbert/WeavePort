"""Prove architecture guards reject representative regressions."""
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("architecture", ROOT / "scripts/check-architecture.py")
architecture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(architecture)


class ArchitectureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in ("src", "sdks/python/weaveport_sdk", "sdks/typescript/src"):
            shutil.copytree(ROOT / name, self.root / name, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
        (self.root / "docs").mkdir()
        for name in ("Directory.Packages.props", "docs/runtime-diagnostics.md"):
            shutil.copy2(ROOT / name, self.root / name)

    def change(self, name, old, new):
        path = self.root / name
        path.write_text(path.read_text().replace(old, new))

    def test_current_boundaries(self):
        self.assertEqual(architecture.check(self.root), [])

    def test_outward_dependency(self):
        self.change("src/WeavePort.Abstractions/WeavePort.Abstractions.csproj", "</Project>",
                    '<ItemGroup><ProjectReference Include="../WeavePort.Hosting/WeavePort.Hosting.csproj" /></ItemGroup></Project>')
        self.assertTrue(any("Outward dependency" in error for error in architecture.check(self.root)))

    def test_facade_implementation(self):
        with (self.root / "sdks/typescript/src/index.ts").open("a") as output:
            output.write("\nclass Runtime {}\n")
        self.assertTrue(any("facade contains" in error for error in architecture.check(self.root)))

    def test_scattered_version(self):
        self.change("src/WeavePort.Hosting/WeavePort.Hosting.csproj", 'Include="Tomlyn"', 'Include="Tomlyn" Version="0.19.0"')
        self.assertTrue(any("Scattered package" in error for error in architecture.check(self.root)))

    def test_duplicate_log_id(self):
        self.change("src/WeavePort.Hosting/Diagnostics/RuntimeLogEvents.cs", "= 1002", "= 1001")
        self.assertTrue(any("duplicate logging" in error for error in architecture.check(self.root)))


if __name__ == "__main__":
    unittest.main()
