#!/usr/bin/env python3
"""Reject nested business branches/loops while allowing early-exit loop guards."""

import ast
import subprocess
from pathlib import Path

CONTROLS = (ast.If, ast.For, ast.AsyncFor, ast.While)
SCOPES = (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda)
EXITS = (ast.Return, ast.Raise, ast.Continue, ast.Break)


def is_guard(node):
    return (
        isinstance(node, ast.If)
        and not node.orelse
        and node.body
        and isinstance(node.body[-1], EXITS)
        and not any(
            isinstance(child, CONTROLS)
            for statement in node.body
            for child in ast.walk(statement)
        )
    )


def is_forbidden_nesting(node, parent, alternative):
    return (
        isinstance(node, CONTROLS)
        and parent is not None
        and not alternative
        and not (not isinstance(parent, ast.If) and is_guard(node))
    )


def violations(source):
    failures = []

    def visit(node, parent=None, alternative=False):
        if isinstance(node, SCOPES):
            parent = None
        if is_forbidden_nesting(node, parent, alternative):
            failures.append(node.lineno)
        if isinstance(node, CONTROLS):
            parent = node
        for child in ast.iter_child_nodes(node):
            is_alternative = (
                isinstance(node, ast.If)
                and len(node.orelse) == 1
                and node.orelse[0] is child
                and isinstance(child, ast.If)
            )
            visit(child, parent, is_alternative)

    visit(ast.parse(source))
    return failures


def verify_fixtures():
    allowed = [
        "if a:\n return\nif b:\n return",
        "for x in xs:\n if x is None:\n  continue\n use(x)",
        "if a:\n a()\nelif b:\n b()\nelse:\n c()",
    ]
    rejected = [
        "if a:\n if b:\n  use()",
        "for x in xs:\n for y in ys:\n  use(x,y)",
        "if a:\n for x in xs:\n  use(x)",
        "for x in xs:\n if x:\n  use(x)",
    ]
    for source in allowed:
        assert not violations(source), source
    for source in rejected:
        assert violations(source), source


def maintained_python_files(root):
    names = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "--", "*.py"],
        cwd=root,
        text=True,
    ).splitlines()
    # Retained report scripts reproduce old checkouts; their bytes are evidence.
    return [
        root / name
        for name in sorted(set(names))
        if not name.startswith(("reports/", "openspec/")) and (root / name).is_file()
    ]


if __name__ == "__main__":
    verify_fixtures()
    root = Path(__file__).resolve().parents[1]
    failures = [
        f"{path.relative_to(root)}:{line}"
        for path in maintained_python_files(root)
        for line in violations(path.read_text())
    ]
    print("\n".join(failures) if failures else "Python control flow: passed")
    raise SystemExit(bool(failures))
