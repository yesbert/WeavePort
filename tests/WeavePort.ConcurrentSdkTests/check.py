"""Wire-level C# author SDK checks; no host implementation is substituted."""
import json
import os
import pathlib
import queue
import threading
import subprocess
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
DLL = pathlib.Path(os.environ.get("WP_CONCURRENT_SDK_DLL", str(ROOT / "tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll")))

class Worker:
    def __init__(self, shared=False):
        self.process = subprocess.Popen(["dotnet", str(DLL)] + (["shared"] if shared else []), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
        self.lines = queue.Queue()
        def receive():
            for line in self.process.stdout:
                self.lines.put(line)
            self.lines.put(None)
        threading.Thread(target=receive, daemon=True).start()
        ready = self.read()
        assert ready["protocol"] == (2 if shared else 1)
        if shared:
            self.send({"type": "configure", "degree": 4})
    def send(self, frame):
        self.process.stdin.write(json.dumps(frame) + "\n")
        self.process.stdin.flush()
    def read(self):
        # The watchdog also detects deadlocks in stream callback routing.
        try:
            line = self.lines.get(timeout=5)
        except queue.Empty:
            raise AssertionError("Worker did not respond") from None
        assert line, self.process.stderr.read()
        return json.loads(line)
    def invoke(self, id, operation, payload, tenant="A"):
        self.send(dict(type="invoke", id=id, operation=operation, payload=payload, context=dict(tenant=tenant, configuration={})))
    def call(self, id, operation, input, tenant="A"):
        self.invoke(id, "$sdk.call", dict(operation=operation, input=input), tenant)
    def close(self):
        self.process.kill()
        self.process.wait()

w = Worker(True)
try:
    w.call("slow", "identity", {"delay": 300}, "A")
    w.call("fast", "identity", {"delay": 0}, "B")
    fast, slow = w.read(), w.read()
    assert fast["id"] == "fast" and fast["value"] == {"tenant": "B", "current": "B"}
    assert slow["value"] == {"tenant": "A", "current": "A"}
    w.call("cancel", "identity", {"delay": 200, "cleanupDelay": 200})
    start = time.monotonic()
    w.send(dict(type="cancel", id="cancel"))
    w.call("healthy", "identity", {"delay": 0}, "B")
    assert w.read()["id"] == "healthy"
    cancelled = w.read()
    assert cancelled["type"] == "cancelled" and time.monotonic() - start >= .35
    w.call("cb1", "callback", {"tenant": "A"})
    w.call("cb2", "callback", {"tenant": "B"}, "B")
    callbacks = [w.read(), w.read()]
    for frame in reversed(callbacks):
        w.send(dict(type="callback-result", id=frame["id"], callbackId=frame["callbackId"], value=frame["payload"]))
    results = {f["id"]: f["value"] for f in [w.read(), w.read()]}
    assert results == {"cb1": {"tenant": "A"}, "cb2": {"tenant": "B"}}
    w.call("bad", "failure", {})
    assert w.read()["code"] == "sdk-error"
    w.call("good", "identity", {"delay": 0})
    assert w.read()["type"] == "result"
    w.send(dict(type="cancel", id="good"))
    w.call("after-late-cancel", "identity", {"delay": 0})
    assert w.read()["id"] == "after-late-cancel"
    w.call("denied", "callback", {})
    callback = w.read()
    w.send(dict(type="callback-result", id="denied", callbackId=callback["callbackId"], value=None, error="denied"))
    assert w.read()["code"] == "sdk-error"
finally:
    w.close()

w = Worker()
try:
    w.invoke("open", "$sdk.start", dict(operation="live", input={}))
    stream = w.read()["value"]["stream"]
    start = time.monotonic()
    w.invoke("first", "$sdk.next", dict(stream=stream))
    assert w.read()["value"] == dict(items=[1], done=False)
    assert time.monotonic() - start < .25
    w.invoke("heartbeat", "$sdk.next", dict(stream=stream))
    assert w.read()["value"] == dict(items=[], done=False)
    time.sleep(.3)
    w.invoke("second", "$sdk.next", dict(stream=stream))
    frame = w.read()
    assert frame["type"] == "callback" and frame["id"] == "second"
    w.send(dict(type="callback-result", id="second", callbackId=frame["callbackId"], value={}))
    assert w.read()["value"] == dict(items=[2], done=True)
    w.invoke("source", "$sdk.source.open", dict(operation="bytes", input=dict(bytes=1000000)))
    source = w.read()["value"]["source"]
    import base64
    data = bytearray()
    for i in range(1000):
        w.invoke(str(i), "$sdk.source.read", dict(source=source, chunkBytes=65536))
        frame = w.read()
        block = frame["value"]
        data.extend(base64.b64decode(block["data"]))
        if block["done"]:
            assert frame["reusable"]
            break
    assert data == bytes(i % 256 for i in range(1000000))
    w.invoke("bad-source", "$sdk.source.open", dict(operation="badDisposal", input={}))
    source = w.read()["value"]["source"]
    w.invoke("bad-source-read", "$sdk.source.read", dict(source=source, chunkBytes=65536))
    assert w.read()["code"] == "cleanup-error"
finally:
    w.close()
print("C# concurrent SDK, cancellation cleanup, callback routing, live stream and source checks passed")
