import { SourceSession } from './source.js';
import type { SourceHandler, BinarySource } from './source.js';
export type { BinarySource } from './source.js';
import { runConcurrent } from './concurrent.js';
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
    constructor(private readonly pluginVersion: string = '1', private readonly options: { concurrentCalls?: boolean } = {}) {
        if (typeof pluginVersion !== 'string' || !pluginVersion.trim()) throw new Error('Invalid plugin version');
    }
    private functions = new Map<string, Handler>();
    private streams = new Map<string, Generator>();
    private sources = new Map<string, SourceHandler>();
    function<T, R>(name: string, handler: (input: T, context: PluginContext, signal: AbortSignal) => Promise<R>): this {
        this.validate(name); this.functions.set(name, handler); return this;
    }
    stream<T, R>(name: string, handler: (input: T, context: PluginContext, signal: AbortSignal) => AsyncIterable<R>): this {
        this.validate(name); this.streams.set(name, handler); return this;
    }
    source<T>(name: string, handler: (input: T, context: PluginContext, signal: AbortSignal) => BinarySource | Promise<BinarySource>): this {
        this.validate(name); this.sources.set(name, handler); return this;
    }
    private validate(name: string) {
        if (!name || name.startsWith('$') || this.functions.has(name) || this.streams.has(name) || this.sources.has(name)) throw new Error('Invalid or duplicate operation');
    }
    async run(): Promise<void> { await new Runtime(this.functions, this.streams, this.sources, this.pluginVersion, this.options.concurrentCalls === true).run(); }
}

class Runtime {
    private reader!: AsyncIterator<string>;
    private output!: Writable;
    private socket?: Socket;
    private source = new SourceSession();
    private iterator?: AsyncIterator<unknown>;
    private context?: SessionContext;
    private abort?: AbortController;
    private streamId?: string;
    private pending?: unknown;
    private hasPending = false;
    private bytes = 0;
    private callbackId = 0;
    private advance?: Promise<IteratorResult<unknown>>;
    private streamScope?: { request?: Invocation; active: boolean };
    private exchangeActive = false;
    private exchangeWaiters: (() => void)[] = [];
    private callbackTail: Promise<void> = Promise.resolve();
    constructor(private functions: Map<string, Handler>, private streams: Map<string, Generator>, private sources: Map<string, SourceHandler>, private pluginVersion: string, private concurrentCalls: boolean) {}
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
        if (scope === this.streamScope && !this.exchangeActive) {
            await new Promise<void>(resolve => this.exchangeWaiters.push(resolve));
        }
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
            if (reply.error != null || reply.success === false) throw new Error('Host callback failed');
            return reply.value;
        } finally { release(); }
    }
    private async close(): Promise<void> {
        const iterator = this.iterator;
        this.source.release();
        const advance = this.advance; this.advance = undefined;
        this.streamScope = undefined;
        this.iterator = undefined; this.streamId = undefined; this.pending = undefined; this.hasPending = false;
        this.abort?.abort(); this.abort = undefined;
        try {
            if (advance) await advance.catch(() => undefined);
            if (iterator?.return) {
                try { await iterator.return(); }
                catch (error) { throw new SessionCleanupError([error], "Stream cleanup failed"); }
            }
        }
        finally { await this.completeContext(); }
    }
    private async completeContext(): Promise<void> {
        const context = this.context;
        this.context = undefined;
        await context?.complete();
    }
    private async dispatch(request: Invocation): Promise<unknown> {
        const { operation, payload } = request;
        if (operation === '$sdk.source.read' || operation === '$sdk.source.close') {
            if (!this.source.identity || payload.source !== this.source.identity) throw new Error('Source ownership');
            if (operation === '$sdk.source.close') { await this.close(); return {}; }
            const result = await this.source.read(payload);
            if (result.done) await this.close();
            return result;
        }
        if (operation === '$sdk.next' || operation === '$sdk.close') {
            if (!this.streamId || payload.stream !== this.streamId) throw new Error('Stream ownership');
            if (operation === '$sdk.close') { await this.close(); return {}; }
            const items: unknown[] = [];
            let batchBytes = 2, done = false;
            while (items.length < 16) {
                let item: unknown;
                if (this.hasPending) { item = this.pending; this.pending = undefined; this.hasPending = false; }
                else {
                    this.advance ??= this.iterator!.next();
                    let timer: ReturnType<typeof setTimeout> | undefined;
                    const next = await Promise.race([this.advance, new Promise<undefined>(resolve => {
                        timer = setTimeout(() => resolve(undefined), items.length ? 0 : 25);
                    })]);
                    clearTimeout(timer);
                    if (next === undefined) break;
                    this.advance = undefined;
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
        if (this.iterator || this.source.identity) throw new Error('Session already active');
        const context = this.context = new SessionContext(request.context.tenant, request.context.configuration, <T>(name: string, input: unknown) => this.callback<T>(name, input));
        const abort = new AbortController();
        if (operation === '$sdk.source.open') {
            this.abort = abort;
            return await this.source.open(this.sources.get(payload.operation)!, payload.input, context, abort.signal);
        }
        if (operation === '$sdk.call') {
            try { return await this.functions.get(payload.operation)!(payload.input, context, abort.signal); }
            finally { try { abort.abort(); } finally { await this.completeContext(); } }
        }
        if (operation !== '$sdk.start') throw new Error('Unknown SDK operation');
        this.abort = abort;
        this.streamScope = scopes.getStore();
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
        await this.send({ type: 'ready', protocol: this.concurrentCalls ? 2 : 1, pluginVersion: this.pluginVersion, sessionCleanup: 1, ...(this.concurrentCalls ? { concurrentCalls: 1 } : {}) });
        if (this.concurrentCalls) {
            try { await runConcurrent(() => this.read(), value => this.send(value), this.functions); }
            finally { this.socket?.destroy(); }
            return;
        }
        try {
            let request: Invocation | undefined;
            while ((request = await this.read()) !== undefined) {
                const invocation = request;
                const scope = this.streamScope ?? { request: invocation, active: true };
                scope.request = invocation; scope.active = true;
                this.exchangeActive = true;
                for (const resume of this.exchangeWaiters.splice(0)) resume();
                await scopes.run(scope, async () => {
                    try {
                        const value = await this.dispatch(invocation);
                        this.exchangeActive = false;
                        scope.active = false;
                        await this.callbackTail;
                        await this.send({ type: 'result', id: invocation.id, value, reusable: !this.context && !this.iterator });
                    }
                    catch (error) {
                        let cleanupFailed = error instanceof SessionCleanupError;
                        try { await this.close(); } catch { cleanupFailed = true; }
                        await this.send({ type: 'error', id: invocation.id, code: cleanupFailed ? 'cleanup-error' : 'sdk-error' });
                    }
                    finally { this.exchangeActive = false; scope.active = false; scope.request = undefined; }
                });
                request = undefined;
            }
        } finally { await this.close(); this.socket?.destroy(); }
    }
}
