import { pathToFileURL } from 'node:url';
const sdkIndex = process.argv.indexOf('--sdk-path');
const { PluginApplication, PluginFileError } = await import(
    sdkIndex >= 0
        ? pathToFileURL(process.argv[sdkIndex + 1]).href
        : '../../../sdks/typescript/dist/index.js'
);
const app = new PluginApplication('1');
const retained = {};

// Reads the ZIP end-of-central-directory record, then the directory, using positional reads only.
async function entryNames(input) {
    const tail = await input.read(input.size - 22, 22);
    const view = new DataView(tail.buffer, tail.byteOffset, tail.byteLength);
    const count = view.getUint16(10, true);
    const size = view.getUint32(12, true);
    const offset = view.getUint32(16, true);
    const directory = await input.read(offset, size);
    const entries = new DataView(directory.buffer, directory.byteOffset, directory.byteLength);
    const names = [];
    let position = 0;
    for (let index = 0; index < count; index++) {
        const nameLength = entries.getUint16(position + 28, true);
        const extra = entries.getUint16(position + 30, true);
        const comment = entries.getUint16(position + 32, true);
        names.push(Buffer.from(directory.subarray(position + 46, position + 46 + nameLength)).toString('utf8'));
        position += 46 + nameLength + extra + comment;
    }
    return names;
}

async function code(action) {
    try {
        await action();
        return '';
    } catch (error) {
        if (error instanceof PluginFileError) {
            return error.code;
        }
        throw error;
    }
}

app.function('zip-summary', async (value, context) => ({
    names: await entryNames(context.openInput('sheet')),
    inputs: context.inputs.map((input) => ({ name: input.name, length: input.length, mediaType: input.mediaType })),
}));
app.function('copy', async (value, context) => {
    const source = context.openInput('sheet');
    const target = await context.createOutput('copy.bin', 'application/octet-stream');
    for (let offset = 0; offset < source.size; ) {
        const chunk = await source.read(offset, 100_000);
        await target.write(chunk);
        offset += chunk.length;
    }
    await target.close();
    return { length: target.written };
});
app.function('leave-open', async (value, context) => {
    const target = await context.createOutput('open.bin', 'text/plain');
    await target.write(new Uint8Array([1]));
    return { done: true };
});
app.function('refusals', async (value, context) => ({
    codes: [
        await code(async () => context.openInput('nope')),
        await code(() => context.createOutput('x.bin', 'text/plain')),
    ],
}));
app.function('cached-reads', async (value, context) => {
    const source = context.openInput('sheet');
    for (let index = 0; index < 10; index++) {
        await source.read(index * 1000, 100);
    }
    return { done: true };
});
app.function('late-store', async (value, context) => {
    retained.sheet = context.openInput('sheet');
    return { stored: true };
});
app.function('late-read', async () => {
    try {
        await retained.sheet.read(0, 16);
        return { refused: false };
    } catch {
        return { refused: true };
    }
});
await app.run();
