import asyncio
import hashlib
import os
from pathlib import Path
import sys

sdk_path = (
    sys.argv[sys.argv.index("--sdk-path") + 1]
    if "--sdk-path" in sys.argv
    else str(Path(__file__).resolve().parents[3] / "sdks/python")
)
if sdk_path != "-":
    sys.path.insert(0, sdk_path)
from weaveport_sdk import PluginApplication

app = PluginApplication("1", concurrent_calls="--exclusive" not in sys.argv)


@app.function("echo")
async def echo(value, context):
    return dict(tenant=context.tenant, value=value, pid=os.getpid())


@app.function("barrier")
async def barrier(value, context):
    result = await context.call_host("barrier", value)
    return dict(tenant=context.tenant, callback=result)


@app.function("callbacks")
async def callbacks(value, context):
    result = None
    for _ in range(value.get("count", 1)):
        result = await context.call_host(value.get("operation", "echo"), value)
    return dict(tenant=context.tenant, callback=result)


@app.function("delay")
async def delay(value, context):
    try:
        if value.get("announce"):
            await context.call_host("entered", {})
    finally:
        await asyncio.sleep(value.get("milliseconds", 150) / 1000)
    return context.tenant


@app.function("fail")
async def fail(value, context):
    raise RuntimeError("author failure")


@app.function("cleanupFail")
async def cleanup_fail(value, context):
    def cleanup():
        raise RuntimeError("cleanup failure")

    context.on_close(cleanup)
    return None


@app.function("crash")
async def crash(value, context):
    await asyncio.sleep(value.get("milliseconds", 0) / 1000)
    os._exit(7)


@app.function("cpu")
def cpu(value, context):
    result = hashlib.pbkdf2_hmac(
        "sha256", b"weaveport", b"benchmark", value.get("iterations", 100000)
    )
    return dict(tenant=context.tenant, digest=result.hex())


@app.function("malformed")
async def malformed(value, context):
    sys.__stdout__.write(
        "{not-json}\n"
        if value.get("invalidJson")
        else '{"type":"result","id":"unknown","value":null}\n'
    )
    sys.__stdout__.flush()
    await asyncio.sleep(2)
    return None


@app.stream("items")
async def items(value, context):
    yield 1


app.run()
