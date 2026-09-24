"""Chronological observations and final stage evidence."""

import asyncio
import json
import os
import time
import docker_memory
from matrix_metrics import Histogram
from reuse_support import write


async def observe_resources(stage):
    while not stage.stop.is_set():
        memory = await asyncio.to_thread(
            docker_memory.snapshot, stage.engine, stage.known.copy()
        )
        headroom = await asyncio.to_thread(stage.read_pressure)
        ps = await stage.run_command("/bin/ps", "-axo", "pid=,ppid=,rss=")
        processes = [
            tuple(map(int, line.split())) for line in ps.splitlines() if line.strip()
        ]
        helper_pids = {getattr(lane.process, "pid", None) for lane in stage.lanes}
        snapshot = {
            "hostRssMiB": next(
                (rss / 1024 for pid, ppid, rss in processes if pid == os.getpid())
            ),
            "helpersRssMiB": sum(
                (rss / 1024 for pid, ppid, rss in processes if pid in helper_pids)
            ),
            "at": time.time(),
            "elapsed": time.perf_counter() - stage.start,
            "ownedMiB": memory["ownedMiB"],
            "ownedRawMiB": memory["ownedRawMiB"],
            "owned": memory["owned"],
            "pressure": headroom,
            "counts": stage.counts.copy(),
            "windowResponseMs": stage.window.report(),
            "windowEchoResponseMs": stage.window_short.report(),
            "hostCpuSeconds": time.process_time() - stage.initial_cpu,
        }
        stage.window, stage.window_short = (Histogram(), Histogram())
        stage.resources.add(snapshot)
        with (stage.folder / "samples.jsonl").open("a") as stream:
            stream.write(json.dumps(snapshot) + "\n")
        if (
            headroom["freePercent"] < 10
            or headroom["swapUsedMiB"] is None
            or headroom["swapUsedMiB"] - stage.baseline["swapUsedMiB"] > 512
        ):
            raise RuntimeError("Memory pressure guard")
        if (stage.folder.parent / "STOP").exists():
            raise RuntimeError("Operator stop")
        try:
            await asyncio.wait_for(stage.stop.wait(), 3)
        except asyncio.TimeoutError:
            pass


def report(stage, remaining):
    result = {
        "config": stage.config,
        "serialization": "one in-flight call per customer/plugin key",
        "image": stage.image,
        "counts": stage.counts,
        "partiallyServedCustomers": len(stage.customer_calls),
        "offeredCustomers": stage.population
        or (stage.counts["offered"] + stage.calls_per_customer - 1)
        // stage.calls_per_customer,
        "elapsed": stage.elapsed,
        "preparedSeconds": stage.prepared,
        "customersPerSecond": stage.counts["customersCompleted"] / stage.elapsed,
        "requestsPerSecond": stage.counts["correct"] / stage.elapsed,
        "hostCpuCores": (time.process_time() - stage.initial_cpu) / stage.elapsed,
        "latency": {k: v.report() for k, v in stage.hist.items()},
        "byWorkload": {k: v.report() for k, v in stage.classes.items()},
        "memorySamplesWithLiveContainers": stage.resources.live_samples,
        "memoryPeakMiB": stage.resources.working_peak,
        "helperScope": "RSS of live owned Docker CLI PIDs only; observer children excluded",
        "hostRssPeakMiB": stage.resources.host_peak,
        "helpersRssPeakMiB": stage.resources.helpers_peak,
        "memoryRawPeakMiB": stage.resources.raw_peak,
        "starts": sum((lane.starts for lane in stage.lanes)),
        "retirements": sum((lane.retirements for lane in stage.lanes)),
        "failure": stage.failure,
        "failedExamples": stage.failures,
        "known": sorted(stage.known),
        "remaining": sorted(remaining),
    }
    if stage.policy_pool:
        result["poolPolicy"] = stage.policy_pool.report()
    result["passed"] = (
        not stage.failure
        and (not stage.counts["failed"])
        and (
            stage.counts["completed"] + stage.counts["dropped"]
            == stage.counts["offered"]
        )
    )
    result["meetsOneSecondSlo"] = (
        result["passed"]
        and stage.counts["correct"] == stage.counts["offered"]
        and (result["latency"]["responseMs"].get("p99", float("inf")) <= 1000)
    )
    result["normalEchoP99WithinOneSecond"] = (
        not stage.counts["failed"]
        and result["byWorkload"].get("echo", {}).get("p99", float("inf")) <= 1000
    )
    write(stage.folder / "summary.json", result)
    print(
        stage.folder.name,
        stage.counts,
        "customers/s",
        round(result["customersPerSecond"], 2),
        "p99",
        round(result["latency"]["responseMs"].get("p99", 0), 2),
        "MiB",
        result["memoryPeakMiB"],
        "failure",
        stage.failure,
        flush=True,
    )
    if stage.failure or stage.counts["failed"]:
        raise RuntimeError("Stage failed; retained at " + str(stage.folder))
    return result
