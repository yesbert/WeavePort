"""Update maintained package manifests using reviewed pins and registry evidence."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from dataclasses import asdict
import json
from pathlib import Path
import subprocess
import sys

from inventory import discover, pins
from registry import latest, version_key
from update import apply

ROOT = Path(__file__).resolve().parents[2]


def target(item, available, holds):
    matching = [pin for pin in holds if pin["registry"] == item.registry and pin["package"] == item.package
                and pin.get("path", item.path) == item.path]
    if len(matching) > 1:
        raise ValueError("Ambiguous pins: " + item.package)
    if matching:
        pin = matching[0]
        version_key(pin["version"])
        return pin["version"], "held: " + pin["reason"] + "; review: " + pin["review_when"]
    if version_key(available) < version_key(item.version):
        raise ValueError("Registry would downgrade " + item.package)
    return available, "latest stable"


def resolve(root):
    items = discover(root)
    holds = pins(root)
    keys = sorted({(item.registry, item.package) for item in items})
    with ThreadPoolExecutor(max_workers=8) as pool:
        versions = dict(zip(keys, pool.map(lambda key: latest(*key), keys)))
    return [(item, *target(item, versions[(item.registry, item.package)], holds)) for item in items]


def execute(root, command):
    print("RUN " + " ".join(command), flush=True)
    subprocess.run(command, cwd=root, check=True)


def build(root):
    execute(root, ["./scripts/prepare-core-packages.sh"])
    execute(root, ["dotnet", "pack", "src/WeavePort.Testing", "-c", "Release", "-o", "artifacts/packages"])
    execute(root, ["dotnet", "build", "WeavePort.slnx", "-c", "Release"])


def tests(root):
    execute(root, ["./scripts/verify-portable.sh"])
    execute(root, ["npm", "ci", "--prefix", "sdks/typescript", "--ignore-scripts"])
    execute(root, ["npm", "test", "--prefix", "sdks/typescript"])


def audit(root, plan):
    command = ["dotnet", "list", "WeavePort.slnx", "package", "--vulnerable", "--include-transitive", "--format", "json"]
    result = subprocess.check_output(command, cwd=root, text=True)
    (root / "artifacts/dependency-update/nuget-audit.json").write_text(result)
    findings = json.loads(result)
    packages = [p for project in findings.get("projects", []) for framework in project.get("frameworks", [])
                for group in ("topLevelPackages", "transitivePackages") for p in framework.get(group, [])]
    if any(p.get("vulnerabilities") for p in packages):
        raise ValueError("NuGet vulnerability audit reported findings")
    projects = sorted({str(Path(item.path).parent) for item, _, _ in plan if item.kind == "npm"})
    for project in projects:
        execute(root, ["npm", "audit", "--prefix", project, "--audit-level=low"])


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="Resolve and report without writes, installs, builds or audits")
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--skip-tests", action="store_true")
    parser.add_argument("--skip-audit", action="store_true")
    return parser.parse_args()


def main():
    args = arguments()
    plan = resolve(ROOT)
    for item, value, reason in plan:
        print(f"{item.registry}: {item.package} {item.version} -> {value} ({item.path}; {reason})", flush=True)
    if args.dry_run:
        return
    output = ROOT / "artifacts/dependency-update"
    output.mkdir(parents=True, exist_ok=True)
    report = [{**asdict(item), "target": value, "reason": reason} for item, value, reason in plan]
    (output / "plan.json").write_text(json.dumps(report, indent=2) + "\n")
    apply(ROOT, plan)
    execute(ROOT, ["dotnet", "tool", "restore"])
    if not args.skip_build:
        build(ROOT)
    if not args.skip_tests:
        tests(ROOT)
    if not args.skip_audit:
        audit(ROOT, plan)
    print("Dependency update completed. Review licenses/API baselines and run committed ./scripts/verify.sh before release.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))
