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
)
assert contract["FailureCodes"]["SdkError"] == "sdk-error"
assert contract["FailureCodes"]["CleanupError"] == "cleanup-error"
subprocess.run(
    ["python3", str(root / "scripts/generate-protocol.py"), "--check"], check=True
)
print("PASS 5 independent protocol contract assertions and generated drift check")
