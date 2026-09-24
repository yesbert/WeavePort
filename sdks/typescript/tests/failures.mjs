import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cleanupAll, executeWithCleanup } from '../dist/runtime/cleanup.js';
import { decodeFrame } from '../dist/runtime/messages.js';

test('all cleanup causes and primary failure remain observable', async () => {
    const primary = new Error('primary secret');
    const cleanup = new Error('cleanup secret');
    const action = async () => { throw primary; };
    const release = async () => { throw cleanup; };
    await assert.rejects(cleanupAll(action, release), error => {
        assert.deepEqual(error.errors, [primary, cleanup]);
        return true;
    });
    await assert.rejects(executeWithCleanup(action, release), error => {
        assert.deepEqual(error.errors, [primary, cleanup]);
        assert.equal(error.hasExecutionFailure, true);
        return true;
    });
    await assert.rejects(executeWithCleanup(action, async () => {}), error => error === primary);
});
test('decode rejects malformed protocol boundaries and preserves valid frames', () => {
    for (const frame of [null, [], {}, {type: 'invoke'}, {type:'configure',degree:1025},
        {type:'callback-result',id:'a',callbackId:1}]) assert.throws(() => decodeFrame(frame));
    assert.deepEqual(decodeFrame({type:'configure',degree:16}), {type:'configure',degree:16});
    assert.equal(decodeFrame({type:'invoke',id:'x',operation:'$sdk.call',payload:{operation:'echo',input:1},
        context:{tenant:'a',configuration:{}}}).payload.input, 1);
});
