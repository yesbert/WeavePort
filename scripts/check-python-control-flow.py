#!/usr/bin/env python3
"""Reject nested business branches/loops while allowing early-exit loop guards."""

import ast
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


def violations(source):
    failures = []

    def visit(node, parent=None, alternative=False):
        if isinstance(node, SCOPES):
            parent = None
        if isinstance(node, CONTROLS):
            if (
                parent is not None
                and not alternative
                and not (not isinstance(parent, ast.If) and is_guard(node))
            ):
                failures.append(node.lineno)
            parent = node
        for field, value in ast.iter_fields(node):
            children = value if isinstance(value, list) else [value]
            for child in children:
                if not isinstance(child, ast.AST):
                    continue
                is_alternative = (
                    isinstance(node, ast.If)
                    and field == "orelse"
                    and len(children) == 1
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


if __name__ == "__main__":
    verify_fixtures()
    root = Path(__file__).resolve().parents[1]
    failures = [
        f"{path.relative_to(root)}:{line}"
        for path in sorted((root / "sdks/python/weaveport_sdk").rglob("*.py"))
        for line in violations(path.read_text())
    ]
    print("\n".join(failures) if failures else "Python control flow: passed")
    raise SystemExit(bool(failures))
