"""Wire-level C# author SDK checks; no host implementation is substituted."""

import base64
import io
import json
import os
import pathlib
import queue
import threading
import subprocess
import time
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
DLL = pathlib.Path(
    os.environ.get(
        "WP_CONCURRENT_SDK_DLL",
        str(
            ROOT
            / "tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll"
        ),
    )
)


class Worker:
    def __init__(self, shared=False, arguments=()):
        self.process = subprocess.Popen(
            ["dotnet", str(DLL)] + (["shared"] if shared else []) + list(arguments),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
        )
        self.lines = queue.Queue()
        threading.Thread(target=self.receive, daemon=True).start()
        ready = self.read()
        self.ready = ready
        assert ready["protocol"] == (2 if shared else 1)
        if shared:
            self.send({"type": "configure", "degree": 4})

    def receive(self):
        for line in self.process.stdout:
            self.lines.put(line)
        self.lines.put(None)

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
        self.send(
            dict(
                type="invoke",
                id=id,
                operation=operation,
                payload=payload,
                context=dict(tenant=tenant, configuration={}),
            )
        )

    def call(self, id, operation, input, tenant="A"):
        self.invoke(id, "$sdk.call", dict(operation=operation, input=input), tenant)

    def close(self):
        self.process.kill()
        self.process.wait()
        self.process.stdin.close()
        self.process.stdout.close()
        self.process.stderr.close()


def check_identity_and_cancellation(worker):
    worker.call("slow", "identity", {"delay": 300}, "A")
    worker.call("fast", "identity", {"delay": 0}, "B")
    fast, slow = worker.read(), worker.read()
    assert fast["id"] == "fast" and fast["value"] == {"tenant": "B", "current": "B"}
    assert slow["value"] == {"tenant": "A", "current": "A"}
    worker.call("cancel", "identity", {"delay": 200, "cleanupDelay": 200})
    start = time.monotonic()
    worker.send(dict(type="cancel", id="cancel"))
    worker.call("healthy", "identity", {"delay": 0}, "B")
    assert worker.read()["id"] == "healthy"
    cancelled = worker.read()
    assert cancelled["type"] == "cancelled" and time.monotonic() - start >= 0.35


def check_callback_routing(worker):
    worker.call("cb1", "callback", {"tenant": "A"})
    worker.call("cb2", "callback", {"tenant": "B"}, "B")
    callbacks = [worker.read(), worker.read()]
    for frame in reversed(callbacks):
        worker.send(
            dict(
                type="callback-result",
                id=frame["id"],
                callbackId=frame["callbackId"],
                value=frame["payload"],
            )
        )
    results = {f["id"]: f["value"] for f in [worker.read(), worker.read()]}
    assert results == {"cb1": {"tenant": "A"}, "cb2": {"tenant": "B"}}


def check_failure_isolation(worker):
    worker.call("bad", "failure", {})
    assert worker.read()["code"] == "sdk-error"
    worker.call("good", "identity", {"delay": 0})
    assert worker.read()["type"] == "result"
    worker.send(dict(type="cancel", id="good"))
    worker.call("after-late-cancel", "identity", {"delay": 0})
    assert worker.read()["id"] == "after-late-cancel"
    worker.call("denied", "callback", {})
    callback = worker.read()
    worker.send(
        dict(
            type="callback-result",
            id="denied",
            callbackId=callback["callbackId"],
            value=None,
            error="denied",
        )
    )
    assert worker.read()["code"] == "sdk-error"


def check_retired_callbacks(worker):
    # Keep an independent callback live while cancelled callbacks retire without replies.
    worker.call("still-active", "callback", {"owner": "B"}, "B")
    active = worker.read()
    retired = []
    for index in range(4100):
        invocation = f"cancel-callback-{index}"
        worker.call(invocation, "callback", {})
        pending = worker.read()
        assert pending["type"] == "callback"
        retired.append((invocation, pending["callbackId"]))
        worker.send(dict(type="cancel", id=invocation))
        assert worker.read() == dict(type="cancelled", id=invocation)
    worker.send(
        dict(
            type="callback-result",
            id=active["id"],
            callbackId=active["callbackId"],
            value={"owner": "B"},
        )
    )
    assert worker.read()["value"] == {"owner": "B"}
    recent_id, recent_callback = retired[-1]
    worker.send(
        dict(type="callback-result", id=recent_id, callbackId=recent_callback, value={})
    )
    worker.call("after-late-reply", "identity", {"delay": 0})
    assert worker.read()["id"] == "after-late-reply"
    expired_id, expired_callback = retired[0]
    worker.send(
        dict(
            type="callback-result", id=expired_id, callbackId=expired_callback, value={}
        )
    )
    assert (
        worker.process.wait(timeout=5) != 0
    ), "Expired callback identities must retire the channel"


def check_shared():
    worker = Worker(shared=True)
    try:
        check_identity_and_cancellation(worker)
        check_callback_routing(worker)
        check_failure_isolation(worker)
        check_retired_callbacks(worker)
    finally:
        worker.close()


def check_live_stream(worker):
    worker.invoke("open", "$sdk.start", dict(operation="live", input={}))
    stream = worker.read()["value"]["stream"]
    start = time.monotonic()
    worker.invoke("first", "$sdk.next", dict(stream=stream))
    assert worker.read()["value"] == dict(items=[1], done=False)
    assert time.monotonic() - start < 0.25
    worker.invoke("heartbeat", "$sdk.next", dict(stream=stream))
    assert worker.read()["value"] == dict(items=[], done=False)
    time.sleep(0.3)
    worker.invoke("second", "$sdk.next", dict(stream=stream))
    frame = worker.read()
    assert frame["type"] == "callback" and frame["id"] == "second"
    worker.send(
        dict(
            type="callback-result",
            id="second",
            callbackId=frame["callbackId"],
            value={},
        )
    )
    assert worker.read()["value"] == dict(items=[2], done=True)


