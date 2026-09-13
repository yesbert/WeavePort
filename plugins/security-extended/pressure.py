"""Finite Docker-only verification operations; not a general exploit plugin."""
import json
import os
import subprocess
import sys
import threading
import time


def send(request, value):
    print(json.dumps(dict(type="result", id=request["id"], value=value)), flush=True)


def run(request):
    op = request["operation"]
    if op == "workspace":
        if "text" in request["payload"]:
            with open("/tmp/security-marker", "w") as f:
                f.write(request["payload"]["text"])
        try:
            with open("/tmp/security-marker") as f:
                return f.read(4096)
        except FileNotFoundError:
            return ""
    if op == "cpu":
        end = time.monotonic() + 3
        while time.monotonic() < end:
            pass
        return dict(completed=True)
    if op == "memory":
        blocks = []
        for _ in range(96):
            blocks.append(bytearray(4 * 1024 * 1024))
        return dict(unexpectedSurvival=True)
    if op == "pids":
        children = []
        error = None
        try:
            for _ in range(80):
                try:
                    children.append(subprocess.Popen(["/bin/sleep", "3"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
                except OSError as e:
                    error = e.errno
                    break
            time.sleep(0.5)
            return dict(created=len(children), error=error)
        finally:
            for child in children:
                child.terminate()
            for child in children:
                child.wait(timeout=4)
    if op == "threads":
        threads = []
        release = threading.Event()
        denied = False
        try:
            for _ in range(80):
                try:
                    t = threading.Thread(target=lambda: release.wait(3))
                    t.start()
                    threads.append(t)
                except RuntimeError:
                    denied = True
                    break
            return dict(created=len(threads), denied=denied)
        finally:
            release.set()
            for t in threads:
                t.join(4)
    if op == "tmpfs":
        size = 0
        error = None
        try:
            with open("/tmp/pressure", "wb", buffering=0) as f:
                for _ in range(32):
                    size += f.write(b"x" * (1024 * 1024))
        except OSError as e:
            error = e.errno
        finally:
            if os.path.exists("/tmp/pressure"):
                os.unlink("/tmp/pressure")
        return dict(bytes=size, error=error)
    if op == "stdout":
        sys.stdout.write("x" * (2 * 1024 * 1024) + "\n")
        sys.stdout.flush()
        return None
    if op == "stderr":
        for _ in range(128):
            sys.stderr.write("x" * 65536)
        sys.stderr.flush()
        return dict(bytes=8 * 1024 * 1024)
    if op == "foreign":
        # Only the runner's synthetic victim PID is accepted, never arbitrary paths.
        pid = int(request["payload"]["victimPid"])
        if pid <= 1:
            raise ValueError("invalid test PID")
        results = {}
        for path in [f"/proc/{pid}/root/tmp/security-marker", "/var/run/docker.sock", "/ipc"]:
            try:
                fd = os.open(path, os.O_RDONLY)
                os.close(fd)
                results[path] = 0
            except OSError as e:
                results[path] = e.errno
        return results
    if op == "remember":
        global previous
        previous = request["id"]
        return True
    if op == "replay":
        print(json.dumps(dict(type="result", id=previous, value="stale")), flush=True)
        return None
    if op in ["callback-budget", "callback-duplicate"]:
        for index in range(16):
            callback_id = "same" if op == "callback-duplicate" else str(index)
            print(json.dumps(dict(type="callback", id=request["id"], callbackId=callback_id, operation="read", payload={})), flush=True)
            if not sys.stdin.readline():
                return None
        return dict(unexpectedSurvival=True)
    if op == "callback":
        print(json.dumps(dict(type="callback", id=request["id"], callbackId="one", operation=request["payload"]["operation"], payload=request["payload"].get("args", {}))), flush=True)
        return json.loads(sys.stdin.readline())["value"]
    return request["payload"]


print('{"type":"ready","protocol":1,"pluginVersion":"1"}', flush=True)
for line in sys.stdin:
    request = json.loads(line)
    try:
        result = run(request)
        if request["operation"] not in ["stdout", "replay"]:
            send(request, result)
    except Exception:
        print(json.dumps(dict(type="error", id=request["id"], code="fixture-error")), flush=True)
