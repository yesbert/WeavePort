"""Retain a compact, complete successful run without copying build or process logs."""

import argparse
from itertools import product
import json
from pathlib import Path
from urllib.parse import parse_qsl


def read(path):
    return json.loads(path.read_text())


def collect_timing(benchmarks):
    timing = []
    for row in benchmarks:
        if not row["Statistics"] or not row["Statistics"]["N"]:
            raise ValueError("Missing timing observations")
        timing.append(
            dict(
                kind="cold" if "Cold" in row["Type"] else "warm",
                parameters=dict(parse_qsl(row["Parameters"])),
                statisticsNs=row["Statistics"],
                memory=row.get("Memory"),
                measurements=[
                    m
                    for m in row["Measurements"]
                    if m["IterationMode"] == "Workload"
                    and m["IterationStage"] in ("Actual", "Result")
                ],
            )
        )
    return timing


def merge_fixture_hashes(fixtures, identity, root):
    for name, digest in identity["artifacts"].items():
        name = Path(name).relative_to(root).as_posix()
        if name in fixtures and fixtures[name] != digest:
            raise ValueError("Fixture changed during run: " + name)
        fixtures[name] = digest


def render_range(rows, key, divisor):
    values = [row[key] / divisor for row in rows]
    return f"{min(values):.2f}–{max(values):.2f}"


def collect(run):
    provenance = read(run / "result.json")
    if provenance["status"] != "passed" or any(
        s["exitCode"] for s in provenance["stages"]
    ):
        raise ValueError("Only a complete successful run can become current evidence")
    if not provenance.get("loadedAuthors"):
        raise ValueError("Author SDK identity verification is required")
    timing = []
    for file in sorted((run / "bdn/results").glob("*-report-full.json")):
        report = read(file)
        provenance["benchmarkEnvironment"] = report["HostEnvironmentInfo"]
        timing.extend(collect_timing(report["Benchmarks"]))
    warm = {
        tuple(r["parameters"].get(k) for k in ("Language", "Topology", "Case"))
        for r in timing
        if r["kind"] == "warm"
    }
    cold = {
        tuple(r["parameters"].get(k) for k in ("Language", "Topology"))
        for r in timing
        if r["kind"] == "cold"
    }
    languages, topologies = ("csharp", "python", "typescript"), ("local", "gateway")
    if (
        warm
        != {
            (a, b, c)
            for a in languages
            for b in topologies
            for c in ("tiny", "callback", "list-64k", "list-8m")
        }
        or cold != {(a, b) for a in languages for b in topologies}
        or len(timing) != 30
    ):
        raise ValueError("Incomplete or duplicate benchmark matrix")
    loads = [read(p) for p in sorted(run.glob("load-*/load.json"))]
    expected = {
        (a, b, c, d)
        for a in languages
        for b in topologies
        for c in ("callback", "list-64k")
        for d in (1, 2)
    }
    actual = {(r["language"], r["topology"], r["scenario"], r["repeat"]) for r in loads}
    if actual != expected or len(loads) != 24:
        raise ValueError("Incomplete or duplicate request matrix")
    for row in loads:
        if (
            row["errors"]
            or row["concurrency"] != 16
            or len(row["lanes"]) != 16
            or any(l["Errors"] or not l["Count"] for l in row["lanes"])
        ):
            raise ValueError("Unserved or failed client")
    root = Path(__file__).resolve().parents[2]
    identities = [read(p) for p in run.glob("*/identity-*.json")]
    fixtures = {}
    for identity in identities:
        merge_fixture_hashes(fixtures, identity, root)
    provenance.update(
        fixtureHashes=fixtures,
        runId=run.name,
        python=(run / "timing/python-version.txt").read_text().strip(),
        node=(run / "timing/node-version.txt").read_text().strip(),
        docker=(run / "timing/docker-status.txt").read_text().strip(),
        swapBefore=(run / "timing/swap-before.txt").read_text().strip(),
        swapAfter=(run / "timing/swap-after.txt").read_text().strip(),
    )
    return dict(provenance=provenance, timing=timing, loads=loads)


