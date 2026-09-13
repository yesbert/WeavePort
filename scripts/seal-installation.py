#!/usr/bin/env python3
"""Offline first-party build step. Never run against an executing installation."""
import hashlib
import json
import pathlib
import sys
import shutil

root, plugin, version, contract, entry = sys.argv[1:6]
root = pathlib.Path(root)
release = root / "releases" / version
for cache in release.rglob("__pycache__"):
    shutil.rmtree(cache)
runtimes = {"dotnet": sys.argv[6]}
entries = {"dotnet": entry}
if len(sys.argv) > 7:
    runtimes["python"] = sys.argv[7]
    entries["python"] = "plugin.py"
    for path in sorted((root / "python").rglob("*.py")):
        if "weaveport_sdk" in path.parts:
            runtimes["sdk/" + path.relative_to(root / "python").as_posix()] = str(path)

def digest(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()

files = {p.relative_to(release).as_posix(): digest(p) for p in sorted(release.rglob("*"))
         if p.is_file() and p.name != "installation.json" and "__pycache__" not in p.parts}
policy = json.loads((pathlib.Path(__file__).resolve().parent.parent / "compatibility/local-v1.json").read_text())
policy["AuthorSdks"] = {alias: policy["AuthorSdks"][alias] for alias in entries}
manifest = dict(Schema=1, Plugin=plugin, Version=version, Contract=contract,
                EntryPoints=entries, Files=files,
                RuntimeFiles={key: digest(path) for key, path in sorted(runtimes.items())}, Compatibility=policy)
(release / "installation.json").write_text(json.dumps(manifest, indent=2) + "\n")
