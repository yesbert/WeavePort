"""Exercise review failures on copies; never rewrite approved package/API baselines."""
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile

root = Path.cwd()
work = root / "artifacts/compatibility" / ("negative-" + uuid.uuid4().hex)
work.mkdir()
baseline = root / "compatibility/public-api.txt"
original = baseline.read_bytes()
changed = work / "changed-api.txt"
changed.write_bytes(original + b"review-required\n")
result = subprocess.run([sys.argv[1], str(root / "tests/compatibility/bin/Release/net10.0/Compatibility.dll"), str(root), "--baseline", str(changed)], capture_output=True, text=True)
assert result.returncode != 0 and "API matches reviewed baseline" in result.stderr
assert baseline.read_bytes() == original and changed.read_bytes() == original + b"review-required\n"
print("PASS: API drift fails without rewriting either reviewed or candidate baseline")
packages = work / "packages"
packages.mkdir()
for name in ("Abstractions", "Hosting", "Sdk.Client", "Sdk"):
    shutil.copyfile(root / f"artifacts/packages/WeavePort.{name}.0.3.0.nupkg", packages / f"WeavePort.{name}.0.3.0.nupkg")
path = packages / "WeavePort.Hosting.0.3.0.nupkg"
with zipfile.ZipFile(path) as archive:
    content = {name: archive.read(name) for name in archive.namelist()}
name = next(name for name in content if name.endswith(".nuspec"))
metadata = ET.fromstring(content[name])
metadata.find(".//{*}dependency").set("version", "9.0.0")
content[name] = ET.tostring(metadata)
with zipfile.ZipFile(path, "w") as archive:
    for name, data in content.items():
        archive.writestr(name, data)
result = subprocess.run([sys.executable, "tests/compatibility/check-packages.py", str(packages)], capture_output=True, text=True)
assert result.returncode != 0 and "dependency closure" in result.stderr
print("PASS: an actual packed dependency mismatch fails metadata verification")
print(f"Verification passed: 2 negative review-gate assertions. Evidence: {work}")
