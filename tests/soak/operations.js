import { Session } from './session.js';
import { streamId, decode, require, statusCode, ok, request } from './protocol.js';

const ROW_WIDTH = 8192;
const MAX_BATCH_ITEMS = 16;
const rowText = 'x'.repeat(ROW_WIDTH);

export async function verifyEcho(session, owner, measured, config) {
    await measured('echo', () =>
        session.exchange(
            request('echo', { owner, text: 'Grüße 🚀 <>&', iteration: __ITER }),
            undefined,
            (reply) => {
                ok(reply);
                const value = decode(reply.json);
                require(value.owner === owner &&
                    value.text === 'Grüße 🚀 <>&' &&
                    value.iteration === __ITER, 'Echo mismatch');
                require(!config.injectFailure, 'Injected validation failure');
            },
        ),
    );
}

export async function verifyCallback(session, owner, measured) {
    await measured('callback', () =>
        session.exchange(
            request('owner', { operation: 'host.owner', input: { tenant: 'forged' } }),
            undefined,
            (reply) => {
                ok(reply);
                const value = decode(reply.json);
                require(value.owner === owner &&
                    value.secret === `synthetic-${owner}`, 'Callback authority mismatch');
            },
        ),
    );
}

export async function verifyRecords(session, owner, measured) {
    const count = __ITER % 20 === 0 ? 1024 : 8;
    let received = 0;
    await measured('records', () =>
        session.exchange(
            request('records', { count, width: ROW_WIDTH, callbacks: true }, 'STREAM', streamId(0)),
            (rows) => {
                require(Array.isArray(rows) && rows.length <= MAX_BATCH_ITEMS, 'Invalid batch');
                for (const row of rows) {
                    require(row.id === received++ &&
                        row.text === rowText &&
                        row.owner === owner, 'Row identity/content mismatch');
                }
            },
            (reply) => {
                ok(reply);
                require(received === count, 'Incomplete stream');
            },
        ),
    );
}

export async function verifyCancellation(session, client, binding, owner, measured, faults) {
    await measured('cancel', async () => {
        const id = streamId(2);
        let cancellation;
        let cancelSession;
        const active = session.exchange(
            request('records', { count: 10000, width: 32, delayMs: 2 }, 'STREAM', id),
            (rows) => {
                for (const row of rows) {
                    require(row.owner === owner, 'Cancel stream tenant mismatch');
                }
                if (rows.length === 0 || cancellation) {
                    return;
                }
                cancelSession = new Session(client, binding.Credential);
                cancellation = cancelSession
                    .exchange(request('', {}, 'CANCEL', id))
                    .then(() => cancelSession.close());
                // The rejection is observed below after the active stream drains.
                cancellation.catch(() => {});
            },
            (reply) => {
                require(reply.cancelled, 'Missing cancellation terminal');
                faults.add(1);
            },
        );
        await active;
        require(cancellation, 'Cancellation not dispatched');
        await cancellation;
    });
}

export async function verifyFaults(session, client, binding, owner, measured, faults) {
    // Stagger faults across tenants; each affects only this binding.
    if (__ITER % 25 !== (__VU - 1) % 25) {
        return;
    }
    await measured('failure', () =>
        session.exchange(
            request('records', { count: 40, width: 32, failAt: 20 }, 'STREAM', streamId(1)),
            (rows) => {
                for (const row of rows) {
                    require(row.owner === owner, 'Fault stream tenant mismatch');
                }
            },
            (reply) => {
                require(reply.error === 'sdk-error' &&
                    !reply.cancelled, 'Missing expected provider failure');
                faults.add(1);
            },
        ),
    );
    await measured('denied', () =>
        session.exchange(
            request('owner', { operation: 'forbidden', input: {} }),
            undefined,
            (reply) => {
                require(reply.error === 'denied', 'Missing callback denial');
                faults.add(1);
            },
        ),
    );
    await measured('crash', () =>
        session.exchange(request('crash', {}), undefined, (reply) => {
            require(reply.error === 'failed', `Missing crash failure: ${statusCode(reply)}`);
            faults.add(1);
        }),
    );
    await measured('recovery', () =>
        session.exchange(request('echo', { owner }), undefined, (reply) => {
            ok(reply);
            require(decode(reply.json).owner === owner, 'Recovery mismatch');
        }),
    );
    await verifyCancellation(session, client, binding, owner, measured, faults);
    await measured('recovery', () =>
        session.exchange(request('echo', { owner }), undefined, (reply) => {
            ok(reply);
            require(decode(reply.json).owner === owner, 'Post-cancel recovery mismatch');
        }),
    );
}
