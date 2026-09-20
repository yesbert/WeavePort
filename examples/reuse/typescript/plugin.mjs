import { PluginApplication } from '@weaveport/sdk';

const app = new PluginApplication();
app.function('transform', async (input, context, signal) => {
    signal.throwIfAborted();
    // These objects belong to one invocation, never to a module-level customer cache.
    const bytes = Buffer.from(input.text, 'utf8');
    context.onClose(() => { bytes.fill(0); });
    const cache = new Map();
    context.onClose(() => cache.clear());
    cache.set('result', input.text.toUpperCase());
    return { tenant: context.tenant, text: cache.get('result'), bytes: bytes.length };
});
await app.run();
