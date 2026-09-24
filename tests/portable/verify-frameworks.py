"""Compare WeavePort matching to real dotnet framework selection without plugin code."""

from itertools import product
import json
from pathlib import Path
import subprocess
import sys

ROLL_FORWARD_POLICIES = (
    "Disable",
    "LatestPatch",
    "Minor",
    "LatestMinor",
    "Major",
    "LatestMajor",
)
FRAMEWORK_PAIRS = (
    ("8.0.0", "10.0.0"),
    ("10.0.0", "8.0.0"),
    ("8.0.28", "8.0.0"),
    ("9.0.0", "9.0.0"),
)
CORE_FRAMEWORK = "Microsoft.NETCore.App"
WEB_FRAMEWORK = "Microsoft.AspNetCore.App"


def framework(name, version):
    return {"name": name, "version": version}


def configurations(core_versions):
    minimum_major = min(int(version.split(".")[0]) for version in core_versions)
    minimum = f"{minimum_major}.0.0"
    for policy in ROLL_FORWARD_POLICIES:
        yield {"rollForward": policy, "framework": framework(CORE_FRAMEWORK, minimum)}
    for version in (core_versions[-1], "99.0.0", f"{minimum_major}.0.999"):
        yield {
            "rollForward": "Disable",
            "framework": framework(CORE_FRAMEWORK, version),
        }
    for secondary in (WEB_FRAMEWORK, "Missing.Framework"):
        yield {
            "frameworks": [
                framework(CORE_FRAMEWORK, minimum),
                framework(secondary, minimum),
            ]
        }
    for policy, (core_version, web_version) in product(
        ROLL_FORWARD_POLICIES, FRAMEWORK_PAIRS
    ):
        yield {
            "rollForward": policy,
            "frameworks": [
                framework(CORE_FRAMEWORK, core_version),
                framework(WEB_FRAMEWORK, web_version),
            ],
        }


def build_oracle(dotnet, work):
    work.mkdir(parents=True, exist_ok=True)
    (work / "Oracle.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        "<TargetFramework>net8.0</TargetFramework><UseAppHost>false</UseAppHost>"
        "</PropertyGroup></Project>"
    )
    (work / "Program.cs").write_text(
        "System.Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);"
    )
    subprocess.run(
        [dotnet, "build", str(work / "Oracle.csproj"), "-c", "Release"],
        check=True,
        capture_output=True,
    )
    return work / "bin/Release/net8.0"


def observe(dotnet, output, options, inventory):
    config = {"runtimeOptions": options}
    (output / "Oracle.runtimeconfig.json").write_text(json.dumps(config))
    result = subprocess.run(
        [dotnet, str(output / "Oracle.dll")], text=True, capture_output=True, timeout=10
    )
    return dict(
        config=json.dumps(config),
        inventory=inventory,
        expected=result.returncode == 0,
        observed=result.stdout.strip(),
    )


def main():
    dotnet = sys.argv[1]
    root = Path(__file__).resolve().parents[2]
    work = root / "artifacts/portable/framework-oracle"
    output = build_oracle(dotnet, work)
    inventory = subprocess.check_output([dotnet, "--list-runtimes"], text=True)
    core_versions = [
        line.split()[1]
        for line in inventory.splitlines()
        if line.startswith(CORE_FRAMEWORK + " ") and "-" not in line.split()[1]
    ]
    results = [
        observe(dotnet, output, options, inventory)
        for options in configurations(core_versions)
    ]
    path = work / "cases.json"
    path.write_text(json.dumps(results, indent=2) + "\n")
    subprocess.run(
        [
            dotnet,
            "run",
            "--project",
            str(root / "tests/WeavePort.Hosting.Tests"),
            "-c",
            "Release",
            "--",
            "--framework-oracle",
            str(path),
        ],
        check=True,
    )


if __name__ == "__main__":
    main()
