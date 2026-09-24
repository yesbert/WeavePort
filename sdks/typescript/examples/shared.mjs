import { PluginApplication } from '../dist/index.js';

const app = new PluginApplication('1', { concurrentCalls: true });
app.function('score', async (value, context, signal) => {
    await new Promise((resolve) => setTimeout(resolve, value.delayMs ?? 0));
    signal.throwIfAborted();
    return { tenant: context.tenant, score: value.number * 2 };
});
app.function('callback', async (value, context) => await context.callHost('echo', value));
await app.run();