def check_immediate_callback(worker):
    worker.invoke(
        "immediate-start", "$sdk.start", dict(operation="immediateCallback", input={})
    )
    immediate = worker.read()["value"]["stream"]
    worker.invoke("immediate-first", "$sdk.next", dict(stream=immediate))
    first = worker.read()
    assert first["type"] == "result" and first["value"] == dict(items=[1], done=False)
    worker.invoke("immediate-second", "$sdk.next", dict(stream=immediate))
    callback = worker.read()
    assert callback["type"] == "callback" and callback["id"] == "immediate-second"
    worker.send(
        dict(
            type="callback-result",
            id="immediate-second",
            callbackId=callback["callbackId"],
            value={},
        )
    )
    assert worker.read()["value"] == dict(items=[2], done=True)


def check_early_close(worker):
    worker.invoke(
        "early-start", "$sdk.start", dict(operation="immediateCallback", input={})
    )
    early = worker.read()["value"]["stream"]
    worker.invoke("early-first", "$sdk.next", dict(stream=early))
    assert worker.read()["value"] == dict(items=[1], done=False)
    worker.invoke("early-close", "$sdk.close", dict(stream=early))
    closed = worker.read()
    assert closed["type"] == "result" and closed["reusable"]
    worker.call("after-early-close", "identity", {"delay": 0})
    assert worker.read()["type"] == "result"


def check_source_bytes(worker):
    worker.invoke(
        "source", "$sdk.source.open", dict(operation="bytes", input=dict(bytes=1000000))
    )
    source = worker.read()["value"]["source"]
    data = bytearray()
    for i in range(1000):
        worker.invoke(str(i), "$sdk.source.read", dict(source=source, chunkBytes=65536))
        frame = worker.read()
        block = frame["value"]
        data.extend(base64.b64decode(block["data"]))
        if block["done"]:
            assert frame["reusable"]
            break
    assert data == bytes(i % 256 for i in range(1000000))


def check_source_cleanup(worker):
    worker.invoke(
        "bad-source", "$sdk.source.open", dict(operation="badDisposal", input={})
    )
    source = worker.read()["value"]["source"]
    worker.invoke(
        "bad-source-read", "$sdk.source.read", dict(source=source, chunkBytes=65536)
    )
    assert worker.read()["code"] == "cleanup-error"


def check_streams():
    worker = Worker(shared=False)
    try:
        check_live_stream(worker)
        check_immediate_callback(worker)
        check_early_close(worker)
        check_source_bytes(worker)
        check_source_cleanup(worker)
    finally:
        worker.close()


def archive():
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_STORED) as target:
        for name in ("a.xml", "b.xml"):
            target.writestr(name, bytes(range(256)) * 400)
    return buffer.getvalue()


def file_call(worker, id, operation, data, outputs=True, refuse=None):
    """Invoke a file function and serve its file callbacks from memory; returns (terminal, written)."""
    files = dict(inputs=[dict(name="sheet", length=len(data), mediaType="application/zip")])
    if outputs:
        files["outputs"] = dict(maximumCount=1, maximumBytes=1 << 24)
    worker.send(
        dict(
            type="invoke",
            id=id,
            operation="$sdk.call",
            payload=dict(operation=operation, input={}),
            context=dict(tenant="A", configuration={}),
            files=files,
        )
    )
    written = bytearray()
    while True:
        frame = worker.read()
        if frame["type"] != "callback":
            return frame, bytes(written)
        reply = dict(type="callback-result", id=id, callbackId=frame["callbackId"], value={})
        reply.update(file_reply(frame, data, written, refuse))
        worker.send(reply)


def file_reply(frame, data, written, refuse):
    operation, payload = frame["operation"], frame["payload"]
    if operation == refuse:
        return dict(error="output-not-permitted")
    if operation == "$sdk.input.read":
        block = data[payload["offset"] : payload["offset"] + payload["length"]]
        return dict(value=dict(data=base64.b64encode(block).decode()))
    if operation == "$sdk.output.write":
        written += base64.b64decode(payload["data"])
    if operation == "$sdk.output.complete":
        return dict(value=dict(length=len(written)))
    return {}


def check_files():
    worker = Worker(arguments=["--file-worker"])
    try:
        assert worker.ready["files"] == 1, "Exclusive C# workers must advertise files"
        data = archive()
        summary, _ = file_call(worker, "summary", "zip-summary", data, outputs=False)
        assert summary["value"]["names"] == ["a.xml", "b.xml"], summary
        copied, written = file_call(worker, "copy", "copy", data)
        assert copied["type"] == "result" and written == data
        cached, _ = file_call(worker, "cached", "cached-reads", data, outputs=False)
        assert cached["type"] == "result"
        refusals, _ = file_call(worker, "refusals", "refusals", data, outputs=False)
        assert refusals["value"]["codes"] == ["input-not-found", "output-not-permitted"]
        refused, _ = file_call(worker, "refused", "copy", data, refuse="$sdk.output.create")
        assert refused["type"] == "error" and refused["code"] == "sdk-error"
        file_call(worker, "store", "late-store", data, outputs=False)
        late, _ = file_call(worker, "late", "late-read", data, outputs=False)
        assert late["value"]["refused"] is True
    finally:
        worker.close()


if __name__ == "__main__":
    check_shared()
    check_streams()
    check_files()
    print(
        "C# concurrent SDK, cancellation cleanup, callback routing, live stream, source and function file checks passed"
    )
