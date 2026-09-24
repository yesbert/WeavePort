import { createConnection } from 'node:net';
import { once } from 'node:events';
import { createInterface } from 'node:readline';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';

type Request = { id: string; operation: string; payload: any; context: any; traceId: string };
const channel = process.env.WEAVEPORT_SOCKET
    ? createConnection(process.env.WEAVEPORT_SOCKET)
    : undefined;
if (channel) await once(channel, 'connect');
const output = channel ?? process.stdout;
const lines = createInterface({ input: channel ?? process.stdin, crlfDelay: Infinity })[
    Symbol.asyncIterator
]();
const workspace = (process.env.WEAVEPORT_WORKSPACE ?? '/tmp') + '/state';
let counter = 0;
let callbackId = 0;
function send(value: unknown): void {
    output.write(JSON.stringify(value) + '\n');
}
async function callback(r: Request, operation: string, payload: unknown): Promise<any> {
    const cid = String(++callbackId);
    send({ type: 'callback', id: r.id, callbackId: cid, operation, payload });
    const reply = JSON.parse((await lines.next()).value!);
    if (reply.id !== r.id || reply.callbackId !== cid) throw new Error('callback identity');
    return reply.value;
}
async function execute(r: Request): Promise<any> {
    const p = r.payload;
    switch (r.operation) {
        case 'environment':
            return { parentSecretPresent: process.env.WEAVEPORT_PARENT_CANARY !== undefined };
        case 'trace':
            return r.traceId;
        case 'echo':
            return p;
        case 'bulk-map': {
            const data = Buffer.from(p.data, 'base64');
            const lowercaseA = 'a'.charCodeAt(0);
            const uppercaseA = 'A'.charCodeAt(0);
            for (let i = 0; i < data.length; i++) {
                if (data[i] !== lowercaseA) continue;
                data[i] = uppercaseA;
            }
            return { data: data.toString('base64') };
        }
        case 'search':
            return (await callback(r, 'documents.read', p))
                .filter((d: any) => d.text.toLowerCase().includes((p.query ?? '').toLowerCase()))
                .sort((a: any, b: any) => a.id.localeCompare(b.id));
        case 'context':
            return r.context;
        case 'counter':
            return ++counter;
        case 'reduce':
            return { state: p.state + p.amount, events: [{ kind: 'changed', at: p.now }] };
        case 'delay':
            await new Promise((resolve) => setTimeout(resolve, p.ms));
            return p;
        case 'crash':
            process.exit(17);
        case 'exception':
            throw new Error('deliberate failure');
        case 'hang':
            while (true) {}
        case 'memory': {
            const blocks: Buffer[] = [];
            while (true) blocks.push(Buffer.alloc(8 * 1024 * 1024, 42));
        }
        case 'oversize':
            return 'x'.repeat(2 * 1024 * 1024);
        case 'malformed':
            output.write('not-json\n');
            return null;
        case 'callback':
            return await callback(r, p.operation, p.args ?? {});
        case 'callback-flood':
            for (let n = 0; n < (p.count ?? 10); n++) await callback(r, 'documents.read', {});
            return null;
        case 'workspace':
            if (p.text !== undefined) writeFileSync(workspace, p.text);
            return existsSync(workspace) ? readFileSync(workspace, 'utf8') : '';
        case 'subprocess':
            return execFileSync('/bin/sh', ['-c', 'printf child-ok'], { encoding: 'utf8' });
        default:
            throw new Error('unknown operation');
    }
}
send({ type: 'ready', protocol: 1, pluginVersion: process.env.PLUGIN_VERSION ?? '1' });
while (true) {
    const line = await lines.next();
    if (line.done) break;
    const request: Request = JSON.parse(line.value);
    try {
        send({ type: 'result', id: request.id, value: await execute(request) });
    } catch {
        send({ type: 'error', id: request.id, code: 'plugin-error' });
    }
}
