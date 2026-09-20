"""Qualify one committed native candidate; preserve all outputs on success or failure."""
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import time
import uuid

import reuse


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(checkout):
    trees = ["artifacts/reuse-source", "artifacts/reuse-packed", "artifacts/reuse-example", "artifacts/packages", "artifacts/sdk-version-tests/host",
             "artifacts/sdk-version-tests/wheel", "artifacts/sdk-version-tests/node_modules/@weaveport/sdk"]
    for app in ["decision-room", "document-workshop", "appointment-desk"]:
        trees += [f"artifacts/{app}/host", f"artifacts/{app}/releases"]
    trees += ["artifacts/decision-room/wheel"]
    for app in ["decision-room", "sdk-version-tests"]:
        sites = list((checkout / f"artifacts/{app}/python/lib").glob("python*/site-packages"))
        assert len(sites) == 1, "Expected one Python environment"
        trees += [str(p.relative_to(checkout)) for p in sites[0].glob("weaveport_sdk*")]
    files = []
    for tree in trees:
        path = checkout / tree
        assert path.is_dir(), "Missing frozen tree: " + tree
        files += [p for p in path.rglob("*") if p.is_file() and "__pycache__" not in p.parts]
    files += [checkout / f"artifacts/sdk-version-tests/{name}" for name in
              ["worker.py", "worker.mjs", "weaveport-sdk-0.2.0.tgz"]]
    return {str(p.relative_to(checkout)): sha(p) for p in sorted(set(files))}


def without_hashes(value):
    if isinstance(value, dict):
        return {k: without_hashes(v) for k, v in value.items() if k != "contentHash"}
    if isinstance(value, list):
        return [without_hashes(v) for v in value]
    return value


def verify_source(checkout, env, run):
    paths = subprocess.check_output(["git", "diff", "--name-only", "HEAD"], cwd=checkout, env=env, text=True).splitlines()
    changes = {}
    for path in paths:
        assert path.endswith("/packages.lock.json"), "Unexpected tracked change: " + path
        before = subprocess.check_output(["git", "show", "HEAD:" + path], cwd=checkout, env=env, text=True)
        after = (checkout / path).read_text()
        assert without_hashes(json.loads(before)) == without_hashes(json.loads(after)), "Dependency topology changed: " + path
        changes[path] = {"before": hashlib.sha256(before.encode()).hexdigest(), "after": sha(checkout / path)}
    untracked = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard"], cwd=checkout, env=env, text=True)
    assert not untracked.strip(), "Unexpected untracked source files: " + untracked
    (run / "lockfile-refreshes.json").write_text(json.dumps(changes, indent=2))
    return changes


def build_candidate(checkout, env, run, stage):
    stage("core-packages", ["./scripts/prepare-core-packages.sh"])
    stage("packed-hosting-regressions", ["dotnet", "run", "--project", "tests/WeavePort.Hosting.Tests", "-c", "Release", "-p:UsePackedCore=true"])
    stage("packed-scheduling-regressions", ["dotnet", "run", "--project", "tests/WeavePort.Scheduling.Tests", "-c", "Release", "-p:UsePackedCore=true"])
    stage("mcp-fixture-install", ["npm", "ci", "--prefix", "tests/mcp", "--ignore-scripts"])
    stage("packed-mcp-interop", ["dotnet", "tests/WeavePort.Hosting.Tests/bin/Release/net10.0/WeavePort.Hosting.Tests.dll", "--mcp-interop", shutil.which("node"), str(checkout / "tests/mcp/server.mjs")])
    stage("mcp-example-restore", ["dotnet", "restore", "examples/mcp", "--force", "--force-evaluate", "--no-cache"])
    stage("packed-mcp-example", ["dotnet", "run", "--project", "examples/mcp", "-c", "Release", "--", shutil.which("node"), str(checkout / "tests/mcp/server.mjs")])
    packages = {p.name: sha(p) for p in sorted((checkout / "artifacts/packages").glob("*.nupkg"))}
    package_set = run / "package-set.json"
    package_set.write_text(json.dumps(packages, indent=2))
    env["WEAVEPORT_PACKAGE_SET"] = str(package_set)
    stage("sdk-build", ["./scripts/sdk-versions.sh", "--build-only"])
    wheel = next((checkout / "artifacts/sdk-version-tests/wheel").glob("*.whl"))
    env["WEAVEPORT_PYTHON_WHEEL"] = str(wheel)
    for app in ["decision-room", "document-workshop", "appointment-desk"]:
        stage(app + "-build", ["./scripts/" + app + ".sh", "--build", "--build-only"])
    reuse.build(checkout, stage)
    frozen = inventory(checkout)
    (run / "artifact-manifest.json").write_text(json.dumps(frozen, indent=2))
    stage("fixed-package-set", ["python3", "scripts/check-package-set.py", str(package_set)])
    decision_wheel = next((checkout / "artifacts/decision-room/wheel").glob("*.whl"))
    assert sha(wheel) == sha(decision_wheel), "Consumers used different Python author wheels"
    return frozen, package_set


