#!/usr/bin/env python3
"""Package the retained qualified candidate without rebuilding its artifacts."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile

from documentation import prepare
from weaveport import APPS, checked_path, digest, inventory, read, verify_files, write

VERSION = "0.1.0-internal.2"
REPOSITORY = Path(__file__).resolve().parents[2]
EVIDENCE = REPOSITORY / "reports/release/0.1.0-internal.2/candidate"


def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def copy_qualified_artifacts(checkout, bundle, expected):
    artifact_map = {}
    for source, sha in expected.items():
        parts = Path(source).parts
        target = None
        if parts[1] in APPS and parts[2] in ("host", "releases"):
            target = Path("apps", *parts[1:])
        elif parts[1] == "packages":
            target = Path("packages/nuget", parts[-1])
        elif source == "artifacts/decision-room/wheel/weaveport_sdk-0.1.0-py3-none-any.whl":
            target = Path("packages/python", parts[-1])
        elif source == "artifacts/sdk-version-tests/weaveport-sdk-0.1.0.tgz":
            target = Path("packages/typescript", parts[-1])
        if target is not None:
            copy(checked_path(checkout, source), bundle / target)
            artifact_map[target.as_posix()] = {"candidatePath": source, "sha256": sha}
    return artifact_map


def copy_templates(checkout, bundle, result):
    # Sources are read from the qualified Git object, never working-tree overrides.
    commit = result["sourceCommit"]
    prefixes = [f"samples/{name}/" for name, _ in APPS.values()] + ["samples/Shared/"]
    tracked = subprocess.check_output(["git", "ls-tree", "-r", "--name-only", commit],
                                      cwd=REPOSITORY, text=True).splitlines()
    for name in tracked:
        if name in ("Directory.Build.props", "global.json") or any(name.startswith(p) for p in prefixes):
            target = bundle / "templates" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(subprocess.check_output(["git", "show", f"{commit}:{name}"], cwd=REPOSITORY))
    # Candidate qualification recorded these lockfile content-hash refreshes.
    # Ship the qualified locks, not earlier same-version package identities.
    for name, hashes in result["lockfileRefreshes"].items():
        verify_files(checkout, {name: hashes["after"]})
        copy(checked_path(checkout, name), bundle / "templates" / name)
    shutil.copytree(bundle / "packages/nuget", bundle / "templates/artifacts/packages")
    (bundle / "templates/NuGet.Config").write_text('''<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="weaveport-internal" value="artifacts/packages" /></packageSources>
</configuration>
''')


def package(candidate, output):
    result = read(EVIDENCE / "result.json")
    expected = read(EVIDENCE / "artifact-manifest.json")
    if result["status"] != "passed" or digest(EVIDENCE / "artifact-manifest.json") != result["artifactManifestSha256"]:
        raise ValueError("Invalid retained qualification evidence")
    # Checked-in evidence, not caller-provided metadata, anchors candidate bytes.
    checkout = candidate / "checkout"
    verify_files(checkout, expected)
    name = f"WeavePort-{VERSION}-osx-arm64"
    bundle = output / name
    archive = output / f"{name}.tar.gz"
    if bundle.exists() or archive.exists():
        raise ValueError("Output already exists; choose a new output directory")
    bundle.mkdir(parents=True)
    artifact_map = copy_qualified_artifacts(checkout, bundle, expected)
    commit = result["sourceCommit"]
    copy_templates(checkout, bundle, result)
    copy(REPOSITORY / "tools/distribution/weaveport.py", bundle / "weaveport.py")
    copy(REPOSITORY / "docs/internal-distribution.md", bundle / "README.md")
    for file in EVIDENCE.iterdir():
        if file.is_file():
            copy(file, bundle / "evidence" / file.name)
    prepare(bundle, commit)
    manifest = {
        "schema": 1, "version": VERSION, "qualifiedSourceCommit": commit,
        "components": {"nuget": read(EVIDENCE / "package-set.json"),
                       "python": "weaveport-sdk 0.1.0", "typescript": "@weaveport/sdk 0.1.0"},
        "prerequisites": {key: result[key] for key in ("platform", "architecture", "python", "runtimeExecutables")},
        "qualificationAssertions": result["assertions"], "qualifiedArtifacts": artifact_map,
        "files": inventory(bundle),
    }
    write(bundle / "distribution.json", manifest)
    # Recheck copied qualified bytes before sealing the archive.
    verify_files(bundle, {name: info["sha256"] for name, info in artifact_map.items()})
    with tarfile.open(archive, "w:gz") as stream:
        stream.add(bundle, arcname=bundle.name)
    (output / f"{archive.name}.sha256").write_text(f"{digest(archive)}  {archive.name}\n")
    print(json.dumps({"archive": str(archive), "sha256": digest(archive),
                      "files": len(manifest["files"]), "qualifiedArtifacts": len(artifact_map)}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidate", type=Path, help="Retained candidate directory containing checkout/")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        package(args.candidate.resolve(), args.output.resolve())
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"REFUSED: {error}", file=sys.stderr)
        sys.exit(1)
