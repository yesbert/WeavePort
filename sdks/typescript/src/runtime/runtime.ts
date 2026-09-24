import { Channel } from './channel.js';
import { encodeJson } from './messages.js';
import type { InboundFrame, Invocation, SdkPayload } from './messages.js';
import { cleanupAll, executeWithCleanup } from './cleanup.js';
import { FailureCodes, FrameKinds, ProtocolVersions, SdkOperations } from '../protocol.js';
import { StreamBuffer } from './stream-buffer.js';
import { SourceSession } from '../source.js';
import type { SourceHandler } from '../source.js';
import { runConcurrent } from './concurrent.js';
import { AsyncLocalStorage } from 'node:async_hooks';
import { randomUUID } from 'node:crypto';

import { SessionContext, SessionCleanupError } from '../session.js';
import type { Handler, Generator } from './handlers.js';
type InvocationScope = { request?: Invocation; active: boolean };
class StreamClosedError extends Error {}
const scopes = new AsyncLocalStorage<InvocationScope>();

export class Runtime {
    private readonly channel = new Channel();
    private source = new SourceSession();
    private streamBuffer = new StreamBuffer(encodeJson);
    private iterator?: AsyncIterator<unknown>;
    private context?: SessionContext;
    private abort?: AbortController;
    private streamId?: string;
    private callbackId = 0;
    private streamScope?: InvocationScope;
    private exchangeActive = false;
    private bufferedStreamItems = false;
    private closingStream = false;
    private exchangeWaiters: (() => void)[] = [];
    private callbackTail: Promise<void> = Promise.resolve();
    constructor(
        private functions: Map<string, Handler>,
        private streams: Map<string, Generator>,
        private sources: Map<string, SourceHandler>,
        private pluginVersion: string,
        private concurrentCalls: boolean,
    ) {}
    private async callback<T>(operation: string, payload: unknown): Promise<T> {
        const scope = scopes.getStore();
        const streamCallback = scope === this.streamScope;
        if (streamCallback && (!this.exchangeActive || this.bufferedStreamItems)) {
            await new Promise<void>((resolve) => this.exchangeWaiters.push(resolve));
        }
        if (streamCallback && this.closingStream) {
            throw new StreamClosedError('Stream closed');
        }
        let release!: () => void;
        const previous = this.callbackTail;
        this.callbackTail = new Promise<void>((resolve) => (release = resolve));
        await previous;
        try {
            if (!scope?.active || !scope.request) {
                throw new Error('Expired invocation');
            }
            const callbackId = String(++this.callbackId),
                id = scope.request.id;
            await this.channel.send({
                type: FrameKinds.Callback,
                id,
                callbackId,
                operation,
                payload,
            });
            const reply = await this.channel.read();
            if (
                reply?.type !== FrameKinds.CallbackResult ||
                reply.id !== id ||
                reply.callbackId !== callbackId
            ) {
                throw new Error('Callback identity');
            }
            if (reply.error != null || reply.success === false) {
                throw new Error('Host callback failed');
            }
            return reply.value as T;
        } finally {
            release();
        }
    }
    private async close(): Promise<void> {
        const iterator = this.iterator;
        this.closingStream = true;
        this.source.release();
        const advance = this.streamBuffer.reset();
        this.streamScope = undefined;
        this.bufferedStreamItems = false;
        this.iterator = undefined;
        this.streamId = undefined;
        this.abort?.abort();
        this.abort = undefined;
        await cleanupAll(
            async () => {
                try {
                    if (advance) {
                        await advance;
                    }
                } catch (error) {
                    if (!(error instanceof StreamClosedError)) {
                        throw error;
                    }
                }
            },
            async () => {
                if (iterator?.return) {
                    await iterator.return();
                }
            },
            () => this.completeContext(),
        );
    }
    private async completeContext(): Promise<void> {
        const context = this.context;
        this.context = undefined;
        await context?.complete();
    }
    private async dispatch(request: Invocation): Promise<unknown> {
        const { operation, payload } = request;
        if (operation === SdkOperations.SourceRead || operation === SdkOperations.SourceClose) {
            return await this.dispatchSource(operation, payload);
        }
        if (operation === SdkOperations.Next || operation === SdkOperations.Close) {
            return await this.dispatchStream(operation, payload);
        }
        if (this.iterator || this.source.identity) {
            throw new Error('Session already active');
        }
        const context = (this.context = new SessionContext(
            request.context.tenant,
            request.context.configuration,
            <T>(name: string, input: unknown) => this.callback<T>(name, input),
        ));
        const abort = new AbortController();
        if (operation === SdkOperations.SourceOpen) {
            this.abort = abort;
            return await this.source.open(
                this.sources.get(payload.operation ?? '')!,
                payload.input,
                context,
                abort.signal,
            );
        }
        if (operation === SdkOperations.Call) {
            return await executeWithCleanup(
                () =>
                    this.functions.get(payload.operation ?? '')!(
                        payload.input,
                        context,
                        abort.signal,
                    ),
                () =>
                    cleanupAll(
                        async () => {
                            abort.abort();
                        },
                        () => this.completeContext(),
                    ),
            );
        }
        if (operation !== SdkOperations.Start) {
            throw new Error('Unknown SDK operation');
        }
        this.abort = abort;
        this.closingStream = false;
        this.streamScope = scopes.getStore();
        this.iterator = this.streams.get(payload.operation ?? '')!(
            payload.input,
            context,
            abort.signal,
        )[Symbol.asyncIterator]();
        this.streamId = randomUUID();
        return { stream: this.streamId };
    }

