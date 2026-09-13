#!/usr/bin/env python3
"""Install and launch the trusted internal distribution; no network dependencies."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

APPS = {
    "decision-room": ("DecisionRoom", "DECISION"),
    "document-workshop": ("DocumentWorkshop", "DOCUMENT"),
    "appointment-desk": ("AppointmentDesk", "APPOINTMENT"),
}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text())


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def checked_path(root, name):
    relative = Path(name)
    if relative.is_absolute() or ".." in relative.parts or not relative.parts:
        raise ValueError(f"Invalid inventory path: {name}")
    path = root
    for part in relative.parts:
        path = path / part
        if path.is_symlink():
            raise ValueError(f"Symlink in delivery content: {name}")
    return path


def verify_files(root, expected):
    for name, sha in expected.items():
        path = checked_path(root, name)
        if not path.is_file() or digest(path) != sha:
            raise ValueError(f"Content mismatch: {name}")


def inventory(root):
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Unexpected symlink: {path.relative_to(root)}")
        if path.is_file():
            result[path.relative_to(root).as_posix()] = digest(path)
    return result


def bundle_check(root):
    manifest = read(root / "distribution.json")
    if manifest["schema"] != 1:
        raise ValueError("Unsupported distribution schema")
    verify_files(root, manifest["files"])
    actual = set(inventory(root)) - {"distribution.json"}
    if actual != set(manifest["files"]):
        raise ValueError("Unexpected delivery content")
    return manifest


def environment():
    # Do not inherit SDK/package overrides or Python module injection settings.
    env = {key: value for key, value in os.environ.items()
           if key in ("PATH", "HOME", "TMPDIR", "LANG", "LC_ALL")}
    env.update(PYTHONDONTWRITEBYTECODE="1", PIP_CONFIG_FILE=os.devnull,
               PIP_DISABLE_PIP_VERSION_CHECK="1", DOTNET_ROLL_FORWARD="LatestPatch")
    return env


def command(args, **kwargs):
    return subprocess.run([str(arg) for arg in args], env=environment(),
                          check=True, **kwargs)


def runtime_check(manifest, dotnet, python):
    required = manifest["prerequisites"]
    if platform.system() != "Darwin" or platform.machine() != "arm64":
        raise ValueError("This distribution is qualified only on macOS arm64")
    for name, path in (("dotnet", dotnet), ("python3", python)):
        if not path.is_file() or digest(path) != required["runtimeExecutables"][name]:
            raise ValueError(f"Qualified runtime executable mismatch: {name}")
    runtimes = command([dotnet, "--list-runtimes"], capture_output=True, text=True).stdout
    if {line.split()[1] for line in runtimes.splitlines()
            if line.startswith("Microsoft.NETCore.App 10.0.")} != {"10.0.12"}:
        raise ValueError("Required shared framework: Microsoft.NETCore.App 10.0.12")
    version = command([python, "--version"], capture_output=True, text=True).stdout.strip()
    if version != required["python"]:
        raise ValueError(f"Required Python: {required['python']}")


def app_content(root, manifest):
    expected = {name.removeprefix("apps/"): sha for name, sha in manifest["files"].items()
                if name.startswith("apps/")}
    verify_files(root / "var", expected)
    for app in APPS:
        for part in ("host", "releases"):
            actual = inventory(root / "var" / app / part)
            prefix = f"{app}/{part}/"
            selected = {name.removeprefix(prefix): sha for name, sha in expected.items()
                        if name.startswith(prefix)}
            if actual != selected:
                raise ValueError(f"Changed installed executable content: {app}/{part}")
    python_root = root / "var/decision-room/python"
    release = read(root / "var/decision-room/releases/1/installation.json")
    sdk = {name.removeprefix("sdk/"): sha.lower()
           for name, sha in release["RuntimeFiles"].items() if name.startswith("sdk/")}
    verify_files(python_root, sdk)
    actual_sdk = {path.relative_to(python_root).as_posix() for path in python_root.rglob("*.py")
                  if "weaveport_sdk" in path.parts}
    if actual_sdk != set(sdk):
        raise ValueError("Changed installed Python SDK content")
    if digest(python_root / "bin/python") != release["RuntimeFiles"]["python"].lower():
        raise ValueError("Changed virtual environment runtime")


def install(bundle, destination, dotnet, python):
    if destination.exists() or destination.is_symlink():
        raise ValueError("Destination already exists; installation never replaces existing state")
    manifest = bundle_check(bundle)
    runtime_check(manifest, dotnet, python)
    destination.mkdir(parents=True, exist_ok=False)
    # Failed setup intentionally leaves an incomplete directory for inspection.
    shutil.copytree(bundle, destination / "payload")
    shutil.copy2(bundle / "weaveport.py", destination / "weaveport.py")
    shutil.copytree(bundle / "apps", destination / "var")
    for app in APPS:
        (destination / "var" / app / "active-version.txt").write_text("1\n")
    venv = destination / "var/decision-room/python"
    command([python, "-m", "venv", venv])
    wheel = next((destination / "payload/packages/python").glob("*.whl"))
    command([venv / "bin/python", "-m", "pip", "--isolated", "install", "--no-index",
             "--no-deps", "--no-compile", wheel])
    bundle_check(destination / "payload")
    app_content(destination, manifest)
    write(destination / "installation.json", {
        "schema": 1, "version": manifest["version"], "root": str(destination),
        "manifestSha256": digest(destination / "payload/distribution.json"),
        "dotnet": str(dotnet), "python": str(python),
    })
    print(f"Installed {manifest['version']} at {destination}")


def doctor(root):
    receipt_path = root / "installation.json"
    if not receipt_path.is_file():
        raise ValueError("Incomplete installation: no completion receipt")
    receipt = read(receipt_path)
    if receipt["schema"] != 1 or receipt["root"] != str(root):
        raise ValueError("Installation moved or receipt incompatible; install into a new destination")
    payload = root / "payload"
    if digest(payload / "distribution.json") != receipt["manifestSha256"]:
        raise ValueError("Distribution manifest changed")
    manifest = bundle_check(payload)
    if receipt["version"] != manifest["version"]:
        raise ValueError("Receipt version mismatch")
    verify_files(root, {"weaveport.py": manifest["files"]["weaveport.py"]})
    runtime_check(manifest, Path(receipt["dotnet"]), Path(receipt["python"]))
    app_content(root, manifest)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    setup = sub.add_parser("install", help="Install this extracted bundle into a new directory")
    setup.add_argument("destination", type=Path)
    setup.add_argument("--dotnet", default=shutil.which("dotnet"), type=Path)
    setup.add_argument("--python", default=sys.executable, type=Path)
    sub.add_parser("doctor", help="Check installed content and required runtimes")
    run = sub.add_parser("run", help="Run an installed example (use absolute paths for custom files)")
    run.add_argument("app", choices=APPS)
    run.add_argument("arguments", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    if args.action == "install":
        if not args.dotnet:
            raise ValueError("dotnet prerequisite missing; pass --dotnet")
        install(root, args.destination.parent.resolve() / args.destination.name, args.dotnet.resolve(), args.python.resolve())
        return 0
    receipt = doctor(root)
    if args.action == "doctor":
        print(f"PASS: {receipt['version']} content and qualified runtimes; state preserved")
        return 0
    name, prefix = APPS[args.app]
    env = environment()
    env[f"WP_{prefix}_ROOT"] = str(root / "var" / args.app)
    env[f"WP_{prefix}_DOTNET"] = receipt["dotnet"]
    env["WP_DECISION_PYTHON"] = str(root / "var/decision-room/python/bin/python")
    arguments = args.arguments[1:] if args.arguments[:1] == ["--"] else args.arguments
    os.chdir(root / "payload/templates")
    os.execve(receipt["dotnet"], [receipt["dotnet"],
              str(root / "var" / args.app / "host" / f"{name}.Host.dll"), *arguments], env)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError, KeyError, StopIteration, subprocess.CalledProcessError) as error:
        print(f"REFUSED: {error}", file=sys.stderr)
        sys.exit(1)
