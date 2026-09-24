import asyncio
import os
from pathlib import Path
import sys
import uuid

if len(sys.argv) > 1 and sys.argv[1] != "-":
    sys.path.insert(0, sys.argv[1])
from weaveport_sdk import PluginApplication

app = PluginApplication()
previous = None
cache = {}
old_file = None
hidden = None


@app.function("session")
async def session(value, context):
    global previous, old_file
    expired = previous is None
    if previous is not None:
        try:
            await previous.call_host("who", {})
        except RuntimeError:
            expired = True
    if cache or old_file and old_file.exists():
        raise RuntimeError("Registered residue")
    secret = context.configuration["secret"]
    cache["secret"] = secret
    context.on_close(cache.clear)
    old_file = Path(os.getenv("WEAVEPORT_WORKSPACE", "/tmp")) / (
        uuid.uuid4().hex + ".session"
    )
    path = old_file
    context.on_close(lambda: path.unlink())
    stream = context.own(path.open("w"))
    stream.write(secret)
    previous = context
    owner = await context.call_host("who", {"tenant": "forged"})
    return dict(tenant=context.tenant, secret=secret, expired=expired, callback=owner)


@app.function("context")
async def density(value, context):
    local = bytearray(context.configuration["data"], "utf-8")
    context.on_close(local.clear)
    return dict(tenant=context.tenant, configuration=dict(context.configuration))


@app.function("stash")
async def stash(value, context):
    global hidden
    hidden = context.tenant
    return dict(stored=True)


@app.function("probe")
async def probe(value, context):
    return dict(hidden=hidden)


@app.function("cleanup-fail")
async def fail(value, context):
    def cleanup():
        raise RuntimeError("Deliberate cleanup failure")

    context.on_close(cleanup)
    return {}


@app.function("cleanup-hang")
async def hang(value, context):
    context.on_close(lambda: asyncio.sleep(30))
    return {}


@app.function("delay")
async def delay(value, context):
    await asyncio.sleep(value["ms"] / 1000)
    return dict(tenant=context.tenant)


@app.stream("rows")
async def rows(value, context):
    global previous
    previous = context
    for i in range(20):
        yield dict(tenant=context.tenant, i=i)


app.run()
