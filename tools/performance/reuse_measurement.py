"""Turnover and population replay, including owned cleanup and retained evidence."""

import asyncio
import json
import os
import random
import time
import docker_memory
from density import pressure
from reuse_lane import Lane
from reuse_support import command, request, correct, quantiles, write


async def monitor(engine, known, stop, samples, baseline):
    while not stop.is_set():
        snapshot = await asyncio.to_thread(docker_memory.snapshot, engine, known.copy())
        headroom = await asyncio.to_thread(pressure)
        ps = await command("/bin/ps", "-axo", "pid=,ppid=,rss=")
        rows = [
            tuple(map(int, line.split())) for line in ps.splitlines() if line.strip()
        ]
        host = next(rss / 1024 for pid, ppid, rss in rows if pid == os.getpid())
        children = sum(rss / 1024 for pid, ppid, rss in rows if ppid == os.getpid())
        samples.append(
            {
                "at": time.time(),
                "ownedMiB": snapshot["ownedMiB"],
                "ownedRawMiB": snapshot["ownedRawMiB"],
                "containers": len(snapshot["owned"]),
                "snapshotSeconds": snapshot["durationSeconds"],
                "hostRssMiB": host,
                "childRssMiB": children,
                "pressure": headroom,
            }
        )
        if (
            headroom["freePercent"] < 10
            or headroom["swapUsedMiB"] is None
            or headroom["swapUsedMiB"] - baseline["swapUsedMiB"] > 512
        ):
            raise RuntimeError("System headroom guard")
        try:
            await asyncio.wait_for(stop.wait(), 1)
        except asyncio.TimeoutError:
            pass


MIN_TURNOVER_CALLS = 200
MIN_TURNOVER_SECONDS = 10
MAX_TURNOVER_CALLS = 50000


async def prepare_lanes(lanes):
    # Every concurrent start must settle before cleanup can safely own the batch.
    results = await asyncio.gather(
        *(lane.start() for lane in lanes), return_exceptions=True
    )
    for result in results:
        if isinstance(result, BaseException):
            raise result


async def check_run(output, observer):
    if observer.done():
        await observer
    if (output / "STOP").exists():
        raise RuntimeError("Operator stop")


async def run_turnover(output, observer, invoke, start):
    index = 0
    while (
        index < MIN_TURNOVER_CALLS or time.perf_counter() - start < MIN_TURNOVER_SECONDS
    ):
        await check_run(output, observer)
        await invoke(index, time.perf_counter() - start)
        index += 1
        if index >= MAX_TURNOVER_CALLS:
            break


async def run_population(
    output, observer, invoke, start, seed, count, seconds, pending
):
    randomizer = random.Random(seed)
    schedule = sorted(randomizer.random() * seconds for _ in range(count))
    for index, due in enumerate(schedule):
        await asyncio.sleep(max(0, start + due - time.perf_counter()))
        await check_run(output, observer)
        pending.append(asyncio.create_task(invoke(index, due)))
    await asyncio.sleep(max(0, start + seconds - time.perf_counter()))
    await asyncio.gather(*pending)


async def measure(
    output,
    mode,
    kind,
    engine,
    count=500,
    seconds=60,
    seed=1729,
    repeat=1,
    image="weaveport-reuse-experiment:1",
):
    folder = output / f"{kind}-{mode}-{repeat}"
    folder.mkdir()
    known, rows, samples = set(), [], []
    lanes = [Lane(mode, known, image) for _ in range(4 if kind == "population" else 1)]
    stop = asyncio.Event()
    observer = asyncio.create_task(monitor(engine, known, stop, samples, pressure()))
    failure, pending = None, []
    prepared = 0.0
    start = time.perf_counter()
    try:
        # Ready reusable containers belong to the infrastructure pool; record preparation separately.
        if mode != "fresh":
            await prepare_lanes(lanes)
        prepared = time.perf_counter() - start
        start = time.perf_counter()

        async def invoke(i, due):
            row = await lanes[i % len(lanes)].call(request(i, len(lanes)), start + due)
            rows.append(row)

        if kind == "turnover":
            await run_turnover(output, observer, invoke, start)
        else:
            await run_population(
                output, observer, invoke, start, seed, count, seconds, pending
            )
        elapsed = time.perf_counter() - start
    except Exception as error:
        failure = type(error).__name__ + ": " + str(error)
        elapsed = time.perf_counter() - start
        await asyncio.gather(*pending, return_exceptions=True)
    finally:
        cleanup_start = time.perf_counter()
        cleanup_results = await asyncio.gather(
            *(lane.close() for lane in lanes), return_exceptions=True
        )
        for error in cleanup_results:
            if not isinstance(error, BaseException):
                continue
            failure = failure or "cleanup-" + type(error).__name__ + ": " + str(error)
        cleanup_seconds = time.perf_counter() - cleanup_start
        stop.set()
        try:
            await observer
        except Exception as error:
            failure = failure or "observer-" + type(error).__name__ + ": " + str(error)
    remaining = [
        name
        for name in (
            await command("docker", "ps", "-a", "--format", "{{.Names}}")
        ).splitlines()
        if name in known
    ]
    success = sum(correct(row) for row in rows)
    result = {
        "mode": mode,
        "kind": kind,
        "repeat": repeat,
        "seed": seed,
        "calls": len(rows),
        "uniqueCustomers": len({r["request"]["tenant"] for r in rows}),
        "success": success,
        "failure": failure,
        "passed": failure is None
        and success == len(rows)
        and len(rows) > 0
        and not remaining,
        "seconds": elapsed,
        "rps": success / elapsed,
        "preparationSeconds": prepared,
        "cleanupSeconds": cleanup_seconds,
        "starts": sum(lane.starts for lane in lanes),
        "retirements": sum(lane.retirements for lane in lanes),
        "remaining": remaining,
        "responseMs": quantiles([r["responseMs"] for r in rows]),
        "turnoverMs": quantiles([r["turnoverMs"] for r in rows]),
        "queueMs": quantiles([r["queueMs"] for r in rows]),
        "startupMs": quantiles([r["startupMs"] for r in rows if r["startupMs"]]),
        "sdkCleanupMs": quantiles([r["outcome"].get("cleanupMs", 0) for r in rows]),
        "peakContainerMiB": max((s["ownedMiB"] for s in samples), default=None),
        "peakHostRssMiB": max((s["hostRssMiB"] for s in samples), default=None),
        "peakChildRssMiB": max((s["childRssMiB"] for s in samples), default=None),
        "scope": "Experimental Python supervisor/SDK scope and Docker stdio, not the released .NET host or SDK. Fresh lifecycle retirement included in turnover.",
    }
    result["meetsOneSecondP99"] = (
        result["passed"] and result["responseMs"]["p99"] <= 1000
    )
    write(folder / "result.json", result)
    write(folder / "resources.json", samples)
    with (folder / "calls.jsonl").open("w") as stream:
        for row in rows:
            stream.write(json.dumps(row) + "\n")
    print(json.dumps(result), flush=True)
    if not result["passed"]:
        raise RuntimeError("Failed experiment; inspect retained results")
    return result
