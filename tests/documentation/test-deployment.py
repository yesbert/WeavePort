"""Reject unsafe deployments and preserve the previous release on validation failure."""

import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "receiver", Path(__file__).resolve().parents[2] / "scripts/ci/receive-site.py"
)
receiver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(receiver)


def archive(extra=None, run=1):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz") as tar:
        files = {
            name: b"text"
            for name in (
                "index.html",
                "llms.txt",
                "llms-full.txt",
                "docs/ai-documentation.md",
            )
        }
        files["deployment.json"] = json.dumps(
            {"revision": "a" * 40, "run": run}
        ).encode()
        for name, data in files.items():
            item = tarfile.TarInfo(name)
            item.size = len(data)
            tar.addfile(item, io.BytesIO(data))
        if extra:
            tar.addfile(extra, io.BytesIO(b""))
    buffer.seek(0)
    return buffer


class DeploymentTests(unittest.TestCase):
    def test_activation_and_rollback_preservation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            receiver.receive(root, archive(run=2))
            old = (root / "current").resolve()
            receiver.receive(root, archive(run=2))
            self.assertEqual(old, (root / "current").resolve())
            with self.assertRaises(ValueError):
                receiver.receive(root, archive(run=1))
            self.assertEqual(old, (root / "current").resolve())
            receiver.receive(root, archive(run=3))
            self.assertTrue(old.is_dir())
            self.assertNotEqual(old, (root / "current").resolve())

    def test_path_links_and_server_config_are_rejected(self):
        for name, kind in [
            ("../escape.txt", tarfile.REGTYPE),
            ("link.md", tarfile.SYMTYPE),
            (".htaccess", tarfile.REGTYPE),
            ("execute.php", tarfile.REGTYPE),
        ]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                item = tarfile.TarInfo(name)
                item.type = kind
                item.linkname = "/tmp"
                with self.assertRaises(ValueError):
                    receiver.receive(Path(directory), archive(item))
                self.assertFalse((Path(directory) / "current").exists())


if __name__ == "__main__":
    unittest.main()
