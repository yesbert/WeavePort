"""Check source ownership boundaries without requiring a compiled application."""
import ast
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DEPENDENCIES = {
    "WeavePort.Abstractions": set(),
    "WeavePort.Sdk": set(),
    "WeavePort.Sdk.Client": {"WeavePort.Abstractions"},
    "WeavePort.Hosting": {"WeavePort.Abstractions", "WeavePort.Sdk.Client"},
    "WeavePort.Composition": {"WeavePort.Sdk.Client"},
    "WeavePort.Sdk.Gateway.Client": {"WeavePort.Sdk.Client"},
    "WeavePort.Sdk.Gateway": {"WeavePort.Sdk.Gateway.Client"},
    "WeavePort.Testing": {"WeavePort.Abstractions"},
}


def check(root=ROOT):
    failures = []

    def require(condition, message):
        if not condition:
            failures.append(message)

    projects = list((root / "src").glob("*/*.csproj"))
    for project in projects:
        require(project.stem in DEPENDENCIES, f"Unreviewed package boundary: {project.stem}")
        if project.stem not in DEPENDENCIES:
            continue
        tree = ET.parse(project)
        actual = {Path(item.attrib["Include"]).stem for item in tree.findall(".//ProjectReference")}
        require(actual <= DEPENDENCIES[project.stem], f"Outward dependency in {project.stem}: {actual}")

    central = ET.parse(root / "Directory.Packages.props")
    versions = central.findall(".//PackageVersion")
    names = [item.attrib["Include"] for item in versions]
    require(len(names) == len(set(names)), "Duplicate central package versions")
    require(central.findtext(".//ManagePackageVersionsCentrally") == "true", "Central package management disabled")
    require(central.findtext(".//CentralPackageTransitivePinningEnabled") == "false", "Unreviewed transitive pinning")
    for area in ("src", "tests", "tools", "benchmarks", "plugins"):
        for project in (root / area).rglob("*.csproj"):
            if {"bin", "obj", "node_modules"} & set(project.parts):
                continue
            for item in ET.parse(project).findall(".//PackageReference"):
                require("Version" not in item.attrib and "VersionOverride" not in item.attrib
                        and item.find("Version") is None and item.find("VersionOverride") is None,
                        f"Scattered package version: {project.relative_to(root)}")
                require(item.get("Include") in names, f"Missing central version: {item.get('Include')}")

    typescript = root / "sdks/typescript/src"
    facade = (typescript / "index.ts").read_text()
    without_comments = re.sub(r"/\*.*?\*/|//[^\n]*", "", facade, flags=re.S)
    require(all(re.fullmatch(r"export (?:type )?\{[^}]+\} from ['\"][^'\"]+['\"];", line.strip())
                for line in without_comments.splitlines() if line.strip()), "TypeScript facade contains implementation")
    for path in typescript.rglob("*.ts"):
        require(not re.search(r"from ['\"][^'\"]*index\.js['\"]", path.read_text()),
                f"Internal import through public facade: {path.relative_to(root)}")

    python = root / "sdks/python/weaveport_sdk"
    for path in python.glob("*.py"):
        tree = ast.parse(path.read_text())
        if path.name == "__init__.py":
            for node in tree.body:
                require(isinstance(node, ast.ImportFrom) or
                        isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str) or
                        isinstance(node, ast.Assign) and all(isinstance(target, ast.Name) and target.id == "__all__" for target in node.targets),
                        "Python facade contains implementation")
        else:
            for node in ast.walk(tree):
                require(not (isinstance(node, ast.ImportFrom) and
                             (node.module == "weaveport_sdk" or node.level == 1 and node.module is None)),
                        f"Internal import through public facade: {path.name}")

    diagnostics = root / "src/WeavePort.Hosting/Diagnostics"
    events = re.findall(r"const int (\w+) = (\d+);", (diagnostics / "RuntimeLogEvents.cs").read_text())
    require(len(events) > 0 and len(events) == len({number for _, number in events}), "Missing or duplicate logging IDs")
    methods = (diagnostics / "RuntimeLog.cs").read_text()
    catalogue = (root / "docs/runtime-diagnostics.md").read_text()
    for name, number in events:
        require(f"LoggerMessage(RuntimeLogEvents.{name}," in methods, f"Unused generated event: {name}")
        require(f"| `{name}` | {number} |" in catalogue, f"Undocumented event: {name}")
    require(not re.search(r"LoggerMessage\(\d", methods), "Use named logging IDs")
    return failures


if __name__ == "__main__":
    errors = check()
    if errors:
        raise SystemExit("\n".join(errors))
    print("PASS architecture, central versions, SDK facades and diagnostic catalogue")
