import { FrameKinds, ProtocolLimits } from '../protocol.js';

export type SdkPayload = {
    operation?: string;
    input?: unknown;
    stream?: string;
    source?: string;
    chunkBytes?: number;
};
export type Invocation = {
    type: typeof FrameKinds.Invoke;
    id: string;
    operation: string;
    payload: SdkPayload;
    context: { tenant: string; configuration: unknown };
};
export type CallbackReply = {
    type: typeof FrameKinds.CallbackResult;
    id: string;
    callbackId: string;
    value?: unknown;
    error?: unknown;
    success?: boolean;
};
export type Cancellation = { type: typeof FrameKinds.Cancel; id: string };
export type Configuration = { type: typeof FrameKinds.Configure; degree: number };
export type InboundFrame = Invocation | CallbackReply | Cancellation | Configuration;
export type Terminal = {
    type: typeof FrameKinds.Result | typeof FrameKinds.Error | typeof FrameKinds.Cancelled;
    id: string;
    value?: unknown;
    reusable?: boolean;
    code?: string;
    primaryCode?: string;
    cleanupFailed?: boolean;
};

function requireObject(value: unknown): Record<string, unknown> {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
        throw new TypeError('Expected protocol object');
    }
    return value as Record<string, unknown>;
}
function requireString(value: unknown): string {
    if (typeof value !== 'string') {
        throw new TypeError('Expected protocol string');
    }
    return value;
}
function decodeInvocation(frame: Record<string, unknown>): Invocation {
    const payload = requireObject(frame.payload);
    const context = requireObject(frame.context);
    if (!('configuration' in context)) {
        throw new TypeError('Missing configuration');
    }
    if (payload.operation !== undefined) {
        requireString(payload.operation);
    }
    if (payload.stream !== undefined) {
        requireString(payload.stream);
    }
    if (payload.source !== undefined) {
        requireString(payload.source);
    }
    if (payload.chunkBytes !== undefined && typeof payload.chunkBytes !== 'number') {
        throw new TypeError('Invalid source chunk size');
    }
    return {
        type: FrameKinds.Invoke,
        id: requireString(frame.id),
        operation: requireString(frame.operation),
        payload: payload as SdkPayload,
        context: { tenant: requireString(context.tenant), configuration: context.configuration },
    };
}
export function decodeFrame(value: unknown): InboundFrame {
    const frame = requireObject(value);
    switch (frame.type) {
        case FrameKinds.Invoke:
            return decodeInvocation(frame);
        case FrameKinds.Cancel:
            return { type: FrameKinds.Cancel, id: requireString(frame.id) };
        case FrameKinds.CallbackResult:
            return {
                type: FrameKinds.CallbackResult,
                id: requireString(frame.id),
                callbackId: requireString(frame.callbackId),
                value: frame.value,
                error: frame.error,
                success: frame.success === false ? false : undefined,
            };
        case FrameKinds.Configure:
            if (
                typeof frame.degree !== 'number' ||
                !Number.isInteger(frame.degree) ||
                frame.degree < 1 ||
                frame.degree > ProtocolLimits.MaximumConcurrentCalls
            ) {
                throw new TypeError('Invalid concurrency degree');
            }
            return { type: FrameKinds.Configure, degree: frame.degree };
        default:
            throw new TypeError('Unknown protocol frame');
    }
}

export function encodeJson(value: unknown): string {
    const text = JSON.stringify(value);
    if (text === undefined) {
        throw new TypeError('Result is not JSON');
    }
    return text;
}
