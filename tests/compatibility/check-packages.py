"""Verify exact metadata in built artifacts, independent of installation declarations."""

import json
from pathlib import Path
import subprocess
import sys
import tarfile
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/distribution"))
from documentation import validate_package

EXPECTED_DEPENDENCIES = {
    "WeavePort.Abstractions": set(),
    "WeavePort.Hosting": {
        "WeavePort.Abstractions",
        "WeavePort.Sdk.Client",
        "Microsoft.Extensions.Logging.Abstractions",
        "Tomlyn",
        "Chasm.SemanticVersioning",
    },
    "WeavePort.Sdk.Client": {"WeavePort.Abstractions"},
    "WeavePort.Sdk": set(),
}


def check(condition, label):
    assert condition, label
    print("PASS: " + label)


def verify_dotnet_package(package_directory, external, name, version):
    path = package_directory / f"{name}.{version}.nupkg"
    validate_package(path)
    with zipfile.ZipFile(path) as archive:
        spec = ET.fromstring(
            archive.read(next(p for p in archive.namelist() if p.endswith(".nuspec")))
        )

        def elements(tag):
            return spec.findall(".//{*}" + tag)

        check(
            elements("id")[0].text == name and elements("version")[0].text == version,
            name + " packed identity matches exact matrix",
        )
        deps = elements("dependency")
        check(
            {d.attrib["id"] for d in deps} == EXPECTED_DEPENDENCIES[name]
            and all(
                d.attrib["version"]
                in (
                    external.get(d.attrib["id"], version),
                    f"[{external.get(d.attrib['id'], version)}]",
                )
                for d in deps
            ),
            name + " dependency closure matches reviewed package set",
        )
        check(
            any(
                p.startswith("lib/net10.0/") and p.endswith(name + ".dll")
                for p in archive.namelist()
            ),
            name + " supplies net10.0 implementation",
        )


def verify_python_sdk(root, policy):
    sdk_root = root / "artifacts/sdk-version-tests"
    python = sdk_root / "python/bin/python"
    installed = subprocess.check_output(
        [
            str(python),
            "-c",
            "import importlib.metadata; print(importlib.metadata.version('weaveport-sdk'))",
        ],
        text=True,
    ).strip()
    check(
        installed == policy["AuthorSdks"]["python"]["Version"],
        "installed Python SDK metadata matches matrix",
    )
    wheel = next((sdk_root / "wheel").glob("weaveport_sdk-*.whl"))
    with zipfile.ZipFile(wheel) as archive:
        metadata = archive.read(
            next(p for p in archive.namelist() if p.endswith(".dist-info/METADATA"))
        ).decode()
    check(
        "Version: " + installed + "\n" in metadata,
        "built Python wheel matches installed SDK",
    )


def verify_node_sdk(root, policy):
    sdk_root = root / "artifacts/sdk-version-tests"
    node = json.loads(
        (sdk_root / "node_modules/@weaveport/sdk/package.json").read_text()
    )
    expected = policy["AuthorSdks"]["node"]
    check(
        node["name"] == expected["Package"] and node["version"] == expected["Version"],
        "installed TypeScript SDK metadata matches matrix",
    )
    with tarfile.open(sdk_root / f"weaveport-sdk-{expected['Version']}.tgz") as archive:
        packed = json.load(archive.extractfile("package/package.json"))
    check(
        packed["name"] == node["name"] and packed["version"] == node["version"],
        "built npm package matches installed SDK",
    )


def main():
    policy = json.loads((ROOT / "compatibility/local-v1.json").read_text())
    packages = dict(policy["HostPackages"])
    sdk = policy["AuthorSdks"]["dotnet"]
    packages[sdk["Package"]] = sdk["Version"]
    external = json.loads((ROOT / "compatibility/external-packages.json").read_text())
    package_directory = (
        Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "artifacts/packages"
    )
    for name, version in packages.items():
        verify_dotnet_package(package_directory, external, name, version)
    verify_python_sdk(ROOT, policy)
    verify_node_sdk(ROOT, policy)
    print(f"Verification passed: {len(packages) * 3 + 4} artifact metadata assertions.")


if __name__ == "__main__":
    main()
