"""Build and verify optional consumers using the already prepared immutable feed."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys


def run(*command):
    subprocess.run([str(value) for value in command], check=True)


def clear_cache(cache):
    if cache.exists():
        shutil.rmtree(cache)


def build_consumers(root, out):
    # Each build starts with a dedicated empty package cache; no global cached
    # development package may substitute for the prepared release artifacts.
    cache = out / "packages"
    clear_cache(cache)
    for project, destination in [
        ("examples/gateway/Worker", "worker"),
        ("examples/gateway/Client", "client"),
        ("examples/gateway/Server", "server"),
        ("tests/WeavePort.Optional.Tests", "tests"),
        ("plugins/csharp", "bulk-worker"),
        ("tests/WeavePort.Composition.Tests", "bulk-tests"),
    ]:
        run(
            "dotnet",
            "restore",
            project,
            "--force",
            "--force-evaluate",
            "--no-cache",
            f"-p:RestorePackagesPath={cache}",
        )
        run(
            "dotnet",
            "publish",
            project,
            "-c",
            "Release",
            "--no-restore",
            "--self-contained",
            "false",
            f"-p:RestorePackagesPath={cache}",
            "-o",
            out / destination,
            "--nologo",
        )
    config = {
        "Dotnet": shutil.which("dotnet"),
        "Python": sys.executable,
        "Node": shutil.which("node"),
        "Csharp": str(out / "bulk-worker/WeavePort.SamplePlugin.dll"),
        "PythonScript": str(root / "plugins/python/worker.py"),
        "TypeScriptScript": str(root / "plugins/typescript/worker.ts"),
        "WorkspaceRoot": str(out / "bulk-workspaces"),
    }
    (out / "bulk-config.json").write_text(json.dumps(config, indent=2))


def verify_consumers(root, out):
    run(
        "dotnet",
        out / "tests/WeavePort.Optional.Tests.dll",
        root,
        shutil.which("dotnet"),
    )
    run("dotnet", out / "tests/WeavePort.Optional.Tests.dll", "--api", root)
    config = json.loads((out / "client/Client.runtimeconfig.json").read_text())
    frameworks = config["runtimeOptions"].get(
        "frameworks", [config["runtimeOptions"].get("framework", {})]
    )
    assert all(
        f.get("name") != "Microsoft.AspNetCore.App" for f in frameworks
    ), "Client acquired an ASP.NET Core dependency"
    print("PASS: client-only consumer requires no ASP.NET Core shared framework")
    run(sys.executable, "tests/optional/check-packages.py")


def verify_bulk(out, mode):
    evidence = out / ("bulk-" + mode)
    evidence.mkdir(exist_ok=True)
    os.environ.update(
        WEAVEPORT_LOCAL_CONFIG=str(out / "bulk-config.json"),
        WEAVEPORT_BULK_OUTPUT=str(evidence),
    )
    run("dotnet", out / "bulk-tests/WeavePort.BulkDemo.dll", mode)


def main():
    root = Path(__file__).resolve().parents[1]
    os.chdir(root)
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--build-only", action="store_true")
    modes.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    out = root / "artifacts/optional"
    out.mkdir(parents=True, exist_ok=True)
    if not args.verify_only:
        build_consumers(root, out)
    if args.build_only:
        return
    verify_consumers(root, out)
    for mode in ("verify", "http"):
        verify_bulk(out, mode)


if __name__ == "__main__":
    main()
