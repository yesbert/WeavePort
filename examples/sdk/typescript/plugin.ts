import { PluginApplication } from '@weaveport/sdk';
import { setTimeout as delay } from 'node:timers/promises';

let closed = 0;
type Query = { count: number; width: number; delayMs?: number; failAt?: number; callbacks?: boolean };
const app = new PluginApplication();
app.function('echo', async input => input);
app.function<{operation: string; input: unknown}, unknown>('owner', async (input, context) => context.callHost(input.operation, input.input));
app.function('state', async () => ({ closed }));
app.function('crash', async () => process.exit(17));
app.stream<Query, {id: number; text: string; owner: string}>('records', async function* (query, context, signal) {
    const wait = query.delayMs ?? 0;
    if (!Number.isInteger(query.count) || query.count < 0 || query.count > 1_000_000 || query.width < 0 || query.width > 200_000 || wait < 0 || wait > 1000) throw new Error('Invalid query');
    try {
        for (let i = 0; i < query.count; i++) {
            signal.throwIfAborted();
            if (i === query.failAt) throw new Error('Fixture failure');
            if (wait) await delay(wait, undefined, { signal });
            let owner = context.tenant;
            if (query.callbacks && i % 4 === 0) owner = (await context.callHost<{owner: string}>('host.owner', { tenant: 'forged' })).owner;
            yield { id: i, text: 'x'.repeat(query.width), owner };
        }
    } finally { closed++; }
});
await app.run();
