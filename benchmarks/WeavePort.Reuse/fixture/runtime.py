import json
import os
import shutil
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path
if not Path("/.dockerenv").exists() or Path(__file__).resolve() != Path("/fixture/runtime.py"):
    raise RuntimeError("This destructive-cleanup fixture may run only in its disposable Docker image")

import plugin_a
import plugin_b
from scope import Scope


def send(value):
    print(json.dumps(value, separators=(",", ":")), flush=True)


def processes():
    return {int(p.name) for p in Path("/proc").iterdir() if p.name.isdigit()}


def descriptors():
    result = set()
    for name in os.listdir("/proc/self/fd"):
        try:
            result.add((name, os.readlink("/proc/self/fd/" + name)))
        except FileNotFoundError:
            pass
    return result


def wipe():
    # Only the two writable fixture directories, never a host mount.
    for root in (Path("/tmp"), Path("/dev/shm")):
        for entry in root.iterdir():
            if entry.is_dir() and not entry.is_symlink():
                shutil.rmtree(entry)
            else:
                entry.unlink()


def operation(request, scope):
    op = request.get("op", "echo")
    if op == "echo":
        plugin = {"a": plugin_a, "b": plugin_b}[request["plugin"]]
        return plugin.execute(scope, request["payload"])
    if op == "managed":
        (scope.workspace / "canary").write_text(scope.tenant)
        os.environ["CUSTOMER_CANARY"] = scope.tenant
        scope.background(lambda stop: stop.wait(30))
        pid = scope.child([sys.executable, "-c", "import time;time.sleep(30)"])
        return {"pid": pid}
    if op == "probe":
        return {"hidden": plugin_a._hidden, "environment": os.getenv("CUSTOMER_CANARY"),
                "files": [str(p) for root in (Path('/tmp'), Path('/dev/shm')) for p in root.iterdir() if p != scope.workspace],
                "oldPidAlive": request.get("oldPid", -1) in processes()}
    if op == "authority":
        granted = scope.call_host("read")
        try:
            scope.call_host("admin")
            denied = False
        except PermissionError:
            denied = True
        scope.active = False
        try:
            scope.call_host("read")
            expired = False
        except PermissionError:
            expired = True
        return {"granted": granted, "denied": denied, "expired": expired}
    if op == "unmanaged_file":
        Path('/dev/shm/canary').write_text(scope.tenant)
        Path('/tmp/canary').write_text(scope.tenant)
        return None
    if op == "hidden":
        plugin_a.leave_hidden_canary(scope.tenant)
        return None
    if op == "cleanup_failure":
        scope.on_close(lambda: (_ for _ in ()).throw(RuntimeError("deliberate")))
        return None
    if op == "untracked_thread":
        threading.Thread(target=lambda: time.sleep(30), daemon=True).start()
        return None
    if op == "escaped_child":
        # Escape the invocation process group; the supervisor must refuse reuse.
        child = subprocess.Popen([sys.executable, '-c', 'import time;time.sleep(30)'], start_new_session=True,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        return {"pid": child.pid}
    if op == "hang":
        time.sleep(30)
    if op == "crash":
        os._exit(17)
    raise ValueError("Unknown fixture operation")


def invoke(request):
    start = time.perf_counter()
    environment, cwd = dict(os.environ), os.getcwd()
    before_threads, before_fds = set(threading.enumerate()), descriptors()
    scope = Scope(request["tenant"], request.get("grants", ["read"]))
    result, error = None, None
    try:
        result = operation(request, scope)
    except Exception as caught:
        error = type(caught).__name__
    executing = time.perf_counter() - start
    cleaned = time.perf_counter()
    reusable = True
    try:
        scope.close()
        os.chdir(cwd)
        os.environ.clear()
        os.environ.update(environment)
        wipe()
        if set(threading.enumerate()) - before_threads or descriptors() - before_fds:
            raise RuntimeError("Untracked live resources")
    except Exception as caught:
        reusable, error = False, "cleanup-" + type(caught).__name__
    return {"status": "ok" if error is None else "failed", "value": result, "reusable": reusable,
            "executionMs": executing * 1000, "cleanupMs": (time.perf_counter() - cleaned) * 1000}


def isolated(request):
    baseline = processes()
    start = time.perf_counter()
    child = subprocess.Popen([sys.executable, '-u', __file__, 'child'], stdin=subprocess.PIPE,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
    result = {"status": "failed", "reusable": False}
    try:
        output, _ = child.communicate(json.dumps(request) + '\n', timeout=1.5)
        if child.returncode == 0:
            result = json.loads(output)
    except subprocess.TimeoutExpired:
        result["error"] = "timeout"
    finally:
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        child.wait(timeout=.5)
    cleaned = time.perf_counter()
    leftovers = processes() - baseline
    if leftovers:
        result["reusable"] = False
        result["error"] = "descendants-remain"
    wipe()
    result["processTurnoverMs"] = (time.perf_counter() - start) * 1000
    result["supervisorCleanupMs"] = (time.perf_counter() - cleaned) * 1000
    return result


if __name__ == '__main__':
    if sys.argv[1] == 'child':
        send(invoke(json.loads(sys.stdin.readline())))
    else:
        mode = sys.argv[1]
        send({"ready": True, "mode": mode})
        for line in sys.stdin:
            request = json.loads(line)
            baseline = processes()
            result = isolated(request) if mode == 'process' else invoke(request)
            if processes() - baseline:
                result["reusable"] = False
                result["error"] = "descendants-remain"
            result["id"] = request["id"]
            send(result)
            if not result["reusable"]:
                break
