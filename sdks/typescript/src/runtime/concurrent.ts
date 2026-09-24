import type {
    InboundFrame,
    Invocation,
    CallbackReply,
    Cancellation,
    Terminal,
} from './messages.js';
import { FailureCodes, FrameKinds, ProtocolLimits, SdkOperations } from '../protocol.js';
import { SessionContext } from '../session.js';

const MAXIMUM_RETIRED_IDENTITIES = 4096;

import type { Handler } from './handlers.js';
type Call = { abort: AbortController; active: boolean; task?: Promise<void> };
type Reply = {
    resolve(value: unknown): void;
    reject(error: Error): void;
    owner: string;
};

/** A single reader owns routing; handlers never read frames directly. */
export async function runConcurrent(
    read: () => Promise<InboundFrame | undefined>,
    send: (value: unknown) => Promise<void>,
    functions: Map<string, Handler>,
): Promise<void> {
    const config = await read();
    if (
        config?.type !== 'configure' ||
        !Number.isInteger(config.degree) ||
        config.degree < 1 ||
        config.degree > ProtocolLimits.MaximumConcurrentCalls
    ) {
        throw new Error('Expected bounded concurrency configuration');
    }
    await new ConcurrentRuntime(read, send, functions, config.degree).run();
}

class ConcurrentRuntime {
    private readonly calls = new Map<string, Call>();
    private readonly callbacks = new Map<string, Reply>();
    private callbackId = 0;
    private readonly completed = new Set<string>();
    private readonly cancelledCallbacks = new Map<string, string>();

    constructor(
        private readonly read: () => Promise<InboundFrame | undefined>,
        private readonly send: (value: unknown) => Promise<void>,
        private readonly functions: Map<string, Handler>,
        private readonly degree: number,
    ) {}

    private async execute(request: Invocation, call: Call): Promise<void> {
        const pending: Promise<unknown>[] = [];
        const callback = <T>(operation: string, payload: unknown): Promise<T> => {
            if (!call.active || call.abort.signal.aborted)
                return Promise.reject(new Error('Expired invocation'));
            const id = String(++this.callbackId);
            const promise = new Promise<T>((resolve, reject) => {
                this.callbacks.set(id, {
                    resolve: (value) => resolve(value as T),
                    reject,
                    owner: request.id,
                });
                void this.send({
                    type: FrameKinds.Callback,
                    id: request.id,
                    callbackId: id,
                    operation,
                    payload,
                }).catch(reject);
            });
            // Observe immediately even if author code does not await this callback.
            pending.push(
                promise.catch((error) => {
                    return { callbackError: error };
                }),
            );
            return promise;
        };
        const context = new SessionContext(
            request.context.tenant,
            request.context.configuration,
            callback,
        );
        let response: Terminal = {
            type: FrameKinds.Error,
            id: request.id,
            code: FailureCodes.SdkError,
        };
        try {
            if (request.operation !== SdkOperations.Call)
                throw new Error('Concurrent workers support unary functions only');
            const handler = this.functions.get(request.payload.operation ?? '');
            if (!handler) throw new Error('Unknown operation');
            const value = await handler(request.payload.input, context, call.abort.signal);
            response = { type: FrameKinds.Result, id: request.id, value, reusable: true };
        } catch {
            response = { type: FrameKinds.Error, id: request.id, code: FailureCodes.SdkError };
        } finally {
            call.active = false;
            try {
                await context.complete();
            } catch {
                response = {
                    type: FrameKinds.Error,
                    id: request.id,
                    code: FailureCodes.CleanupError,
                    primaryCode: response.code ?? FailureCodes.CleanupError,
                    cleanupFailed: true,
                };
            }
            const replies = await Promise.all(pending);
            if (
                replies.some(
                    (reply) => reply && typeof reply === 'object' && 'callbackError' in reply,
                ) &&
                response.code !== FailureCodes.CleanupError
            )
                response = { type: FrameKinds.Error, id: request.id, code: FailureCodes.SdkError };
            if (call.abort.signal.aborted && response.code !== FailureCodes.CleanupError)
                response = { type: FailureCodes.Cancelled, id: request.id };
            try {
                const encoded = JSON.stringify(response);
                if (Buffer.byteLength(encoded) > ProtocolLimits.FrameBytes)
                    throw new Error('Frame limit');
            } catch {
                response = { type: FrameKinds.Error, id: request.id, code: FailureCodes.SdkError };
            }
            // A cancelled host callback can ignore cancellation and never reply. Keep
            // only bounded identity tombstones after releasing invocation-owned closures.
            for (const [id, reply] of this.callbacks) {
                if (reply.owner !== request.id) continue;
                this.callbacks.delete(id);
                this.cancelledCallbacks.set(id, request.id);
                if (this.cancelledCallbacks.size <= MAXIMUM_RETIRED_IDENTITIES) continue;
                this.cancelledCallbacks.delete(this.cancelledCallbacks.keys().next().value!);
            }
            this.calls.delete(request.id);
            this.completed.add(request.id);
            if (this.completed.size > MAXIMUM_RETIRED_IDENTITIES)
                this.completed.delete(this.completed.values().next().value!);
            await this.send(response);
        }
    }

    async run(): Promise<void> {
        try {
            let frame: InboundFrame | undefined;
            while ((frame = await this.read()) !== undefined) {
                switch (frame.type) {
                    case 'invoke':
                        this.admit(frame);
                        break;
                    case 'callback-result':
                        this.completeCallback(frame);
                        break;
                    case 'cancel':
                        this.cancel(frame);
                        break;
                    default:
                        throw new Error('Unknown protocol frame');
                }
            }
        } finally {
            for (const call of this.calls.values()) call.abort.abort();
            for (const reply of this.callbacks.values()) reply.reject(new Error('Channel closed'));
            await Promise.allSettled([...this.calls.values()].map((call) => call.task));
        }
    }

    private admit(frame: Invocation): void {
        if (
            typeof frame.id !== 'string' ||
            this.calls.has(frame.id) ||
            this.completed.has(frame.id) ||
            this.calls.size >= this.degree
        )
            throw new Error('Invalid invocation admission');
        if (
            typeof frame.operation !== 'string' ||
            !frame.payload ||
            typeof frame.payload !== 'object' ||
            !frame.context ||
            typeof frame.context.tenant !== 'string' ||
            !('configuration' in frame.context)
        )
            throw new Error('Malformed invocation');
        const call: Call = { abort: new AbortController(), active: true };
        this.calls.set(frame.id, call);
        call.task = this.execute(frame, call);
    }

    private completeCallback(frame: CallbackReply): void {
        const reply = this.callbacks.get(frame.callbackId);
        if (
            !reply &&
            this.cancelledCallbacks.has(frame.callbackId) &&
            this.cancelledCallbacks.get(frame.callbackId) === frame.id
        ) {
            this.cancelledCallbacks.delete(frame.callbackId);
            return;
        }
        if (!reply) throw new Error('Unknown callback identity');
        if (reply.owner !== frame.id) throw new Error('Unknown callback identity');
        this.callbacks.delete(frame.callbackId);
        if (frame.error != null || frame.success === false)
            reply.reject(new Error('Host callback failed'));
        else reply.resolve(frame.value);
    }

    private cancel(frame: Cancellation): void {
        const call = this.calls.get(frame.id);
        if (!call && this.completed.has(frame.id)) return;
        if (!call) throw new Error('Unknown cancellation identity');
        call.abort.abort();
        for (const reply of this.callbacks.values()) {
            if (reply.owner !== frame.id) continue;
            reply.reject(new Error('Invocation cancelled'));
        }
    }
}