def render(data):
    p = data["provenance"]
    env = p["benchmarkEnvironment"]
    lines = [
        "# Current performance baseline — " + p["runId"][:8],
        "",
        f"Measured core: **{p['coreVersion']}**, exact qualified NuGet and installed author SDK bytes. Benchmark source: `{p['sourceCommit']}`. Run: `{p['runId']}`.",
        "",
        "All 30 timing cells and 24 bounded request runs passed. The separate SDK correctness stage passed before timing. Optional Gateway was built from this source and is identified separately; it is outside the core distribution.",
        "",
        f"Environment: {env['ProcessorName']}, {env['OsVersion']}, {env['Architecture']}, SDK {env['DotNetCliVersion']}, {env['RuntimeVersion']}, {p['python']}, Node {p['node']}. Recorded Docker state: {p['docker']}. These are synthetic same-machine trusted-process and loopback measurements, not a capacity limit, SLO or qualification of Windows, remote networks or hostile plugins.",
        "",
        "## Complete-result timing",
        "",
        "Arithmetic means in milliseconds. Warm operations include complete result validation; cold includes harness setup, first callback, provenance hashing/writing and disposal. Six requested measured iterations per cell, one launch; BenchmarkDotNet may exclude outliers. [Machine-readable evidence](evidence.json) retains actual sample counts, distributions, standard deviations, confidence intervals and pre/post-exclusion measurements. Small samples and cold-start variability limit precision.",
        "",
        "| Language | Topology | Tiny | Callback | 64 KiB | 8 MiB | Cold lifecycle |",
        "| --- | --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for language, topology in product(
        ("csharp", "python", "typescript"), ("local", "gateway")
    ):
        rows = [
            r
            for r in data["timing"]
            if r["parameters"]["Language"] == language
            and r["parameters"]["Topology"] == topology
        ]
        values = {
            r["parameters"].get("Case", "cold"): r["statisticsNs"]["Mean"] / 1e6
            for r in rows
        }
        lines.append(
            "| "
            + " | ".join(
                [language, topology]
                + [
                    f"{values[k]:.3f}"
                    for k in ("tiny", "callback", "list-64k", "list-8m", "cold")
                ]
            )
            + " |"
        )
    lines += [
        "",
        "## Individual requests and resources",
        "",
        "Each cell uses 16 clients, one outstanding operation each, two separate three-second runs after warmup plus draining. Ranges below span the two runs. All clients completed requests without errors. p99 comes from request histograms with approximately 1% bucket resolution, not benchmark iteration statistics. Request latency excludes validation; throughput includes it.",
        "",
        "| Language | Topology | Workload | Requests/s | Request p99 ms | Peak summed RSS MiB |",
        "| --- | --- | --- | ---: | ---: | ---: |",
    ]
    for language, topology, scenario in product(
        ("csharp", "python", "typescript"),
        ("local", "gateway"),
        ("callback", "list-64k"),
    ):
        rows = [
            r
            for r in data["loads"]
            if (r["language"], r["topology"], r["scenario"])
            == (language, topology, scenario)
        ]
        ranges = [
            render_range(rows, key, divisor)
            for key, divisor in (("rps", 1), ("p99Ms", 1), ("peakCombinedRss", 1048576))
        ]
        lines.append("| " + " | ".join([language, topology, scenario] + ranges) + " |")
    lines += [
        "",
        "RSS samples cover the measuring host, optional gateway and known worker root processes every 100 ms. Shared pages can be counted repeatedly; descendants, Docker VM and unrelated applications are excluded, and sampling can miss peaks. Managed allocations cover the measuring .NET process only. Raw per-client outcomes and resource samples are retained in evidence.json.",
        "",
        "Reproduce with the [current benchmark command and workload definitions](../../../docs/benchmarking.md). Full logs and generated builds stay in ignored local artifacts. Earlier attempts failed project discovery or used a stale npm installation; none contribute to this baseline. No performance improvement over those historical runs is claimed.",
        "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    data = collect(args.run.resolve())
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "evidence.json").write_text(json.dumps(data, indent=2) + "\n")
    (args.output / "README.md").write_text(render(data))


if __name__ == "__main__":
    main()
