"""One serialized experimental container lane with explicit image ownership."""

import asyncio
import json
import time
import uuid
from reuse_support import command


class Lane:
    def __init__(self, mode, known, image="weaveport-reuse-experiment:1"):
        self.mode, self.known = mode, known
        self.image = image
        self.process, self.name = None, None
        self.starts, self.retirements = 0, 0
        self.lock = asyncio.Lock()

    async def start(self):
        self.name = "wp-reuse-" + uuid.uuid4().hex
        self.known.add(self.name)
        self.process = await asyncio.create_subprocess_exec(
            "docker",
            "run",
            "--name",
            self.name,
            "--label",
            "weaveport.reuse-experiment=true",
            "--init",
            "--interactive",
            "--network",
            "none",
            "--read-only",
            "--cap-drop",
            "ALL",
            "--security-opt",
            "no-new-privileges",
            "--user",
            "65532:65532",
            "--memory",
            "64m",
            "--memory-swap",
            "64m",
            "--cpus",
            ".5",
            "--pids-limit",
            "64",
            "--tmpfs",
            "/tmp:rw,noexec,nosuid,size=16m,mode=1777",
            "--shm-size",
            "8m",
            "--log-driver",
            "none",
            self.image,
            "process" if self.mode == "process" else "trusted",
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.DEVNULL,
            limit=1048576,
        )
        ready = json.loads(await asyncio.wait_for(self.process.stdout.readline(), 5))
        if not ready.get("ready"):
            raise RuntimeError("Missing ready handshake")
        self.starts += 1

    async def close(self):
        if self.name is None:
            return
        name, process = self.name, self.process
        # Keep identity until the daemon confirms removal. No broad label/prefix deletion.
        await command("docker", "rm", "--force", name)
        self.name, self.process = None, None
        self.retirements += 1
        if process is not None:
            await asyncio.wait_for(process.wait(), 5)

    async def call(self, request, intended=None):
        intended = intended or time.perf_counter()
        async with self.lock:
            admitted = time.perf_counter()
            startup = retirement = 0.0
            outcome = {"status": "failed", "reusable": False}
            try:
                if self.process is None:
                    began = time.perf_counter()
                    await self.start()
                    startup = (time.perf_counter() - began) * 1000
                name = self.name
                self.process.stdin.write(
                    (json.dumps(request, separators=(",", ":")) + "\n").encode()
                )
                await self.process.stdin.drain()
                outcome = json.loads(
                    await asyncio.wait_for(self.process.stdout.readline(), 2.5)
                )
                if outcome.get("id") != request["id"]:
                    raise RuntimeError("Mismatched response identity")
            except Exception as error:
                name = self.name
                outcome = {
                    "status": "failed",
                    "reusable": False,
                    "error": type(error).__name__,
                }
            response = time.perf_counter()
            if self.mode == "fresh" or not outcome.get("reusable"):
                began = time.perf_counter()
                await self.close()
                retirement = (time.perf_counter() - began) * 1000
            return {
                "request": request,
                "container": name,
                "outcome": outcome,
                "queueMs": (admitted - intended) * 1000,
                "startupMs": startup,
                "retirementMs": retirement,
                "responseMs": (response - intended) * 1000,
                "turnoverMs": (time.perf_counter() - intended) * 1000,
            }
