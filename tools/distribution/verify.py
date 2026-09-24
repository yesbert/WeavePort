#!/usr/bin/env python3
"""Exercise the archive outside the checkout and retain isolated test evidence."""

import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile

from documentation import validate, validate_package
from weaveport import APPS, digest, environment, read, write


def verify_templates(root, target, run):
    launcher = [sys.executable, "-B", target / "weaveport.py"]
    # Independent consumers use only copied sources and the archive's local feed.
    consumer = root / "copied templates"
    shutil.copytree(target / "payload/templates", consumer)
    projects = [
        (name, role)
        for name, _ in APPS.values()
        for role in ("Host", "Plugin" if name == "DecisionRoom" else "Worker")
    ]
    for name, role in projects:
        run(
            f"build-{name}-{role}",
            [
                "dotnet",
                "build",
                f"samples/{name}/{role}",
                "-c",
                "Release",
                "--nologo",
                "-p:NuGetAudit=false",
            ],
            cwd=consumer,
        )
    for assets in consumer.glob("samples/**/obj/project.assets.json"):
        data = read(assets)
        assert all(
            Path(path).is_relative_to(consumer) for path in data["packageFolders"]
        )
        sdk_feed = Path(shutil.which("dotnet")).resolve().parent / "library-packs"
        assert all(
            Path(path).is_relative_to(consumer) or Path(path) == sdk_feed
            for path in data["project"]["restore"]["sources"]
        )
    expected_packages = read(target / "payload/distribution.json")["components"][
        "nuget"
    ]
    expected_by_name = {name.lower(): sha for name, sha in expected_packages.items()}
    restored = list(consumer.glob("artifacts/*/packages/**/*.nupkg"))
    assert {path.name for path in restored} == set(expected_by_name)
    assert all(digest(path) == expected_by_name[path.name] for path in restored)
    # Builds cannot alter the installed templates/feed.
    run("doctor-after-template-builds", [*launcher, "doctor"])


def verify_refusals(bundle, target, setup, run):
    run(
        "wrong-runtime",
        [*setup, target, "--dotnet", sys.executable],
        1,
        "runtime executable mismatch",
    )
    assert not target.exists()
    damaged = bundle / "packages/nuget/WeavePort.Hosting.0.1.0-internal.2.nupkg"
    original = damaged.read_bytes()
    damaged.write_bytes(original + b"changed")
    run("damaged-bundle", [*setup, target], 1, "Content mismatch")
    assert not target.exists()
    damaged.write_bytes(original)
    extra = bundle / "unexpected.txt"
    extra.write_text("extra")
    run("extra-bundle-file", [*setup, target], 1, "Unexpected delivery content")
    extra.unlink()


def verify(archive):
    root = Path(tempfile.mkdtemp(prefix="weaveport distribution ")).resolve()
    logs = root / "logs"
    logs.mkdir()
    result = {"archiveSha256": digest(archive), "status": "running", "stages": []}

    def run(label, args, expected=0, contains=None, cwd=root):
        completed = subprocess.run(
            [str(arg) for arg in args],
            cwd=cwd,
            env=environment(),
            capture_output=True,
            text=True,
            timeout=180,
        )
        output = completed.stdout + completed.stderr
        (logs / f"{label}.log").write_text(output)
        assertions = sum(line.startswith("PASS:") for line in output.splitlines())
        stage = {
            "name": label,
            "exitCode": completed.returncode,
            "expectedExitCode": expected,
            "assertions": assertions,
        }
        result["stages"].append(stage)
        write(root / "result.json", result)
        if completed.returncode != expected or (contains and contains not in output):
            raise ValueError(f"Failed {label}: {output[-4000:]}")
        print(f"PASS: {label} ({assertions} application assertions)", flush=True)

    try:
        with tarfile.open(archive) as stream:
            stream.extractall(root / "extracted", filter="data")
        bundle = next((root / "extracted").iterdir())
        validate(bundle)
        validate(bundle / "templates")
        for path in (bundle / "packages/nuget").glob("WeavePort.*.nupkg"):
            validate_package(path)
        setup = [sys.executable, "-B", bundle / "weaveport.py", "install"]
        target = root / "installed product"
        verify_refusals(bundle, target, setup, run)
        run("install", [*setup, target], contains="Installed 0.1.0-internal.2")
        launcher = [sys.executable, "-B", target / "weaveport.py"]
        run("doctor", [*launcher, "doctor"], contains="content and qualified runtimes")
        receipt = target / "installation.json"
        receipt_bytes = receipt.read_bytes()
        receipt.unlink()
        run(
            "incomplete-installation",
            [*launcher, "run", "appointment-desk"],
            1,
            "Incomplete installation",
        )
        receipt.write_bytes(receipt_bytes)
        changed = target / "var/appointment-desk/host/AppointmentDesk.Host.dll"
        original = changed.read_bytes()
        changed.write_bytes(original + b"changed")
        run(
            "damaged-installed-host",
            [*launcher, "run", "appointment-desk"],
            1,
            "Content mismatch",
        )
        changed.write_bytes(original)
        for app in APPS:
            run(f"verify-{app}", [*launcher, "run", app, "--", "--verify"])
        run("normal-decision", [*launcher, "run", "decision-room"])
        run("normal-document", [*launcher, "run", "document-workshop"])
        run("normal-appointment", [*launcher, "run", "appointment-desk"])
        marker = target / "var/retained-recovery-evidence.txt"
        marker.write_text("must remain unchanged")
        # Venv symlinks are expected; compare regular generated state and executable bytes.
        before = {
            p.relative_to(target / "var").as_posix(): digest(p)
            for p in (target / "var").rglob("*")
            if p.is_file() and not p.is_symlink()
        }
        run(
            "used-destination-refused",
            [*setup, target],
            1,
            "Destination already exists",
        )
        run("doctor-after-use", [*launcher, "doctor"])
        after = {
            p.relative_to(target / "var").as_posix(): digest(p)
            for p in (target / "var").rglob("*")
            if p.is_file() and not p.is_symlink()
        }
        assert before == after, "Reinstall/doctor changed application state"
        verify_templates(root, target, run)
        result["applicationAssertions"] = sum(
            stage["assertions"]
            for stage in result["stages"]
            if stage["name"].startswith("verify-")
        )
        assert result["applicationAssertions"] == 144
        result["statePreserved"] = True
        result["externalConsumerBuilds"] = 6
        result["status"] = "passed"
    except BaseException as error:
        result["status"] = "failed"
        result["failure"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        write(root / "result.json", result)
        print(f"Evidence and standalone test installation: {root}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    verify(args.archive.resolve())
