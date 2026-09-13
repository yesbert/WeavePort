"""Private context is rejected while future public OpenSpec archives remain valid."""
import importlib.util
from pathlib import Path
import tempfile

spec = importlib.util.spec_from_file_location("public_tree", Path(__file__).resolve().parents[2] / "scripts/check-public-tree.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    module.validate(["openspec/specs/runtime/spec.md", "openspec/changes/archive/future-change/design.md"], root)
    for name in [".agents/skills/a.md", ".claude/settings.json", ".codex/config.toml", "AGENTS.md", "CLAUDE.md"]:
        try:
            module.validate([name], root)
        except ValueError:
            pass
        else:
            raise AssertionError("Accepted tracked private context")
    (root / "guide.md").write_text("[private](https://" + "dev.azure.com/example)")
    try:
        module.validate(["guide.md"], root)
    except ValueError:
        pass
    else:
        raise AssertionError("Accepted private documentation reference")
print("PASS: private entries refused; future OpenSpec archives accepted")
