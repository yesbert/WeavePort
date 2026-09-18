import { Client, Stream } from 'k6/net/grpc';
import encoding from 'k6/encoding';
import { Counter, Rate, Trend } from 'k6/metrics';
import { setTimeout } from 'k6/timers';

const config = JSON.parse(open(__ENV.WP_SOAK_CONFIG));
const rowText = 'x'.repeat(8192);
const client = new Client();
client.load(['../../src/WeavePort.Sdk.Gateway.Client'], 'gateway.proto');
const errors = new Rate('unexpected_errors');
const operations = new Counter('completed_operations');
const latency = new Trend('operation_ms', true);
const faults = new Counter('expected_faults');
const thresholds = {
  unexpected_errors: [{ threshold: 'rate==0', abortOnFail: true }],
  completed_operations: ['count>0'],
  operation_ms: [{ threshold: `p(99)<${config.p99Ms}`, abortOnFail: true, delayAbortEval: '30s' }],
};
for (let i = 0; i < config.bindings.length; i++) {
  thresholds[`completed_operations{tenant:tenant-${i}}`] = ['count>0'];
  thresholds[`unexpected_errors{tenant:tenant-${i}}`] = ['rate==0'];
  thresholds[`operation_ms{tenant:tenant-${i}}`] = [{ threshold: `p(99)<${config.p99Ms}`, abortOnFail: true, delayAbortEval: '30s' }];
}
for (const kind of ['echo', 'callback', 'records', 'failure', 'denied', 'crash', 'cancel', 'recovery']) {
  thresholds[`completed_operations{kind:${kind}}`] = ['count>0'];
  thresholds[`operation_ms{kind:${kind}}`] = [{ threshold: `p(99)<${config.p99Ms}`, abortOnFail: true, delayAbortEval: '30s' }];
}
export const options = {
  scenarios: { soak: { executor: 'constant-vus', vus: config.bindings.length, duration: `${config.seconds}s`, gracefulStop: '15s' } },
  thresholds,
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(95)', 'p(99)'],
  systemTags: ['status', 'method', 'name', 'scenario'],
};
let connected = false;
let windowStart = Date.now();
let currentKind = "connect";
let windowCount = 0;
let windowMax = 0;

function streamId(kind) { return __VU.toString(16).padStart(8, '0') + __ITER.toString(16).padStart(16, '0') + kind.toString(16).padStart(8, '0'); }
function payload(value) { return encoding.b64encode(JSON.stringify(value)); }
function decode(value) { return JSON.parse(encoding.b64decode(value, 'std', 's')); }
function require(value, reason) { if (!value) { const error = new Error(reason); error.safeReason = reason; throw error; } }
function statusCode(reply) { return ['failed', 'denied', 'protocol-error', 'busy', 'timeout', 'cancelled', 'disabled', 'gateway-failed'].includes(reply.error) ? reply.error : 'other'; }
function tags(kind) { return { tenant: `tenant-${__VU - 1}`, language: config.bindings[__VU - 1].Language, kind }; }
function delay(ms) { return new Promise(resolve => setTimeout(resolve, ms)); }

class Session {
  constructor(credential) {
    this.pending = null;
    this.failure = null;
    this.stream = new Stream(client, 'weaveport.sdk.WorkerGateway/Session', {
      metadata: { 'x-weaveport-binding': credential }, timeout: '30s',
    });
    this.ended = new Promise(resolve => { this.endResolve = resolve; });
    this.stream.on('data', reply => {
      const active = this.pending;
      if (!active) { this.failure = new Error('Unsolicited response'); return; }
      try {
        if (reply.complete) {
          active.terminal(reply);
          this.pending = null;
          active.resolve();
        } else active.row(decode(reply.json));
      } catch (error) {
        this.pending = null;
        this.failure = error;
        active.reject(error);
      }
    });
    this.stream.on('error', () => this.fail(new Error('gRPC transport error')));
    this.stream.on('end', () => {
      if (this.pending) this.fail(new Error('Missing terminal response'));
      this.endResolve();
    });
  }
  fail(error) {
    this.failure = error;
    if (this.pending) { this.pending.reject(error); this.pending = null; }
    this.endResolve();
  }
  exchange(request, row = () => { throw new Error('Unexpected row'); }, terminal = ok) {
    if (this.failure) return Promise.reject(this.failure);
    require(!this.pending, 'Concurrent exchange on one session');
    return new Promise((resolve, reject) => {
      this.pending = { row, terminal, resolve, reject };
      this.stream.write(request);
    });
  }
  async close() { this.stream.end(); await this.ended; if (this.failure) throw this.failure; }
}
function ok(reply) { require(!reply.error && !reply.cancelled, `Unexpected plugin failure: ${statusCode(reply)}`); }
function request(operation, input, mode = 'CALL', streamId = '') { return { operation, input: payload(input), mode, streamId }; }
async function measured(kind, action) {
  const started = Date.now();
  currentKind = kind;
  try {
    await action();
    const elapsed = Date.now() - started;
    latency.add(elapsed, tags(kind)); operations.add(1, tags(kind)); errors.add(false, tags(kind));
    windowCount++; windowMax = Math.max(windowMax, elapsed);
  } catch (error) { errors.add(true, tags(kind)); throw error; }
}

