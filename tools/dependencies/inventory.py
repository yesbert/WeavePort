"""Discover maintained dependencies; never refresh historical release evidence."""

from dataclasses import dataclass
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


@dataclass(frozen=True)
class Dependency:
    registry: str
    package: str
    version: str
    path: str
    kind: str
    section: str = ""


def tracked(root):
    return subprocess.check_output(["git", "ls-files"], cwd=root, text=True).splitlines()


def maintained(path):
    return not path.startswith(("reports/", "openspec/", "artifacts/")) and "/fixtures/" not in path


def npm_dependencies(root, path):
    data = json.loads((root / path).read_text())
    groups = ("dependencies", "devDependencies", "optionalDependencies")
    return [Dependency("npm", name, version, path, "npm", group)
            for group in groups for name, version in data.get(group, {}).items()
            if not version.startswith(("file:", "workspace:", "link:"))]


def discover(root):
    props = "Directory.Packages.props"
    result = [Dependency("nuget", item.attrib["Include"], item.attrib["Version"], props, "nuget")
              for item in ET.parse(root / props).iter("PackageVersion")
              if not item.attrib["Include"].startswith("WeavePort.")]
    for path in filter(maintained, tracked(root)):
        result.extend(discover_file(root, path))
    return result


def discover_file(root, path):
    file = root / path
    if file.name == "package.json":
        return npm_dependencies(root, path)
    if path == ".config/dotnet-tools.json":
        return [Dependency("nuget", name, value["version"], path, "tool")
                for name, value in json.loads(file.read_text())["tools"].items()]
    if file.name == "pyproject.toml":
        return [Dependency("pypi", name, version, path, "python")
                for name, version in re.findall(r'"([\w.-]+)==([\d.]+)"', file.read_text())]
    if path.startswith(".github/workflows/") and file.suffix in (".yml", ".yaml"):
        return workflow_dependencies(path, file.read_text())
    return []


def pins(root):
    path = root / ".dependency-pins.json"
    if not path.exists():
        return []
    data = json.loads(path.read_text())
    for pin in data["pins"]:
        if not pin.get("reason") or not pin.get("review_when"):
            raise ValueError("Dependency pins require reason and review_when")
        if pin["registry"] not in ("nuget", "npm", "pypi"):
            raise ValueError("Unknown pin registry")
    return data["pins"]


def workflow_dependencies(path, text):
    tools = [Dependency("nuget", name, version, path, "workflow-tool")
             for name, version in re.findall(r"dotnet tool install ([\w.-]+) --version ([\d.]+)", text)]
    npm = [Dependency("npm", name, version, path, "workflow-npm")
           for name, version in re.findall(r"npm install --global ([@\w/.-]+)@([\d.]+)", text)]
    return tools + npm
