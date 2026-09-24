"""Check optional package identity, dependency closure and client-only deployment."""

import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile


def verify_package(root, version, name, dependencies):
    path = root / "artifacts/packages" / f"{name}.{version}.nupkg"
    with zipfile.ZipFile(path) as archive:
        spec = ET.fromstring(archive.read(name + ".nuspec"))
        value = lambda key: spec.findtext(".//{*}" + key)
        assert value("id") == name and value("version") == version
        assert value("license") == "MIT"
        assert (
            value("readme") in archive.namelist()
            and value("icon") in archive.namelist()
        )
        actual = {
            d.attrib["id"]: d.attrib["version"]
            for d in spec.findall(".//{*}dependency")
        }
        assert actual == {
            k: v.replace("$version", version) for k, v in dependencies.items()
        }, (name, actual)
        assert not any(
            "Grpc.Tools" in n or n.endswith("/protoc") for n in archive.namelist()
        )
        assert not name.endswith(".Client") or not spec.findall(
            ".//{*}frameworkReference"
        ), "Client requires shared hosting framework"
        print("PASS: optional package metadata and dependency contract " + name)


def verify_license(assets, key, license_name):
    name, release = key.split("/")
    relative = Path(name.lower(), release, f"{name.lower()}.{release}.nupkg")
    package = next(
        Path(folder) / relative
        for folder in assets["packageFolders"]
        if (Path(folder) / relative).exists()
    )
    with zipfile.ZipFile(package) as archive:
        metadata = ET.fromstring(
            archive.read(next(n for n in archive.namelist() if n.endswith(".nuspec")))
        )
        assert metadata.findtext(".//{*}license") == license_name, key


def verify_source_closure(root, project, dependencies):
    assets = json.loads(
        (root / "src" / project / "obj/project.assets.json").read_text()
    )
    actual = {
        key: value
        for key, value in assets["libraries"].items()
        if value["type"] == "package"
    }
    assert set(actual) == set(dependencies), (project, set(actual), set(dependencies))
    for key, license_name in dependencies.items():
        verify_license(assets, key, license_name)
    print("PASS: exact optional dependency licenses " + project)


def main():
    root = Path(__file__).resolve().parents[2]
    version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
    expected = json.loads(
        (root / "compatibility/optional-dependencies.json").read_text()
    )
    for name, dependencies in expected["packages"].items():
        verify_package(root, version, name, dependencies)
    for project, dependencies in expected["sourceClosures"].items():
        verify_source_closure(root, project, dependencies)


if __name__ == "__main__":
    main()
