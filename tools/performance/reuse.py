"""Experimental customer/plugin turnover. No production cross-tenant reuse is enabled."""

import argparse
import asyncio
import hashlib
from itertools import product
import json
from pathlib import Path
import time
import docker_memory
from density import pressure
from reuse_isolation import isolation
from reuse_measurement import measure
from reuse_support import command, write

ROOT = Path(__file__).resolve().parents[2]
IMAGE = "weaveport-reuse-experiment:1"


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    parser.add_argument(
        "--phase", choices=["isolation", "turnover", "population", "all"], default="all"
    )
    parser.add_argument("--modes", default="fresh,process,trusted")
    parser.add_argument("--repeats", type=int, default=2)
    parser.add_argument(
        "--clients",
        type=int,
        default=500,
        help="Unique one-call customers during the 60-second population replay",
    )
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    modes = args.modes.split(",")
    if (
        any(m not in ("fresh", "process", "trusted") for m in modes)
        or not 1 <= args.repeats <= 3
        or not 1 <= args.clients <= 10000
        or ("fresh" in modes and args.clients > 1000)
    ):
        raise ValueError("Experiment bounds")
    identity = {
        "at": time.time(),
        "machine": await command(
            "/usr/sbin/sysctl",
            "-n",
            "hw.model",
            "hw.memsize",
            "hw.ncpu",
            "machdep.cpu.brand_string",
        ),
        "docker": json.loads(await command("docker", "info", "--format", "{{json .}}")),
        "image": json.loads(
            await command(
                "docker", "image", "inspect", IMAGE, "--format", "{{json .Id}}"
            )
        ),
        "sourceHashes": {
            str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in [
                *sorted((ROOT / "tools/performance").glob("reuse*.py")),
                Path(docker_memory.__file__),
                *sorted((ROOT / "benchmarks/WeavePort.Reuse").rglob("*.py")),
            ]
        },
        "headroom": pressure(),
    }
    identity["docker"] = {
        k: identity["docker"][k]
        for k in ("MemTotal", "NCPU", "ServerVersion", "Architecture")
    }
    image = identity[
        "image"
    ]  # Pin every created container to the inspected local artifact.
    write(output / "identity.json", identity)
    engine = docker_memory.Engine()
    if args.phase in ("all", "isolation"):
        await isolation(output, image)
    results = []
    kinds = [kind for kind in ("turnover", "population") if args.phase in ("all", kind)]
    for kind, repeat, index in product(kinds, range(args.repeats), range(len(modes))):
        mode = (modes if repeat % 2 == 0 else list(reversed(modes)))[index]
        results.append(
            await measure(
                output,
                mode,
                kind,
                engine,
                count=args.clients,
                seed=1729 + repeat,
                repeat=repeat + 1,
                image=image,
            )
        )
        write(output / "summary.json", results)


if __name__ == "__main__":
    asyncio.run(main())
