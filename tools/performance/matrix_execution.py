"""Bounded producers and serial consumers for one matrix stage."""

import asyncio
import random
import time
import matrix_workloads


async def saturated(stage, lane):
    while time.perf_counter() < stage.start + stage.seconds:
        await check_observer(stage)
        i = stage.counts["offered"]
        stage.counts["offered"] += 1
        matrix_workloads.consume(
            stage,
            await (stage.policy_pool or lane).call(matrix_workloads.choose(stage, i)),
        )
        if stage.counts["failed"] > 3:
            raise RuntimeError("Repeated correctness failure")


async def queued(stage, lane):
    while True:
        key, item = await stage.queue.get()
        try:
            if item is None:
                return
            i, due = item
            if (time.perf_counter() - due) * 1000 > stage.deadline_ms:
                stage.counts["dropped"] += 1
                continue
            matrix_workloads.consume(
                stage,
                await (stage.policy_pool or lane).call(
                    matrix_workloads.choose(stage, i), due
                ),
            )
        finally:
            stage.queue.task_done(key)


async def check_observer(stage):
    if stage.observer is not None and stage.observer.done():
        await stage.observer


async def execute_load(stage):
    if stage.config.get("arrival", "saturated") == "saturated" and (
        not stage.grouped_saturation
    ):
        stage.workers = [
            asyncio.create_task(saturated(stage, lane)) for lane in stage.lanes
        ]
        await asyncio.gather(*stage.workers)
        return
    stage.workers = [asyncio.create_task(queued(stage, lane)) for lane in stage.lanes]
    if stage.grouped_saturation:
        await offer_groups(stage)
    else:
        await offer_scheduled(stage)
    await asyncio.sleep(max(0, stage.start + stage.seconds - time.perf_counter()))
    await stage.queue.join()
    for _ in stage.workers:
        await stage.queue.put(None)
    await asyncio.gather(*stage.workers)


async def offer_groups(stage):
    index = 0
    while (
        time.perf_counter() < stage.start + stage.seconds
        or index % stage.calls_per_customer
    ):
        await check_observer(stage)
        await stage.queue.put((index, time.perf_counter()))
        stage.counts["offered"] += 1
        index += 1


async def offer_scheduled(stage):
    randomizer = random.Random(stage.config.get("seed", 1729))
    phases = (
        sorted(
            (
                randomizer.random() * stage.config["periodSeconds"]
                for _ in range(stage.population)
            )
        )
        if stage.population
        else None
    )
    due = phases[0] if phases else 0.0
    index = 0
    while due < stage.seconds:
        await asyncio.sleep(max(0, stage.start + due - time.perf_counter()))
        await check_observer(stage)
        stage.hist["generatorLagMs"].add(
            (time.perf_counter() - stage.start - due) * 1000
        )
        stage.counts["offered"] += 1
        try:
            stage.queue.put_nowait((index, stage.start + due))
        except asyncio.QueueFull:
            stage.counts["dropped"] += 1
        index += 1
        due = next_due(stage, index, due, phases, randomizer)


def next_due(stage, index, due, phases, randomizer):
    if phases:
        return (
            index // stage.population * stage.config["periodSeconds"]
            + phases[index % stage.population]
        )
    rate = stage.config.get("rate", 1)
    if stage.config["arrival"] == "burst":
        return index // max(1, int(rate))
    return due + randomizer.expovariate(rate)
