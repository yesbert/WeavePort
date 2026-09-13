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
        if PRIVATE_PARTS.intersection(path.parts):
            failures.append(name + ": local context must not be tracked")
        if path.suffix.lower() in {".md", ".txt", ".yaml", ".yml"} and (root / path).is_file():
            content = (root / path).read_text()
            if re.search(r"https?://dev\.azure\.com/|/(?:Users|Volumes)/", content):
                failures.append(name + ": private repository or machine-local reference")
    if failures:
        raise ValueError("\n".join(failures))


if __name__ == "__main__":
    names = subprocess.check_output(["git", "ls-files"], cwd=ROOT, text=True).splitlines()
    validate(names)
    print("PASS: tracked public tree excludes local context and private documentation references")
