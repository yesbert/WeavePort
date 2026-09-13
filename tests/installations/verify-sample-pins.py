"""Separate coordinator processes against isolated copies; no normal sample state is mutated."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

root = Path.cwd()
work = root / "artifacts" / "installation-tests" / ("sample-pins-" + uuid.uuid4().hex)
work.mkdir(parents=True)
count = 0

def check(condition, label):
    global count
    assert condition, label
    count += 1
    print("PASS: " + label)

def setup(slug, app, prefix):
    local = work / slug
    shutil.copytree(root / "artifacts" / slug / "releases", local / "releases")
    (local / "active-version.txt").write_text("1\n")
    env = dict(os.environ, **{prefix + "_ROOT": str(local), prefix + "_DOTNET": sys.argv[1]})
    def run(*args, expected=0):
        result = subprocess.run([sys.argv[1], str(root / "artifacts" / slug / "host" / (app + ".Host.dll")), *args], env=env, text=True, capture_output=True)
        assert result.returncode == expected, (args, result.returncode, result.stdout, result.stderr)
        return result
    return local, run

local, run = setup("appointment-desk", "AppointmentDesk", "WP_APPOINTMENT")
run("--request", "pinned", "--lose-response", expected=2)
store = local / "calendar" / "calendar.json"
baseline = store.read_bytes()
manifest = local / "releases" / "1" / "installation.json"
approved = manifest.read_bytes()
manifest.write_bytes(approved + b" ")
run("--request", "pinned", expected=1)
check(store.read_bytes() == baseline, "changed pinned manifest refuses action recovery without overwriting effect")
changed = json.loads(approved)
changed["Compatibility"]["HostApi"] = 99
manifest.write_text(json.dumps(changed))
run("--request", "pinned", expected=1)
check(store.read_bytes() == baseline, "incompatible host API refuses recovery without overwriting booking")
manifest.write_bytes(approved)
run("--activate", "2")
run("--request", "pinned")
check(store.read_bytes() == baseline, "fresh CLI under v2 default reconciles exact v1 booking")
run("--request", "pinned", "--version", "2", expected=1)
check(store.read_bytes() == baseline, "explicit conflicting CLI selection leaves booking unchanged")
binary = local / "releases" / "1" / "AppointmentDesk.Worker.dll"
saved = binary.read_bytes()
binary.unlink()
run("--request", "pinned", expected=1)
check(store.read_bytes() == baseline, "missing selected action release cannot fall forward or overwrite state")
binary.write_bytes(saved)
legacy = local / "legacy"
legacy.mkdir()
legacy_file = legacy / "calendar.json"
legacy_file.write_text(json.dumps(dict(Schema=1, Entries=[])))
old = legacy_file.read_bytes()
run("--store", str(legacy), expected=1)
check(legacy_file.read_bytes() == old, "legacy calendar schema is refused unchanged")

local, run = setup("document-workshop", "DocumentWorkshop", "WP_DOCUMENT")
run("--version", "2")
result = next((local / "documents").glob("*.ndjson"))
header = json.loads(result.read_text().splitlines()[0])
check(header["installation"]["version"] == "2", "explicit reader release is retained in committed metadata")
manifest = local / "releases" / "1" / "installation.json"
data = json.loads(manifest.read_text())
data["Contract"] = "document-workshop/v999"
manifest.write_text(json.dumps(data))
failed_store = local / "refused"
run("--store", str(failed_store), expected=1)
check(not failed_store.exists(), "incompatible reader installation refuses before staging")
(work / "result.txt").write_text(f"{count} assertions passed\n")
print(f"Verification passed: {count} assertions. Evidence: {work}")
