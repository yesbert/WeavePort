import asyncio
import os
import sys
from pathlib import Path
sdk = sys.argv[sys.argv.index('--sdk-path') + 1] if '--sdk-path' in sys.argv else str(Path(__file__).resolve().parents[3] / 'sdks/python')
sys.path.insert(0, sdk)
from weaveport_sdk import PluginApplication
app = PluginApplication('1')
@app.function('echo')
async def echo(value, context):
    await asyncio.sleep(value.get('delay', 0) / 1000)
    return dict(pid=os.getpid(), tenant=context.tenant)
@app.stream('slow')
async def slow(value, context):
    await asyncio.sleep(value.get('firstDelay', 0) / 1000)
    yield dict(pid=os.getpid(), item=1)
    await asyncio.sleep(value.get('nextDelay', 300) / 1000)
    yield dict(pid=os.getpid(), item=2)
@app.function('identity')
async def identity(value, context):
    return dict(tenant=context.tenant)
@app.function('stats')
async def stats(value, context):
    return dict(pid=os.getpid())
@app.stream('live')
async def live(value, context):
    yield 1
    await asyncio.sleep(.3)
    await context.call_host('echo', value)
    yield 2
class PatternSource:
    def __init__(self, count):
        self.remaining = count
    def read(self, size):
        count = min(self.remaining, size)
        self.remaining -= count
        return b'Z' * count
    def close(self):
        pass
@app.source('large')
async def large(value, context):
    return PatternSource(value['bytes'])
app.run()
