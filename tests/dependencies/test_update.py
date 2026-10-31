"""Dependency updater safety checks, independent of live registries."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/dependencies"))
from inventory import Dependency, discover_file, maintained, workflow_dependencies
from registry import version_key
from run import target
from update import apply, rewrite


class UpdateTests(unittest.TestCase):
    def test_numeric_stable_order(self):
        self.assertGreater(version_key("2.10.1"), version_key("2.9.9"))
        with self.assertRaises(ValueError):
            version_key("3.0.0-preview.1")

    def test_pin_repairs_drift_and_reports_reason(self):
        item = Dependency("nuget", "Example", "2.0.0", "Directory.Packages.props", "nuget")
        pins = [{"registry": "nuget", "package": "Example", "version": "1.0.0", "reason": "compatibility", "review_when": "new API supported"}]
        version, reason = target(item, "3.0.0", pins)
        self.assertEqual("1.0.0", version)
        self.assertIn("compatibility", reason)
        with self.assertRaises(ValueError):
            target(item, "1.0.0", [])

    def test_local_npm_reference_is_not_updated(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "package.json").write_text(json.dumps({"dependencies": {"local": "file:../sdk.tgz", "external": "1.0.0"}}))
            result = discover_file(root, "package.json")
            self.assertEqual(["external"], [item.package for item in result])
        self.assertFalse(maintained("reports/release/package.json"))
        self.assertFalse(maintained("tests/example/fixtures/package.json"))

    def test_failed_npm_resolution_restores_all_manifest_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = '{"dependencies":{"example":"1.0.0"}}\n'
            (root / "package.json").write_text(package)
            (root / "package-lock.json").write_text("old lock\n")
            item = Dependency("npm", "example", "1.0.0", "package.json", "npm", "dependencies")
            with patch("update.subprocess.run", side_effect=subprocess.CalledProcessError(1, "npm")):
                with self.assertRaises(subprocess.CalledProcessError):
                    apply(root, [(item, "2.0.0", "latest stable")])
            self.assertEqual(package, (root / "package.json").read_text())
            self.assertEqual("old lock\n", (root / "package-lock.json").read_text())

    def test_xml_update_preserves_other_packages_and_formatting(self):
        text = '<PackageVersion Include="Example" Version="1.0.0" />\n<PackageVersion Include="Other" Version="1.0.0" />\n'
        item = Dependency("nuget", "Example", "1.0.0", "Directory.Packages.props", "nuget")
        self.assertEqual(text.replace('Example" Version="1.0.0', 'Example" Version="2.0.0'), rewrite(text, item, "2.0.0"))
        with self.assertRaises(ValueError):
            rewrite(text + text, item, "2.0.0")

    def test_workflow_prerelease_is_not_silently_truncated(self):
        source = "dotnet tool install example --version 1.2.3-preview.1\nnpm install --global @scope/tool@1.2.3-beta.2"
        items = workflow_dependencies(".github/workflows/test.yml", source)
        self.assertEqual(["1.2.3-preview.1", "1.2.3-beta.2"], [item.version for item in items])
        for item in items:
            with self.assertRaises(ValueError):
                target(item, "2.0.0", [])

    def test_portable_checks_follow_sdk_build(self):
        from run import tests
        with patch("run.execute") as execute:
            tests(ROOT)
        commands = [call.args[1] for call in execute.call_args_list]
        self.assertLess(commands.index(["npm", "test", "--prefix", "sdks/typescript"]),
                        commands.index(["./scripts/verify-portable.sh"]))

    def test_fresh_example_resolution_waits_for_sdk_archive(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for project in ("sdks/typescript", "examples/sdk/typescript"):
                (root / project).mkdir(parents=True)
                (root / project / "package.json").write_text('{"version":"0.4.1","devDependencies":{"typescript":"7.0.2"}}')
            plan = [(Dependency("npm", "typescript", "7.0.2", project + "/package.json", "npm", "devDependencies"), "7.0.2", "latest stable")
                    for project in ("sdks/typescript", "examples/sdk/typescript")]
            events = []
            archive = root / "artifacts/sdk-packages-js/weaveport-sdk-0.4.1.tgz"
            def simulate(command, cwd, check):
                events.append(command[:2])
                if command[:2] == ["npm", "pack"]:
                    self.assertIn(["npm", "ci"], events)
                    self.assertIn(["npm", "run"], events)
                    archive.parent.mkdir(parents=True, exist_ok=True)
                    archive.write_bytes(b"fresh SDK archive")
                    return
                if cwd == root / "examples/sdk/typescript":
                    self.assertTrue(archive.exists(), "consumer resolved before archive preparation")
            with patch("update.subprocess.run", side_effect=simulate):
                apply(root, plan)

    def test_dry_run_never_calls_apply_or_verification(self):
        from run import main
        with patch("run.arguments") as arguments, patch("run.resolve", return_value=[]), patch("run.apply") as mutation, patch("run.execute") as command:
            arguments.return_value.dry_run = True
            main()
            mutation.assert_not_called()
            command.assert_not_called()


if __name__ == "__main__":
    unittest.main()
