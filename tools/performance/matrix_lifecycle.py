"""Owned lane preparation, bounded batches and confirmed cleanup."""

import asyncio
import docker_memory
from reuse_support import write


async def verify_headroom(stage):
    if stage.baseline["freePercent"] < 10 or stage.baseline["swapUsedMiB"] is None:
        raise RuntimeError("Insufficient initial host memory headroom")
    if stage.config["workers"] <= 64:
        return
    info = await asyncio.to_thread(stage.engine.get, "/info")
    background = await asyncio.to_thread(docker_memory.snapshot, stage.engine, set())
    available = info["MemTotal"] / 1048576 - background["allContainersRawMiB"] - 2048
    reserved = stage.config["workers"] * stage.config.get("memory", 128)
    write(
        stage.folder / "headroom.json",
        {
            "dockerMiB": info["MemTotal"] / 1048576,
            "backgroundRawMiB": background["allContainersRawMiB"],
            "additionalReserveMiB": 2048,
            "availableForPoolMiB": available,
            "requestedPoolReservationMiB": reserved,
        },
    )
    if reserved > available:
        raise RuntimeError("Insufficient measured Docker headroom for expanded pool")


async def prepare_lanes(stage):
    if stage.config["mode"] == "fresh":
        return
    for begin in range(0, len(stage.lanes), 4):
        await prepare_batch(stage, stage.lanes[begin : begin + 4])


async def prepare_batch(stage, lanes):
    # Settle the entire bounded start batch before reporting failure to cleanup.
    results = await asyncio.gather(
        *(lane.start() for lane in lanes), return_exceptions=True
    )
    for result in results:
        if isinstance(result, BaseException):
            raise result


async def settle_observer(stage):
    if stage.observer is None:
        return
    try:
        await stage.observer
    except Exception as error:
        stage.failure = stage.failure or repr(error)


async def cleanup_lanes(stage):
    stage.stop.set()
    await settle_observer(stage)
    results = await asyncio.gather(
        *(lane.close() for lane in stage.lanes), return_exceptions=True
    )
    for result in results:
        if not isinstance(result, BaseException):
            continue
        stage.failure = stage.failure or "cleanup: " + repr(result)


async def remaining_names(stage):
    names = await stage.run_command("docker", "ps", "-a", "--format", "{{.Names}}")
    return set(names.splitlines()) & stage.known


async def reconcile_owned(stage):
    # A timed-out create may finish after deletion; reconcile only exact owned names.
    remaining = await remaining_names(stage)
    if not remaining:
        return remaining
    for name in sorted(remaining):
        await stage.run_command("docker", "rm", "--force", name)
    remaining = await remaining_names(stage)
    if remaining:
        stage.failure = stage.failure or "Owned containers remain"
    return remaining
