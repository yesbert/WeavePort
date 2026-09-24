"""Prepare offline consumer guidance and reject unresolved artifact-relative links."""

import os
from pathlib import Path
import posixpath
import re
import subprocess
from urllib.parse import unquote, quote, urlsplit
import zipfile

LINK = re.compile(r"\[([^\]\n]*)\]\(([^)\n]+)\)")
REPOSITORY = Path(__file__).resolve().parents[2]
REMOTE = "https://github.com/yesbert/WeavePort"


def relative_target(source, href):
    parsed = urlsplit(href.strip("<>"))
    if parsed.scheme or parsed.netloc or not parsed.path:
        return None
    return posixpath.normpath(
        posixpath.join(posixpath.dirname(source), unquote(parsed.path))
    )


def check_links(contents, names):
    failures = []
    for source, text in contents.items():
        failures.extend(missing_links(source, text, names))
    if failures:
        raise ValueError(
            "Missing relative documentation targets: " + "; ".join(failures)
        )


def target_exists(target, names):
    return target in names or any(name.startswith(target + "/") for name in names)


def missing_links(source, text, names):
    failures = []
    for match in LINK.finditer(text):
        target = relative_target(source, match[2])
        if target is None or target_exists(target, names):
            continue
        failures.append(f"{source}: {match[2]}")
    return failures


def validate(root):
    names = {
        path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file()
    }
    contents = {
        name: (root / name).read_text() for name in names if name.endswith(".md")
    }
    check_links(contents, names)


def validate_package(path):
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        check_links(
            {
                name: archive.read(name).decode()
                for name in names
                if name.endswith(".md")
            },
            names,
        )


class GuidanceClosure:
    """Copy the transitive guide set and rewrite links for one offline layout."""

    def __init__(self, layout, commit, tracked, templates):
        self.layout = layout
        self.commit = commit
        self.tracked = tracked
        self.templates = templates
        self.pending = [
            (f"docs/{name}", Path("docs", name))
            for name in (
                "native-operations.md",
                "embedded-coordinator.md",
                "runtime-diagnostics.md",
            )
        ]
        if templates:
            self.pending.extend(
                (path.relative_to(layout).as_posix(), path.relative_to(layout))
                for path in (layout / "samples").rglob("*.md")
            )

    def write(self):
        visited = set()
        while self.pending:
            source, destination = self.pending.pop()
            if source in visited:
                continue
            visited.add(source)
            text = subprocess.check_output(
                ["git", "show", f"{self.commit}:{source}"], cwd=REPOSITORY, text=True
            )
            output = self.layout / destination
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(
                LINK.sub(lambda match: self.rewrite(source, destination, match), text)
            )

    def rewrite(self, source, destination, match):
        target = relative_target(source, match[2])
        if target is None:
            return match[0]
        if not target_exists(target, self.tracked):
            raise ValueError(
                f"Missing repository documentation target: {source}: {target}"
            )
        local = self.local_target(target)
        if local is None:
            return f'[{match[1]} (repository context)]({REMOTE}/blob/{self.commit}/{quote(target, safe="/")})'
        href = os.path.relpath(local, destination.parent).replace(os.sep, "/")
        fragment = urlsplit(match[2]).fragment
        return f"[{match[1]}]({href}" + (f"#{fragment}" if fragment else "") + ")"

    def local_target(self, target):
        if target.startswith("docs/") and target.endswith(".md"):
            local = Path(target)
            self.pending.append((target, local))
            return local
        sample = Path(target) if self.templates else Path("templates", target)
        if target.startswith("samples/") and (self.layout / sample).exists():
            return sample
        return None


def prepare(bundle, commit):
    tracked = set(
        subprocess.check_output(
            ["git", "ls-tree", "-r", "--name-only", commit], cwd=REPOSITORY, text=True
        ).splitlines()
    )
    # Each copied templates directory has its own complete guidance closure.
    GuidanceClosure(bundle, commit, tracked, templates=False).write()
    GuidanceClosure(bundle / "templates", commit, tracked, templates=True).write()
    validate(bundle)
    validate(bundle / "templates")
    for path in (bundle / "packages/nuget").glob("WeavePort.*.nupkg"):
        validate_package(path)
