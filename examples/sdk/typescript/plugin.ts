import { PluginApplication, type PluginContext } from '@weaveport/sdk';
import { setTimeout as delay } from 'node:timers/promises';

let closed = 0;
type Query = {
    count: number;
    width: number;
    delayMs?: number;
    failAt?: number;
    callbacks?: boolean;
};
const app = new PluginApplication();
app.function('echo', async (input) => input);
app.function<{ operation: string; input: unknown }, unknown>('owner', async (input, context) =>
    context.callHost(input.operation, input.input),
);
app.function('state', async () => ({ closed }));
app.function('crash', async () => process.exit(17));
app.stream<Query, { id: number; text: string; owner: string }>(
    'records',
    async function* (query, context, signal) {
        const wait = query.delayMs ?? 0;
        if (
            !Number.isInteger(query.count) ||
            query.count < 0 ||
            query.count > 1_000_000 ||
            query.width < 0 ||
            query.width > 200_000 ||
            wait < 0 ||
            wait > 1000
        )
            throw new Error('Invalid query');
        try {
            for (let i = 0; i < query.count; i++) {
                signal.throwIfAborted();
                if (i === query.failAt) throw new Error('Fixture failure');
                yield await createRow(query, i, wait, context, signal);
            }
        } finally {
            closed++;
        }
    },
);
async function createRow(
    query: Query,
    index: number,
    wait: number,
    context: PluginContext,
    signal: AbortSignal,
) {
    if (wait) {
        await delay(wait, undefined, { signal });
    }
    const owner = await resolveOwner(query, index, context);
    return { id: index, text: 'x'.repeat(query.width), owner };
}

async function resolveOwner(query: Query, index: number, context: PluginContext): Promise<string> {
    const callbackInterval = 4;
    if (!query.callbacks || index % callbackInterval !== 0) {
        return context.tenant;
    }
    return (await context.callHost<{ owner: string }>('host.owner', { tenant: 'forged' })).owner;
}

await app.run();
