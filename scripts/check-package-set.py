"""Refuse changed/missing core packages when reusing a fixed candidate feed."""
import hashlib
import json
from pathlib import Path
import sys

policy = json.loads(Path("compatibility/local-v1.json").read_text())
versions = dict(policy["HostPackages"])
sdk = policy["AuthorSdks"]["dotnet"]
versions[sdk["Package"]] = sdk["Version"]
versions.update({name: sdk["Version"] for name in json.loads(Path("build/release-packages.json").read_text())})
versions.update(json.loads(Path("compatibility/external-packages.json").read_text()))
expected = {f"{name}.{version}.nupkg" for name, version in versions.items()}
recorded = json.loads(Path(sys.argv[1]).read_text())
feed = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("artifacts/packages")
if set(recorded) != expected or {p.name for p in feed.glob("*.nupkg")} != expected:
    raise SystemExit("Fixed core package set has unexpected or missing identities.")
for name in sorted(expected):
    if hashlib.sha256((feed / name).read_bytes()).hexdigest() != recorded[name]:
        raise SystemExit("Fixed core package hash mismatch: " + name)
print("Fixed core package set verified.")
