"""Independent literal expectations prevent accidental protocol changes."""

import json
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[2]
contract = json.loads((root / "contracts/protocol.json").read_text())
assert contract["SdkOperations"]["Call"] == "$sdk.call"
assert contract["SdkOperations"]["SourceClose"] == "$sdk.source.close"
assert contract["ProtocolLimits"] == dict(
    FrameBytes=1048576,
    UnaryBytes=524288,
    StreamItemBytes=131072,
    StreamBatchBytes=262144,
    StreamBatchItems=16,
    StreamTotalBytes=67108864,
    SourceChunkMinimumBytes=4096,
    SourceChunkMaximumBytes=262144,
    SourceChunkDefaultBytes=65536,
    MaximumConcurrentCalls=1024,
    FileBlockMinimumBytes=4096,
    FileBlockMaximumBytes=262144,
    FileBlockDefaultBytes=65536,
    FileNameMaximumLength=128,
    FileMediaTypeMaximumLength=255,
    MaximumFileInputs=64,
    MaximumFileOutputs=64,
    FileInputReadBudgetFactor=4,
    FileBlockCacheBlocks=4,
)
assert contract["FailureCodes"]["SdkError"] == "sdk-error"
assert contract["FailureCodes"]["CleanupError"] == "cleanup-error"
assert contract["SdkOperations"]["InputRead"] == "$sdk.input.read"
assert contract["SdkOperations"]["OutputComplete"] == "$sdk.output.complete"
assert contract["FailureCodes"]["InputLimit"] == "input-limit"
assert contract["FailureCodes"]["InputReadLimit"] == "input-read-limit"
assert contract["FileErrors"] == dict(
    InputNotFound="input-not-found",
    OutputNotPermitted="output-not-permitted",
    OutputExists="output-exists",
    OutputClosed="output-closed",
    InvalidRange="invalid-range",
)
assert contract["ProtocolVersions"]["Files"] == 1
subprocess.run(
    ["python3", str(root / "scripts/generate-protocol.py"), "--check"], check=True
)
print("PASS 11 independent protocol contract assertions and generated drift check")
