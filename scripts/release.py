"""Validate a release tag and export the exact NuGet artifacts from a passed candidate."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate_tag(tag, root=ROOT):
    match = re.fullmatch(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?", tag)
    require(match is not None, "Expected vMAJOR.MINOR.PATCH[-prerelease]")
    version = tag[1:]
    suffix = match[4]
    require(not suffix or all(not part.isdigit() or part == "0" or not part.startswith("0")
                             for part in suffix.split(".")), "Numeric prerelease identifiers cannot have leading zeros")
    require(not suffix or "internal" not in suffix.lower().split("."), "Internal candidates must not be published to NuGet.org")
    props = ET.parse(root / "Directory.Build.props")
    require(props.findtext(".//Version") == version, "Tag must exactly match Directory.Build.props Version")
    require((root / "LICENSE").is_file(), "Confirm the repository license before publishing")
    require(ET.parse(root / "src/Directory.Build.props").findtext(".//PackageLicenseExpression"),
            "Confirm the package license before publishing")
    return version


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def export(candidate, destination, version, root=ROOT):
    record = json.loads((candidate / "result.json").read_text())
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    require(record["status"] == "passed" and record["sourceCommit"] == commit,
            "Candidate must have passed for the checked-out commit")
    manifest = json.loads((candidate / "artifact-manifest.json").read_text())
    require(digest(candidate / "artifact-manifest.json") == record["artifactManifestSha256"], "Candidate manifest changed")
    package_hashes = json.loads((candidate / "package-set.json").read_text())
    packages = json.loads((root / "build/release-packages.json").read_text())
    require(len(packages) == len(set(packages)) and packages, "Invalid release allowlist")
    selected = []
    for package in packages:
        path = candidate / "checkout/artifacts/packages" / f"{package}.{version}.nupkg"
        relative = path.relative_to(candidate / "checkout").as_posix()
        require(path.is_file(), f"Missing package: {path.name}")
        require(digest(path) == package_hashes.get(path.name) == manifest.get(relative), f"Package changed: {path.name}")
        with zipfile.ZipFile(path) as archive:
            spec = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith(".nuspec"))))
            def value(name):
                return spec.findtext(".//{*}" + name)
            require(value("id") == package and value("version") == version, "Packed identity mismatch")
            expected_license = ET.parse(root / "src/Directory.Build.props").findtext(".//PackageLicenseExpression")
            license_element = spec.find(".//{*}license")
            require(expected_license and value("license") == expected_license
                    and license_element.get("type") == "expression", "Packed license must match the confirmed package license")
            require(value("readme") in archive.namelist(), "Missing packaged readme")
            require(value("icon") == "logo.png" and "logo.png" in archive.namelist(),
                    "Missing packaged project icon")
            require(archive.read("logo.png") == (root / "website/assets/logo.png").read_bytes(),
                    "Packaged icon differs from the project logo")
            repository = spec.find(".//{*}repository")
            require(repository is not None and repository.get("url") == "https://github.com/yesbert/WeavePort"
                    and repository.get("commit") == commit, "Missing exact GitHub source provenance")
        selected.append(path)
        symbols = path.with_suffix(".snupkg")
        symbol_relative = symbols.relative_to(candidate / "checkout").as_posix()
        require(symbols.is_file() and digest(symbols) == manifest.get(symbol_relative), "Missing or changed qualified symbols")
        selected.append(symbols)
    policy = json.loads((root / "compatibility/local-v1.json").read_text())["AuthorSdks"]
    author_root = candidate / "checkout/artifacts/sdk-version-tests"
    author_files = [author_root / "wheel" / f"weaveport_sdk-{policy['python']['Version']}-py3-none-any.whl",
                    author_root / f"weaveport-sdk-{policy['node']['Version']}.tgz"]
    for path in author_files:
        relative = path.relative_to(candidate / "checkout").as_posix()
        require(path.is_file() and digest(path) == manifest.get(relative), "Missing or changed qualified author SDK: " + path.name)
        expected_license = (root / "LICENSE").read_bytes()
        if path.suffix == ".whl":
            with zipfile.ZipFile(path) as archive:
                license_path = f"weaveport_sdk-{policy['python']['Version']}.dist-info/licenses/LICENSE"
                require(license_path in archive.namelist() and archive.read(license_path) == expected_license,
                        "Author SDK license missing or differs from the project license")
        else:
            with tarfile.open(path) as archive:
                require("package/LICENSE" in archive.getnames(), "Author SDK license missing")
                member = archive.getmember("package/LICENSE")
                require(member.isfile() and archive.extractfile(member).read() == expected_license,
                        "Author SDK license differs from the project license")
        selected.append(path)
    require(not destination.exists(), "Output directory already exists; use a new destination")
    destination.mkdir(parents=True)
    for path in selected:
        shutil.copy2(path, destination / path.name)
    print(f"Exported {len(packages)} qualified packages and symbols plus {len(author_files)} author SDK artifacts")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tag")
    parser.add_argument("--candidate", type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/release")
    args = parser.parse_args()
    try:
        version = validate_tag(args.tag)
        if args.candidate:
            export(args.candidate.resolve(), args.output.resolve(), version)
        print(f"Validated release {version}")
    except (ValueError, KeyError, FileNotFoundError, ET.ParseError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error)) from error
