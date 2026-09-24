import base64
import json
import os
import subprocess
import sys
import time

if endpoint := os.getenv("WEAVEPORT_SOCKET"):
    import socket

    channel = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    channel.connect(endpoint)
    sys.stdin = channel.makefile("r", encoding="utf-8")
    sys.stdout = channel.makefile("w", encoding="utf-8")

counter = 0


def send(value):
    print(json.dumps(value, separators=(",", ":")), flush=True)


def callback(request, operation, payload):
    callback.counter += 1
    cid = str(callback.counter)
    send(
        dict(
            type="callback",
            id=request["id"],
            callbackId=cid,
            operation=operation,
            payload=payload,
        )
    )
    reply = json.loads(sys.stdin.readline())
    if reply["id"] != request["id"] or reply["callbackId"] != cid:
        raise ValueError("callback identity")
    return reply["value"]


callback.counter = 0


def hang_operation(request):
    payload = request["payload"]
    while True:
        pass


def memory_operation(request):
    payload = request["payload"]
    blocks = []
    while True:
        blocks.append(bytearray(8 * 1024 * 1024))


def callback_flood_operation(request):
    payload = request["payload"]
    for _ in range(payload.get("count", 10)):
        callback(request, "documents.read", {})
    return None


def workspace_operation(request):
    payload = request["payload"]
    path = os.path.join(os.getenv("WEAVEPORT_WORKSPACE", "/tmp"), "state")
    if "text" in payload:
        with open(path, "w") as stream:
            stream.write(payload["text"])
    if not os.path.exists(path):
        return ""
    with open(path) as stream:
        return stream.read()


def execute(request):
    global counter
    payload = request["payload"]
    op = request["operation"]
    if op == "environment":
        return dict(parentSecretPresent="WEAVEPORT_PARENT_CANARY" in os.environ)
    if op == "trace":
        return request["traceId"]
    if op == "echo":
        return payload
    if op == "bulk-map":
        return dict(
            data=base64.b64encode(
                base64.b64decode(payload["data"], validate=True).replace(b"a", b"A")
            ).decode("ascii")
        )
    if op == "search":
        docs = callback(request, "documents.read", payload)
        return sorted(
            [d for d in docs if payload.get("query", "").lower() in d["text"].lower()],
            key=lambda d: d["id"],
        )
    if op == "context":
        return request["context"]
    if op == "counter":
        counter += 1
        return counter
    if op == "reduce":
        return dict(
            state=payload["state"] + payload["amount"],
            events=[dict(kind="changed", at=payload["now"])],
        )
    if op == "delay":
        time.sleep(payload["ms"] / 1000)
        return payload
    if op == "crash":
        os._exit(17)
    if op == "exception":
        raise ValueError("deliberate failure")
    if op == "hang":
        return hang_operation(request)
    if op == "memory":
        return memory_operation(request)
    if op == "oversize":
        return "x" * (2 * 1024 * 1024)
    if op == "malformed":
        print("not-json", flush=True)
        return None
    if op == "callback":
        return callback(request, payload["operation"], payload.get("args", {}))
    if op == "callback-flood":
        return callback_flood_operation(request)
    if op == "workspace":
        return workspace_operation(request)
    if op == "subprocess":
        return subprocess.check_output(["/bin/sh", "-c", "printf child-ok"], text=True)
    raise ValueError("unknown operation")


send(dict(type="ready", protocol=1, pluginVersion=os.getenv("PLUGIN_VERSION", "1")))
for line in sys.stdin:
    request = json.loads(line)
    try:
        send(dict(type="result", id=request["id"], value=execute(request)))
    except Exception:
        send(dict(type="error", id=request["id"], code="plugin-error"))
