"""Shared file, identity and host-headroom operations for density experiments."""

import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def command(*args, timeout=30):
    return subprocess.check_output(
        args, cwd=ROOT, text=True, stderr=subprocess.PIPE, timeout=timeout
    ).strip()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def mib(value):
    match = re.match(r"([0-9.]+)([a-zA-Z]+)", value.strip())
    if not match:
        raise ValueError("Unknown memory unit: " + value)
    scale = {
        "B": 1 / 1048576,
        "kB": 1000 / 1048576,
        "KiB": 1 / 1024,
        "MB": 1000000 / 1048576,
        "MiB": 1,
        "GB": 1000000000 / 1048576,
        "GiB": 1024,
    }
    return float(match[1]) * scale[match[2]]


def pressure():
    text = command("/usr/bin/memory_pressure", "-Q")
    found = re.search(r"System-wide memory free percentage:\s*(\d+)%", text)
    if not found:
        raise RuntimeError("macOS memory headroom unavailable")
    swap = command("/usr/sbin/sysctl", "-n", "vm.swapusage")
    used = re.search(r"used = ([0-9.]+)M", swap)
    return {
        "freePercent": int(found[1]),
        "swapUsedMiB": float(used[1]) if used else None,
    }


def refresh_known(folder, known, position):
    resource = folder / "resources.jsonl"
    if not resource.exists():
        return position
    with resource.open() as stream:
        stream.seek(position)
        while True:
            begin = stream.tell()
            line = stream.readline()
            if not line or not line.endswith("\n"):
                return begin
            known.update(json.loads(line)["instances"])
