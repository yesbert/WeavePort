"""Reject tracked local context and private documentation references."""

from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PRIVATE_PARTS = {".agents", ".claude", ".codex", "AGENTS.md", "CLAUDE.md"}


def validate(names, root=ROOT):
    failures = []
    for name in names:
        path = Path(name)
        failures.extend(
            [name + ": local context must not be tracked"]
            if PRIVATE_PARTS.intersection(path.parts)
            else []
        )
        failures.extend(document_failures(name, root))
    if failures:
        raise ValueError("\n".join(failures))


def document_failures(name, root):
    path = root / name
    if (
        path.suffix.lower() not in {".md", ".txt", ".yaml", ".yml"}
        or not path.is_file()
    ):
        return []
    if re.search(r"https?://dev\.azure\.com/|/(?:Users|Volumes)/", path.read_text()):
        return [name + ": private repository or machine-local reference"]
    return []


if __name__ == "__main__":
    names = subprocess.check_output(
        ["git", "ls-files"], cwd=ROOT, text=True
    ).splitlines()
    validate(names)
    print(
        "PASS: tracked public tree excludes local context and private documentation references"
    )
