"""Cross-language wire tests; build TypeScript before running this suite."""
import base64
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time
import unittest

ROOT = Path(__file__).resolve().parents[3]

PYTHON = '''
import asyncio, io, time
from weaveport_sdk import PluginApplication
app = PluginApplication(concurrent_calls=CONCURRENT)
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
        await asyncio.sleep(.2)
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
'''
TS = '''
import { PluginApplication } from 'SDK';
const app = new PluginApplication('1', {concurrentCalls: CONCURRENT});
app.function('work', async (value, context, signal) => {
    await new Promise(resolve => setTimeout(resolve, (value.delay ?? 0) * 1000));
    if (value.callback) return await context.callHost('echo', context.tenant);
    return context.tenant;
});
app.function('cleanup', async (value, context) => {
    context.onClose(async () => {
        await new Promise(resolve => setTimeout(resolve, (value.delay ?? 0) * 1000));
        if (value.fail) throw new Error('cleanup failed');
    });
    return context.tenant;
});
app.function('invalid', async () => 1n);
app.stream('brokenClose', async function*() {
    try { yield 1; await new Promise(resolve => setTimeout(resolve, 100)); yield 2; }
    finally { throw new Error('iterator close failed'); }
});
app.stream('immediateCallback', async function*(value, context) {
    if (value.before) yield await context.callHost('echo', 0);
    yield 1;
    yield await context.callHost('echo', 2);
});
app.stream('items', async function*(value, context) {
    yield 1;
    await new Promise(resolve => setTimeout(resolve, 120));
    yield await context.callHost('echo', 2);
});
app.source('bytes', () => {
    let sent = false;
    return {read() {if(sent) return new Uint8Array(); sent=true; return Buffer.from('hello');}, close() {}};
});
await app.run();
'''


