import { pathToFileURL } from 'node:url';
const sdkIndex = process.argv.indexOf('--sdk-path');
const { PluginApplication } = await import(sdkIndex >= 0 ? pathToFileURL(process.argv[sdkIndex + 1]).href : '../../../sdks/typescript/dist/index.js');
const app = new PluginApplication('1');
app.function('echo', async (value, context) => {
    await new Promise(resolve => setTimeout(resolve, value.delay ?? 0));
    return {pid: process.pid, tenant: context.tenant};
});
app.stream('slow', async function* (value) {
    await new Promise(resolve => setTimeout(resolve, value.firstDelay ?? 0));
    yield {pid: process.pid, item: 1};
    await new Promise(resolve => setTimeout(resolve, value.nextDelay ?? 300));
    yield {pid: process.pid, item: 2};
});
app.function('identity', async (value, context) => ({tenant: context.tenant}));
app.function('stats', async () => ({pid: process.pid}));
app.stream('live', async function* (value, context) {
    yield 1;
    await new Promise(resolve => setTimeout(resolve, 300));
    await context.callHost('echo', value);
    yield 2;
});
app.stream('immediateCallback', async function* (value, context) {
    yield 1;
    await context.callHost('echo', value);
    yield 2;
});
app.source('large', async value => {
    let remaining = value.bytes;
    return {
        read(size) { const count = Math.min(remaining, size); remaining -= count; return new Uint8Array(count).fill(0x5a); },
        close() { }
    };
});
await app.run();
