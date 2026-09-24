"""Independent author fixture for exclusive and concurrent wire scenarios."""

import asyncio
import io
import os
import time
from weaveport_sdk import PluginApplication

app = PluginApplication(
    concurrent_calls=os.environ["WEAVEPORT_TEST_CONCURRENT"] == "true"
)


@app.function("work")
async def work(value, context):
    await asyncio.sleep(value.get("delay", 0))
    if value.get("callback"):
        return await context.call_host("echo", context.tenant)
    return context.tenant


@app.function("sync")
def sync(value, context):
    time.sleep(value.get("delay", 0))
    if value.get("callback"):
        return context.call_host_sync("echo", context.tenant)
    return context.tenant


@app.function("cleanup")
async def cleanup(value, context):
    async def close():
        await asyncio.sleep(value.get("delay", 0))
        if value.get("fail"):
            raise RuntimeError("cleanup failed")

    context.on_close(close)
    return context.tenant


@app.function("invalid")
async def invalid(value, context):
    return object()


@app.stream("brokenClose")
async def broken_close(value, context):
    try:
        yield 1
        await asyncio.sleep(0.2)
        yield 2
    finally:
        raise RuntimeError("iterator close failed")


@app.stream("immediateCallback")
async def immediate_callback(value, context):
    if value.get("before"):
        yield await context.call_host("echo", 0)
    yield 1
    yield await context.call_host("echo", 2)


@app.stream("items")
async def items(value, context):
    yield 1
    await asyncio.sleep(0.12)
    yield await context.call_host("echo", 2)


@app.source("bytes")
def source(value, context):
    return io.BytesIO(b"hello")


app.run()
