"""Own one native gateway/k6 run, including evidence and process-group cleanup."""

import json
import os
from pathlib import Path
import platform
import signal
import subprocess
import tempfile
import time
import uuid

from support import (
    ROOT,
    cleanup_uncertain,
    digest,
    process_table,
    providers,
    read_line,
    sample,
    stop,
    summarize_resources,
    verify_package,
)


def prepare(args, executables):
    output = (
        args.output
        or ROOT
        / "artifacts/soak"
        / (time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:8])
    ).resolve()
    output.mkdir(parents=True, exist_ok=False)
    config = providers(
        output,
        args.clients,
        executables["dotnet"],
        executables["node"],
        args.max_concurrent_starts,
    )
    record = {
        "status": "running",
        "sourceCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
        "settings": {
            key: str(value) if isinstance(value, Path) else value
            for key, value in vars(args).items()
        },
        "platform": platform.platform(),
        "artifacts": {
            str(Path(p).relative_to(ROOT)): h for p, h in config["Artifacts"].items()
        },
        "runtimes": {
            name: subprocess.check_output(
                [path, "version" if name == "k6" else "--version"], text=True
            ).strip()
            for name, path in executables.items()
        },
        "scope": "Native trusted stdio providers, loopback gRPC; sampled owned process-group RSS includes k6, shared pages may repeat; no sandbox or capacity qualification.",
    }
    record["packages"] = {
        p.name: digest(p)
        for p in (ROOT / "artifacts/packages").glob("WeavePort.*.nupkg")
    }
    record["tooling"] = {
        str(p.relative_to(ROOT)): digest(p)
        for p in list((ROOT / "tests/soak").glob("*.js"))
        + list((ROOT / "tools/soak").glob("*.py"))
    }
    (output / "source.patch").write_bytes(
        subprocess.check_output(["git", "diff", "HEAD"], cwd=ROOT)
    )
    return output, config, record


def interrupted(signum, _frame):
    raise KeyboardInterrupt(f"Signal {signum}")


