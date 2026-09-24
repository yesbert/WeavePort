"""Freeze built artifact bytes and allow only hash refreshes in source lockfiles."""

import hashlib
import json
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(checkout):
    trees = [
        "artifacts/shared-example",
        "examples/sources/bin/Release/net10.0",
        "artifacts/shared-source",
        "artifacts/shared-packed",
        "artifacts/concurrent-sdk-source",
        "artifacts/concurrent-sdk-packed",
        "artifacts/reuse-source",
        "artifacts/reuse-packed",
        "artifacts/reuse-example",
        "artifacts/packages",
        "artifacts/sdk-version-tests/host",
        "artifacts/sdk-version-tests/wheel",
        "artifacts/sdk-version-tests/node_modules/@weaveport/sdk",
    ]
    for app in ["decision-room", "document-workshop", "appointment-desk"]:
        trees += [f"artifacts/{app}/host", f"artifacts/{app}/releases"]
    trees += ["artifacts/decision-room/wheel"]
    trees += [
        f"artifacts/optional/{name}"
        for name in ["worker", "client", "server", "tests", "bulk-worker", "bulk-tests"]
    ]
    for app in ["decision-room", "sdk-version-tests"]:
        sites = list(
            (checkout / f"artifacts/{app}/python/lib").glob("python*/site-packages")
        )
        assert len(sites) == 1, "Expected one Python environment"
        trees += [str(p.relative_to(checkout)) for p in sites[0].glob("weaveport_sdk*")]
    files = []
    for tree in trees:
        path = checkout / tree
        assert path.is_dir(), "Missing frozen tree: " + tree
        files += [
            p for p in path.rglob("*") if p.is_file() and "__pycache__" not in p.parts
        ]
    files += [
        checkout / f"artifacts/sdk-version-tests/{name}"
        for name in ["worker.py", "worker.mjs", "weaveport-sdk-0.3.0.tgz"]
    ]
    return {str(p.relative_to(checkout)): sha(p) for p in sorted(set(files))}


def without_hashes(value):
    if isinstance(value, dict):
        return {k: without_hashes(v) for k, v in value.items() if k != "contentHash"}
    if isinstance(value, list):
        return [without_hashes(v) for v in value]
    return value


def verify_source(checkout, env, run):
    paths = subprocess.check_output(
        ["git", "diff", "--name-only", "HEAD"], cwd=checkout, env=env, text=True
    ).splitlines()
    changes = {}
    for path in paths:
        assert path.endswith("/packages.lock.json"), (
            "Unexpected tracked change: " + path
        )
        before = subprocess.check_output(
            ["git", "show", "HEAD:" + path], cwd=checkout, env=env, text=True
        )
        after = (checkout / path).read_text()
        assert without_hashes(json.loads(before)) == without_hashes(
            json.loads(after)
        ), ("Dependency topology changed: " + path)
        changes[path] = {
            "before": hashlib.sha256(before.encode()).hexdigest(),
            "after": sha(checkout / path),
        }
    untracked = subprocess.check_output(
        ["git", "ls-files", "--others", "--exclude-standard"],
        cwd=checkout,
        env=env,
        text=True,
    )
    assert not untracked.strip(), "Unexpected untracked source files: " + untracked
    (run / "lockfile-refreshes.json").write_text(json.dumps(changes, indent=2))
    return changes
