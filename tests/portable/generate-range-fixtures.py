"""Optional fixture refresh: run from the repository with packaging==25.0 installed."""

import json
from itertools import product
from pathlib import Path
from packaging.specifiers import SpecifierSet

RANGES = [
    ">=3.11,<4",
    "~=3.11",
    "~=3.11.0",
    "==3.11.*",
    "!=3.11.*",
    "==3.11",
    "!=3.11",
    "<3.11",
    "<=3.11",
    ">3.11",
    ">=3.11",
    ">=3.11a1",
    ">=3.11rc1",
    ">=3.11.dev1",
    ">3.11.post1",
    "<=3.11rc2",
    "~=3.11a1",
    "==1!3.11.*",
    ">=1!3.11",
    "==3.11+abc",
    "===3.11.0",
    ">=3.11,!=3.12.*",
    ">=3.11rc1,<3.11",
    ">3.11a1",
    "<3.11.post1",
    ">=3.11rc1,!=3.11rc2",
]
VERSIONS = [
    "3.10.9",
    "3.11",
    "3.11.0",
    "3.11.1",
    "3.12",
    "4.0",
    "3.11a0",
    "3.11a1",
    "3.11b1",
    "3.11rc1",
    "3.11rc2",
    "3.11.dev1",
    "3.11a1.dev1",
    "3.11.post0",
    "3.11.post1",
    "3.11.post1.dev1",
    "3.11+abc",
    "3.11+def",
    "3.11+123",
    "1!3.11",
    "3.12a1",
    "0!3.11",
    "3.11.0.0",
    "3.11preview2",
    "v3.11",
    "3.11-1",
]


def main():
    cases = [
        dict(
            range=specifier,
            version=version,
            expected=SpecifierSet(specifier).contains(version),
        )
        for specifier, version in product(RANGES, VERSIONS)
    ]
    root = Path(__file__).resolve().parents[2]
    fixture = root / "tests/WeavePort.Hosting.Tests/fixtures/python-ranges.json"
    fixture.write_text("[\n" + ",\n".join(json.dumps(case) for case in cases) + "\n]\n")


if __name__ == "__main__":
    main()