class SoakRun:
    def __init__(self, args, executables):
        self.args, self.executables = args, executables
        self.output, self.config, self.record = prepare(args, executables)
        self.gateway = self.k6 = None
        self.started = time.monotonic()
        self.resources = self.output / "resources.jsonl"
        self.reason = None
        self.baseline_rss = None
        self.forced = {"k6": False, "gateway": False}

    def run(self):
        signal.signal(signal.SIGTERM, interrupted)
        try:
            self.execute()
            self.reason = self.reason or self.verify_outcome()
            self.record["status"] = "passed" if self.reason is None else "failed"
        except BaseException as error:
            self.reason = self.reason or type(error).__name__
            self.record["status"] = "failed"
        finally:
            self.finish()
        return 0 if self.record["status"] == "passed" else 1

    def execute(self):
        with (self.output / "gateway.log").open("w") as gateway_log, (
            self.output / "k6.log"
        ).open("w") as k6_log, self.resources.open("w") as samples:
            private = self.start_gateway(gateway_log)
            with tempfile.TemporaryDirectory(prefix="weaveport-soak-") as private_dir:
                self.start_k6(private, private_dir, k6_log)
                self.monitor(samples)
                self.forced["k6"] = stop(self.k6)
                self.record["k6ExitCode"] = self.k6.returncode
            self.forced["gateway"] = stop(self.gateway, "quit")

    def start_gateway(self, log):
        self.gateway = subprocess.Popen(
            [
                self.executables["dotnet"],
                str(ROOT / "artifacts/sdk-worker-host/WeavePort.WorkerHost.dll"),
            ],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=log,
            text=True,
            bufsize=1,
            start_new_session=True,
            env={"DOTNET_ROLL_FORWARD": "LatestPatch"},
        )
        self.send_gateway(json.dumps(self.config))
        ready = read_line(self.gateway, 30)
        self.record["loaded"] = ready["Loaded"]
        self.record["workerHostSha256"] = digest(
            ROOT / "artifacts/sdk-worker-host/WeavePort.WorkerHost.dll"
        )
        self.send_gateway("warmup")
        warm = read_line(self.gateway, 180)
        if warm.get("warmed") != self.args.clients:
            raise RuntimeError("Incomplete tenant warmup")
        self.record["warmedTenants"] = warm["warmed"]
        for name, checksum in ready["Loaded"].items():
            verify_package(ROOT / "artifacts/packages", name, checksum)
        verify_package(
            ROOT / "artifacts/packages",
            "WeavePort.Sdk",
            digest(ROOT / "artifacts/sdk-csharp/WeavePort.Sdk.dll"),
        )
        credentials = dict(
            zip(
                (binding["Tenant"] for binding in self.config["Bindings"]),
                ready["Credentials"],
            )
        )
        return {
            "address": ready["Address"].removeprefix("http://"),
            "bindings": [
                dict(binding, Credential=credentials[binding["Tenant"]])
                for binding in self.config["Bindings"]
            ],
            "seconds": self.args.seconds,
            "paceMs": self.args.pace_ms,
            "p99Ms": self.args.p99_ms,
            "injectFailure": self.args.inject_failure,
        }

    def send_gateway(self, value):
        self.gateway.stdin.write(value + "\n")
        self.gateway.stdin.flush()

    def start_k6(self, private, private_dir, log):
        secret = Path(private_dir) / "bindings.json"
        secret.write_text(json.dumps(private))
        secret.chmod(0o600)
        env = os.environ.copy()
        env.update(
            WP_SOAK_CONFIG=str(secret),
            WP_SOAK_SUMMARY=str(self.output / "k6-summary.json"),
        )
        self.k6 = subprocess.Popen(
            [
                self.executables["k6"],
                "run",
                "--quiet",
                "--address",
                "127.0.0.1:0",
                str(ROOT / "tests/soak/workload.js"),
            ],
            cwd=ROOT,
            env=env,
            stdout=log,
            stderr=subprocess.STDOUT,
            start_new_session=True,
        )
        print("Soak evidence: " + str(self.output), flush=True)
        self.record["pids"] = {"gateway": self.gateway.pid, "k6": self.k6.pid}
        self.save_record()

    def monitor(self, samples):
        while self.k6.poll() is None:
            if (self.output / "STOP").exists():
                self.reason = "operator-stop"
                break
            if self.gateway.poll() is not None:
                self.reason = "gateway-exited"
                break
            item = sample(self.gateway, self.k6)
            item["elapsedSeconds"] = time.monotonic() - self.started
            samples.write(json.dumps(item) + "\n")
            samples.flush()
            self.reason = self.resource_failure(item)
            if self.reason is not None:
                break
            time.sleep(self.args.sample_seconds)
        if self.reason is None and self.k6.poll() != 0:
            self.reason = "k6-failed"
        self.record["k6ExitCode"] = self.k6.poll()

    def resource_failure(self, item):
        rss = sum(process["rssKiB"] for process in item["processes"]) / 1024
        if rss > self.args.max_rss_mib:
            return "rss-limit"
        if self.growth_exceeded(item["elapsedSeconds"], rss):
            return "rss-growth-limit"
        if cleanup_uncertain(item["gateway"]["snapshot"]):
            return "cleanup-uncertain"
        if item["elapsedSeconds"] > self.args.seconds + 60:
            return "run-deadline"
        return None

    def growth_exceeded(self, elapsed, rss):
        if elapsed < self.args.growth_after:
            return False
        self.baseline_rss = rss if self.baseline_rss is None else self.baseline_rss
        return rss - self.baseline_rss > self.args.max_growth_mib

    def summary_failure(self):
        path = self.output / "k6-summary.json"
        if not path.exists():
            return "missing-k6-summary"
        metrics = json.loads(path.read_text())["metrics"]
        counts = [
            metrics.get(f"completed_operations{{tenant:tenant-{index}}}", {})
            .get("values", {})
            .get("count", 0)
            for index in range(self.args.clients)
        ]
        if any(count <= 0 for count in counts):
            return "unserved-tenant"
        if any(
            not threshold["ok"]
            for metric in metrics.values()
            for threshold in metric.get("thresholds", {}).values()
        ):
            return "failed-threshold"
        return None

    def verify_outcome(self):
        summary_failure = self.summary_failure()
        if summary_failure:
            return summary_failure
        if self.gateway.returncode != 0:
            return "gateway-cleanup-failed"
        workspace = self.output / "workers"
        if workspace.exists() and any(workspace.iterdir()):
            return "retained-workspace"
        if any(
            digest(path) != checksum
            for path, checksum in self.config["Artifacts"].items()
        ):
            return "fixture-changed-during-run"
        if any(
            digest(ROOT / "artifacts/packages" / name) != checksum
            for name, checksum in self.record["packages"].items()
        ):
            return "package-changed-during-run"
        if (
            digest(ROOT / "artifacts/sdk-worker-host/WeavePort.WorkerHost.dll")
            != self.record["workerHostSha256"]
        ):
            return "worker-host-changed-during-run"
        return None

    def cleanup_process(self, name, process, command, errors):
        try:
            self.forced[name] = stop(process, command) or self.forced[name]
        except Exception as error:
            self.forced[name] = True
            errors.append(name + ": " + type(error).__name__)

    def finish(self):
        errors = []
        for name, process, command in (
            ("k6", self.k6, None),
            ("gateway", self.gateway, "quit"),
        ):
            self.cleanup_process(name, process, command, errors)
        self.record["forcedCleanup"] = self.forced
        self.record["cleanupErrors"] = errors
        owned = {
            process.pid for process in (self.gateway, self.k6) if process is not None
        }
        remaining = [row for row in process_table() if row["group"] in owned]
        self.record["remainingProcesses"] = remaining
        if remaining or any(self.forced.values()):
            self.record["status"] = "failed"
            self.reason = self.reason or "incomplete-cleanup"
        self.record["stopReason"] = self.reason or "completed"
        self.record["elapsedSeconds"] = time.monotonic() - self.started
        if self.resources.exists():
            self.record["resources"] = summarize_resources(self.resources)
        self.save_record()
        print(
            f"Soak {self.record['status']}: {self.record['stopReason']} — {self.output}",
            flush=True,
        )

    def save_record(self):
        (self.output / "result.json").write_text(json.dumps(self.record, indent=2))