def verify_candidate(checkout, env, run, stage, package_set):
    reuse.verify(checkout, stage)
    for app in ["decision-room", "document-workshop", "appointment-desk"]:
        stage(app + "-verify", ["./scripts/" + app + ".sh", "--verify"])
    env.update(WP_VERSION_ROOT=str(checkout / "artifacts/sdk-version-tests"),
               WP_VERSION_DOTNET=shutil.which("dotnet"), WP_VERSION_NODE=shutil.which("node"))
    stage("sdk-verify", [env["WP_VERSION_DOTNET"], str(checkout / "artifacts/sdk-version-tests/host/WeavePort.SdkVersionTests.dll")])
    stage("recovery-verify", ["./scripts/verify-recovery.sh"])
    stage("compatibility-verify", ["./scripts/verify-compatibility.sh"])
    # Copy-only negative control: a changed package cannot enter the fixed feed unnoticed.
    negative = run / "negative-packages"
    shutil.copytree(checkout / "artifacts/packages", negative)
    changed = next(negative.glob("*.nupkg"))
    with changed.open("ab") as output:
        output.write(b"changed")
    refusal = stage("changed-package-refused", ["python3", "scripts/check-package-set.py", str(package_set), str(negative)], expected=1)
    assert "Fixed core package hash mismatch:" in refusal, "Negative gate failed for an unrelated reason"


def main():
    if sys.platform != "darwin":
        raise SystemExit("The current internal candidate qualification requires native macOS.")
    source = Path(__file__).resolve().parents[2]
    revision = subprocess.check_output(["git", "rev-parse", sys.argv[1] if len(sys.argv) > 1 else "HEAD"], cwd=source, text=True).strip()
    run = source / "artifacts/candidates" / (time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:8])
    run.mkdir(parents=True)
    checkout = run / "checkout"
    cache = run / "cache"
    cache.mkdir()
    logs = run / "logs"
    logs.mkdir()
    env = {k: v for k, v in os.environ.items() if k in {"PATH", "HOME", "LANG", "LC_ALL", "TMPDIR"}}
    env.update(NUGET_PACKAGES=str(cache / "nuget"), NUGET_HTTP_CACHE_PATH=str(cache / "nuget-http"),
               DOTNET_CLI_HOME=str(cache / "dotnet"), DOTNET_CLI_TELEMETRY_OPTOUT="1",
               npm_config_cache=str(cache / "npm"), npm_config_userconfig=str(cache / "npmrc"),
               PIP_CACHE_DIR=str(cache / "pip"), PIP_CONFIG_FILE=os.devnull, PYTHONNOUSERSITE="1")
    (cache / "npmrc").write_text("")
    record = dict(sourceCommit=revision, status="running", platform=platform.platform(), architecture=platform.machine(), stages=[])

    def stage(name, command, expected=0, cwd=None):
        print("STAGE: " + name, flush=True)
        started = time.monotonic()
        with (logs / (name + ".log")).open("w") as output:
            result = subprocess.run(command, cwd=cwd or checkout, env=env, stdout=output, stderr=subprocess.STDOUT)
        text = (logs / (name + ".log")).read_text()
        item = dict(name=name, exitCode=result.returncode, expectedExitCode=expected,
                    seconds=round(time.monotonic() - started, 3), assertions=len(re.findall(r"^PASS:", text, re.M)) + sum(int(n) for n in re.findall(r"^PASS (\d+) ", text, re.M)))
        record["stages"].append(item)
        (run / "result.json").write_text(json.dumps(record, indent=2))
        if result.returncode != expected:
            raise RuntimeError("Stage failed: " + name + "; inspect retained log")
        return text

    try:
        stage("checkout", ["git", "worktree", "add", "--detach", str(checkout), revision], cwd=source)
        assert not (checkout / "artifacts").exists(), "Checkout contains preexisting artifacts"
        assert not list(checkout.glob("src/*/bin")), "Checkout contains preexisting binaries"
        for name, command in [("dotnet", ["dotnet", "--version"]), ("python", ["python3", "--version"]),
                              ("node", ["node", "--version"]), ("npm", ["npm", "--version"])]:
            record[name] = stage("tool-" + name, command).strip()
        runtimes = stage("dotnet-runtimes", ["dotnet", "--list-runtimes"])
        record["sharedRuntimes"] = [" ".join(line.split()[:2]) for line in runtimes.splitlines()]
        record["runtimeExecutables"] = {name: sha(Path(shutil.which(name)).resolve()) for name in ["dotnet", "python3", "node"]}
        stage("hosting-regressions", ["dotnet", "run", "--project", "tests/WeavePort.Hosting.Tests", "-c", "Release"])
        stage("scheduling-regressions", ["dotnet", "run", "--project", "tests/WeavePort.Scheduling.Tests", "-c", "Release"])
        stage("density-observer-controls", [sys.executable, "tools/performance/test_density.py"])
        stage("gateway-regressions", ["dotnet", "run", "--project", "tests/WeavePort.Gateway.Tests", "-c", "Release"])
        stage("code-style", ["dotnet", "run", "--project", "tools/WeavePort.CodeStyle", "-c", "Release", "--", str(checkout)])
        stage("documentation-regressions", ["python3", "tests/documentation/check-links.py"])
        frozen, package_set = build_candidate(checkout, env, run, stage)
        verify_candidate(checkout, env, run, stage, package_set)
        assert frozen == inventory(checkout), "Frozen consumer artifacts changed during verification"
        assert record["runtimeExecutables"] == {name: sha(Path(shutil.which(name)).resolve()) for name in ["dotnet", "python3", "node"]}
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
    print(f"Candidate passed: {record['assertions']} assertions; {record['artifactFiles']} frozen files.")


if __name__ == "__main__":
    main()