class Worker:
    def __init__(self, language, concurrent=True):
        self.temp = tempfile.TemporaryDirectory()
        suffix = '.py' if language == 'python' else '.mjs'
        path = Path(self.temp.name) / ('plugin' + suffix)
        content = PYTHON.replace('CONCURRENT', str(concurrent)) if language == 'python' else TS.replace('CONCURRENT', str(concurrent).lower()).replace('SDK', (ROOT / 'sdks/typescript/dist/index.js').as_uri())
        path.write_text(content)
        env = dict(os.environ, PYTHONPATH=str(ROOT / 'sdks/python'))
        self.process = subprocess.Popen(['python3' if language == 'python' else 'node', str(path)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True)
        self.lines = queue.Queue()
        def pump():
            for line in self.process.stdout:
                self.lines.put(json.loads(line))
        threading.Thread(target=pump, daemon=True).start()
        ready = self.receive()
        assert ready['protocol'] == (2 if concurrent else 1), ready
        if concurrent:
            self.send(type='configure', degree=2)

    def send(self, **frame):
        self.process.stdin.write(json.dumps(frame) + '\n')
        self.process.stdin.flush()

    def invoke(self, id, wire_operation='$sdk.call', **payload):
        self.send(type='invoke', id=id, operation=wire_operation, payload=payload, context=dict(tenant=id, configuration={}))

    def receive(self):
        try:
            return self.lines.get(timeout=3)
        except queue.Empty:
            raise AssertionError('Worker timed out')

    def close(self):
        self.process.kill()
        self.process.communicate(timeout=3)
        self.temp.cleanup()


class ProtocolTests(unittest.TestCase):
    def workers(self, concurrent=True):
        for language in ('python', 'typescript'):
            worker = Worker(language, concurrent)
            self.addCleanup(worker.close)
            yield language, worker

    def test_overlap_callbacks_and_error_isolation(self):
        for language, worker in self.workers():
            with self.subTest(language=language):
                worker.invoke('slow', operation='work', input={'delay': .15})
                worker.invoke('fast', operation='work', input={'callback': True})
                callback = worker.receive()
                self.assertEqual(callback['id'], 'fast')
                worker.send(type='callback-result', id='fast', callbackId=callback['callbackId'], error={'code': 'denied'})
                self.assertEqual(worker.receive()['type'], 'error')
                self.assertEqual(worker.receive()['value'], 'slow')
                worker.invoke('healthy', operation='work', input={})
                self.assertEqual(worker.receive()['value'], 'healthy')

    def test_invalid_author_result_is_isolated(self):
        for language, worker in self.workers():
            with self.subTest(language=language):
                worker.invoke('invalid', operation='invalid', input={})
                self.assertEqual(worker.receive()['code'], 'sdk-error')
                worker.invoke('next', operation='work', input={})
                self.assertEqual(worker.receive()['value'], 'next')

    def test_iterator_cleanup_failure_is_channel_fatal(self):
        for language, worker in self.workers(False):
            with self.subTest(language=language):
                worker.invoke('start', '$sdk.start', operation='brokenClose', input={})
                stream = worker.receive()['value']['stream']
                worker.invoke('first', '$sdk.next', stream=stream)
                self.assertEqual(worker.receive()['value']['items'], [1])
                worker.invoke('close', '$sdk.close', stream=stream)
                self.assertEqual(worker.receive()['code'], 'cleanup-error')

    def test_cancel_waits_for_actual_completion(self):
        for language, worker in self.workers():
            with self.subTest(language=language):
                worker.invoke('cancel', operation='work', input={'delay': .15})
                started = time.monotonic()
                worker.send(type='cancel', id='cancel')
                worker.invoke('other', operation='work', input={})
                self.assertEqual(worker.receive()['value'], 'other')
                self.assertEqual(worker.receive(), {'type': 'cancelled', 'id': 'cancel'})
                self.assertGreater(time.monotonic() - started, .1)

    def test_cleanup_is_owned_until_completion_and_failure_is_fatal(self):
        for language, worker in self.workers():
            with self.subTest(language=language):
                worker.invoke('cleanup', operation='cleanup', input={'delay': .15})
                worker.send(type='cancel', id='cleanup')
                worker.invoke('independent', operation='work', input={})
                self.assertEqual(worker.receive()['value'], 'independent')
                self.assertEqual(worker.receive()['type'], 'cancelled')
                worker.invoke('failure', operation='cleanup', input={'fail': True})
                self.assertEqual(worker.receive()['code'], 'cleanup-error')

    def test_shared_stream_is_rejected_without_corrupting_channel(self):
        for language, worker in self.workers():
            with self.subTest(language=language):
                worker.invoke('stream', '$sdk.start', operation='items', input={})
                self.assertEqual(worker.receive()['type'], 'error')
                worker.invoke('next', operation='work', input={})
                self.assertEqual(worker.receive()['type'], 'result')

    def test_late_known_cancellation_does_not_retire_channel(self):
        for language, worker in self.workers():
            with self.subTest(language=language):
                worker.invoke('completed', operation='work', input={})
                self.assertEqual(worker.receive()['type'], 'result')
                worker.send(type='cancel', id='completed')
                worker.invoke('next', operation='work', input={})
                self.assertEqual(worker.receive()['value'], 'next')

    def test_sync_python_callback_and_cancel(self):
        worker = Worker('python')
        self.addCleanup(worker.close)
        worker.invoke('sync', operation='sync', input={'callback': True})
        callback = worker.receive()
        worker.send(type='callback-result', id='sync', callbackId=callback['callbackId'], value='ok', error=None)
        self.assertEqual(worker.receive()['value'], 'ok')
        worker.invoke('cancel', operation='sync', input={'delay': .15})
        worker.send(type='cancel', id='cancel')
        self.assertEqual(worker.receive()['type'], 'cancelled')

    def test_buffered_item_is_delivered_before_following_callback(self):
        for language, worker in self.workers(False):
            with self.subTest(language=language):
                worker.invoke('start', '$sdk.start', operation='immediateCallback', input={})
                stream = worker.receive()['value']['stream']
                worker.invoke('first', '$sdk.next', stream=stream)
                first = worker.receive()
                self.assertEqual(first['type'], 'result')
                self.assertEqual(first['value'], {'items': [1], 'done': False})
                worker.invoke('second', '$sdk.next', stream=stream)
                callback = worker.receive()
                self.assertEqual(callback['type'], 'callback')
                self.assertEqual(callback['id'], 'second')
                worker.send(type='callback-result', id='second', callbackId=callback['callbackId'], value=2)
                second = worker.receive()['value']
                self.assertEqual(second['items'], [2])
                if not second['done']:
                    worker.invoke('end', '$sdk.next', stream=stream)
                    self.assertTrue(worker.receive()['value']['done'])

    def test_early_close_after_buffered_item_cancels_deferred_callback(self):
        for language, worker in self.workers(False):
            with self.subTest(language=language):
                worker.invoke('start', '$sdk.start', operation='immediateCallback', input={})
                stream = worker.receive()['value']['stream']
                worker.invoke('first', '$sdk.next', stream=stream)
                self.assertEqual(worker.receive()['value']['items'], [1])
                started = time.monotonic()
                worker.invoke('close', '$sdk.close', stream=stream)
                closed = worker.receive()
                self.assertEqual(closed['type'], 'result')
                self.assertTrue(closed['reusable'])
                self.assertLess(time.monotonic() - started, 2)
                worker.invoke('healthy', operation='work', input={})
                self.assertEqual(worker.receive()['value'], 'healthy')

    def test_callback_before_first_item_is_not_deferred(self):
        for language, worker in self.workers(False):
            with self.subTest(language=language):
                worker.invoke('start', '$sdk.start', operation='immediateCallback', input={'before': True})
                stream = worker.receive()['value']['stream']
                worker.invoke('first', '$sdk.next', stream=stream)
                callback = worker.receive()
                self.assertEqual(callback['type'], 'callback')
                worker.send(type='callback-result', id='first', callbackId=callback['callbackId'], value=0)
                first = worker.receive()['value']
                self.assertEqual(first['items'], [0, 1])

    def test_stream_heartbeat_and_binary_source(self):
        for language, worker in self.workers(False):
            with self.subTest(language=language):
                worker.invoke('start', '$sdk.start', operation='items', input={})
                stream = worker.receive()['value']['stream']
                worker.invoke('first', '$sdk.next', stream=stream)
                self.assertEqual(worker.receive()['value'], {'items': [1], 'done': False})
                worker.invoke('heartbeat', '$sdk.next', stream=stream)
                self.assertEqual(worker.receive()['value'], {'items': [], 'done': False})
                time.sleep(.15)
                worker.invoke('last', '$sdk.next', stream=stream)
                callback = worker.receive()
                self.assertEqual(callback['type'], 'callback')
                self.assertEqual(callback['id'], 'last')
                worker.send(type='callback-result', id='last', callbackId=callback['callbackId'], value=2)
                result = worker.receive()['value']
                self.assertEqual(result['items'], [2])
                if not result['done']:
                    worker.invoke('end', '$sdk.next', stream=stream)
                    self.assertTrue(worker.receive()['value']['done'])
                worker.invoke('open', '$sdk.source.open', operation='bytes', input={})
                source = worker.receive()['value']['source']
                worker.invoke('read', '$sdk.source.read', source=source, chunkBytes=4096)
                self.assertEqual(base64.b64decode(worker.receive()['value']['data']), b'hello')
                worker.invoke('eof', '$sdk.source.read', source=source, chunkBytes=4096)
                eof = worker.receive()
                self.assertTrue(eof['value']['done'])
                self.assertTrue(eof['reusable'])


if __name__ == '__main__':
    unittest.main()
