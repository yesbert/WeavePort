"""Exercise review failures on copies; never rewrite approved package/API baselines."""

import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile


def verify_api_drift(root, work, dotnet):
    baseline = root / "compatibility/public-api.txt"
    original = baseline.read_bytes()
    changed = work / "changed-api.txt"
    changed.write_bytes(original + b"review-required\n")
    result = subprocess.run(
        [
            dotnet,
            str(root / "tests/compatibility/bin/Release/net10.0/Compatibility.dll"),
            str(root),
            "--baseline",
            str(changed),
        ],
        capture_output=True,
        text=True,
    )
    assert result.returncode != 0 and "API matches reviewed baseline" in result.stderr
    assert (
        baseline.read_bytes() == original
        and changed.read_bytes() == original + b"review-required\n"
    )
    print(
        "PASS: API drift fails without rewriting either reviewed or candidate baseline"
    )


def verify_dependency_drift(root, work):
    packages = work / "packages"
    packages.mkdir()
    policy = json.loads((root / "compatibility/local-v1.json").read_text())
    versions = dict(policy["HostPackages"])
    sdk = policy["AuthorSdks"]["dotnet"]
    versions[sdk["Package"]] = sdk["Version"]
    for name, version in versions.items():
        filename = f"{name}.{version}.nupkg"
        shutil.copyfile(root / "artifacts/packages" / filename, packages / filename)
    path = packages / f"WeavePort.Hosting.{versions['WeavePort.Hosting']}.nupkg"
    with zipfile.ZipFile(path) as archive:
        content = {name: archive.read(name) for name in archive.namelist()}
    name = next(name for name in content if name.endswith(".nuspec"))
    metadata = ET.fromstring(content[name])
    metadata.find(".//{*}dependency").set("version", "9.0.0")
    content[name] = ET.tostring(metadata)
    with zipfile.ZipFile(path, "w") as archive:
        for name, data in content.items():
            archive.writestr(name, data)
    result = subprocess.run(
        [sys.executable, "tests/compatibility/check-packages.py", str(packages)],
        capture_output=True,
        text=True,
    )
    assert result.returncode != 0 and "dependency closure" in result.stderr
    print("PASS: an actual packed dependency mismatch fails metadata verification")


def main():
    root = Path(__file__).resolve().parents[2]
    work = root / "artifacts/compatibility" / ("negative-" + uuid.uuid4().hex)
    work.mkdir(parents=True)
    verify_api_drift(root, work, sys.argv[1])
    verify_dependency_drift(root, work)
    print(f"Verification passed: 2 negative review-gate assertions. Evidence: {work}")


if __name__ == "__main__":
    main()
