"""Shared wire requests, subprocess execution and evidence formatting for reuse probes."""

import asyncio
import json
import math


def write(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n")


async def command(*args, timeout=15):
    proc = await asyncio.create_subprocess_exec(
        *args, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE
    )
    try:
        out, err = await asyncio.wait_for(proc.communicate(), timeout)
    except BaseException:
        if proc.returncode is None:
            proc.kill()
        await proc.wait()
        raise
    if proc.returncode:
        raise RuntimeError(err.decode()[-1000:])
    return out.decode().strip()


def request(number, lanes=1, op="echo", **extras):
    return {
        "id": str(number),
        "tenant": "customer-" + str(number),
        "plugin": "a" if (number // lanes) % 2 == 0 else "b",
        "payload": "Abc123xy" * 8,
        "grants": ["read"],
        "op": op,
        **extras,
    }


def correct(row):
    r, outcome = row["request"], row["outcome"]
    expected = r["payload"].upper() if r["plugin"] == "a" else r["payload"][::-1]
    return (
        outcome.get("status") == "ok"
        and outcome.get("reusable")
        and outcome.get("value")
        == {"tenant": r["tenant"], "plugin": r["plugin"], "value": expected}
    )


def quantiles(values):
    if not values:
        return None
    ordered = sorted(values)
    return {
        "count": len(values),
        "mean": sum(values) / len(values),
        **{
            key: ordered[max(0, math.ceil(len(values) * q) - 1)]
            for key, q in [("p50", 0.5), ("p95", 0.95), ("p99", 0.99)]
        },
        "max": ordered[-1],
    }