export default async function () {
  let session;
  try {
    if (!connected) { client.connect(config.address, { plaintext: true, timeout: '5s' }); connected = true; }
    const binding = config.bindings[__VU - 1];
    session = new Session(binding.Credential);
    const owner = binding.Tenant;
    await measured('echo', () => session.exchange(request('echo', { owner, text: 'Grüße 🚀 <>&', iteration: __ITER }), undefined, reply => {
      ok(reply); const value = decode(reply.json);
      require(value.owner === owner && value.text === 'Grüße 🚀 <>&' && value.iteration === __ITER, 'Echo mismatch');
      require(!config.injectFailure, 'Injected validation failure');
    }));
    await measured('callback', () => session.exchange(request('owner', { operation: 'host.owner', input: { tenant: 'forged' } }), undefined, reply => {
      ok(reply); const value = decode(reply.json);
      require(value.owner === owner && value.secret === `synthetic-${owner}`, 'Callback authority mismatch');
    }));
    const count = __ITER % 20 === 0 ? 1024 : 8;
    let received = 0;
    await measured('records', () => session.exchange(request('records', { count, width: 8192, callbacks: true }, 'STREAM', streamId(0)), rows => {
      require(Array.isArray(rows) && rows.length > 0 && rows.length <= 16, 'Invalid batch');
      for (const row of rows) {
        require(row.id === received++ && row.text === rowText && row.owner === owner, 'Row identity/content mismatch');
      }
    }, reply => { ok(reply); require(received === count, 'Incomplete stream'); }));
    // Stagger faults across tenants; each affects only this binding.
    if (__ITER % 25 === (__VU - 1) % 25) {
      await measured('failure', () => session.exchange(request('records', { count: 40, width: 32, failAt: 20 }, 'STREAM', streamId(1)), rows => {
        for (const row of rows) require(row.owner === owner, 'Fault stream tenant mismatch');
      }, reply => { require(reply.error === 'failed' && !reply.cancelled, 'Missing expected provider failure'); faults.add(1); }));
      await measured('denied', () => session.exchange(request('owner', { operation: 'forbidden', input: {} }), undefined, reply => {
        require(reply.error === 'denied', 'Missing callback denial'); faults.add(1);
      }));
      await measured('crash', () => session.exchange(request('crash', {}), undefined, reply => {
        require(reply.error === 'failed', `Missing crash failure: ${statusCode(reply)}`); faults.add(1);
      }));
      await measured('recovery', () => session.exchange(request('echo', { owner }), undefined, reply => { ok(reply); require(decode(reply.json).owner === owner, 'Recovery mismatch'); }));
      await measured('cancel', async () => {
        const id = streamId(2);
        let cancellation;
        let cancelSession;
        const active = session.exchange(request('records', { count: 10000, width: 32, delayMs: 2 }, 'STREAM', id), rows => {
          for (const row of rows) require(row.owner === owner, 'Cancel stream tenant mismatch');
          if (!cancellation) {
            cancelSession = new Session(binding.Credential);
            cancellation = cancelSession.exchange(request('', {}, 'CANCEL', id)).then(() => cancelSession.close());
            // The rejection is observed below after the active stream drains.
            cancellation.catch(() => {});
          }
        }, reply => { require(reply.cancelled, 'Missing cancellation terminal'); faults.add(1); });
        await active;
        require(cancellation, 'Cancellation not dispatched');
        await cancellation;
      });
      await measured('recovery', () => session.exchange(request('echo', { owner }), undefined, reply => { ok(reply); require(decode(reply.json).owner === owner, 'Post-cancel recovery mismatch'); }));
    }
    await session.close();
    if (__ITER % 100 === 99) { client.close(); connected = false; }
    if (Date.now() - windowStart >= 60000) {
      console.log(JSON.stringify({ window: true, tenant: owner, seconds: (Date.now() - windowStart) / 1000, completed: windowCount, maxMs: windowMax }));
      windowStart = Date.now(); windowCount = 0; windowMax = 0;
    }
    await delay(config.paceMs);
  } catch (error) {
    errors.add(true, tags('iteration'));
    // Never include credentials or plugin data in logs.
    console.error(`Unexpected workload failure kind=${currentKind} tenant-${__VU - 1} iteration ${__ITER} reason=${error.safeReason || 'transport-or-unclassified'}`);
    client.close(); connected = false;
    await delay(config.paceMs);
  }
}
export function handleSummary(data) {
  return { [__ENV.WP_SOAK_SUMMARY]: JSON.stringify(data, null, 2) };
}
