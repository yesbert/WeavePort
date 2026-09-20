import { AsyncLocalStorage } from 'node:async_hooks';
import { Console } from 'node:console';
import { createConnection, Socket } from 'node:net';
import { createInterface } from 'node:readline';
import { randomUUID } from 'node:crypto';
import type { Readable, Writable } from 'node:stream';

import { SessionContext, SessionCleanupError } from './session.js';
import type { PluginContext } from './session.js';
export type { PluginContext } from './session.js';
type Handler = (input: any, context: PluginContext, signal: AbortSignal) => Promise<unknown>;
type Generator = (input: any, context: PluginContext, signal: AbortSignal) => AsyncIterable<unknown>;
type Invocation = { id: string; operation: string; payload: any; context: { tenant: string; configuration: unknown } };
const scopes = new AsyncLocalStorage<{ request?: Invocation; active: boolean }>();
const encode = (value: unknown) => {
    const text = JSON.stringify(value);
    if (text === undefined) throw new TypeError('Result is not JSON');
    return text;
};

/** Register business handlers; this SDK supplies the executable protocol runtime. */
export class PluginApplication {
    /** Author-declared artifact version, verified against the host binding at startup. */
    constructor(private readonly pluginVersion: string = '1') {
        if (typeof pluginVersion !== 'string' || !pluginVersion.trim()) throw new Error('Invalid plugin version');
    }
    private functions = new Map<string, Handler>();
    private streams = new Map<string, Generator>();
    function<T, R>(name: string, handler: (input: T, context: PluginContext, signal: AbortSignal) => Promise<R>): this {
        this.validate(name); this.functions.set(name, handler); return this;
    }
    stream<T, R>(name: string, handler: (input: T, context: PluginContext, signal: AbortSignal) => AsyncIterable<R>): this {
        this.validate(name); this.streams.set(name, handler); return this;
    }
    private validate(name: string) {
        if (!name || name.startsWith('$') || this.functions.has(name) || this.streams.has(name)) throw new Error('Invalid or duplicate operation');
    }
    async run(): Promise<void> { await new Runtime(this.functions, this.streams, this.pluginVersion).run(); }
}

