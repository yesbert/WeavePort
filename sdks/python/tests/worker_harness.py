"""Own a fixture subprocess and exchange literal wire frames with bounded waits."""

import json
import os
from pathlib import Path
import queue
import subprocess
import threading

ROOT = Path(__file__).resolve().parents[3]
REPLY_TIMEOUT_SECONDS = 3


class Worker:
    def __init__(self, language, concurrent=True):
        suffix = ".py" if language == "python" else ".mjs"
        fixture = Path(__file__).parent / "fixtures" / ("protocol_worker" + suffix)
        environment = dict(
            os.environ,
            PYTHONPATH=str(ROOT / "sdks/python"),
            WEAVEPORT_TEST_CONCURRENT=str(concurrent).lower(),
            WEAVEPORT_TEST_SDK=(ROOT / "sdks/typescript/dist/index.js").as_uri(),
        )
        executable = "python3" if language == "python" else "node"
        self.process = subprocess.Popen(
            [executable, str(fixture)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=environment,
            text=True,
        )
        self.lines = queue.Queue()
        threading.Thread(target=self._read_frames, daemon=True).start()
        ready = self.receive()
        assert ready["protocol"] == (2 if concurrent else 1), ready
        if concurrent:
            self.send(type="configure", degree=2)

    def _read_frames(self):
        for line in self.process.stdout:
            self.lines.put(json.loads(line))

    def send(self, **frame):
        self.process.stdin.write(json.dumps(frame) + "\n")
        self.process.stdin.flush()

    def invoke(self, identity, wire_operation="$sdk.call", **payload):
        self.send(
            type="invoke",
            id=identity,
            operation=wire_operation,
            payload=payload,
            context=dict(tenant=identity, configuration={}),
        )

    def receive(self):
        try:
            return self.lines.get(timeout=REPLY_TIMEOUT_SECONDS)
        except queue.Empty as error:
            raise AssertionError("Worker timed out") from error

    def close(self):
        self.process.kill()
        self.process.communicate(timeout=REPLY_TIMEOUT_SECONDS)
