"""Kill an isolated test coordinator; never infer production kill authority from stored PIDs."""
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import uuid

if sys.platform != "darwin":
    raise SystemExit("This reference crash qualification currently requires native macOS.")
root = Path.cwd()
work = root / "artifacts" / "recovery-tests" / uuid.uuid4().hex
local = work / "installation"
shutil.copytree(root / "artifacts/appointment-desk/releases", local / "releases")
(local / "active-version.txt").write_text("1\n")
store = work / "calendar"
barrier = work / "booked.json"
command = [sys.argv[1], str(root / "artifacts/appointment-desk/host/AppointmentDesk.Host.dll")]
env = dict(os.environ, WP_APPOINTMENT_ROOT=str(local), WP_APPOINTMENT_DOTNET=sys.argv[1])
checks = []

def check(condition, label):
    assert condition, label
    checks.append(label)
    print("PASS: " + label, flush=True)

def run(*args, expected=0):
    result = subprocess.run(command + ["--store", str(store), *args], env=env,
                            text=True, capture_output=True, timeout=25)
    assert result.returncode == expected, (result.returncode, result.stdout, result.stderr)
    return result

def wait_until(predicate, seconds=8):
    deadline = time.monotonic() + seconds
    while not predicate():
        if time.monotonic() >= deadline:
            raise TimeoutError("Test barrier timed out")
        time.sleep(0.02)

def barrier_ready():
    if child.poll() is not None:
        return True
    try:
        json.loads(barrier.read_text())
        return True
    except (FileNotFoundError, json.JSONDecodeError):
        return False

def running(pid):
    result = subprocess.run(["ps", "-p", str(pid), "-o", "stat="], text=True, capture_output=True, timeout=3)
    return result.returncode == 0 and result.stdout.strip() and not result.stdout.strip().startswith("Z")

with (work / "child.stdout").open("w") as output, (work / "child.stderr").open("w") as errors:
    child = subprocess.Popen(command + ["--store", str(store), "--request", "crash", "--tenant", "a",
                                       "--hold-after-booking", str(barrier)], env=env,
                             stdout=output, stderr=errors, start_new_session=True)
    cleanup_completed = False
    try:
        wait_until(barrier_ready)
        assert child.poll() is None, "Coordinator exited before crash barrier"
        ready = json.loads(barrier.read_text())
        worker = int(re.search(r"-p(\d+)$", ready["Instance"]).group(1))
        check(os.getpgid(worker) == child.pid, "fixture worker belongs to the newly created test process group")
        calendar = store / "calendar.json"
        baseline = calendar.read_bytes()
        recorded = json.loads(baseline)["Entries"][0]
        check(recorded["Outcome"]["Status"] == "booked", "booking is committed before coordinator termination")
        marker = store / "runtime/run.json"
        original_marker = marker.read_bytes()
        generation = json.loads(original_marker)["Generation"]
        workspace = store / "runtime/workers" / generation
        check(any(workspace.iterdir()), "live run owns a generation workspace")
        child.kill()  # Only the coordinator root; deliberately no process-tree termination.
        child.wait(timeout=5)
        check(child.returncode == -signal.SIGKILL, "actual coordinator root was terminated without graceful shutdown")
        observed_worker_running = bool(running(worker))
        blocked = run("--request", "crash", "--tenant", "a", expected=1)
        check("Native startup blocked" in blocked.stderr, "fresh coordinator refuses an unclean run marker")
        check(calendar.read_bytes() == baseline and marker.read_bytes() == original_marker,
              "blocked restart preserves exact booking and marker bytes")
        check(any(workspace.iterdir()), "coordinator death leaves workspace evidence rather than confirming cleanup")
        # This authority comes from launching this isolated POSIX session, not from a stale marker PID.
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        wait_until(lambda: not running(worker))
        check(not running(worker), "supervised test cleanup confirms the cooperative fixture worker is no longer running")
        cleanup_completed = True
        quarantine = work / "quarantine"
        quarantine.mkdir()
        shutil.move(str(workspace), str(quarantine / generation))
        shutil.move(str(marker), str(quarantine / "run.json"))
        check((quarantine / "run.json").read_bytes() == original_marker,
              "operator recovery archives old generation evidence without resetting domain state")
        run("--request", "crash", "--tenant", "a")
        check(calendar.read_bytes() == baseline, "exact request after supervised recovery returns original booking without duplication")
        check(not marker.exists(), "successful recovered CLI clears only its clean run marker")
        run("--request", "crash", "--tenant", "b")
        entries = json.loads(calendar.read_text())["Entries"]
        check(len(entries) == 2 and entries[0] == recorded and entries[1]["Outcome"]["BookingId"] != recorded["Outcome"]["BookingId"],
              "another customer gets an independent booking after recovery")
        (work / "result.json").write_text(json.dumps(dict(
            passed=len(checks), checks=checks, workerRunningImmediatelyAfterCrash=observed_worker_running,
            scope="macOS cooperative native fixture; external process-group supervision", generation=generation), indent=2))
        print(f"Verification passed: {len(checks)} assertions. Evidence: {work}")
    finally:
        if not cleanup_completed:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        child.wait(timeout=5)
