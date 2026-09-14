"""Release gates reject internal tags, identity drift and changed qualified packages."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("release", Path(__file__).resolve().parents[2] / "scripts/release.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>0.1.0-preview.1</Version></PropertyGroup></Project>")
        (self.root / "LICENSE").write_text("MIT")
        (self.root / "src").mkdir()
        (self.root / "website/assets").mkdir(parents=True)
        (self.root / "website/assets/logo.png").write_bytes(b"fixture logo")
        (self.root / "src/Directory.Build.props").write_text(
            "<Project><PropertyGroup><PackageLicenseExpression>MIT</PackageLicenseExpression></PropertyGroup></Project>")

    def test_tag_contract(self):
        self.assertEqual(release.validate_tag("v0.1.0-preview.1", self.root), "0.1.0-preview.1")
        for tag in ["v0.1.0-internal.2", "v0.1.0", "v0.1.0-preview.01", "v01.1.0", "v0.1.0;echo", "0.1.0-preview.1"]:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.validate_tag(tag, self.root)

    def test_missing_license(self):
        (self.root / "LICENSE").unlink()
        with self.assertRaisesRegex(ValueError, "license"):
            release.validate_tag("v0.1.0-preview.1", self.root)

    def candidate(self, icon=b"fixture logo", include_icon=True):
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        subprocess.run(["git", "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
                        "commit", "--allow-empty", "-qm", "fixture"], cwd=self.root, check=True)
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=self.root, text=True).strip()
        (self.root / "build").mkdir()
        (self.root / "build/release-packages.json").write_text('["WeavePort.Sdk"]')
        candidate = self.root / "candidate"
        feed = candidate / "checkout/artifacts/packages"
        feed.mkdir(parents=True)
        package = feed / "WeavePort.Sdk.0.1.0-preview.1.nupkg"
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr("WeavePort.Sdk.nuspec", '<package><metadata><id>WeavePort.Sdk</id><version>0.1.0-preview.1</version>'
                             '<license type="expression">MIT</license><readme>PACKAGE.md</readme><icon>logo.png</icon>'
                             f'<repository url="https://github.com/yesbert/WeavePort" commit="{commit}"/></metadata></package>')
            archive.writestr("PACKAGE.md", "Fixture")
            if include_icon:
                archive.writestr("logo.png", icon)
        symbols = package.with_suffix(".snupkg")
        symbols.write_bytes(b"fixture symbols")
        manifest = {f"artifacts/packages/{p.name}": release.digest(p) for p in [package, symbols]}
        (candidate / "artifact-manifest.json").write_text(json.dumps(manifest))
        (candidate / "package-set.json").write_text(json.dumps({package.name: release.digest(package)}))
        (candidate / "result.json").write_text(json.dumps({"status": "passed", "sourceCommit": commit,
               "artifactManifestSha256": release.digest(candidate / "artifact-manifest.json")}))
        return candidate, package

    def test_exports_original_bytes_and_rejects_overwrite(self):
        candidate, package = self.candidate()
        output = self.root / "output"
        release.export(candidate, output, "0.1.0-preview.1", self.root)
        self.assertEqual((output / package.name).read_bytes(), package.read_bytes())
        with self.assertRaisesRegex(ValueError, "already exists"):
            release.export(candidate, output, "0.1.0-preview.1", self.root)

    def test_changed_package_refused_before_output(self):
        candidate, package = self.candidate()
        package.write_bytes(package.read_bytes() + b"changed")
        with self.assertRaisesRegex(ValueError, "Package changed"):
            release.export(candidate, self.root / "output", "0.1.0-preview.1", self.root)
        self.assertFalse((self.root / "output").exists())

    def test_missing_icon_refused(self):
        candidate, _ = self.candidate(include_icon=False)
        with self.assertRaisesRegex(ValueError, "Missing packaged project icon"):
            release.export(candidate, self.root / "output", "0.1.0-preview.1", self.root)
        self.assertFalse((self.root / "output").exists())

    def test_different_icon_refused(self):
        candidate, _ = self.candidate(icon=b"old logo")
        with self.assertRaisesRegex(ValueError, "differs from the project logo"):
            release.export(candidate, self.root / "output", "0.1.0-preview.1", self.root)
        self.assertFalse((self.root / "output").exists())

    def test_failed_candidate_refused(self):
        candidate, _ = self.candidate()
        record = json.loads((candidate / "result.json").read_text())
        record["status"] = "failed"
        (candidate / "result.json").write_text(json.dumps(record))
        with self.assertRaisesRegex(ValueError, "must have passed"):
            release.export(candidate, self.root / "output", "0.1.0-preview.1", self.root)

    def test_changed_symbols_refused(self):
        candidate, package = self.candidate()
        package.with_suffix(".snupkg").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "qualified symbols"):
            release.export(candidate, self.root / "output", "0.1.0-preview.1", self.root)

    def test_different_commit_refused(self):
        candidate, _ = self.candidate()
        record = json.loads((candidate / "result.json").read_text())
        record["sourceCommit"] = "0" * 40
        (candidate / "result.json").write_text(json.dumps(record))
        with self.assertRaisesRegex(ValueError, "checked-out commit"):
            release.export(candidate, self.root / "output", "0.1.0-preview.1", self.root)


if __name__ == "__main__":
    unittest.main()
