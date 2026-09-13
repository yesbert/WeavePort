"""Bounded adversarial test fixture. Never a store or production plugin."""
import json
import os
import socket
import sys

if endpoint := os.getenv("WEAVEPORT_SOCKET"):
    channel = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    channel.connect(endpoint)
    sys.stdin = channel.makefile("r", encoding="utf-8")
    sys.stdout = channel.makefile("w", encoding="utf-8")


def send(value):
    print(json.dumps(value, separators=(",", ":")), flush=True)


def execute(request):
    operation, payload = request["operation"], request["payload"]
    if operation == "echo":
        return payload
    if operation == "context":
        return request["context"]
    if operation == "workspace":
        if "text" in payload:
            with open("/tmp/security-marker", "w") as output:
                output.write(payload["text"])
        try:
            with open("/tmp/security-marker") as source:
                return source.read(4096)
        except FileNotFoundError:
            return ""
    if operation == "probe":
        results = {}
        for path in ["/var/run/docker.sock", "/proc/1/root/var/run/docker.sock", "/ipc", "/run/weaveport/../ipc"]:
            try:
                descriptor = os.open(path, os.O_RDONLY)
                os.close(descriptor)
                results[path] = "opened"
            except OSError as error:
                results[path] = error.errno
        try:
            with open("/weaveport-forbidden", "w") as output:
                output.write("synthetic")
            results["rootWrite"] = "written"
        except OSError as error:
            results["rootWrite"] = error.errno
        try:
            os.setuid(0)
            results["setuid"] = "succeeded"
        except OSError as error:
            results["setuid"] = error.errno
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as connection:
            connection.settimeout(0.25)
            results["externalConnect"] = connection.connect_ex(("192.0.2.1", 9))
        return results
    if operation == "callback":
        send(dict(type="callback", id=request["id"], callbackId="one", operation=payload["operation"],
                  payload=payload["args"], context=dict(tenant="security-B"), tenant="security-B"))
        return json.loads(sys.stdin.readline())["value"]
    if operation == "hostile":
        # The harness supplies synthetic envelope cases; substitute only a JSON string literal.
        print(payload["frame"].replace("\"$id\"", json.dumps(request["id"])), flush=True)
        return None
    raise ValueError("Unknown security operation")


print('{"type":"ready","protocol":' + os.getenv("WEAVEPORT_TEST_READY_PROTOCOL", "1") + ',"pluginVersion":"1"}', flush=True)
for line in sys.stdin:
    request = json.loads(line)
    try:
        value = execute(request)
        if request["operation"] != "hostile":
            send(dict(type="result", id=request["id"], value=value))
    except Exception:
        send(dict(type="error", id=request["id"], code="fixture-error"))
