"""Qualify one committed native candidate; preserve all outputs on success or failure."""

import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time
import uuid

import reuse
import shared
from inventory import inventory, sha, verify_source
from stages import StageRecorder


def build_candidate(checkout, env, run, stage):
    stage("core-packages", ["./scripts/prepare-core-packages.sh"])
    stage(
        "packed-hosting-regressions",
        [
            "dotnet",
            "run",
            "--project",
            "tests/WeavePort.Hosting.Tests",
            "-c",
            "Release",
            "-p:UsePackedCore=true",
        ],
    )
    stage(
        "packed-scheduling-regressions",
        [
            "dotnet",
            "run",
            "--project",
            "tests/WeavePort.Scheduling.Tests",
            "-c",
            "Release",
            "-p:UsePackedCore=true",
        ],
    )
    stage(
        "mcp-fixture-install",
        ["npm", "ci", "--prefix", "tests/mcp", "--ignore-scripts"],
    )
    stage(
        "packed-mcp-interop",
        [
            "dotnet",
            "tests/WeavePort.Hosting.Tests/bin/Release/net10.0/WeavePort.Hosting.Tests.dll",
            "--mcp-interop",
            shutil.which("node"),
            str(checkout / "tests/mcp/server.mjs"),
        ],
    )
    stage(
        "mcp-example-restore",
        [
            "dotnet",
            "restore",
            "examples/mcp",
            "--force",
            "--force-evaluate",
            "--no-cache",
        ],
    )
    stage(
        "packed-mcp-example",
        [
            "dotnet",
            "run",
            "--project",
            "examples/mcp",
            "-c",
            "Release",
            "--",
            shutil.which("node"),
            str(checkout / "tests/mcp/server.mjs"),
        ],
    )
    packages = {
        p.name: sha(p)
        for p in sorted((checkout / "artifacts/packages").glob("*.nupkg"))
    }
    package_set = run / "package-set.json"
    package_set.write_text(json.dumps(packages, indent=2))
    env["WEAVEPORT_PACKAGE_SET"] = str(package_set)
    stage("sdk-build", ["./scripts/sdk-versions.sh", "--build-only"])
    stage("portable-installations", ["./scripts/verify-portable.sh"])
    wheel = next((checkout / "artifacts/sdk-version-tests/wheel").glob("*.whl"))
    env["WEAVEPORT_PYTHON_WHEEL"] = str(wheel)
    for app in ["decision-room", "document-workshop", "appointment-desk"]:
        stage(app + "-build", ["./scripts/" + app + ".sh", "--build", "--build-only"])
    stage("optional-build", ["python3", "scripts/verify-optional.py", "--build-only"])

    reuse.build(checkout, stage)
    shared.build(checkout, stage)
    frozen = inventory(checkout)
    (run / "artifact-manifest.json").write_text(json.dumps(frozen, indent=2))
    stage(
        "fixed-package-set",
        ["python3", "scripts/check-package-set.py", str(package_set)],
    )
    decision_wheel = next((checkout / "artifacts/decision-room/wheel").glob("*.whl"))
    assert sha(wheel) == sha(
        decision_wheel
    ), "Consumers used different Python author wheels"
    return frozen, package_set


def verify_candidate(checkout, env, run, stage, package_set):
    reuse.verify(checkout, stage)
    shared.verify(checkout, env, stage)
    for app in ["decision-room", "document-workshop", "appointment-desk"]:
        stage(app + "-verify", ["./scripts/" + app + ".sh", "--verify"])
    env.update(
        WP_VERSION_ROOT=str(checkout / "artifacts/sdk-version-tests"),
        WP_VERSION_DOTNET=shutil.which("dotnet"),
        WP_VERSION_NODE=shutil.which("node"),
    )
    stage(
        "sdk-verify",
        [
            env["WP_VERSION_DOTNET"],
            str(
                checkout
                / "artifacts/sdk-version-tests/host/WeavePort.SdkVersionTests.dll"
            ),
        ],
    )
    stage("optional-verify", ["python3", "scripts/verify-optional.py", "--verify-only"])
    stage("recovery-verify", ["./scripts/verify-recovery.sh"])
    stage("compatibility-verify", ["./scripts/verify-compatibility.sh"])
    # Copy-only negative control: a changed package cannot enter the fixed feed unnoticed.
    negative = run / "negative-packages"
    shutil.copytree(checkout / "artifacts/packages", negative)
    changed = next(negative.glob("*.nupkg"))
    with changed.open("ab") as output:
        output.write(b"changed")
    refusal = stage(
        "changed-package-refused",
        ["python3", "scripts/check-package-set.py", str(package_set), str(negative)],
        expected=1,
    )
    assert (
        "Fixed core package hash mismatch:" in refusal
    ), "Negative gate failed for an unrelated reason"


