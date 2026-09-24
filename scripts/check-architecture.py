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


class ArchitectureAudit:
    def __init__(self, root):
        self.root = root
        self.failures = []

    def require(self, condition, message):
        if not condition:
            self.failures.append(message)

    def run(self):
        self.check_dependencies()
        self.check_central_versions()
        self.check_typescript_facade()
        self.check_python_facade()
        self.check_logging_catalogue()
        return self.failures

    def check_dependencies(self):
        for project in (self.root / "src").glob("*/*.csproj"):
            self.require(
                project.stem in DEPENDENCIES,
                f"Unreviewed package boundary: {project.stem}",
            )
            if project.stem not in DEPENDENCIES:
                continue
            references = ET.parse(project).findall(".//ProjectReference")
            actual = {Path(item.attrib["Include"]).stem for item in references}
            self.require(
                actual <= DEPENDENCIES[project.stem],
                f"Outward dependency in {project.stem}: {actual}",
            )

    def maintained_projects(self):
        areas = (
            self.root / area
            for area in ("src", "tests", "tools", "benchmarks", "plugins")
        )
        for project in (
            project for area in areas for project in area.rglob("*.csproj")
        ):
            if {"bin", "obj", "node_modules"} & set(project.parts):
                continue
            yield project

    def check_central_versions(self):
        central = ET.parse(self.root / "Directory.Packages.props")
        names = [
            item.attrib["Include"] for item in central.findall(".//PackageVersion")
        ]
        self.require(
            len(names) == len(set(names)), "Duplicate central package versions"
        )
        self.require(
            central.findtext(".//ManagePackageVersionsCentrally") == "true",
            "Central package management disabled",
        )
        self.require(
            central.findtext(".//CentralPackageTransitivePinningEnabled") == "false",
            "Unreviewed transitive pinning",
        )
        for project in self.maintained_projects():
            self.check_project_versions(project, names)

    def check_project_versions(self, project, names):
        for item in ET.parse(project).findall(".//PackageReference"):
            self.require(
                "Version" not in item.attrib
                and "VersionOverride" not in item.attrib
                and item.find("Version") is None
                and item.find("VersionOverride") is None,
                f"Scattered package version: {project.relative_to(self.root)}",
            )
            self.require(
                item.get("Include") in names,
                f"Missing central version: {item.get('Include')}",
            )

    def check_typescript_facade(self):
        typescript = self.root / "sdks/typescript/src"
        facade = (typescript / "index.ts").read_text()
        without_comments = re.sub(r"/\*.*?\*/|//[^\n]*", "", facade, flags=re.S)
        self.require(
            all(
                re.fullmatch(
                    r"export (?:type )?\{[^}]+\} from ['\"][^'\"]+['\"];", line.strip()
                )
                for line in without_comments.splitlines()
                if line.strip()
            ),
            "TypeScript facade contains implementation",
        )
        for path in typescript.rglob("*.ts"):
            self.require(
                not re.search(r"from ['\"][^'\"]*index\.js['\"]", path.read_text()),
                f"Internal import through public facade: {path.relative_to(self.root)}",
            )

    def check_python_facade(self):
        python = self.root / "sdks/python/weaveport_sdk"
        for path in python.glob("*.py"):
            tree = ast.parse(path.read_text())
            if path.name == "__init__.py":
                self.check_python_exports(tree)
                continue
            self.check_python_imports(tree, path)

    def check_python_exports(self, tree):
        for node in tree.body:
            self.require(
                is_python_export(node), "Python facade contains implementation"
            )

    def check_python_imports(self, tree, path):
        for node in ast.walk(tree):
            self.require(
                not (
                    isinstance(node, ast.ImportFrom)
                    and (
                        node.module == "weaveport_sdk"
                        or node.level == 1
                        and node.module is None
                    )
                ),
                f"Internal import through public facade: {path.name}",
            )

    def check_logging_catalogue(self):
        diagnostics = self.root / "src/WeavePort.Hosting/Diagnostics"
        events = re.findall(
            r"const int (\w+) = (\d+);",
            (diagnostics / "RuntimeLogEvents.cs").read_text(),
        )
        self.require(
            len(events) > 0 and len(events) == len({number for _, number in events}),
            "Missing or duplicate logging IDs",
        )
        methods = (diagnostics / "RuntimeLog.cs").read_text()
        catalogue = (self.root / "docs/runtime-diagnostics.md").read_text()
        for name, number in events:
            self.require(
                f"LoggerMessage(RuntimeLogEvents.{name}," in methods,
                f"Unused generated event: {name}",
            )
            self.require(
                f"| `{name}` | {number} |" in catalogue, f"Undocumented event: {name}"
            )
        self.require(
            not re.search(r"LoggerMessage\(\d", methods), "Use named logging IDs"
        )


def is_python_export(node):
    if isinstance(node, ast.ImportFrom):
        return True
    if isinstance(node, ast.Expr):
        return isinstance(node.value, ast.Constant) and isinstance(
            node.value.value, str
        )
    if not isinstance(node, ast.Assign):
        return False
    if not all(
        isinstance(target, ast.Name) and target.id == "__all__"
        for target in node.targets
    ):
        return False
    try:
        names = ast.literal_eval(node.value)
    except (ValueError, TypeError):
        return False
    return isinstance(names, (list, tuple)) and all(
        isinstance(name, str) for name in names
    )


def check(root=ROOT):
    return ArchitectureAudit(root).run()


def cli():
    errors = check()
    if errors:
        raise SystemExit("\n".join(errors))
    print("PASS architecture, central versions, SDK facades and diagnostic catalogue")


if __name__ == "__main__":
    cli()
