"""Own one bounded density process and its asynchronous memory observer."""

from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import signal
import subprocess
import time

import docker_memory
from density_support import ROOT, command, pressure, refresh_known, write_json


class DockerSamples:
    def __init__(self, folder, enabled):
        self.folder = folder
        self.engine = docker_memory.Engine() if enabled else None
        self.pool = ThreadPoolExecutor(max_workers=1)
        self.pending = None
        self.latest = None
        self.started_at = 0

    def record(self, sample):
        with (self.folder / "docker-memory.jsonl").open("a") as stream:
            stream.write(json.dumps(sample) + "\n")

    def poll(self, known):
        if self.engine is None:
            return None
        self.collect_ready()
        if self.pending is not None and time.monotonic() - self.started_at > 15:
            raise TimeoutError("Docker memory snapshot exceeded 15 seconds")
        if self.pending is None and time.monotonic() - self.started_at >= 2:
            self.started_at = time.monotonic()
            self.pending = self.pool.submit(
                docker_memory.snapshot, self.engine, known.copy()
            )
        if self.latest is None:
            return None
        result = {key: value for key, value in self.latest.items() if key != "rows"}
        result["ageSeconds"] = time.time() - self.latest["at"]
        return result

    def collect_ready(self):
        if self.pending is None or not self.pending.done():
            return
        self.latest = self.pending.result()
        self.record(self.latest)
        self.pending = None

    def finish(self):
        self.pool.shutdown(wait=True, cancel_futures=True)
        if self.pending is None:
            return None
        sample = self.pending.result()
        self.record(sample)
        return sample


class StageSupervisor:
    def __init__(self, config, baseline):
        self.config = config
        self.baseline = baseline
        self.folder = Path(config["Output"])
        self.known = set()
        self.samples = []
        self.position = 0
        self.reason = None
        self.stop_at = None

    def sample(self, observer, deadline):
        sample = {"at": time.time(), "pressure": pressure()}
        self.position = refresh_known(self.folder, self.known, self.position)
        memory = observer.poll(self.known)
        if memory is not None:
            sample["docker"] = memory
        self.samples.append(sample)
        write_json(self.folder / "supervisor.json", self.samples)
        self.check_limits(sample, deadline)

    def check_limits(self, sample, deadline):
        current = sample["pressure"]
        if (
            current["freePercent"] < 10
            or current["swapUsedMiB"] is None
            or current["swapUsedMiB"] - self.baseline["swapUsedMiB"] > 512
        ):
            self.reason = "system-headroom"
        if (
            sample.get("docker", {}).get("ownedRawMiB", 0)
            > self.config["MemoryBudgetMiB"]
        ):
            self.reason = "owned-container-memory"
        if (self.folder.parent / "STOP").exists():
            self.reason = "operator-stop"
        if time.monotonic() > deadline:
            self.reason = "wall-deadline"

    def observe(self, process, observer):
        deadline = (
            time.monotonic() + self.config["Seconds"] + self.config["Clients"] * 2 + 90
        )
        while process.poll() is None:
            try:
                self.sample(observer, deadline)
            except Exception as error:
                self.reason = "observer-" + type(error).__name__ + ": " + str(error)
            if self.stop_if_required(process):
                break
            time.sleep(0.5)

    def stop_if_required(self, process):
        if self.reason and self.stop_at is None:
            (self.folder / "STOP").touch()
            self.stop_at = time.monotonic()
        if self.stop_at is None or time.monotonic() - self.stop_at <= 30:
            return False
        # Only this explicitly owned process group, never unrelated Docker services.
        os.killpg(process.pid, signal.SIGTERM)
        process.wait(timeout=15)
        self.reason += "-forced-termination"
        return True

    def finish_observation(self, observer):
        try:
            sample = observer.finish()
            if sample is not None:
                self.samples.append({"at": time.time(), "docker": sample})
        except Exception as error:
            self.reason = self.reason or "observer-" + type(
                error
            ).__name__ + ": " + str(error)
        self.position = refresh_known(self.folder, self.known, self.position)
        if observer.engine and not any(
            sample.get("docker", {}).get("owned") for sample in self.samples
        ):
            self.reason = self.reason or "no-owned-container-memory-observed"

    def report(self, process):
        result_path = self.folder / "result.json"
        result = (
            json.loads(result_path.read_text())
            if result_path.exists()
            else {"passed": False, "failure": "missing-result"}
        )
        remaining = self.remaining_containers()
        owned = [
            sample["docker"]
            for sample in self.samples
            if sample.get("docker", {}).get("owned")
        ]
        result["supervisor"] = {
            "reason": self.reason,
            "exitCode": process.returncode,
            "remainingKnownContainers": remaining,
            "peakSampledContainerMiB": max(
                (sample["ownedMiB"] for sample in owned), default=None
            ),
            "peakSampledContainerRawMiB": max(
                (sample["ownedRawMiB"] for sample in owned), default=None
            ),
            "maxDockerSampleAgeSeconds": max(
                (
                    sample.get("docker", {}).get("ageSeconds", 0)
                    for sample in self.samples
                ),
                default=None,
            ),
            "samples": len(self.samples),
        }
        result["passed"] = (
            result["passed"]
            and process.returncode == 0
            and self.reason is None
            and not remaining
        )
        write_json(result_path, result)
        return result

    def remaining_containers(self):
        if self.config["Adapter"] != "docker":
            return []
        return sorted(
            self.known.intersection(
                command("docker", "ps", "-a", "--format", "{{.Names}}").splitlines()
            )
        )

    def run(self):
        self.folder.mkdir(parents=True)
        write_json(self.folder / "config.json", self.config)
        observer = DockerSamples(self.folder, self.config["Adapter"] == "docker")
        try:
            with (self.folder / "console.log").open("w") as log:
                process = subprocess.Popen(
                    [
                        "dotnet",
                        str(
                            ROOT
                            / "benchmarks/WeavePort.Density/bin/Release/net10.0/WeavePort.Density.dll"
                        ),
                        str(self.folder / "config.json"),
                    ],
                    cwd=ROOT,
                    stdout=log,
                    stderr=subprocess.STDOUT,
                    start_new_session=True,
                )
                self.observe(process, observer)
        finally:
            self.finish_observation(observer)
        return self.report(process)


def run_stage(config, baseline):
    return StageSupervisor(config, baseline).run()
