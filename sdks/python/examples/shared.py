"""Shared native worker: one model, bounded concurrent invocations."""
import time
from weaveport_sdk import PluginApplication

app = PluginApplication("1", concurrent_calls=True)
# Initialize a trusted, thread-safe model once here.

@app.function("score")
def score(value, context):
    # Native numerical libraries can release the GIL. This delay stands in for work.
    time.sleep(value.get("delayMs", 0) / 1000)
    if context.cancelled:
        return None
    return {"tenant": context.tenant, "score": value["number"] * 2}

@app.function("callback")
def callback(value, context):
    return context.call_host_sync("echo", value)

app.run()
