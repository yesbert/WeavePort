"""Local process ownership, provenance and bounded resource observations."""
import hashlib
import json
import math
import os
from pathlib import Path
import selectors
import shutil
import signal
import subprocess
import time
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify_package(feed, name, checksum):
    package = feed / f'{name}.0.2.1.nupkg'
    with zipfile.ZipFile(package) as archive:
        actual = hashlib.sha256(archive.read(f'lib/net10.0/{name}.dll')).hexdigest()
    if actual != checksum:
        raise RuntimeError('Loaded/package identity mismatch: ' + name)


def read_line(process, timeout=10):
    with selectors.DefaultSelector() as selector:
        selector.register(process.stdout, selectors.EVENT_READ)
        if not selector.select(timeout):
            raise TimeoutError('Gateway control response deadline')
    line = process.stdout.readline()
    if not line:
        raise RuntimeError('Gateway control channel closed')
    return json.loads(line)


def process_table():
    output = subprocess.check_output(['ps', '-axo', 'pid=,ppid=,pgid=,rss=,%cpu='], text=True, timeout=5)
    return [dict(zip(('pid', 'parent', 'group', 'rssKiB', 'cpuPercent'),
                     (int(parts[0]), int(parts[1]), int(parts[2]), int(parts[3]), float(parts[4]))))
            for line in output.splitlines() if len(parts := line.split()) == 5]


def descriptor_counts(pids):
    counts = {pid: None for pid in pids}
    if Path('/proc/self/fd').exists():
        for pid in pids:
            try:
                counts[pid] = len(list(Path(f'/proc/{pid}/fd').iterdir()))
            except (FileNotFoundError, PermissionError):
                pass
    elif shutil.which('lsof'):
        result = subprocess.run(['lsof', '-nP', '-a', '-p', ','.join(map(str, pids)), '-Fpf'],
                                capture_output=True, text=True, timeout=5)
        current = None
        for line in result.stdout.splitlines():
            if line.startswith('p') and line[1:].isdigit():
                current = int(line[1:])
                if current in counts: counts[current] = 0
            elif current in counts and line.startswith('f') and line[1:].isdigit():
                counts[current] += 1
    return counts


def sample(gateway, k6):
    gateway.stdin.write('diagnostics\n')
    gateway.stdin.flush()
    state = read_line(gateway)
    rows = process_table()
    return {'monotonic': time.monotonic(), 'gateway': state,
            'rootDescriptors': descriptor_counts([gateway.pid, k6.pid]),
            'processes': [row for row in rows if row['group'] in (gateway.pid, k6.pid)]}


def stop(process, graceful=None):
    if process is None:
        return False
    forced = False
    if process.poll() is None:
        try:
            if graceful:
                process.stdin.write(graceful + '\n')
                process.stdin.flush()
            else:
                process.send_signal(signal.SIGINT)
            process.wait(timeout=20)
        except (subprocess.TimeoutExpired, BrokenPipeError):
            forced = True
    # Restrict termination to the new session created for this fixture, even if
    # its root exited while children remained. Never use machine-wide process names.
    def remaining():
        return any(row['group'] == process.pid for row in process_table())
    if remaining():
        forced = True
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        deadline = time.monotonic() + 5
        while remaining() and time.monotonic() < deadline:
            time.sleep(0.05)
        if remaining():
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        process.wait(timeout=5)
    return forced


def providers(output, clients, dotnet, node, maximum_concurrent_starts=None):
    definitions = [
        {'Language': 'csharp', 'Executable': dotnet, 'Arguments': [str(ROOT / 'artifacts/sdk-csharp/ExamplePlugin.dll')]},
        {'Language': 'python', 'Executable': str(ROOT / 'artifacts/sdk-python/bin/python'), 'Arguments': [str(ROOT / 'artifacts/sdk-python-example/plugin.py')]},
        {'Language': 'typescript', 'Executable': node, 'Arguments': [str(ROOT / 'examples/sdk/typescript/dist/plugin.js')]},
    ]
    python_sdk = next((ROOT / 'artifacts/sdk-python/lib').glob('python*/site-packages/weaveport_sdk/__init__.py'))
    paths = [Path(p['Arguments'][0]) for p in definitions] + [python_sdk,
        ROOT / 'artifacts/sdk-csharp/WeavePort.Sdk.dll',
        ROOT / 'examples/sdk/typescript/node_modules/@weaveport/sdk/dist/index.js']
    return {'Providers': definitions,
            'Bindings': [{'Tenant': f'tenant-{i}', 'Language': definitions[i % 3]['Language']} for i in range(clients)],
            'Workspace': str(output / 'workers'), 'Socket': False,
            'MaximumConcurrentStarts': startup_limit(clients, maximum_concurrent_starts),
            'Artifacts': {str(path): digest(path) for path in paths}}


def summarize_resources(path):
    count = 0
    peak = 0
    first = last = None
    with path.open() as source:
        for line in source:
            item = json.loads(line)
            count += 1
            peak = max(peak, sum(p['rssKiB'] for p in item['processes']) / 1024)
            if first is None:
                first = item
            last = item
    return {'samples': count, 'peakSummedRssMiB': peak, 'first': first, 'last': last}


def cleanup_uncertain(snapshot):
    """Fail closed on invalid diagnostics; overlapping short removals are healthy."""
    age = snapshot.get('OldestQuarantineSeconds')
    count = snapshot.get('Quarantined')
    if (type(count) is not int or count < 0 or type(age) not in (int, float)
            or not math.isfinite(age) or age < 0 or (count == 0 and age != 0)
            or 'MaintenanceFailure' not in snapshot):
        return True
    return snapshot['MaintenanceFailure'] is not None or age > 30


def startup_limit(clients, requested=None):
    """Match the no-overload fixture to its VUs; production defaults stay unchanged."""
    value = clients if requested is None else requested
    if type(value) is not int or not 1 <= value <= 48:
        raise ValueError('Concurrent startup limit must be between 1 and 48')
    return value
