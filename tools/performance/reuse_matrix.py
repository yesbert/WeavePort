"""Bounded many-customer benchmark; experimental transport, not production server capacity."""
import argparse
import asyncio
import hashlib
import json
import os
from pathlib import Path
import random
import time
import docker_memory
from density import identities, pressure
from matrix_lane import MatrixLane
from matrix_engine import EngineLane
from matrix_metrics import Histogram
from matrix_queue import SerialReadyQueue, grouped_capacity
from matrix_resources import ResourceTotals
from matrix_policy import PolicyPool
from reuse import command, correct, request, write

ROOT = Path(__file__).resolve().parents[2]


async def measure(folder, config, image, engine):
    folder.mkdir()
    write(folder / 'config.json', config)
    known, failures = set(), []
    resources = ResourceTotals()
    lane_type = EngineLane if config.get('transport') == 'engine' else MatrixLane
    extra = {'engine': engine} if lane_type is EngineLane else {}
    lanes = [lane_type(config['mode'], known, image, config.get('cpus', '.5'), config.get('memory', 128), **extra)
             for _ in range(config['workers'])]
    policy_pool = PolicyPool(lanes, config['policy']) if config.get('policy') else None
    hist = {key: Histogram() for key in ('responseMs', 'turnoverMs', 'queueMs', 'executionMs', 'cleanupMs', 'generatorLagMs', 'pluginImportMs', 'childWorkMs',
                    'childStartMs', 'childResponseWaitMs', 'childExitWaitMs', 'processTurnoverMs')}
    counts = {'offered': 0, 'completed': 0, 'correct': 0, 'failed': 0, 'dropped': 0, 'withinOneSecond': 0, 'customersCompleted': 0, 'faultsExpected': 0, 'faultsContained': 0}
    classes, customer_calls = {}, {}
    window, window_short = Histogram(), Histogram()
    calls_per_customer = config.get('callsPerCustomer', 1)
    if not 1 <= calls_per_customer <= 100:
        raise ValueError('Bounded positive calls-per-customer required')
    plugins_per_customer = config.get('pluginsPerCustomer', 2)
    if plugins_per_customer not in (1, 2):
        raise ValueError('Fixture offers one or two plugin modules per customer')
    stop = asyncio.Event()
    baseline = pressure()
    initial_cpu = time.process_time()
    start, prepared, observer, workers, failure = time.perf_counter(), 0, None, [], None
    payload = ('Abc123xy' * ((config.get('payload', 64) + 7) // 8))[:config.get('payload', 64)]
    grouped_saturation = config.get('arrival', 'saturated') == 'saturated' and calls_per_customer > 1
    pending_limit = grouped_capacity(config['workers'], calls_per_customer) if grouped_saturation else config.get('maxPending', 10000)
    population = config.get('population')
    def customer_key(i):
        customer = i % population if population else i // calls_per_customer
        plugin = (i // population if config.get('switchPlugins') else customer) % plugins_per_customer if population else i % plugins_per_customer
        return customer, plugin
    queue = SerialReadyQueue(pending_limit, lambda item: customer_key(item[0]))
    seconds = config.get('seconds', 15)
    deadline_ms = config.get('queueDeadlineMs', 1000)
    def choose(i):
        op = config.get('workload', 'echo')
        if config.get('faultEvery') and i % config['faultEvery'] == 0:
            op = ('hang', 'crash', 'cleanup_failure')[i // config['faultEvery'] % 3]
        if op == 'blend':
            op = 'io' if i % 100 == 0 else 'cpu' if i % 10 == 0 else 'echo'
        customer, plugin = customer_key(i)
        return request(i, op=op, plugin='a' if plugin == 0 else 'b', tenant='customer-' + str(customer), payload=payload, ioMs=config.get('ioMs', 100), cpuMs=config.get('cpuMs', 2),
                       memoryMiB=config.get('memoryMiB', 8))
    def consume(row):
        counts['completed'] += 1
        valid = correct(row)
        expected_fault = row['request']['op'] in ('hang', 'crash', 'cleanup_failure')
        contained = expected_fault and row['outcome'].get('status') == 'failed' and not row['outcome'].get('reusable')
        counts['faultsExpected'] += int(expected_fault)
        counts['faultsContained'] += int(contained)
        counts['correct'] += int(valid)
        counts['failed'] += int(not valid and not contained)
        if valid:
            customer = row['request']['tenant']
            customer_calls[customer] = customer_calls.get(customer, 0) + 1
            if customer_calls[customer] == calls_per_customer:
                counts['customersCompleted'] += 1
                del customer_calls[customer]
        counts['withinOneSecond'] += int(valid and row['responseMs'] <= 1000)
        op = row['request']['op']
        window.add(row['responseMs'])
        if op == 'echo':
            window_short.add(row['responseMs'])
        classes.setdefault(op, Histogram()).add(row['responseMs'])
        for key in hist:
            if key in row:
                hist[key].add(row[key])
            elif key in row['outcome']:
                hist[key].add(row['outcome'][key])
        if not valid and not contained and len(failures) < 20:
            failures.append({k: v for k, v in row.items() if k != 'request'} | {'id': row['request']['id'], 'op': op})
    async def sample():
        nonlocal window, window_short
        while not stop.is_set():
            memory = await asyncio.to_thread(docker_memory.snapshot, engine, known.copy())
            headroom = await asyncio.to_thread(pressure)
            ps = await command('/bin/ps', '-axo', 'pid=,ppid=,rss=')
            processes = [tuple(map(int, line.split())) for line in ps.splitlines() if line.strip()]
            helper_pids = {getattr(lane.process, 'pid', None) for lane in lanes}
            sample = {'hostRssMiB': next(rss / 1024 for pid, ppid, rss in processes if pid == os.getpid()),
                      'helpersRssMiB': sum(rss / 1024 for pid, ppid, rss in processes if pid in helper_pids), 'at': time.time(), 'elapsed': time.perf_counter() - start,
                      'ownedMiB': memory['ownedMiB'], 'ownedRawMiB': memory['ownedRawMiB'],
                      'owned': memory['owned'], 'pressure': headroom, 'counts': counts.copy(),
                      'windowResponseMs': window.report(), 'windowEchoResponseMs': window_short.report(),
                      'hostCpuSeconds': time.process_time() - initial_cpu}
            window, window_short = Histogram(), Histogram()
            resources.add(sample)
            with (folder / 'samples.jsonl').open('a') as stream:
                stream.write(json.dumps(sample) + '\n')
            if headroom['freePercent'] < 10 or headroom['swapUsedMiB'] is None or headroom['swapUsedMiB'] - baseline['swapUsedMiB'] > 512:
                raise RuntimeError('Memory pressure guard')
            if (folder.parent / 'STOP').exists():
                raise RuntimeError('Operator stop')
            try:
                await asyncio.wait_for(stop.wait(), 3)
            except asyncio.TimeoutError:
                pass
    async def saturated(lane):
        while time.perf_counter() < start + seconds:
            if observer.done():
                await observer
            i = counts['offered']
            counts['offered'] += 1
            consume(await (policy_pool or lane).call(choose(i)))
            if counts['failed'] > 3:
                raise RuntimeError('Repeated correctness failure')
    async def queued(lane):
        while True:
            key, item = await queue.get()
            try:
                if item is None:
                    return
                i, due = item
                if (time.perf_counter() - due) * 1000 > deadline_ms:
                    counts['dropped'] += 1
                else:
                    consume(await (policy_pool or lane).call(choose(i), due))
            finally:
                queue.task_done(key)
    try:
        if baseline['freePercent'] < 10 or baseline['swapUsedMiB'] is None:
            raise RuntimeError('Insufficient initial host memory headroom')
        if config['workers'] > 64:
            info = await asyncio.to_thread(engine.get, '/info')
            background = await asyncio.to_thread(docker_memory.snapshot, engine, set())
            available = info['MemTotal'] / 1048576 - background['allContainersRawMiB'] - 2048
            reserved = config['workers'] * config.get('memory', 128)
            write(folder / 'headroom.json', {'dockerMiB': info['MemTotal'] / 1048576,
                  'backgroundRawMiB': background['allContainersRawMiB'], 'additionalReserveMiB': 2048,
                  'availableForPoolMiB': available, 'requestedPoolReservationMiB': reserved})
            if reserved > available:
                raise RuntimeError('Insufficient measured Docker headroom for expanded pool')
        if config['mode'] != 'fresh':
            # Do not turn pool preparation into a simultaneous Docker launch storm.
            for begin in range(0, len(lanes), 4):
                prepared_batch = await asyncio.gather(*(lane.start() for lane in lanes[begin:begin + 4]), return_exceptions=True)
                for error in prepared_batch:
                    if isinstance(error, BaseException):
                        raise error
        prepared = time.perf_counter() - start
        start, initial_cpu = time.perf_counter(), time.process_time()
        observer = asyncio.create_task(sample())
        if config.get('arrival', 'saturated') == 'saturated' and not grouped_saturation:
            workers = [asyncio.create_task(saturated(lane)) for lane in lanes]
            await asyncio.gather(*workers)
        else:
            workers = [asyncio.create_task(queued(lane)) for lane in lanes]
            rng, due, i = random.Random(config.get('seed', 1729)), 0.0, 0
            rate = config.get('rate', 1)
            phases = sorted(rng.random() * config['periodSeconds'] for _ in range(population)) if population else None
            if phases:
                due = phases[0]
            while grouped_saturation and (time.perf_counter() < start + seconds or i % calls_per_customer) or not grouped_saturation and due < seconds:
                if grouped_saturation:
                    if observer.done():
                        await observer
                    await queue.put((i, time.perf_counter()))
                    counts['offered'] += 1
                    i += 1
                    continue
                await asyncio.sleep(max(0, start + due - time.perf_counter()))
                if observer.done():
                    await observer
                hist['generatorLagMs'].add((time.perf_counter() - start - due) * 1000)
                counts['offered'] += 1
                try:
                    queue.put_nowait((i, start + due))
                except asyncio.QueueFull:
                    counts['dropped'] += 1
                i += 1
                if phases:
                    due = (i // population) * config['periodSeconds'] + phases[i % population]
                else:
                    due = (i // max(1, int(rate))) if config['arrival'] == 'burst' else due + rng.expovariate(rate)
            await asyncio.sleep(max(0, start + seconds - time.perf_counter()))
            await queue.join()
            for _ in workers:
                await queue.put(None)
            await asyncio.gather(*workers)
        elapsed = time.perf_counter() - start
        if observer.done():
            await observer
    except BaseException as error:
        failure, elapsed = repr(error), time.perf_counter() - start
        for task in workers:
            task.cancel()
        await asyncio.gather(*workers, return_exceptions=True)
    finally:
        stop.set()
        if observer:
            try:
                await observer
            except Exception as error:
                failure = failure or repr(error)
        cleanup = await asyncio.gather(*(lane.close() for lane in lanes), return_exceptions=True)
        for error in cleanup:
            if isinstance(error, BaseException):
                failure = failure or 'cleanup: ' + repr(error)
    remaining = set((await command('docker', 'ps', '-a', '--format', '{{.Names}}')).splitlines()) & known
    if remaining:
        # A timed-out Docker create may finish after the first delete. Reconcile exact owned names.
        for name in sorted(remaining):
            await command('docker', 'rm', '--force', name)
        remaining = set((await command('docker', 'ps', '-a', '--format', '{{.Names}}')).splitlines()) & known
        if remaining:
            failure = failure or 'Owned containers remain'
    result = {'config': config, 'serialization': 'one in-flight call per customer/plugin key', 'image': image, 'counts': counts, 'partiallyServedCustomers': len(customer_calls),
              'offeredCustomers': population or (counts['offered'] + calls_per_customer - 1) // calls_per_customer, 'elapsed': elapsed, 'preparedSeconds': prepared,
              'customersPerSecond': counts['customersCompleted'] / elapsed, 'requestsPerSecond': counts['correct'] / elapsed, 'hostCpuCores': (time.process_time() - initial_cpu) / elapsed,
              'latency': {k: v.report() for k, v in hist.items()}, 'byWorkload': {k: v.report() for k, v in classes.items()},
              'memorySamplesWithLiveContainers': resources.live_samples,
              'memoryPeakMiB': resources.working_peak,
              'helperScope': 'RSS of live owned Docker CLI PIDs only; observer children excluded',
              'hostRssPeakMiB': resources.host_peak,
              'helpersRssPeakMiB': resources.helpers_peak,
              'memoryRawPeakMiB': resources.raw_peak,
              'starts': sum(lane.starts for lane in lanes), 'retirements': sum(lane.retirements for lane in lanes),
              'failure': failure, 'failedExamples': failures, 'known': sorted(known), 'remaining': sorted(remaining)}
    if policy_pool:
        result['poolPolicy'] = policy_pool.report()
    result['passed'] = not failure and not counts['failed'] and counts['completed'] + counts['dropped'] == counts['offered']
    result['meetsOneSecondSlo'] = result['passed'] and counts['correct'] == counts['offered'] and result['latency']['responseMs'].get('p99', float('inf')) <= 1000
    result['normalEchoP99WithinOneSecond'] = not counts['failed'] and result['byWorkload'].get('echo', {}).get('p99', float('inf')) <= 1000
    write(folder / 'summary.json', result)
    print(folder.name, counts, 'customers/s', round(result['customersPerSecond'], 2), 'p99',
          round(result['latency']['responseMs'].get('p99', 0), 2), 'MiB', result['memoryPeakMiB'], 'failure', failure, flush=True)
    if failure or counts['failed']:
        raise RuntimeError('Stage failed; retained at ' + str(folder))
    return result


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('config', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True)
    identity = identities(args.output)
    image = await command('docker', 'image', 'inspect', os.environ.get('WEAVEPORT_MATRIX_IMAGE', 'weaveport-reuse-matrix:1'), '--format', '{{.Id}}')
    sources = list((ROOT / 'benchmarks/WeavePort.Reuse').rglob('*.py')) + list((ROOT / 'tools/performance').glob('*matrix*.py')) + [ROOT / 'benchmarks/WeavePort.Reuse/Matrix.Dockerfile']
    identity.update({'image': image, 'matrixHashes': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}})
    write(args.output / 'identity.json', identity)
    configs, results = json.loads(args.config.read_text()), []
    engine = docker_memory.Engine()
    for i, config in enumerate(configs):
        if not 1 <= config['workers'] <= (128 if config.get('transport') == 'engine' else 32) or not 0 < config.get('seconds', 15) <= 3600:
            raise ValueError('Experimental resource guard exceeded')
        if config['workers'] * config.get('memory', 128) > 8192:
            raise ValueError('Experimental pool memory guard exceeded')
        results.append(await measure(args.output / f'{i:03d}-{config["mode"]}-{config["workers"]}', config, image, engine))
        write(args.output / 'summary.json', results)


if __name__ == '__main__':
    asyncio.run(main())
