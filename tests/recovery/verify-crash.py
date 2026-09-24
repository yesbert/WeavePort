"""Kill an isolated test coordinator; stored PIDs never grant production kill authority."""

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


def wait_until(predicate, seconds=8):
    deadline = time.monotonic() + seconds
    while not predicate():
        if time.monotonic() >= deadline:
            raise TimeoutError("Test barrier timed out")
        time.sleep(0.02)


def running(pid):
    result = subprocess.run(
        ["ps", "-p", str(pid), "-o", "stat="], text=True, capture_output=True, timeout=3
    )
    return (
        result.returncode == 0
        and result.stdout.strip()
        and not result.stdout.strip().startswith("Z")
    )


def stop_owned_group(child):
    # Authority comes only from the new POSIX session created by this test.
    try:
        os.killpg(child.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


class CrashScenario:
    def __init__(self, root, dotnet):
        self.work = root / "artifacts/recovery-tests" / uuid.uuid4().hex
        local = self.work / "installation"
        shutil.copytree(
            root / "artifacts/appointment-desk/releases", local / "releases"
        )
        (local / "active-version.txt").write_text("1\n")
        self.store = self.work / "calendar"
        self.barrier = self.work / "booked.json"
        self.command = [
            dotnet,
            str(root / "artifacts/appointment-desk/host/AppointmentDesk.Host.dll"),
        ]
        self.env = dict(
            os.environ, WP_APPOINTMENT_ROOT=str(local), WP_APPOINTMENT_DOTNET=dotnet
        )
        self.checks = []
        self.child = None
        self.cleanup_completed = False

    def check(self, condition, label):
        assert condition, label
        self.checks.append(label)
        print("PASS: " + label, flush=True)

    def run_coordinator(self, *args, expected=0):
        result = subprocess.run(
            self.command + ["--store", str(self.store), *args],
            env=self.env,
            text=True,
            capture_output=True,
            timeout=25,
        )
        assert result.returncode == expected, (
            result.returncode,
            result.stdout,
            result.stderr,
        )
        return result

    def barrier_ready(self):
        if self.child.poll() is not None:
            return True
        try:
            json.loads(self.barrier.read_text())
            return True
        except (FileNotFoundError, json.JSONDecodeError):
            return False

    def run(self):
        with (self.work / "child.stdout").open("w") as output, (
            self.work / "child.stderr"
        ).open("w") as errors:
            self.child = subprocess.Popen(
                self.command
                + [
                    "--store",
                    str(self.store),
                    "--request",
                    "crash",
                    "--tenant",
                    "a",
                    "--hold-after-booking",
                    str(self.barrier),
                ],
                env=self.env,
                stdout=output,
                stderr=errors,
                start_new_session=True,
            )
            try:
                self.capture_booking()
                self.verify_crash_and_blocked_restart()
                self.recover_owned_generation()
                self.verify_recovered_booking()
                self.write_evidence()
            finally:
                if not self.cleanup_completed:
                    stop_owned_group(self.child)
                self.child.wait(timeout=5)

    def capture_booking(self):
        wait_until(self.barrier_ready)
        assert self.child.poll() is None, "Coordinator exited before crash barrier"
        ready = json.loads(self.barrier.read_text())
        self.worker = int(re.search(r"-p(\d+)$", ready["Instance"]).group(1))
        self.check(
            os.getpgid(self.worker) == self.child.pid,
            "fixture worker belongs to the newly created test process group",
        )
        self.calendar = self.store / "calendar.json"
        self.baseline = self.calendar.read_bytes()
        self.recorded = json.loads(self.baseline)["Entries"][0]
        self.check(
            self.recorded["Outcome"]["Status"] == "booked",
            "booking is committed before coordinator termination",
        )
        self.marker = self.store / "runtime/run.json"
        self.original_marker = self.marker.read_bytes()
        self.generation = json.loads(self.original_marker)["Generation"]
        self.workspace = self.store / "runtime/workers" / self.generation
        self.check(
            any(self.workspace.iterdir()), "live run owns a generation workspace"
        )

    def verify_crash_and_blocked_restart(self):
        # Kill only the coordinator root to exercise an actual unclean shutdown.
        self.child.kill()
        self.child.wait(timeout=5)
        self.check(
            self.child.returncode == -signal.SIGKILL,
            "actual coordinator root was terminated without graceful shutdown",
        )
        self.observed_worker_running = bool(running(self.worker))
        blocked = self.run_coordinator(
            "--request", "crash", "--tenant", "a", expected=1
        )
        self.check(
            "Native startup blocked" in blocked.stderr,
            "fresh coordinator refuses an unclean run marker",
        )
        self.check(
            self.calendar.read_bytes() == self.baseline
            and self.marker.read_bytes() == self.original_marker,
            "blocked restart preserves exact booking and marker bytes",
        )
        self.check(
            any(self.workspace.iterdir()),
            "coordinator death leaves workspace evidence rather than confirming cleanup",
        )

    def recover_owned_generation(self):
        stop_owned_group(self.child)
        wait_until(lambda: not running(self.worker))
        self.check(
            not running(self.worker),
            "supervised test cleanup confirms the cooperative fixture worker is no longer running",
        )
        self.cleanup_completed = True
        quarantine = self.work / "quarantine"
        quarantine.mkdir()
        shutil.move(str(self.workspace), str(quarantine / self.generation))
        shutil.move(str(self.marker), str(quarantine / "run.json"))
        self.check(
            (quarantine / "run.json").read_bytes() == self.original_marker,
            "operator recovery archives old generation evidence without resetting domain state",
        )

    def verify_recovered_booking(self):
        self.run_coordinator("--request", "crash", "--tenant", "a")
        self.check(
            self.calendar.read_bytes() == self.baseline,
            "exact request after supervised recovery returns original booking without duplication",
        )
        self.check(
            not self.marker.exists(),
            "successful recovered CLI clears only its clean run marker",
        )
        self.run_coordinator("--request", "crash", "--tenant", "b")
        entries = json.loads(self.calendar.read_text())["Entries"]
        self.check(
            len(entries) == 2
            and entries[0] == self.recorded
            and entries[1]["Outcome"]["BookingId"]
            != self.recorded["Outcome"]["BookingId"],
            "another customer gets an independent booking after recovery",
        )

    def write_evidence(self):
        (self.work / "result.json").write_text(
            json.dumps(
                dict(
                    passed=len(self.checks),
                    checks=self.checks,
                    workerRunningImmediatelyAfterCrash=self.observed_worker_running,
                    scope="macOS cooperative native fixture; external process-group supervision",
                    generation=self.generation,
                ),
                indent=2,
            )
        )
        print(
            f"Verification passed: {len(self.checks)} assertions. Evidence: {self.work}"
        )


def main():
    if sys.platform != "darwin":
        raise SystemExit(
            "This reference crash qualification currently requires native macOS."
        )
    CrashScenario(Path(__file__).resolve().parents[2], sys.argv[1]).run()


if __name__ == "__main__":
    main()
