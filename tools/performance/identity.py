"""Verify author implementations from the frozen 0.1.0-internal.2 distribution.

The 0.1.0 SDK pins belong to that historical bundle, not the current package feed.
"""

import hashlib
from pathlib import Path
import tarfile
import zipfile


def verify_authors(root, distribution):
    loaded = {}
    wheel = distribution / "packages/python/weaveport_sdk-0.1.0-py3-none-any.whl"
    sites = list((root / "artifacts/sdk-python/lib").glob("python*/site-packages"))
    if len(sites) != 1:
        raise ValueError("Expected one installed Python SDK environment")
    with zipfile.ZipFile(wheel) as archive:
        for name in archive.namelist():
            if not name.startswith("weaveport_sdk/") or name.endswith("/"):
                continue
            verify(sites[0] / name, archive.read(name), "python/" + name, loaded)
    package = distribution / "packages/typescript/weaveport-sdk-0.1.0.tgz"
    with tarfile.open(package) as archive:
        for entry in archive.getmembers():
            if not entry.isfile() or not entry.name.startswith("package/dist/"):
                continue
            name = Path(entry.name).relative_to("package")
            verify(
                root / "examples/sdk/typescript/node_modules/@weaveport/sdk" / name,
                archive.extractfile(entry).read(),
                "typescript/" + str(name),
                loaded,
            )
    if not loaded:
        raise ValueError("No author SDK implementation found")
    return loaded


def verify(path, expected, name, loaded):
    if path.read_bytes() != expected:
        raise ValueError(
            "Installed author SDK differs from qualified distribution: " + name
        )
    loaded[name] = hashlib.sha256(expected).hexdigest()
