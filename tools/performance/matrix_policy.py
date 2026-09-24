"""Experimental global pool: approved sessions or immutable customer/plugin binding."""

import asyncio
import time


class PolicyPool:
    def __init__(self, lanes, policy):
        if policy not in ("approved", "bound"):
            raise ValueError("Unknown reuse policy")
        self.lanes, self.policy = lanes, policy
        self.bindings = [None] * len(lanes)
        self.busy = set()
        self.last_used = [0.0] * len(lanes)
        self.condition = asyncio.Condition()
        self.hits = self.switches = self.pristine = 0
        self.examples = []

    async def call(self, request, intended=None):
        intended = time.perf_counter() if intended is None else intended
        key = (
            request["tenant"],
            request["plugin"],
            request.get("version", "fixture-v1"),
        )
        index = await self.acquire(key)
        lane, previous = self.lanes[index], self.bindings[index]
        try:
            before = lane.name
            if self.policy == "bound" and previous is not None and previous != key:
                await lane.close()
                self.switches += 1
            elif previous == key and lane.name:
                self.hits += 1
            elif previous is None:
                self.pristine += 1
            self.bindings[index] = key
            row = await lane.call(request, intended)
            if (
                self.policy == "bound"
                and previous is not None
                and previous != key
                and before == row["container"]
            ):
                raise AssertionError(
                    "A bound container crossed its customer/plugin boundary"
                )
            if len(self.examples) < 100:
                self.examples.append(
                    {
                        "previous": previous,
                        "next": key,
                        "before": before,
                        "after": row["container"],
                    }
                )
            return row
        finally:
            async with self.condition:
                if lane.name is None:
                    self.bindings[index] = None
                self.last_used[index] = time.perf_counter()
                self.busy.remove(index)
                self.condition.notify_all()

    def available_indices(self, key):
        matching = [
            index for index, binding in enumerate(self.bindings) if binding == key
        ]
        candidates = (
            matching if self.policy == "bound" and matching else range(len(self.lanes))
        )
        return [index for index in candidates if index not in self.busy]

    async def acquire(self, key):
        async with self.condition:
            while True:
                available = self.available_indices(key)
                if not available:
                    await self.condition.wait()
                    continue
                index = min(
                    available,
                    key=lambda i: (self.bindings[i] != key, self.last_used[i]),
                )
                self.busy.add(index)
                return index

    def report(self):
        return {
            "policy": self.policy,
            "warmBindingHits": self.hits,
            "bindingReplacements": self.switches,
            "pristineAssignments": self.pristine,
            "transitionExamples": self.examples,
        }
