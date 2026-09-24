import asyncio
import os
from weaveport_sdk import PluginApplication

app = PluginApplication()
closed = 0


@app.function("echo")
async def echo(value, context):
    return value


@app.function("owner")
async def owner(value, context):
    return await context.call_host(value["operation"], value["input"])


@app.function("state")
async def state(value, context):
    return dict(closed=closed)


@app.function("crash")
async def crash(value, context):
    os._exit(17)


@app.stream("records")
async def records(query, context):
    global closed
    count, width = query["count"], query["width"]
    delay = query.get("delayMs", 0)
    if not (0 <= count <= 1_000_000 and 0 <= width <= 200_000 and 0 <= delay <= 1000):
        raise ValueError("Invalid query")
    try:
        for i in range(count):
            if i == query.get("failAt", -1):
                raise ValueError("Fixture failure")
            yield await create_row(query, i, delay, context)
    finally:
        closed += 1


async def create_row(query, index, delay_ms, context):
    if delay_ms:
        await asyncio.sleep(delay_ms / 1000)
    owner = await resolve_owner(query, index, context)
    return dict(id=index, text="x" * query["width"], owner=owner)


async def resolve_owner(query, index, context):
    callback_interval = 4
    if not query.get("callbacks", False) or index % callback_interval != 0:
        return context.tenant
    reply = await context.call_host("host.owner", dict(tenant="forged"))
    return reply["owner"]


app.run()
