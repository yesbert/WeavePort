"""Bounded density staircase. Record failed stages; never call a tested ceiling the server maximum."""

import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import shutil
import time

import docker_memory
from density_stage import run_stage
from density_support import ROOT, command, pressure, write_json


def identities(output):
    paths = []
    for folder in ("src", "benchmarks/WeavePort.Density", "plugins"):
        paths.extend(
            p
            for p in (ROOT / folder).rglob("*")
            if p.is_file()
            and not {"bin", "obj", "node_modules"}.intersection(p.parts)
            and p.suffix in (".cs", ".csproj", ".py", ".ts")
        )
    data = {
        "dateUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "commit": command("git", "rev-parse", "HEAD"),
        "dirty": bool(command("git", "status", "--porcelain")),
        "machine": command(
            "/usr/sbin/sysctl",
            "-n",
            "hw.model",
            "hw.memsize",
            "hw.ncpu",
            "machdep.cpu.brand_string",
        ).splitlines(),
        "os": command("/usr/bin/sw_vers"),
        "dotnet": command("dotnet", "--version"),
        "python": command(shutil.which("python3"), "--version"),
        "node": command("node", "--version"),
        "sourceHashes": {
            str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(paths)
        },
        "supervisorSources": {
            name: hashlib.sha256(
                Path(__file__).with_name(name).read_bytes()
            ).hexdigest()
            for name in (
                "density.py",
                "density_stage.py",
                "density_support.py",
                "docker_memory.py",
            )
        },
        "supervisorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "dockerObserverSha256": hashlib.sha256(
            Path(docker_memory.__file__).read_bytes()
        ).hexdigest(),
        "headroomBefore": pressure(),
        "docker": json.loads(command("docker", "info", "--format", "{{json .}}")),
    }
    data["docker"] = {
        k: data["docker"][k]
        for k in ("MemTotal", "NCPU", "ServerVersion", "Architecture")
    }
    data["existingContainers"] = command(
        "docker", "ps", "--format", "{{.Names}}"
    ).splitlines()
    data["fixtureImages"] = command(
        "docker",
        "images",
        "--no-trunc",
        "--format",
        "{{.Repository}}:{{.Tag}} {{.ID}}",
        "weaveport-poc-*",
    ).splitlines()
    write_json(output / "identity.json", data)
    return data


def parse_arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    parser.add_argument("--clients", default="4,8,16,32")
    parser.add_argument(
        "--workers",
        type=int,
        default=None,
        help="Optional count ceiling; omitted means memory-only admission",
    )
    parser.add_argument("--memory-mib", type=int, default=4096)
    parser.add_argument("--worker-memory-mib", type=int, default=64)
    parser.add_argument("--concurrent-starts", type=int, default=8)
    parser.add_argument("--idle-ms", type=int, default=120000)
    parser.add_argument(
        "--max-owned-rss-mib",
        type=int,
        default=4096,
        help="macOS host and owned child RSS guard; includes Docker CLI processes",
    )
    parser.add_argument(
        "--registered",
        type=int,
        default=0,
        help="Registered population; 0 means the active client count",
    )
    parser.add_argument(
        "--traffic", choices=["saturation", "population"], default="saturation"
    )
    parser.add_argument(
        "--calls-per-minute",
        type=float,
        default=1,
        help="Per customer, with a reproducible random phase; population mode only",
    )
    parser.add_argument("--seed", type=int, default=1729)
    parser.add_argument("--seconds", type=int, default=10)
    parser.add_argument("--repeats", type=int, default=2)
    parser.add_argument("--adapters", default="native,docker")
    parser.add_argument("--modes", default="scheduled")
    parser.add_argument(
        "--language", choices=["python", "typescript", "csharp"], default="python"
    )
    parser.add_argument(
        "--rates",
        default="0",
        help="0 = closed-loop; positive rates = fixed arrivals/sec, including queue and generator lag",
    )
    parser.add_argument("--p99-ms", type=int, default=1000)
    parser.add_argument("--payload-bytes", type=int, default=64)
    parser.add_argument("--pristine", type=int, default=0)
    return parser.parse_args()


def main():
    args = parse_arguments()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    identity = identities(output)
    results = []
    for adapter, mode in product(args.adapters.split(","), args.modes.split(",")):
        if not run_staircase(args, output, identity, results, adapter, mode):
            return
    print(
        "Completed bounded staircase. Passing final stage is a tested lower bound, not a discovered server maximum."
    )


def run_staircase(args, output, identity, results, adapter, mode):
    cases = product(
        map(int, args.clients.split(",")),
        map(float, args.rates.split(",")),
        range(args.repeats),
    )
    for clients, rate, repeat in cases:
        if (output / "STOP").exists():
            return False
        config = stage_configuration(args, output, adapter, mode, clients, rate, repeat)
        folder = Path(config["Output"])
        print("Running", folder.name, flush=True)
        result = run_stage(config, identity["headroomBefore"])
        results.append({"stage": folder.name, **result})
        write_json(output / "summary.json", results)
        print(
            json.dumps(
                {
                    "stage": folder.name,
                    "passed": result["passed"],
                    "rps": result.get("throughput"),
                    "p99": result.get("totals", {}).get("latency", {}).get("p99"),
                    "supervisor": result["supervisor"],
                }
            ),
            flush=True,
        )
        if not result["passed"]:
            break
    return True


def stage_configuration(args, output, adapter, mode, clients, rate, repeat):
    folder = (
        output / f"{adapter}-{mode}-{args.traffic}-c{clients}-r{rate:g}-{repeat + 1}"
    )
    executable = shutil.which(
        {"python": "python3", "typescript": "node", "csharp": "dotnet"}[args.language]
    )
    image = (
        command(
            "docker",
            "image",
            "inspect",
            "weaveport-poc-" + args.language + ":1",
            "--format",
            "{{.Id}}",
        )
        if adapter == "docker"
        else None
    )
    return dict(
        DockerImage=image,
        Root=str(ROOT),
        Output=str(folder),
        Adapter=adapter,
        Mode=mode,
        Language=args.language,
        Executable=executable,
        Clients=clients,
        Workers=args.workers,
        Seconds=args.seconds,
        MemoryBudgetMiB=args.memory_mib,
        WorkerMemoryMiB=args.worker_memory_mib,
        ConcurrentStarts=args.concurrent_starts,
        IdleMs=args.idle_ms,
        MaxOwnedRssMiB=args.max_owned_rss_mib,
        Rate=rate,
        P99Ms=args.p99_ms,
        PayloadBytes=args.payload_bytes,
        Pristine=args.pristine,
        RegisteredClients=args.registered,
        Traffic=args.traffic,
        CallsPerCustomerPerMinute=args.calls_per_minute,
        Seed=args.seed + repeat,
    )


if __name__ == "__main__":
    main()
