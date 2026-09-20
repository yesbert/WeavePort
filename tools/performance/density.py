"""Bounded density staircase. Record failed stages; never call a tested ceiling the server maximum."""
from concurrent.futures import ThreadPoolExecutor
import docker_memory
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]


def command(*args, timeout=30):
    return subprocess.check_output(args, cwd=ROOT, text=True, stderr=subprocess.PIPE, timeout=timeout).strip()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def mib(value):
    match = re.match(r"([0-9.]+)([a-zA-Z]+)", value.strip())
    if not match:
        raise ValueError("Unknown memory unit: " + value)
    scale = {"B": 1 / 1048576, "kB": 1000 / 1048576, "KiB": 1 / 1024, "MB": 1000000 / 1048576,
             "MiB": 1, "GB": 1000000000 / 1048576, "GiB": 1024}
    return float(match[1]) * scale[match[2]]


def pressure():
    text = command("/usr/bin/memory_pressure", "-Q")
    found = re.search(r"System-wide memory free percentage:\s*(\d+)%", text)
    if not found:
        raise RuntimeError("macOS memory headroom unavailable")
    swap = command("/usr/sbin/sysctl", "-n", "vm.swapusage")
    used = re.search(r"used = ([0-9.]+)M", swap)
    return {"freePercent": int(found[1]), "swapUsedMiB": float(used[1]) if used else None}


def identities(output):
    paths = []
    for folder in ("src", "benchmarks/WeavePort.Density", "plugins"):
        paths.extend(p for p in (ROOT / folder).rglob("*") if p.is_file() and not {"bin", "obj", "node_modules"}.intersection(p.parts)
                     and p.suffix in (".cs", ".csproj", ".py", ".ts"))
    data = {"dateUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "commit": command("git", "rev-parse", "HEAD"),
            "dirty": bool(command("git", "status", "--porcelain")), "machine": command("/usr/sbin/sysctl", "-n", "hw.model", "hw.memsize", "hw.ncpu", "machdep.cpu.brand_string").splitlines(),
            "os": command("/usr/bin/sw_vers"), "dotnet": command("dotnet", "--version"),
            "python": command(shutil.which("python3"), "--version"), "node": command("node", "--version"),
            "sourceHashes": {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(paths)},
            "supervisorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "dockerObserverSha256": hashlib.sha256(Path(docker_memory.__file__).read_bytes()).hexdigest(),
            "headroomBefore": pressure(), "docker": json.loads(command("docker", "info", "--format", '{{json .}}'))}
    data["docker"] = {k: data["docker"][k] for k in ("MemTotal", "NCPU", "ServerVersion", "Architecture")}
    data["existingContainers"] = command("docker", "ps", "--format", "{{.Names}}").splitlines()
    data["fixtureImages"] = command("docker", "images", "--no-trunc", "--format", "{{.Repository}}:{{.Tag}} {{.ID}}", "weaveport-poc-*").splitlines()
    write_json(output / "identity.json", data)
    return data


def refresh_known(folder, known, position):
    resource = folder / "resources.jsonl"
    if not resource.exists():
        return position
    with resource.open() as stream:
        stream.seek(position)
        while True:
            begin = stream.tell()
            line = stream.readline()
            if not line or not line.endswith("\n"):
                return begin
            known.update(json.loads(line)["instances"])



