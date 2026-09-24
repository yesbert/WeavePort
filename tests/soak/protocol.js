import encoding from 'k6/encoding';

export function streamId(kind) {
    return (
        __VU.toString(16).padStart(8, '0') +
        __ITER.toString(16).padStart(16, '0') +
        kind.toString(16).padStart(8, '0')
    );
}
export function payload(value) {
    return encoding.b64encode(JSON.stringify(value));
}
export function decode(value) {
    return JSON.parse(encoding.b64decode(value, 'std', 's'));
}
export function require(value, reason) {
    if (!value) {
        const error = new Error(reason);
        error.safeReason = reason;
        throw error;
    }
}
export function statusCode(reply) {
    return [
        'failed',
        'sdk-error',
        'denied',
        'protocol-error',
        'busy',
        'timeout',
        'cancelled',
        'disabled',
        'gateway-failed',
    ].includes(reply.error)
        ? reply.error
        : 'other';
}
export function ok(reply) {
    require(!reply.error && !reply.cancelled, `Unexpected plugin failure: ${statusCode(reply)}`);
}
export function request(operation, input, mode = 'CALL', streamId = '') {
    return { operation, input: payload(input), mode, streamId };
}
