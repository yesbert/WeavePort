import assert from 'node:assert/strict';
import { test } from 'node:test';
import { SessionContext, SessionCleanupError } from '../dist/session.js';
import { PluginApplication } from '../dist/index.js';

test('public registration remains available through the package entry point', () => {
    const app = new PluginApplication('1');
    assert.equal(app.function('echo', async value => value), app);
    assert.throws(() => app.function('echo', async value => value));
});

test('cleanup preserves causes, reverse ordering and context expiry', async () => {
    const context = new SessionContext('tenant', {}, async () => undefined);
    const actions = [];
    const failure = new Error('private cleanup detail');
    context.onClose(() => actions.push('first'));
    context.onClose(() => { actions.push('failure'); throw failure; });
    context.onClose(() => actions.push('last'));
    await assert.rejects(context.complete(), error => {
        assert.ok(error instanceof SessionCleanupError);
        assert.ok(error instanceof AggregateError);
        assert.equal(error.code, 'cleanup-error');
        assert.deepEqual(error.errors, [failure]);
        return true;
    });
    assert.deepEqual(actions, ['last', 'failure', 'first']);
    assert.throws(() => context.tenant);
});
