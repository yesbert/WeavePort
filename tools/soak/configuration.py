"""Bounded command-line configuration for the native soak supervisor."""

import argparse
import math
from pathlib import Path
import platform
import shutil
from support import startup_limit


def parse_options():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seconds", type=int, default=14400)
    parser.add_argument("--clients", type=int, default=24)
    parser.add_argument(
        "--max-concurrent-starts",
        type=int,
        help="Fixture startup slots (default: client count); does not change library defaults",
    )
    parser.add_argument("--pace-ms", type=int, default=100)
    parser.add_argument("--p99-ms", type=float, default=2000)
    parser.add_argument("--max-rss-mib", type=float, default=8192)
    parser.add_argument("--max-growth-mib", type=float, default=2048)
    parser.add_argument("--growth-after", type=int, default=300)
    parser.add_argument("--sample-seconds", type=float, default=5)
    parser.add_argument("--output", type=Path)
    parser.add_argument(
        "--inject-failure",
        action="store_true",
        help="Negative control: fail payload validation",
    )
    args = parser.parse_args()
    try:
        args.max_concurrent_starts = startup_limit(
            args.clients, args.max_concurrent_starts
        )
    except ValueError as error:
        parser.error(str(error))
    if not (
        1 <= args.seconds <= 86400
        and 3 <= args.clients <= 48
        and 1 <= args.pace_ms <= 60000
        and args.p99_ms > 0
        and args.max_rss_mib > 0
        and args.max_growth_mib > 0
        and args.growth_after >= 0
        and 0.2 <= args.sample_seconds <= 60
    ):
        parser.error("Invalid bounded soak configuration")
    if not all(
        math.isfinite(value)
        for value in (
            args.p99_ms,
            args.max_rss_mib,
            args.max_growth_mib,
            args.sample_seconds,
        )
    ):
        parser.error("Resource and latency limits must be finite")
    if platform.system() not in ("Darwin", "Linux"):
        parser.error(
            "Supervisor requires macOS/Linux process groups; only macOS is currently qualified"
        )
    executables = {name: shutil.which(name) for name in ("k6", "dotnet", "node")}
    if not all(executables.values()):
        parser.error("Install k6 and repository runtimes first")
    return args, executables
