"""Exercise the maintained author examples against freshly packed SDKs."""

from pathlib import Path
import json
import select
import shutil
import subprocess
import sys


def read_response(process, language):
    if not select.select([process.stdout], [], [], 10)[0]:
        raise AssertionError(language + ": response timeout")
    line = process.stdout.readline()
    if not line:
        raise AssertionError(language + ": provider exited")
    return json.loads(line)


def verify_customer(process, language, index, tenant):
    request = dict(
        type="invoke",
        id=str(index),
        operation="$sdk.call",
        payload=dict(operation="transform", input=dict(text="hello")),
        context=dict(tenant=tenant, configuration={}),
    )
    process.stdin.write(json.dumps(request) + "\n")
    process.stdin.flush()
    response = read_response(process, language)
    assert response["type"] == "result" and response["reusable"] is True, language
    assert response["value"] == dict(tenant=tenant, text="HELLO", bytes=5), language
    print("PASS: " + language + " maintained example customer " + tenant)


def stop_provider(process):
    process.stdin.close()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)
    finally:
        process.stdout.close()
        process.stderr.close()


def verify_provider(language, command):
    process = subprocess.Popen(
        command,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    try:
        assert read_response(process, language)["sessionCleanup"] == 1
        for index, tenant in enumerate(["a", "b", "a"]):
            verify_customer(process, language, index, tenant)
    finally:
        stop_provider(process)


def main():
    root = Path(sys.argv[1]).resolve()
    installed = root / "artifacts/sdk-version-tests"
    example = installed / "reuse-example.mjs"
    # Preserve the maintained example's import; resolve the installed package.
    shutil.copyfile(root / "examples/reuse/typescript/plugin.mjs", example)
    providers = {
        "csharp": [
            sys.argv[2],
            str(root / "artifacts/reuse-example/ReusablePlugin.dll"),
        ],
        "python": [
            str(installed / "python/bin/python"),
            "-u",
            str(root / "examples/reuse/python/plugin.py"),
        ],
        "typescript": [sys.argv[3], str(example)],
    }
    for language, command in providers.items():
        verify_provider(language, command)


if __name__ == "__main__":
    main()
