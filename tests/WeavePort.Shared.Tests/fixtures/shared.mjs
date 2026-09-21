import { pathToFileURL } from 'node:url';
const sdkIndex = process.argv.indexOf('--sdk-path');
const { PluginApplication } = await import(sdkIndex >= 0 ? pathToFileURL(process.argv[sdkIndex + 1]).href : '../../../sdks/typescript/dist/index.js');
import { pbkdf2 } from 'node:crypto';
const app = new PluginApplication('1', { concurrentCalls: !process.argv.includes('--exclusive') });
app.function('echo', async (value, context) => ({ tenant: context.tenant, value, pid: process.pid }));
app.function('barrier', async (value, context) => ({ tenant: context.tenant, callback: await context.callHost('barrier', value) }));
app.function('callbacks', async (value, context) => {
    let result;
    for(let i=0; i<(value.count ?? 1); i++) result = await context.callHost(value.operation ?? 'echo', value);
    return {tenant: context.tenant, callback: result};
});
app.function('delay', async (value, context) => {
    try { if (value.announce) await context.callHost('entered', {}); }
    finally { await new Promise(resolve => setTimeout(resolve, value.milliseconds ?? 150)); }
    return context.tenant;
});
app.function('fail', async () => { throw new Error('author failure'); });
app.function('cleanupFail', async (_, context) => { context.onClose(() => { throw new Error('cleanup failure'); }); return null; });
app.function('crash', async value => { await new Promise(resolve => setTimeout(resolve, value.milliseconds ?? 0)); process.exit(7); });
app.function('cpu', async (value, context) => {
    const result = await new Promise((resolve, reject) => pbkdf2('weaveport', 'benchmark', value.iterations ?? 100000, 32, 'sha256', (error, data) => error ? reject(error) : resolve(data)));
    return {tenant: context.tenant, digest: result.toString('hex')};
});
app.function('malformed', async value => {
    process.stdout.write(value.invalidJson ? '{not-json}\n' : '{"type":"result","id":"unknown","value":null}\n');
    await new Promise(resolve => setTimeout(resolve, 2000));
    return null;
});
app.stream('items', async function* () { yield 1; });
await app.run();
