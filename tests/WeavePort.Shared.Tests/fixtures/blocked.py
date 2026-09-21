"""Adversarial transport fixture: advertises readiness but never drains invokes."""
import json
import sys
import time
print(json.dumps(dict(type="ready", protocol=2, concurrentCalls=1, pluginVersion="1", sessionCleanup=1)), flush=True)
json.loads(sys.stdin.readline())
time.sleep(120)