def run_stage(config, baseline):
    folder = Path(config["Output"])
    folder.mkdir(parents=True)
    write_json(folder / "config.json", config)
    known, samples = set(), []
    position = 0
    engine = docker_memory.Engine() if config["Adapter"] == "docker" else None
    observer = ThreadPoolExecutor(max_workers=1)
    pending = None
    last_docker = None
    last_started = 0
    reason = None
    with (folder / "console.log").open("w") as log:
        proc = subprocess.Popen(["dotnet", str(ROOT / "benchmarks/WeavePort.Density/bin/Release/net10.0/WeavePort.Density.dll"),
                                 str(folder / "config.json")], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        deadline = time.monotonic() + config["Seconds"] + config["Clients"] * 2 + 90
        stop_at = None
        while proc.poll() is None:
            try:
                sample = {"at": time.time(), "pressure": pressure()}
                position = refresh_known(folder, known, position)
                if engine:
                    if pending is not None and pending.done():
                        last_docker = pending.result()
                        with (folder / "docker-memory.jsonl").open("a") as memory_log:
                            memory_log.write(json.dumps(last_docker) + "\n")
                        pending = None
                    if pending is not None and time.monotonic() - last_started > 15:
                        raise TimeoutError("Docker memory snapshot exceeded 15 seconds")
                    if pending is None and time.monotonic() - last_started >= 2:
                        last_started = time.monotonic()
                        pending = observer.submit(docker_memory.snapshot, engine, known.copy())
                    if last_docker is not None:
                        sample["docker"] = {k: v for k, v in last_docker.items() if k != "rows"}
                        sample["docker"]["ageSeconds"] = time.time() - last_docker["at"]
                samples.append(sample)
                write_json(folder / "supervisor.json", samples)
                current = sample["pressure"]
                if current["freePercent"] < 10 or current["swapUsedMiB"] is None or current["swapUsedMiB"] - baseline["swapUsedMiB"] > 512:
                    reason = "system-headroom"
                if sample.get("docker", {}).get("ownedRawMiB", 0) > config["MemoryBudgetMiB"]:
                    reason = "owned-container-memory"
                if (folder.parent / "STOP").exists():
                    reason = "operator-stop"
                if time.monotonic() > deadline:
                    reason = "wall-deadline"
            except Exception as error:
                reason = "observer-" + type(error).__name__ + ": " + str(error)
            if reason and stop_at is None:
                (folder / "STOP").touch()
                stop_at = time.monotonic()
            if stop_at and time.monotonic() - stop_at > 30:
                # Only this explicitly owned process group, never unrelated Docker services.
                os.killpg(proc.pid, signal.SIGTERM)
                proc.wait(timeout=15)
                reason += "-forced-termination"
                break
            time.sleep(.5)
    observer.shutdown(wait=True, cancel_futures=True)
    if pending is not None:
        try:
            final_sample = pending.result()
            samples.append({"at": time.time(), "docker": final_sample})
            with (folder / "docker-memory.jsonl").open("a") as memory_log:
                memory_log.write(json.dumps(final_sample) + "\n")
        except Exception as error:
            reason = reason or "observer-" + type(error).__name__ + ": " + str(error)
    position = refresh_known(folder, known, position)
    if engine and not any(s.get("docker", {}).get("owned") for s in samples):
        reason = reason or "no-owned-container-memory-observed"
    result_path = folder / "result.json"
    result = json.loads(result_path.read_text()) if result_path.exists() else {"passed": False, "failure": "missing-result"}
    remaining = []
    if config["Adapter"] == "docker":
        remaining = sorted(known.intersection(command("docker", "ps", "-a", "--format", "{{.Names}}").splitlines()))
    result["supervisor"] = {"reason": reason, "exitCode": proc.returncode, "remainingKnownContainers": remaining,
                            "peakSampledContainerMiB": max((s["docker"]["ownedMiB"] for s in samples
                                                            if s.get("docker", {}).get("owned")), default=None),
                            "peakSampledContainerRawMiB": max((s["docker"]["ownedRawMiB"] for s in samples if s.get("docker", {}).get("owned")), default=None),
                            "maxDockerSampleAgeSeconds": max((s.get("docker", {}).get("ageSeconds", 0) for s in samples), default=None),
                            "samples": len(samples)}
    result["passed"] = result["passed"] and proc.returncode == 0 and reason is None and not remaining
    write_json(folder / "result.json", result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    parser.add_argument("--clients", default="4,8,16,32")
    parser.add_argument("--workers", type=int, default=None, help="Optional count ceiling; omitted means memory-only admission")
    parser.add_argument("--memory-mib", type=int, default=4096)
    parser.add_argument("--worker-memory-mib", type=int, default=64)
    parser.add_argument("--concurrent-starts", type=int, default=8)
    parser.add_argument("--idle-ms", type=int, default=120000)
    parser.add_argument("--max-owned-rss-mib", type=int, default=4096, help="macOS host and owned child RSS guard; includes Docker CLI processes")
    parser.add_argument("--registered", type=int, default=0, help="Registered population; 0 means the active client count")
    parser.add_argument("--traffic", choices=["saturation", "population"], default="saturation")
    parser.add_argument("--calls-per-minute", type=float, default=1, help="Per customer, with a reproducible random phase; population mode only")
    parser.add_argument("--seed", type=int, default=1729)
    parser.add_argument("--seconds", type=int, default=10)
    parser.add_argument("--repeats", type=int, default=2)
    parser.add_argument("--adapters", default="native,docker")
    parser.add_argument("--modes", default="scheduled")
    parser.add_argument("--language", choices=["python", "typescript", "csharp"], default="python")
    parser.add_argument("--rates", default="0", help="0 = closed-loop; positive rates = fixed arrivals/sec, including queue and generator lag")
    parser.add_argument("--p99-ms", type=int, default=1000)
    parser.add_argument("--payload-bytes", type=int, default=64)
    parser.add_argument("--pristine", type=int, default=0)
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    identity = identities(output)
    results = []
    for adapter in args.adapters.split(","):
        for mode in args.modes.split(","):
            failed = False
            for clients in map(int, args.clients.split(",")):
                for rate in map(float, args.rates.split(",")):
                    if failed: break
                    for repeat in range(args.repeats):
                        if (output / "STOP").exists(): return
                        folder = output / f"{adapter}-{mode}-{args.traffic}-c{clients}-r{rate:g}-{repeat + 1}"
                        executable = shutil.which({"python": "python3", "typescript": "node", "csharp": "dotnet"}[args.language])
                        image = command('docker', 'image', 'inspect', 'weaveport-poc-' + args.language + ':1', '--format', '{{.Id}}') if adapter == 'docker' else None
                        config = dict(DockerImage=image, Root=str(ROOT), Output=str(folder), Adapter=adapter, Mode=mode, Language=args.language,
                                      Executable=executable, Clients=clients, Workers=args.workers, Seconds=args.seconds, MemoryBudgetMiB=args.memory_mib,
                                      WorkerMemoryMiB=args.worker_memory_mib, ConcurrentStarts=args.concurrent_starts, IdleMs=args.idle_ms, MaxOwnedRssMiB=args.max_owned_rss_mib,
                                      Rate=rate, P99Ms=args.p99_ms, PayloadBytes=args.payload_bytes, Pristine=args.pristine, RegisteredClients=args.registered,
                                      Traffic=args.traffic, CallsPerCustomerPerMinute=args.calls_per_minute, Seed=args.seed + repeat)
                        print("Running", folder.name, flush=True)
                        result = run_stage(config, identity["headroomBefore"])
                        results.append({"stage": folder.name, **result})
                        write_json(output / "summary.json", results)
                        print(json.dumps({"stage": folder.name, "passed": result["passed"], "rps": result.get("throughput"),
                                          "p99": result.get("totals", {}).get("latency", {}).get("p99"), "supervisor": result["supervisor"]}), flush=True)
                        if not result["passed"]:
                            failed = True
                            break
                    if failed: break
                if failed: break
    print("Completed bounded staircase. Passing final stage is a tested lower bound, not a discovered server maximum.")


if __name__ == "__main__":
    main()
