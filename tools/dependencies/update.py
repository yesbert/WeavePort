"""Apply a fully resolved dependency plan with failure restoration of manifests."""

import json
from pathlib import Path
import re
import subprocess


def replace_once(text, pattern, replacement):
    result, count = re.subn(pattern, replacement, text)
    if count != 1:
        raise ValueError(f"Expected one dependency declaration, found {count}: {pattern}")
    return result


def rewrite(text, item, version):
    if item.kind in ("npm", "tool"):
        data = json.loads(text)
        write_json_version(data, item, version)
        return json.dumps(data, indent=2) + "\n"
    package = re.escape(item.package)
    old = re.escape(item.version)
    if item.kind == "nuget":
        return replace_once(text, rf'(Include="{package}"\s+Version="){old}(")', lambda m: m[1] + version + m[2])
    if item.kind == "python":
        return replace_once(text, rf'("{package}==){old}(")', lambda m: m[1] + version + m[2])
    if item.kind == "workflow-tool":
        return replace_once(text, rf'(dotnet tool install {package} --version ){old}\b', lambda m: m[1] + version)
    if item.kind == "workflow-npm":
        return replace_once(text, rf'(npm install --global {package}@){old}\b', lambda m: m[1] + version)
    raise ValueError("Unknown manifest kind: " + item.kind)


def write_json_version(data, item, version):
    if item.kind == "tool":
        data["tools"][item.package]["version"] = version
        return
    data[item.section][item.package] = version


def snapshot(root, paths):
    return {path: (root / path).read_bytes() if (root / path).exists() else None for path in paths}


def restore(root, files):
    for name, content in files.items():
        restore_file(root / name, content)


def restore_file(path, content):
    if content is None:
        path.unlink(missing_ok=True)
        return
    path.write_bytes(content)


def apply(root, plan):
    paths = {item.path for item, target, _ in plan}
    npm_projects = {str((root / item.path).parent.relative_to(root)) for item, _, _ in plan if item.kind == "npm"}
    paths.update(str(Path(project) / "package-lock.json") for project in npm_projects)
    before = snapshot(root, paths)
    try:
        for item, target, _ in plan:
            update_file(root, item, target)
        update_npm(root, npm_projects)
    except BaseException:
        restore(root, before)
        raise


def update_file(root, item, target):
    if item.version == target:
        return
    path = root / item.path
    path.write_text(rewrite(path.read_text(), item, target))


def update_npm(root, projects):
    sdk = "sdks/typescript"
    ordered = sorted(projects, key=lambda project: (project != sdk, project))
    for project in ordered:
        update_npm_project(root, project)


def update_npm_project(root, project):
    if project == "examples/sdk/typescript":
        prepare_sdk_archive(root)
    subprocess.run(["npm", "update", "--package-lock-only", "--ignore-scripts", "--no-audit", "--no-fund"], cwd=root / project, check=True)


def prepare_sdk_archive(root):
    project = root / "sdks/typescript"
    output = root / "artifacts/sdk-packages-js"
    output.mkdir(parents=True, exist_ok=True)
    subprocess.run(["npm", "ci", "--ignore-scripts", "--no-audit", "--no-fund"], cwd=project, check=True)
    subprocess.run(["npm", "run", "build"], cwd=project, check=True)
    subprocess.run(["npm", "pack", "--ignore-scripts", "--pack-destination", str(output)], cwd=project, check=True)
