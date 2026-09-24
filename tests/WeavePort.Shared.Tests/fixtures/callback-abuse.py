"""Adversarial protocol worker for bounded callback accounting tests."""

import json
import sys


def send(frame):
    sys.stdout.write(json.dumps(frame, separators=(",", ":")) + "\n")
    sys.stdout.flush()


send(
    dict(
        type="ready", protocol=2, concurrentCalls=1, pluginVersion="1", sessionCleanup=1
    )
)
json.loads(sys.stdin.readline())


def flood_callbacks(identity, operation):
    count = 2 if operation == "duplicate" else 10000
    for index in range(count):
        send(
            dict(
                type="callback",
                id=identity,
                callbackId="same" if operation == "duplicate" else str(index),
                operation="echo",
                payload={},
            )
        )
    send(dict(type="error", id=identity, code="sdk-error"))


def handle_request(request):
    identity = request["id"]
    operation = request["payload"]["operation"]
    if operation in ("duplicate", "flood"):
        flood_callbacks(identity, operation)
        return
    send(
        dict(
            type="result",
            id=identity,
            value=request["context"]["tenant"],
            reusable=True,
        )
    )


for line in sys.stdin:
    request = json.loads(line)
    if request["type"] != "invoke":
        continue
    handle_request(request)
