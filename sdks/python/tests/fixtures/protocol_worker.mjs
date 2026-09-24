// Independent author fixture for exclusive and concurrent wire scenarios.
const { PluginApplication } = await import(process.env.WEAVEPORT_TEST_SDK);
const app = new PluginApplication('1', {
    concurrentCalls: process.env.WEAVEPORT_TEST_CONCURRENT === 'true',
});
app.function('work', async (value, context, signal) => {
    await new Promise((resolve) => setTimeout(resolve, (value.delay ?? 0) * 1000));
    if (value.callback) {
        return await context.callHost('echo', context.tenant);
    }
    return context.tenant;
});
app.function('cleanup', async (value, context) => {
    context.onClose(async () => {
        await new Promise((resolve) => setTimeout(resolve, (value.delay ?? 0) * 1000));
        if (value.fail) {
            throw new Error('cleanup failed');
        }
    });
    return context.tenant;
});
app.function('invalid', async () => 1n);
app.stream('brokenClose', async function* () {
    try {
        yield 1;
        await new Promise((resolve) => setTimeout(resolve, 100));
        yield 2;
    } finally {
        throw new Error('iterator close failed');
    }
});
app.stream('immediateCallback', async function* (value, context) {
    if (value.before) {
        yield await context.callHost('echo', 0);
    }
    yield 1;
    yield await context.callHost('echo', 2);
});
app.stream('items', async function* (value, context) {
    yield 1;
    await new Promise((resolve) => setTimeout(resolve, 120));
    yield await context.callHost('echo', 2);
});
app.source('bytes', () => {
    let sent = false;
    return {
        read() {
            if (sent) {
                return new Uint8Array();
            }
            sent = true;
            return Buffer.from('hello');
        },
        close() {},
    };
});
await app.run();
