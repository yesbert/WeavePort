"""Read-only, bounded Docker Engine one-shot memory sampling over a local Unix socket."""
from concurrent.futures import ThreadPoolExecutor, as_completed
import http.client
import json
import os
import socket
import subprocess
import time


class Engine:
    def __init__(self):
        endpoint = os.environ.get("DOCKER_HOST")
        if not endpoint or os.environ.get("DOCKER_CONTEXT"):
            command = ["docker", "context", "inspect"]
            if os.environ.get("DOCKER_CONTEXT"):
                command.append(os.environ["DOCKER_CONTEXT"])
            endpoint = json.loads(subprocess.check_output(command, text=True, timeout=10))[0]["Endpoints"]["docker"]["Host"]
        if not endpoint.startswith("unix://"):
            raise ValueError("Density memory observer requires a local Docker Unix socket")
        self.path = endpoint[7:]

    def get(self, path):
        connection = http.client.HTTPConnection("localhost", timeout=3)
        try:
            connection.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            connection.sock.settimeout(3)
            connection.sock.connect(self.path)
            connection.request("GET", path)
            response = connection.getresponse()
            if response.status == 404:
                response.read()
                return None
            if response.status != 200:
                raise RuntimeError("Docker memory API HTTP " + str(response.status))
            return json.load(response)
        finally:
            connection.close()


def memory_row(engine, container):
    data = engine.get("/containers/" + container["Id"] + "/stats?stream=false&one-shot=true")
    memory = (data or {}).get("memory_stats", {})
    if "usage" not in memory:
        state = engine.get("/containers/" + container["Id"] + "/json")
        if state and state.get("State", {}).get("Running"):
            raise ValueError("Live container has unavailable memory")
        return {"id": container["Id"], "names": container["Names"], "exitedDuringSample": True}
    usage = memory["usage"]
    stats = memory.get("stats", {})
    cache = stats.get("total_inactive_file", stats.get("inactive_file", 0))
    if not isinstance(usage, int) or usage < 0 or not isinstance(cache, int) or cache < 0:
        raise ValueError("Invalid Docker memory accounting")
    # Match Docker CLI cache subtraction while retaining the raw cgroup accounting separately.
    working = usage - cache if cache < usage else usage
    return {"id": container["Id"], "names": container["Names"], "read": data.get("read"),
            "usageBytes": usage, "workingSetBytes": working, "limitBytes": memory.get("limit"),
            "cpuTotalNs": data.get("cpu_stats", {}).get("cpu_usage", {}).get("total_usage")}


def snapshot(engine, known):
    start = time.monotonic()
    containers = engine.get("/containers/json")
    if not isinstance(containers, list):
        raise ValueError("Container inventory unavailable")
    rows = []
    pool = ThreadPoolExecutor(max_workers=8)
    futures = [pool.submit(memory_row, engine, container) for container in containers]
    try:
        for future in as_completed(futures, timeout=10):
            rows.append(future.result())
    finally:
        pool.shutdown(wait=True, cancel_futures=True)
    owned = [row for row in rows if any(name.lstrip("/") in known for name in row["names"])]
    valid = [row for row in rows if "usageBytes" in row]
    owned_valid = [row for row in owned if "usageBytes" in row]
    scale = 1048576
    return {"at": time.time(), "durationSeconds": time.monotonic() - start, "rows": rows,
            "owned": owned, "ownedMiB": sum(r["workingSetBytes"] for r in owned_valid) / scale,
            "ownedRawMiB": sum(r["usageBytes"] for r in owned_valid) / scale,
            "allContainersMiB": sum(r["workingSetBytes"] for r in valid) / scale,
            "allContainersRawMiB": sum(r["usageBytes"] for r in valid) / scale,
            "unavailableAfterExit": sum("exitedDuringSample" in row for row in owned)}
