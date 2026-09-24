import { Session } from './session.js';
import { verifyEcho, verifyCallback, verifyRecords, verifyFaults } from './operations.js';
import { Client } from 'k6/net/grpc';
import { Counter, Rate, Trend } from 'k6/metrics';
import { setTimeout } from 'k6/timers';

const config = JSON.parse(open(__ENV.WP_SOAK_CONFIG));
const client = new Client();
client.load(['../../src/WeavePort.Sdk.Gateway.Client'], 'gateway.proto');
const errors = new Rate('unexpected_errors');
const operations = new Counter('completed_operations');
const latency = new Trend('operation_ms', true);
const faults = new Counter('expected_faults');
const thresholds = {
    unexpected_errors: [{ threshold: 'rate==0', abortOnFail: true }],
    completed_operations: ['count>0'],
    operation_ms: [
        { threshold: `p(99)<${config.p99Ms}`, abortOnFail: true, delayAbortEval: '30s' },
    ],
};
for (let i = 0; i < config.bindings.length; i++) {
    thresholds[`completed_operations{tenant:tenant-${i}}`] = ['count>0'];
    thresholds[`unexpected_errors{tenant:tenant-${i}}`] = ['rate==0'];
    thresholds[`operation_ms{tenant:tenant-${i}}`] = [
        { threshold: `p(99)<${config.p99Ms}`, abortOnFail: true, delayAbortEval: '30s' },
    ];
}
for (const kind of [
    'echo',
    'callback',
    'records',
    'failure',
    'denied',
    'crash',
    'cancel',
    'recovery',
]) {
    thresholds[`completed_operations{kind:${kind}}`] = ['count>0'];
    thresholds[`operation_ms{kind:${kind}}`] = [
        { threshold: `p(99)<${config.p99Ms}`, abortOnFail: true, delayAbortEval: '30s' },
    ];
}
export const options = {
    scenarios: {
        soak: {
            executor: 'constant-vus',
            vus: config.bindings.length,
            duration: `${config.seconds}s`,
            gracefulStop: '15s',
        },
    },
    thresholds,
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(95)', 'p(99)'],
    systemTags: ['status', 'method', 'name', 'scenario'],
};
let connected = false;
let windowStart = Date.now();
let currentKind = 'connect';
let windowCount = 0;
let windowMax = 0;

function tags(kind) {
    return { tenant: `tenant-${__VU - 1}`, language: config.bindings[__VU - 1].Language, kind };
}
function delay(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

async function measured(kind, action) {
    const started = Date.now();
    currentKind = kind;
    try {
        await action();
        const elapsed = Date.now() - started;
        latency.add(elapsed, tags(kind));
        operations.add(1, tags(kind));
        errors.add(false, tags(kind));
        windowCount++;
        windowMax = Math.max(windowMax, elapsed);
    } catch (error) {
        errors.add(true, tags(kind));
        throw error;
    }
}

export default async function () {
    let session;
    try {
        if (!connected) {
            client.connect(config.address, { plaintext: true, timeout: '5s' });
            connected = true;
        }
        const binding = config.bindings[__VU - 1];
        session = new Session(client, binding.Credential);
        const owner = binding.Tenant;
        await verifyEcho(session, owner, measured, config);
        await verifyCallback(session, owner, measured);
        await verifyRecords(session, owner, measured);
        await verifyFaults(session, client, binding, owner, measured, faults);
        await session.close();
        if (__ITER % 100 === 99) {
            client.close();
            connected = false;
        }
        if (Date.now() - windowStart >= 60000) {
            console.log(
                JSON.stringify({
                    window: true,
                    tenant: owner,
                    seconds: (Date.now() - windowStart) / 1000,
                    completed: windowCount,
                    maxMs: windowMax,
                }),
            );
            windowStart = Date.now();
            windowCount = 0;
            windowMax = 0;
        }
        await delay(config.paceMs);
    } catch (error) {
        errors.add(true, tags('iteration'));
        // Never include credentials or plugin data in logs.
        console.error(
            `Unexpected workload failure kind=${currentKind} tenant-${__VU - 1} iteration ${__ITER} reason=${error.safeReason || 'transport-or-unclassified'}`,
        );
        client.close();
        connected = false;
        await delay(config.paceMs);
    }
}
export function handleSummary(data) {
    return { [__ENV.WP_SOAK_SUMMARY]: JSON.stringify(data, null, 2) };
}
