import asyncio
import sys
import zipfile
from pathlib import Path

sdk = (
    sys.argv[sys.argv.index("--sdk-path") + 1]
    if "--sdk-path" in sys.argv
    else str(Path(__file__).resolve().parents[3] / "sdks/python")
)
sys.path.insert(0, sdk)
from weaveport_sdk import PluginApplication, PluginFileError

import os

app = PluginApplication(
    "1",
    file_block_bytes=(
        int(sys.argv[sys.argv.index("--block-bytes") + 1])
        if "--block-bytes" in sys.argv
        else 65536
    ),
)


@app.function("stats")
async def stats(value, context):
    return dict(pid=os.getpid())

retained = {}


def entry_names(source):
    with zipfile.ZipFile(source.as_file()) as archive:
        return [entry.filename for entry in archive.infolist()]


@app.function("zip-summary")
async def zip_summary(value, context):
    source = await context.open_input("sheet")
    names = await asyncio.to_thread(entry_names, source)
    inputs = [
        dict(name=item.name, length=item.length, mediaType=item.media_type)
        for item in context.inputs
    ]
    return dict(names=names, inputs=inputs)


@app.function("copy")
async def copy(value, context):
    source = await context.open_input("sheet")
    async with await context.create_output("copy.bin", "application/octet-stream") as target:
        offset = 0
        while offset < source.size:
            chunk = await source.read_at(offset, 100_000)
            await target.write(chunk)
            offset += len(chunk)
    return dict(length=target.written)


@app.function("leave-open")
async def leave_open(value, context):
    target = await context.create_output("open.bin", "text/plain")
    await target.write(b"\x01")
    return dict(done=True)


async def code(action):
    try:
        await action()
        return ""
    except PluginFileError as error:
        return error.code


@app.function("refusals")
async def refusals(value, context):
    return dict(
        codes=[
            await code(lambda: context.open_input("nope")),
            await code(lambda: context.create_output("x.bin", "text/plain")),
        ]
    )


@app.function("cached-reads")
async def cached_reads(value, context):
    source = await context.open_input("sheet")
    for index in range(10):
        await source.read_at(index * 1000, 100)
    return dict(done=True)


@app.function("late-store")
async def late_store(value, context):
    retained["sheet"] = await context.open_input("sheet")
    return dict(stored=True)


@app.function("late-read")
async def late_read(value, context):
    try:
        await retained["sheet"].read_at(0, 16)
        return dict(refused=False)
    except (PluginFileError, RuntimeError):
        return dict(refused=True)


@app.function("sync-on-loop")
async def sync_on_loop(value, context):
    source = await context.open_input("sheet")
    try:
        source.as_file().read(16)
        return dict(refused=False)
    except RuntimeError:
        return dict(refused=True)


app.run()
