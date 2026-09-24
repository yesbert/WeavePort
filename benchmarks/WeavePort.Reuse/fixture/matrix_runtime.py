import json
import os
from pathlib import Path
import signal
import sys
import time

if not Path("/.dockerenv").exists() or Path(__file__).resolve() != Path(
    "/fixture/matrix_runtime.py"
):
    raise RuntimeError("Disposable Docker fixture only")

# Trusted helpers only; customer plugin modules are imported inside the executing child.
from runtime_helpers import invoke, kernel_objects, processes, wipe


def run_child(request):
    import subprocess

    child = subprocess.Popen(
        [sys.executable, "-u", __file__, "child"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        start_new_session=True,
    )
    result = {"status": "failed", "reusable": False}
    try:
        output, _ = child.communicate(json.dumps(request) + "\n", timeout=1.5)
        if child.returncode == 0:
            result = json.loads(output)
    except (subprocess.TimeoutExpired, ValueError):
        pass
    finally:
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        child.wait(timeout=1)
        wipe()
    return result


def dispatch(request, mode, controller, baseline):
    if controller:
        result = controller.invoke(request)
    elif mode == "process":
        result = run_child(request)
    else:
        result = invoke(request)
    if processes() - baseline:
        result["reusable"] = False
        result["error"] = "descendants-remain"
    remaining_ipc = kernel_objects()
    if remaining_ipc:
        result["reusable"] = False
        result["error"] = "kernel-ipc-remains"
        result["kernelObjects"] = sorted(remaining_ipc)
    return result


def main():
    mode = sys.argv[1]
    controller = None
    if mode == "forkserver":
        from fork_boundary import ForkBoundary

        controller = ForkBoundary()
    print(json.dumps({"ready": True, "mode": mode}), flush=True)
    for line in sys.stdin:
        request = json.loads(line)
        baseline = processes()
        result = dispatch(request, mode, controller, baseline)
        result["id"] = request["id"]
        print(json.dumps(result, separators=(",", ":")), flush=True)
        if not result.get("reusable"):
            break


def cli():
    if sys.argv[1] == "child":
        print(json.dumps(invoke(json.loads(sys.stdin.readline()))), flush=True)
    else:
        main()


if __name__ == "__main__":
    cli()
