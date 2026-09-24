"""Reproduce the frozen 0.1.0-internal.2 multilingual SDK performance baseline."""

import argparse
from itertools import product
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import uuid
import zipfile

from identity import verify_authors

ROOT = Path(__file__).resolve().parents[2]
RELEASE = ROOT / "reports/release/0.1.0-internal.2/candidate"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_packages(feed, expected):
    for name, checksum in expected.items():
        if digest(feed / name) != checksum:
            raise ValueError("Qualified package differs: " + name)


def verify_loaded(feed, expected):
    loaded = {}
    for filename in expected:
        if not filename.startswith("WeavePort."):
            continue
        verify_loaded_package(feed, filename, loaded)
    return loaded


def verify_loaded_package(feed, filename, loaded):
    with zipfile.ZipFile(feed / filename) as archive:
        for entry in archive.namelist():
            if not entry.startswith("lib/net10.0/") or not entry.endswith(".dll"):
                continue
            folder = (
                "sdk-csharp"
                if filename.startswith("WeavePort.Sdk.0")
                else "sdk-worker-host"
            )
            path = ROOT / "artifacts" / folder / Path(entry).name
            checksum = hashlib.sha256(archive.read(entry)).hexdigest()
            if digest(path) != checksum:
                raise ValueError(
                    "Loaded implementation differs from qualified package: " + path.name
                )
            loaded[path.name] = checksum


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--distribution",
        type=Path,
        required=True,
        help="Extracted bundle or installed payload directory",
    )
    args = parser.parse_args()
    output = (
        ROOT
        / "artifacts/performance"
        / (time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:8])
    )
    output.mkdir(parents=True)
    expected = json.loads((RELEASE / "package-set.json").read_text())
    supplied = args.distribution.resolve() / "packages/nuget"
    verify_packages(supplied, expected)
    feed = ROOT / "artifacts/packages"
    if feed.exists():
        feed.rename(output / "previous-feed")
    feed.mkdir()
    for filename in expected:
        shutil.copy2(supplied / filename, feed / filename)
    env = os.environ.copy()
    env["WEAVEPORT_PACKAGE_SET"] = str(RELEASE / "package-set.json")
    record = {
        "status": "running",
        "sourceCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
        "coreVersion": "0.1.0-internal.2",
        "packages": expected,
        "stages": [],
    }

    def stage(name, command):
        print("STAGE: " + name, flush=True)
        started = time.monotonic()
        with (output / (name + ".log")).open("w") as log:
            result = subprocess.run(
                command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT
            )
        record["stages"].append(
            {
                "name": name,
                "exitCode": result.returncode,
                "seconds": round(time.monotonic() - started, 3),
            }
        )
        (output / "result.json").write_text(json.dumps(record, indent=2))
        if result.returncode:
            raise RuntimeError("Failed stage: " + name)

    try:
        stage("build", ["./scripts/build-sdk.sh"])
        verify_packages(feed, expected)
        record["loadedCore"] = verify_loaded(feed, expected)
        record["loadedAuthors"] = verify_authors(ROOT, args.distribution.resolve())
        record["optionalGateway"] = digest(
            ROOT / "artifacts/sdk-worker-host/WeavePort.Sdk.Gateway.dll"
        )
        record["runtimeExecutables"] = {
            name: digest(Path(shutil.which(name)).resolve())
            for name in ("dotnet", "python3", "node")
        }
        stage(
            "correctness", ["./scripts/sdk.sh", str(output / "correctness"), "verify"]
        )
        stage(
            "benchmarkdotnet",
            [
                "./scripts/sdk.sh",
                str(output / "timing"),
                "--filter",
                "*",
                "--artifacts",
                str(output / "bdn"),
            ],
        )
        for language, topology, scenario, repeat in product(
            ("csharp", "python", "typescript"),
            ("local", "gateway"),
            ("callback", "list-64k"),
            (1, 2),
        ):
            name = f"load-{language}-{topology}-{scenario}-{repeat}"
            stage(
                name,
                [
                    "./scripts/sdk.sh",
                    str(output / name),
                    "load",
                    language,
                    topology,
                    scenario,
                    "16",
                    str(repeat),
                    "3",
                ],
            )
        verify_packages(feed, expected)
        assert record["loadedCore"] == verify_loaded(feed, expected)
        assert record["loadedAuthors"] == verify_authors(
            ROOT, args.distribution.resolve()
        )
        record["status"] = "passed"
    except BaseException as error:
        record["status"] = "failed"
        record["failure"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        (output / "result.json").write_text(json.dumps(record, indent=2))
        print("Performance evidence: " + str(output), flush=True)


if __name__ == "__main__":
    main()
