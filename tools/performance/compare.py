"""Compare complete SDK BenchmarkDotNet runs; retain distributions, not just ratios."""

import argparse
import json
from pathlib import Path


def read(path):
    cells = {}
    environment = None
    for filename in (
        "WeavePort.Benchmarks.SdkBenchmarks-report-full.json",
        "WeavePort.Benchmarks.SdkColdBenchmarks-report-full.json",
    ):
        report = json.loads((path / "results" / filename).read_text())
        environment = report["HostEnvironmentInfo"]
        append_cells(cells, report["Benchmarks"])
    if len(cells) != 30:
        raise ValueError("Expected all 30 SDK timing cells")
    return environment, cells


def append_cells(cells, benchmarks):
    for cell in benchmarks:
        if not cell.get("Statistics") or cell["Statistics"]["N"] < 3:
            raise ValueError("Incomplete benchmark: " + cell["Parameters"])
        key = cell["Type"] + ":" + cell["Parameters"]
        cells[key] = {
            "parameters": cell["Parameters"],
            "statistics": cell["Statistics"],
            "memory": cell.get("Memory"),
            "measurements": [
                m
                for m in cell["Measurements"]
                if m["IterationStage"] == "Actual"
                and m["IterationMode"] in ("Workload", "Result")
            ],
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    before_env, before = read(args.before)
    after_env, after = read(args.after)
    if before.keys() != after.keys():
        raise ValueError("Benchmark cells differ")
    comparisons = []
    for key in sorted(before):
        a, b = before[key], after[key]
        comparisons.append(
            {
                "cell": key,
                "before": a,
                "after": b,
                "meanChangePercent": (
                    b["statistics"]["Mean"] / a["statistics"]["Mean"] - 1
                )
                * 100,
            }
        )
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "comparison.json").write_text(
        json.dumps(
            {
                "beforeEnvironment": before_env,
                "afterEnvironment": after_env,
                "cells": comparisons,
            },
            indent=2,
        )
    )
    lines = [
        "# Optimization comparison",
        "",
        "Complete SDK operations; arithmetic means, not request percentiles. Six requested iterations per cell; retain dispersion and outlier exclusions. Cold includes setup, provenance recording and disposal. Allocations cover the measured caller process only.",
        "",
        "| Case | Before ms | After ms | Mean change | Before allocated B/op | After allocated B/op |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for row in comparisons:
        a, b = row["before"], row["after"]
        am, bm = a.get("memory") or {}, b.get("memory") or {}
        lines.append(
            f"| {row['cell']} | {a['statistics']['Mean']/1e6:.3f} | {b['statistics']['Mean']/1e6:.3f} | {row['meanChangePercent']:+.1f}% | {am.get('BytesAllocatedPerOperation', '—')} | {bm.get('BytesAllocatedPerOperation', '—')} |"
        )
    (args.output / "README.md").write_text("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