class Runtime {
    private reader!: AsyncIterator<string>;
    private output!: Writable;
    private socket?: Socket;
    private iterator?: AsyncIterator<unknown>;
    private context?: SessionContext;
    private abort?: AbortController;
    private streamId?: string;
    private pending?: unknown;
    private hasPending = false;
    private bytes = 0;
    private callbackId = 0;
    private callbackTail: Promise<void> = Promise.resolve();
    constructor(private functions: Map<string, Handler>, private streams: Map<string, Generator>, private pluginVersion: string) {}
    private async read(): Promise<any | undefined> {
        const line = await this.reader.next();
        if (line.done) return undefined;
        if (Buffer.byteLength(line.value) > 1 << 20) throw new Error('Frame limit');
        return JSON.parse(line.value);
    }
    private async send(value: unknown): Promise<void> {
        const line = encode(value);
        if (Buffer.byteLength(line) > 1 << 20) throw new Error('Frame limit');
        await new Promise<void>((resolve, reject) => this.output.write(line + '\n', error => error ? reject(error) : resolve()));
    }
    private async callback<T>(operation: string, payload: unknown): Promise<T> {
        const scope = scopes.getStore();
        let release!: () => void;
        const previous = this.callbackTail;
        this.callbackTail = new Promise<void>(resolve => release = resolve);
        await previous;
        try {
            if (!scope?.active || !scope.request) throw new Error('Expired invocation');
            const callbackId = String(++this.callbackId), id = scope.request.id;
            await this.send({ type: 'callback', id, callbackId, operation, payload });
            const reply = await this.read();
            if (reply?.type !== 'callback-result' || reply.id !== id || reply.callbackId !== callbackId) throw new Error('Callback identity');
            return reply.value;
        } finally { release(); }
    }
    private async close(): Promise<void> {
        const iterator = this.iterator;
        this.iterator = undefined; this.streamId = undefined; this.pending = undefined; this.hasPending = false;
        this.abort?.abort(); this.abort = undefined;
        try { if (iterator?.return) await iterator.return(); }
        finally { await this.completeContext(); }
    }
    private async completeContext(): Promise<void> {
        const context = this.context;
        this.context = undefined;
        await context?.complete();
    }
    private async dispatch(request: Invocation): Promise<unknown> {
        const { operation, payload } = request;
        if (operation === '$sdk.next' || operation === '$sdk.close') {
            if (!this.streamId || payload.stream !== this.streamId) throw new Error('Stream ownership');
            if (operation === '$sdk.close') { await this.close(); return {}; }
            const items: unknown[] = [];
            let batchBytes = 2, done = false;
            while (items.length < 16) {
                let item: unknown;
                if (this.hasPending) { item = this.pending; this.pending = undefined; this.hasPending = false; }
                else {
                    const next = await this.iterator!.next();
                    if (next.done) { done = true; break; }
                    item = next.value;
                }
                const size = Buffer.byteLength(encode(item));
                if (size > 128 << 10) throw new Error('Item limit');
                if (batchBytes + size + 1 > 256 << 10) { this.pending = item; this.hasPending = true; break; }
                batchBytes += size + 1; this.bytes += size;
                if (this.bytes > 64 << 20) throw new Error('Stream limit');
                items.push(item);
            }
            if (done) await this.close();
            return { items, done };
        }
        if (this.iterator) throw new Error('Stream already active');
        const context = this.context = new SessionContext(request.context.tenant, request.context.configuration, <T>(name: string, input: unknown) => this.callback<T>(name, input));
        const abort = new AbortController();
        if (operation === '$sdk.call') {
            try { return await this.functions.get(payload.operation)!(payload.input, context, abort.signal); }
            finally { try { abort.abort(); } finally { await this.completeContext(); } }
        }
        if (operation !== '$sdk.start') throw new Error('Unknown SDK operation');
        this.abort = abort;
        this.iterator = this.streams.get(payload.operation)!(payload.input, context, abort.signal)[Symbol.asyncIterator]();
        this.streamId = randomUUID(); this.bytes = 0;
        return { stream: this.streamId };
    }
    async run(): Promise<void> {
        let input: Readable = process.stdin;
        this.output = process.stdout;
        if (process.env.WEAVEPORT_SOCKET) {
            const socket = createConnection(process.env.WEAVEPORT_SOCKET);
            await new Promise<void>((resolve, reject) => { socket.once('connect', resolve); socket.once('error', reject); });
            this.socket = socket; input = socket; this.output = socket;
        }
        globalThis.console = new Console({ stdout: process.stderr, stderr: process.stderr });
        this.reader = createInterface({ input, crlfDelay: Infinity })[Symbol.asyncIterator]();
        await this.send({ type: 'ready', protocol: 1, pluginVersion: this.pluginVersion, sessionCleanup: 1 });
        try {
            let request: Invocation | undefined;
            while ((request = await this.read()) !== undefined) {
                const invocation = request;
                const scope: { request?: Invocation; active: boolean } = { request: invocation, active: true };
                await scopes.run(scope, async () => {
                    try {
                        const value = await this.dispatch(invocation);
                        scope.active = false;
                        await this.callbackTail;
                        await this.send({ type: 'result', id: invocation.id, value, reusable: !this.context && !this.iterator });
                    }
                    catch (error) {
                        let cleanupFailed = error instanceof SessionCleanupError;
                        try { await this.close(); } catch { cleanupFailed = true; }
                        await this.send({ type: 'error', id: invocation.id, code: cleanupFailed ? 'cleanup-error' : 'sdk-error' });
                    }
                    finally { scope.active = false; scope.request = undefined; }
                });
                request = undefined;
            }
        } finally { await this.close(); this.socket?.destroy(); }
    }
}
