"""A measured operating point needs latency, no loss and a non-growing queue."""

import argparse
import asyncio
import json
from pathlib import Path
import statistics
import docker_memory
from density import identities
from reuse_support import command, write
from reuse_matrix import measure


def stability(folder, result):
    samples = [
        json.loads(line) for line in (folder / "samples.jsonl").read_text().splitlines()
    ]
    samples = [
        row for row in samples if 0 < row["elapsed"] <= result["config"]["seconds"]
    ]
    if len(samples) < 6:
        return {"qualified": False, "reason": "Too few time windows"}
    width = max(2, len(samples) // 3)

    def backlog(row):
        counts = row["counts"]
        return counts["offered"] - counts["completed"] - counts["dropped"]

    early = statistics.mean(backlog(row) for row in samples[:width])
    late = statistics.mean(backlog(row) for row in samples[-width:])
    # Explicit engineering margin: do not accept accumulating >100 ms of offered work.
    allowed = max(2 * result["config"]["workers"], result["config"]["rate"] * 0.1)
    windows = [
        row["windowResponseMs"]["p99"]
        for row in samples
        if row.get("windowResponseMs", {}).get("count", 0) >= 100
    ]
    value = {
        "earlyMeanBacklog": early,
        "lateMeanBacklog": late,
        "allowedBacklogGrowth": allowed,
        "maxWindowP99Ms": max(windows, default=None),
    }
    value["qualified"] = (
        result["meetsOneSecondSlo"]
        and late - early <= allowed
        and bool(windows)
        and max(windows) <= 1000
    )
    return value


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument(
        "--mode", required=True, choices=["trusted", "forkserver", "process"]
    )
    parser.add_argument("--low", type=int, required=True)
    parser.add_argument("--high", type=int, required=True)
    parser.add_argument("--steps", type=int, default=2)
    parser.add_argument("--seconds", type=int, default=60)
    parser.add_argument("--workers", type=int, default=8)
    args = parser.parse_args()
    if (
        not 0 < args.low < args.high
        or not 1 <= args.workers <= 32
        or not 30 <= args.seconds <= 300
        or not 0 <= args.steps <= 4
    ):
        raise ValueError("Bounded refinement configuration required")
    args.output.mkdir(parents=True)
    identity = identities(args.output)
    image = await command(
        "docker", "image", "inspect", "weaveport-reuse-matrix:1", "--format", "{{.Id}}"
    )
    import hashlib

    root = Path(__file__).resolve().parents[2]
    identity.update(
        {
            "image": image,
            "matrixHashes": {
                str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in list((root / "tools/performance").glob("*.py"))
                + list((root / "benchmarks/WeavePort.Reuse/fixture").glob("*.py"))
            },
        }
    )
    write(args.output / "identity.json", identity)
    engine, results = docker_memory.Engine(), []

    async def point(rate):
        qualified = True
        for repeat in [1, 2]:
            config = dict(
                mode=args.mode,
                workers=args.workers,
                seconds=args.seconds,
                cpus="1",
                transport="engine",
                arrival="poisson",
                rate=rate,
                seed=1728 + repeat,
                repeat=repeat,
            )
            folder = args.output / f"{len(results):03d}-{rate}-{repeat}"
            result = await measure(folder, config, image, engine)
            result["stability"] = stability(folder, result)
            write(folder / "summary.json", result)
            results.append(result)
            write(args.output / "summary.json", results)
            qualified = qualified and result["stability"]["qualified"]
        print("Refinement rate", rate, "qualified", qualified, flush=True)
        return qualified

    low, high = args.low, args.high
    if not await point(low):
        write(
            args.output / "bracket.json",
            {
                "qualifiedLow": None,
                "firstRejected": low,
                "reason": "Lower point did not qualify; reduce offered load",
            },
        )
        return
    if await point(high):
        write(
            args.output / "bracket.json",
            {
                "qualifiedLow": high,
                "firstRejected": None,
                "reason": "No upper boundary found in the configured bracket",
            },
        )
        return
    for _ in range(args.steps):
        midpoint = (low + high) // 2
        if midpoint == low:
            break
        qualified = await point(midpoint)
        low, high = (midpoint, high) if qualified else (low, midpoint)
    write(
        args.output / "bracket.json",
        {
            "qualifiedLow": low,
            "firstRejected": high,
            "scope": "Two finite trials per point under the stated latency/backlog contract; not a universal server maximum",
        },
    )


if __name__ == "__main__":
    asyncio.run(main())