    async run(): Promise<void> {
        await this.channel.connect();
        await this.channel.send({
            type: FrameKinds.Ready,
            protocol: this.concurrentCalls
                ? ProtocolVersions.Concurrent
                : ProtocolVersions.Exclusive,
            pluginVersion: this.pluginVersion,
            sessionCleanup: ProtocolVersions.SessionCleanup,
            ...(this.concurrentCalls ? { concurrentCalls: ProtocolVersions.ConcurrentCalls } : {}),
        });
        if (this.concurrentCalls) {
            try {
                await runConcurrent(
                    () => this.channel.read(),
                    (value) => this.channel.send(value),
                    this.functions,
                );
            } finally {
                this.channel.close();
            }
            return;
        }
        try {
            let request: InboundFrame | undefined;
            while ((request = await this.channel.read()) !== undefined) {
                if (request.type !== FrameKinds.Invoke) {
                    throw new Error('Expected invocation');
                }
                const invocation = request;
                const scope = this.streamScope ?? { request: invocation, active: true };
                scope.request = invocation;
                scope.active = true;
                this.exchangeActive = true;
                this.resumeCallbacks();
                await this.exchange(invocation, scope);
                request = undefined;
            }
        } finally {
            await this.close();
            this.channel.close();
        }
    }

    private async exchange(invocation: Invocation, scope: InvocationScope): Promise<void> {
        await scopes.run(scope, async () => {
            try {
                const value = await this.dispatch(invocation);
                this.exchangeActive = false;
                scope.active = false;
                await this.callbackTail;
                await this.channel.send({
                    type: FrameKinds.Result,
                    id: invocation.id,
                    value,
                    reusable: !this.context && !this.iterator,
                });
            } catch (error) {
                let cleanupFailed = error instanceof SessionCleanupError;
                try {
                    await this.close();
                } catch {
                    cleanupFailed = true;
                }
                await this.channel.send({
                    type: FrameKinds.Error,
                    id: invocation.id,
                    code: cleanupFailed ? FailureCodes.CleanupError : FailureCodes.SdkError,
                    primaryCode:
                        error instanceof SessionCleanupError && !error.hasExecutionFailure
                            ? FailureCodes.CleanupError
                            : FailureCodes.SdkError,
                    cleanupFailed,
                });
            } finally {
                this.exchangeActive = false;
                scope.active = false;
                scope.request = undefined;
            }
        });
    }
    private async dispatchSource(operation: string, payload: SdkPayload): Promise<unknown> {
        if (!this.source.identity || payload.source !== this.source.identity) {
            throw new Error('Source ownership');
        }
        if (operation === SdkOperations.SourceClose) {
            await this.close();
            return {};
        }
        const result = await this.source.read(payload);
        if (result.done) {
            await this.close();
        }
        return result;
    }
    private async dispatchStream(operation: string, payload: SdkPayload): Promise<unknown> {
        if (!this.streamId || payload.stream !== this.streamId) {
            throw new Error('Stream ownership');
        }
        if (operation === SdkOperations.Close) {
            await this.close();
            return {};
        }
        this.bufferedStreamItems = false;
        const batch = await this.streamBuffer.read(this.iterator!, () => {
            this.bufferedStreamItems = true;
        });
        if (batch.done) {
            await this.close();
        }
        return batch;
    }
    private resumeCallbacks(): void {
        for (const resume of this.exchangeWaiters.splice(0)) {
            resume();
        }
    }
}
