"""One bounded matrix stage: admitted work, observations and owned-lane cleanup."""

import asyncio
import time
from matrix_engine import EngineLane
from matrix_metrics import Histogram
from matrix_queue import SerialReadyQueue, grouped_capacity
from matrix_resources import ResourceTotals
from matrix_policy import PolicyPool
from reuse_support import write


import matrix_workloads
import matrix_execution
import matrix_observation
import matrix_lifecycle


class MatrixMeasurement:

    def __init__(
        self, folder, config, image, engine, lane_type, read_pressure, run_command
    ):
        self.folder, self.config, self.image, self.engine = (
            folder,
            config,
            image,
            engine,
        )
        self.read_pressure, self.run_command = (read_pressure, run_command)
        self.folder.mkdir()
        write(self.folder / "config.json", self.config)
        self.known, self.failures = (set(), [])
        self.resources = ResourceTotals()
        extra = {"engine": self.engine} if lane_type is EngineLane else {}
        self.lanes = [
            lane_type(
                self.config["mode"],
                self.known,
                self.image,
                self.config.get("cpus", ".5"),
                self.config.get("memory", 128),
                **extra
            )
            for _ in range(self.config["workers"])
        ]
        self.policy_pool = (
            PolicyPool(self.lanes, self.config["policy"])
            if self.config.get("policy")
            else None
        )
        self.hist = {
            key: Histogram()
            for key in (
                "responseMs",
                "turnoverMs",
                "queueMs",
                "executionMs",
                "cleanupMs",
                "generatorLagMs",
                "pluginImportMs",
                "childWorkMs",
                "childStartMs",
                "childResponseWaitMs",
                "childExitWaitMs",
                "processTurnoverMs",
            )
        }
        self.counts = {
            "offered": 0,
            "completed": 0,
            "correct": 0,
            "failed": 0,
            "dropped": 0,
            "withinOneSecond": 0,
            "customersCompleted": 0,
            "faultsExpected": 0,
            "faultsContained": 0,
        }
        self.classes, self.customer_calls = ({}, {})
        self.window, self.window_short = (Histogram(), Histogram())
        self.calls_per_customer = self.config.get("callsPerCustomer", 1)
        if not 1 <= self.calls_per_customer <= 100:
            raise ValueError("Bounded positive calls-per-customer required")
        self.plugins_per_customer = self.config.get("pluginsPerCustomer", 2)
        if self.plugins_per_customer not in (1, 2):
            raise ValueError("Fixture offers one or two plugin modules per customer")
        self.stop = asyncio.Event()
        self.baseline = self.read_pressure()
        self.initial_cpu = time.process_time()
        self.start, self.prepared, self.observer, self.workers, self.failure = (
            time.perf_counter(),
            0,
            None,
            [],
            None,
        )
        self.payload = ("Abc123xy" * ((self.config.get("payload", 64) + 7) // 8))[
            : self.config.get("payload", 64)
        ]
        self.grouped_saturation = (
            self.config.get("arrival", "saturated") == "saturated"
            and self.calls_per_customer > 1
        )
        pending_limit = (
            grouped_capacity(self.config["workers"], self.calls_per_customer)
            if self.grouped_saturation
            else self.config.get("maxPending", 10000)
        )
        self.population = self.config.get("population")
        self.queue = SerialReadyQueue(
            pending_limit, lambda item: matrix_workloads.customer_key(self, item[0])
        )
        self.seconds = self.config.get("seconds", 15)
        self.deadline_ms = self.config.get("queueDeadlineMs", 1000)

    async def run(self):
        try:
            await matrix_lifecycle.verify_headroom(self)
            await matrix_lifecycle.prepare_lanes(self)
            self.prepared = time.perf_counter() - self.start
            self.start, self.initial_cpu = (time.perf_counter(), time.process_time())
            self.observer = asyncio.create_task(
                matrix_observation.observe_resources(self)
            )
            await matrix_execution.execute_load(self)
            self.elapsed = time.perf_counter() - self.start
            await matrix_execution.check_observer(self)
        except BaseException as error:
            self.failure, self.elapsed = (repr(error), time.perf_counter() - self.start)
            for task in self.workers:
                task.cancel()
            await asyncio.gather(*self.workers, return_exceptions=True)
        finally:
            await matrix_lifecycle.cleanup_lanes(self)
        remaining = await matrix_lifecycle.reconcile_owned(self)
        return matrix_observation.report(self, remaining)
