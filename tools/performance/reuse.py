"""Experimental customer/plugin turnover. No production cross-tenant reuse is enabled."""
import argparse
import asyncio
import hashlib
import json
import math
import os
from pathlib import Path
import random
import subprocess
import sys
import time
import uuid
import docker_memory
from density import pressure

ROOT = Path(__file__).resolve().parents[2]
IMAGE = 'weaveport-reuse-experiment:1'


def write(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n')


async def command(*args, timeout=15):
    proc = await asyncio.create_subprocess_exec(*args, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
    try:
        out, err = await asyncio.wait_for(proc.communicate(), timeout)
    except BaseException:
        if proc.returncode is None:
            proc.kill()
        await proc.wait()
        raise
    if proc.returncode:
        raise RuntimeError(err.decode()[-1000:])
    return out.decode().strip()


class Lane:
    def __init__(self, mode, known):
        self.mode, self.known = mode, known
        self.process, self.name = None, None
        self.starts, self.retirements = 0, 0
        self.lock = asyncio.Lock()

    async def start(self):
        self.name = 'wp-reuse-' + uuid.uuid4().hex
        self.known.add(self.name)
        self.process = await asyncio.create_subprocess_exec('docker', 'run', '--name', self.name,
            '--label', 'weaveport.reuse-experiment=true', '--init', '--interactive', '--network', 'none',
            '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--user', '65532:65532',
            '--memory', '64m', '--memory-swap', '64m', '--cpus', '.5', '--pids-limit', '64',
            '--tmpfs', '/tmp:rw,noexec,nosuid,size=16m,mode=1777', '--shm-size', '8m', '--log-driver', 'none',
            IMAGE, 'process' if self.mode == 'process' else 'trusted',
            stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL, limit=1048576)
        ready = json.loads(await asyncio.wait_for(self.process.stdout.readline(), 5))
        if not ready.get('ready'):
            raise RuntimeError('Missing ready handshake')
        self.starts += 1

    async def close(self):
        if self.name is None:
            return
        name, process = self.name, self.process
        # Keep identity until the daemon confirms removal. No broad label/prefix deletion.
        await command('docker', 'rm', '--force', name)
        self.name, self.process = None, None
        self.retirements += 1
        if process is not None:
            await asyncio.wait_for(process.wait(), 5)

    async def call(self, request, intended=None):
        intended = intended or time.perf_counter()
        async with self.lock:
            admitted = time.perf_counter()
            startup = retirement = 0.0
            outcome = {'status': 'failed', 'reusable': False}
            try:
                if self.process is None:
                    began = time.perf_counter()
                    await self.start()
                    startup = (time.perf_counter() - began) * 1000
                name = self.name
                self.process.stdin.write((json.dumps(request, separators=(',', ':')) + '\n').encode())
                await self.process.stdin.drain()
                outcome = json.loads(await asyncio.wait_for(self.process.stdout.readline(), 2.5))
                if outcome.get('id') != request['id']:
                    raise RuntimeError('Mismatched response identity')
            except Exception as error:
                name = self.name
                outcome = {'status': 'failed', 'reusable': False, 'error': type(error).__name__}
            response = time.perf_counter()
            if self.mode == 'fresh' or not outcome.get('reusable'):
                began = time.perf_counter()
                await self.close()
                retirement = (time.perf_counter() - began) * 1000
            return {'request': request, 'container': name, 'outcome': outcome,
                    'queueMs': (admitted - intended) * 1000, 'startupMs': startup, 'retirementMs': retirement,
                    'responseMs': (response - intended) * 1000, 'turnoverMs': (time.perf_counter() - intended) * 1000}


def request(number, lanes=1, op='echo', **extras):
    return {'id': str(number), 'tenant': 'customer-' + str(number),
            'plugin': 'a' if (number // lanes) % 2 == 0 else 'b', 'payload': 'Abc123xy' * 8,
            'grants': ['read'], 'op': op, **extras}


def correct(row):
    r, outcome = row['request'], row['outcome']
    expected = r['payload'].upper() if r['plugin'] == 'a' else r['payload'][::-1]
    return outcome.get('status') == 'ok' and outcome.get('reusable') and outcome.get('value') == {
        'tenant': r['tenant'], 'plugin': r['plugin'], 'value': expected}


def quantiles(values):
    if not values:
        return None
    ordered = sorted(values)
    return {'count': len(values), 'mean': sum(values) / len(values),
            **{key: ordered[max(0, math.ceil(len(values) * q) - 1)] for key, q in [('p50', .5), ('p95', .95), ('p99', .99)]},
            'max': ordered[-1]}


async def isolation(output):
    evidence = []
    known = set()
    for mode in ('fresh', 'process', 'trusted'):
        lane = Lane(mode, known)
        rows = []
        checks = []
        async def call(op='echo', **extras):
            row = await lane.call(request(len(rows), op=op, **extras))
            rows.append(row)
            return row
        def check(name, condition):
            checks.append({'name': name, 'passed': bool(condition)})
        try:
            for _ in range(4):
                check('alternating customer and plugin identity', correct(await call()))
            managed = await call('managed')
            check('managed resource call succeeds', managed['outcome']['status'] == 'ok')
            probe = (await call('probe', oldPid=managed['outcome'].get('value', {}).get('pid', -1)))['outcome']['value']
            check('managed file/environment/child removed', not probe['files'] and probe['environment'] is None and not probe['oldPidAlive'])
            a = (await call('authority'))['outcome']['value']
            check('grant and expiry enforced by scope', a['denied'] and a['expired'] and a['granted'].startswith('customer-'))
            await call('unmanaged_file')
            check('both writable directories swept', not (await call('probe'))['outcome']['value']['files'])
            for fault in ('cleanup_failure', 'untracked_thread', 'escaped_child', 'crash', 'hang'):
                failed = await call(fault)
                next_call = await call()
                check(fault + ' poisons and replaces container', not failed['outcome']['reusable'] and correct(next_call)
                      and next_call['container'] != failed['container'])
            hidden = await call('hidden')
            revealed = (await call('probe'))['outcome']['value']['hidden']
            # This intentionally demonstrates a limitation, not a claim that the trusted mode is secure.
            check('hidden-global negative control has expected outcome',
                  revealed == hidden['request']['tenant'] if mode == 'trusted' else revealed is None)
            evidence.append({'mode': mode, 'checks': checks, 'hiddenGlobalCrossTenantLeak': revealed is not None,
                             'securityBoundary': 'fresh container lifecycle' if mode == 'fresh' else 'cooperative prototype, not hostile-code isolation',
                             'rows': rows})
        finally:
            await lane.close()
    write(output / 'isolation.json', evidence)
    if not all(check['passed'] for group in evidence for check in group['checks']):
        raise RuntimeError('Isolation control did not match expected behavior')
    print('Isolation controls passed; trusted hidden-global leak deliberately reproduced.', flush=True)


async def monitor(engine, known, stop, samples, baseline):
    while not stop.is_set():
        snapshot = await asyncio.to_thread(docker_memory.snapshot, engine, known.copy())
        headroom = await asyncio.to_thread(pressure)
        ps = await command('/bin/ps', '-axo', 'pid=,ppid=,rss=')
        rows = [tuple(map(int, line.split())) for line in ps.splitlines() if line.strip()]
        host = next(rss / 1024 for pid, ppid, rss in rows if pid == os.getpid())
        children = sum(rss / 1024 for pid, ppid, rss in rows if ppid == os.getpid())
        samples.append({'at': time.time(), 'ownedMiB': snapshot['ownedMiB'], 'ownedRawMiB': snapshot['ownedRawMiB'],
                        'containers': len(snapshot['owned']), 'snapshotSeconds': snapshot['durationSeconds'],
                        'hostRssMiB': host, 'childRssMiB': children, 'pressure': headroom})
        if headroom['freePercent'] < 10 or headroom['swapUsedMiB'] is None or headroom['swapUsedMiB'] - baseline['swapUsedMiB'] > 512:
            raise RuntimeError('System headroom guard')
        try:
            await asyncio.wait_for(stop.wait(), 1)
        except asyncio.TimeoutError:
            pass


async def measure(output, mode, kind, engine, count=500, seconds=60, seed=1729, repeat=1):
    folder = output / f'{kind}-{mode}-{repeat}'
    folder.mkdir()
    known, rows, samples = set(), [], []
    lanes = [Lane(mode, known) for _ in range(4 if kind == 'population' else 1)]
    stop = asyncio.Event()
    observer = asyncio.create_task(monitor(engine, known, stop, samples, pressure()))
    failure, pending = None, []
    prepared = 0.0
    start = time.perf_counter()
    try:
        # Ready reusable containers belong to the infrastructure pool; record preparation separately.
        if mode != 'fresh':
            await asyncio.gather(*(lane.start() for lane in lanes))
        prepared = time.perf_counter() - start
        start = time.perf_counter()
        async def invoke(i, due):
            row = await lanes[i % len(lanes)].call(request(i, len(lanes)), start + due)
            rows.append(row)
        if kind == 'turnover':
            i = 0
            while i < 200 or time.perf_counter() - start < 10:
                if observer.done():
                    await observer
                if (output / 'STOP').exists():
                    raise RuntimeError('Operator stop')
                await invoke(i, time.perf_counter() - start)
                i += 1
                if i >= 50000:
                    break
        else:
            randomizer = random.Random(seed)
            schedule = sorted(randomizer.random() * seconds for _ in range(count))
            for i, due in enumerate(schedule):
                await asyncio.sleep(max(0, start + due - time.perf_counter()))
                if observer.done():
                    await observer
                if (output / 'STOP').exists():
                    raise RuntimeError('Operator stop')
                pending.append(asyncio.create_task(invoke(i, due)))
            await asyncio.sleep(max(0, start + seconds - time.perf_counter()))
            await asyncio.gather(*pending)
        elapsed = time.perf_counter() - start
    except Exception as error:
        failure = type(error).__name__ + ': ' + str(error)
        elapsed = time.perf_counter() - start
        await asyncio.gather(*pending, return_exceptions=True)
    finally:
        cleanup_start = time.perf_counter()
        cleanup_results = await asyncio.gather(*(lane.close() for lane in lanes), return_exceptions=True)
        for error in cleanup_results:
            if isinstance(error, BaseException):
                failure = failure or 'cleanup-' + type(error).__name__ + ': ' + str(error)
        cleanup_seconds = time.perf_counter() - cleanup_start
        stop.set()
        try:
            await observer
        except Exception as error:
            failure = failure or 'observer-' + type(error).__name__ + ': ' + str(error)
    remaining = [name for name in (await command('docker', 'ps', '-a', '--format', '{{.Names}}')).splitlines() if name in known]
    success = sum(correct(row) for row in rows)
    result = {'mode': mode, 'kind': kind, 'repeat': repeat, 'seed': seed, 'calls': len(rows), 'uniqueCustomers': len({r['request']['tenant'] for r in rows}),
              'success': success, 'failure': failure, 'passed': failure is None and success == len(rows) and len(rows) > 0 and not remaining,
              'seconds': elapsed, 'rps': success / elapsed, 'preparationSeconds': prepared, 'cleanupSeconds': cleanup_seconds,
              'starts': sum(lane.starts for lane in lanes), 'retirements': sum(lane.retirements for lane in lanes), 'remaining': remaining,
              'responseMs': quantiles([r['responseMs'] for r in rows]), 'turnoverMs': quantiles([r['turnoverMs'] for r in rows]),
              'queueMs': quantiles([r['queueMs'] for r in rows]), 'startupMs': quantiles([r['startupMs'] for r in rows if r['startupMs']]),
              'sdkCleanupMs': quantiles([r['outcome'].get('cleanupMs', 0) for r in rows]),
              'peakContainerMiB': max((s['ownedMiB'] for s in samples), default=None),
              'peakHostRssMiB': max((s['hostRssMiB'] for s in samples), default=None),
              'peakChildRssMiB': max((s['childRssMiB'] for s in samples), default=None),
              'scope': 'Experimental Python supervisor/SDK scope and Docker stdio, not the released .NET host or SDK. Fresh lifecycle retirement included in turnover.'}
    result['meetsOneSecondP99'] = result['passed'] and result['responseMs']['p99'] <= 1000
    write(folder / 'result.json', result)
    write(folder / 'resources.json', samples)
    with (folder / 'calls.jsonl').open('w') as stream:
        for row in rows:
            stream.write(json.dumps(row) + '\n')
    print(json.dumps(result), flush=True)
    if not result['passed']:
        raise RuntimeError('Failed experiment; inspect retained results')
    return result


async def main():
    global IMAGE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    parser.add_argument('--phase', choices=['isolation', 'turnover', 'population', 'all'], default='all')
    parser.add_argument('--modes', default='fresh,process,trusted')
    parser.add_argument('--repeats', type=int, default=2)
    parser.add_argument('--clients', type=int, default=500, help='Unique one-call customers during the 60-second population replay')
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    modes = args.modes.split(',')
    if any(m not in ('fresh','process','trusted') for m in modes) or not 1 <= args.repeats <= 3 or not 1 <= args.clients <= 10000 or ('fresh' in modes and args.clients > 1000):
        raise ValueError('Experiment bounds')
    identity = {'at': time.time(), 'machine': await command('/usr/sbin/sysctl','-n','hw.model','hw.memsize','hw.ncpu','machdep.cpu.brand_string'),
                'docker': json.loads(await command('docker','info','--format','{{json .}}')),
                'image': json.loads(await command('docker','image','inspect',IMAGE,'--format','{{json .Id}}')),
                'sourceHashes': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                 for p in [Path(__file__), Path(docker_memory.__file__), *sorted((ROOT/'benchmarks/WeavePort.Reuse').rglob('*.py'))]},
                'headroom': pressure()}
    identity['docker'] = {k: identity['docker'][k] for k in ('MemTotal','NCPU','ServerVersion','Architecture')}
    IMAGE = identity['image']  # Pin every created container to the inspected local artifact.
    write(output / 'identity.json', identity)
    engine = docker_memory.Engine()
    if args.phase in ('all','isolation'):
        await isolation(output)
    results = []
    for kind in ('turnover', 'population'):
        if args.phase not in ('all', kind):
            continue
        for repeat in range(args.repeats):
            for mode in (modes if repeat % 2 == 0 else list(reversed(modes))):
                results.append(await measure(output, mode, kind, engine, count=args.clients, seed=1729 + repeat, repeat=repeat+1))
                write(output / 'summary.json', results)


if __name__ == '__main__':
    asyncio.run(main())
