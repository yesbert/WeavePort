import { SessionContext } from './session.js';
import type { PluginContext } from './session.js';

type Handler = (input: any, context: PluginContext, signal: AbortSignal) => Promise<unknown>;
type Call = { abort: AbortController; active: boolean; task?: Promise<void> };
type Reply = { resolve(value: any): void; reject(error: Error): void; owner: string };

/** A single reader owns routing; handlers never read frames directly. */
export async function runConcurrent(
    read: () => Promise<any>, send: (value: unknown) => Promise<void>, functions: Map<string, Handler>
): Promise<void> {
    const config = await read();
    if (config?.type !== 'configure' || !Number.isInteger(config.degree) || config.degree < 1 || config.degree > 1024)
        throw new Error('Expected bounded concurrency configuration');
    const calls = new Map<string, Call>(), callbacks = new Map<string, Reply>();
    let callbackId = 0;
    const completed = new Set<string>();
    const cancelledCallbacks = new Map<string, string>();
    const execute = async (request: any, call: Call): Promise<void> => {
        const pending: Promise<unknown>[] = [];
        const callback = <T>(operation: string, payload: unknown): Promise<T> => {
            if (!call.active || call.abort.signal.aborted) return Promise.reject(new Error('Expired invocation'));
            const id = String(++callbackId);
            const promise = new Promise<T>((resolve, reject) => {
                callbacks.set(id, { resolve, reject, owner: request.id });
                void send({ type: 'callback', id: request.id, callbackId: id, operation, payload }).catch(reject);
            });
            // Observe immediately even if author code does not await this callback.
            pending.push(promise.catch(error => { return { callbackError: error }; }));
            return promise;
        };
        const context = new SessionContext(request.context.tenant, request.context.configuration, callback);
        let response: any;
        try {
            if (request.operation !== '$sdk.call') throw new Error('Concurrent workers support unary functions only');
            const handler = functions.get(request.payload.operation);
            if (!handler) throw new Error('Unknown operation');
            const value = await handler(request.payload.input, context, call.abort.signal);
            response = { type: 'result', id: request.id, value, reusable: true };
        } catch { response = { type: 'error', id: request.id, code: 'sdk-error' }; }
        finally {
            call.active = false;
            try { await context.complete(); }
            catch { response = { type: 'error', id: request.id, code: 'cleanup-error' }; }
            const replies = await Promise.all(pending);
            if (replies.some(reply => reply && typeof reply === 'object' && 'callbackError' in reply) && response.code !== 'cleanup-error')
                response = { type: 'error', id: request.id, code: 'sdk-error' };
            if (call.abort.signal.aborted && response.code !== 'cleanup-error') response = { type: 'cancelled', id: request.id };
            try {
                const encoded = JSON.stringify(response);
                if (Buffer.byteLength(encoded) > 1 << 20) throw new Error("Frame limit");
            } catch { response = { type: "error", id: request.id, code: "sdk-error" }; }
            // A cancelled host callback can ignore cancellation and never reply. Keep
            // only bounded identity tombstones after releasing invocation-owned closures.
            for (const [id, reply] of callbacks) {
                if (reply.owner !== request.id) continue;
                callbacks.delete(id);
                cancelledCallbacks.set(id, request.id);
                if (cancelledCallbacks.size > 4096) cancelledCallbacks.delete(cancelledCallbacks.keys().next().value!);
            }
            calls.delete(request.id);
            completed.add(request.id);
            if (completed.size > 4096) completed.delete(completed.values().next().value!);
            await send(response);
        }
    };
    try {
        let frame: any;
        while ((frame = await read()) !== undefined) {
            if (frame.type === 'invoke') {
                if (typeof frame.id !== 'string' || calls.has(frame.id) || completed.has(frame.id) || calls.size >= config.degree)
                    throw new Error('Invalid invocation admission');
                if (typeof frame.operation !== 'string' || !frame.payload || typeof frame.payload !== 'object' ||
                    !frame.context || typeof frame.context.tenant !== 'string' || !('configuration' in frame.context))
                    throw new Error('Malformed invocation');
                const call: Call = { abort: new AbortController(), active: true };
                calls.set(frame.id, call);
                call.task = execute(frame, call);
            } else if (frame.type === 'callback-result') {
                const reply = callbacks.get(frame.callbackId);
                if (!reply) {
                    if (cancelledCallbacks.has(frame.callbackId) && cancelledCallbacks.get(frame.callbackId) === frame.id) {
                        cancelledCallbacks.delete(frame.callbackId);
                        continue;
                    }
                    throw new Error('Unknown callback identity');
                }
                if (reply.owner !== frame.id) throw new Error('Unknown callback identity');
                callbacks.delete(frame.callbackId);
                if (frame.error != null || frame.success === false) reply.reject(new Error('Host callback failed'));
                else reply.resolve(frame.value);
            } else if (frame.type === 'cancel') {
                const call = calls.get(frame.id);
                if (!call) {
                    if (completed.has(frame.id)) continue;
                    throw new Error('Unknown cancellation identity');
                }
                call.abort.abort();
                for (const reply of callbacks.values()) if (reply.owner === frame.id) reply.reject(new Error('Invocation cancelled'));
            } else throw new Error('Unknown protocol frame');
        }
    } finally {
        for (const call of calls.values()) call.abort.abort();
        for (const reply of callbacks.values()) reply.reject(new Error('Channel closed'));
        await Promise.allSettled([...calls.values()].map(call => call.task));
    }
}