def main():
    if sys.platform != "darwin":
        raise SystemExit(
            "The current internal candidate qualification requires native macOS."
        )
    source = Path(__file__).resolve().parents[2]
    revision = subprocess.check_output(
        ["git", "rev-parse", sys.argv[1] if len(sys.argv) > 1 else "HEAD"],
        cwd=source,
        text=True,
    ).strip()
    run = (
        source
        / "artifacts/candidates"
        / (time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:8])
    )
    run.mkdir(parents=True)
    checkout = run / "checkout"
    cache = run / "cache"
    cache.mkdir()
    logs = run / "logs"
    logs.mkdir()
    env = {
        k: v
        for k, v in os.environ.items()
        if k in {"PATH", "HOME", "LANG", "LC_ALL", "TMPDIR"}
    }
    env.update(
        NUGET_PACKAGES=str(cache / "nuget"),
        NUGET_HTTP_CACHE_PATH=str(cache / "nuget-http"),
        DOTNET_CLI_HOME=str(cache / "dotnet"),
        DOTNET_CLI_TELEMETRY_OPTOUT="1",
        npm_config_cache=str(cache / "npm"),
        npm_config_userconfig=str(cache / "npmrc"),
        PIP_CACHE_DIR=str(cache / "pip"),
        PIP_CONFIG_FILE=os.devnull,
        PYTHONNOUSERSITE="1",
    )
    (cache / "npmrc").write_text("")
    record = dict(
        sourceCommit=revision,
        status="running",
        platform=platform.platform(),
        architecture=platform.machine(),
        stages=[],
    )

    stage = StageRecorder(checkout, env, run, record)

    try:
        stage(
            "checkout",
            ["git", "worktree", "add", "--detach", str(checkout), revision],
            cwd=source,
        )
        assert not (
            checkout / "artifacts"
        ).exists(), "Checkout contains preexisting artifacts"
        assert not list(
            checkout.glob("src/*/bin")
        ), "Checkout contains preexisting binaries"
        for name, command in [
            ("dotnet", ["dotnet", "--version"]),
            ("python", ["python3", "--version"]),
            ("node", ["node", "--version"]),
            ("npm", ["npm", "--version"]),
        ]:
            record[name] = stage("tool-" + name, command).strip()
        runtimes = stage("dotnet-runtimes", ["dotnet", "--list-runtimes"])
        record["sharedRuntimes"] = [
            " ".join(line.split()[:2]) for line in runtimes.splitlines()
        ]
        record["runtimeExecutables"] = {
            name: sha(Path(shutil.which(name)).resolve())
            for name in ["dotnet", "python3", "node"]
        }
        stage(
            "hosting-regressions",
            [
                "dotnet",
                "run",
                "--project",
                "tests/WeavePort.Hosting.Tests",
                "-c",
                "Release",
            ],
        )
        stage(
            "scheduling-regressions",
            [
                "dotnet",
                "run",
                "--project",
                "tests/WeavePort.Scheduling.Tests",
                "-c",
                "Release",
            ],
        )
        stage(
            "density-observer-controls",
            [sys.executable, "tools/performance/test_density.py"],
        )
        stage(
            "gateway-regressions",
            [
                "dotnet",
                "run",
                "--project",
                "tests/WeavePort.Gateway.Tests",
                "-c",
                "Release",
            ],
        )
        stage(
            "protocol-contract", ["python3", "scripts/generate-protocol.py", "--check"]
        )
        stage(
            "protocol-fixtures",
            ["python3", "tests/documentation/test-protocol-contract.py"],
        )
        for name, directory, pattern in (
            ("performance-tool-controls", "tools/performance", "test_*.py"),
            ("soak-tool-controls", "tests/soak", "test_*.py"),
            ("release-tool-controls", "tests/release", "test_release.py"),
        ):
            stage(
                name,
                [
                    "python3",
                    "-m",
                    "unittest",
                    "discover",
                    "-s",
                    directory,
                    "-p",
                    pattern,
                ],
            )
        stage("website-controls", ["python3", "tests/documentation/test-website.py"])
        stage("architecture", ["python3", "scripts/check-architecture.py"])
        stage(
            "architecture-guards",
            ["python3", "tests/documentation/test-architecture.py"],
        )
        stage(
            "code-style",
            [
                "dotnet",
                "run",
                "--project",
                "tools/WeavePort.CodeStyle",
                "-c",
                "Release",
                "--",
                str(checkout),
            ],
        )
        stage(
            "documentation-regressions",
            ["python3", "tests/documentation/check-links.py"],
        )
        frozen, package_set = build_candidate(checkout, env, run, stage)
        stage(
            "python-control-flow", ["python3", "scripts/check-python-control-flow.py"]
        )
        stage(
            "typescript-control-flow",
            ["node", "sdks/typescript/scripts/check-control-flow.mjs"],
        )
        stage(
            "sdk-context",
            ["python3", "-m", "unittest", "discover", "-s", "sdks/python/tests"],
        )
        stage(
            "typescript-context",
            [
                "node",
                "--test",
                "sdks/typescript/tests/session.mjs",
                "sdks/typescript/tests/failures.mjs",
            ],
        )
        verify_candidate(checkout, env, run, stage, package_set)
        assert frozen == inventory(
            checkout
        ), "Frozen consumer artifacts changed during verification"
        assert record["runtimeExecutables"] == {
            name: sha(Path(shutil.which(name)).resolve())
            for name in ["dotnet", "python3", "node"]
        }
        record["lockfileRefreshes"] = verify_source(checkout, env, run)
        record["artifactFiles"] = len(frozen)
        record["artifactManifestSha256"] = sha(run / "artifact-manifest.json")
        record["assertions"] = sum(stage["assertions"] for stage in record["stages"])
        record["status"] = "passed"
    except BaseException as error:
        record["status"] = "failed"
        record["failure"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        (run / "result.json").write_text(json.dumps(record, indent=2))
        print("Candidate evidence: " + str(run), flush=True)
    print(
        f"Candidate passed: {record['assertions']} assertions; {record['artifactFiles']} frozen files."
    )


if __name__ == "__main__":
    main()
