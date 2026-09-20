"""Counterbalanced follow-up for the near-threshold 256-customer result."""
import json
from pathlib import Path
import subprocess
import sys
import time

sys.path.insert(0, 'tools/performance')
import docker_memory

root = Path('artifacts/reuse-matrix')
for budget, seed in [(8192, 1731), (4096, 1731), (4096, 1732), (8192, 1732)]:
    name = f'production-python-confirm-{budget}-{seed}'
    engine = docker_memory.Engine()
    baseline = docker_memory.snapshot(engine, set())
    total = engine.get('/info')['MemTotal'] / 1048576
    available = total - baseline['allContainersRawMiB'] - 2048
    (root/(name+'-headroom.json')).write_text(json.dumps({
        'dockerTotalMiB': total, 'backgroundRawMiB': baseline['allContainersRawMiB'],
        'marginMiB': 2048, 'availableForReservationMiB': available, 'reservationMiB': budget}, indent=2))
    if budget > available:
        raise RuntimeError('Population confirmation exceeds observed Docker headroom')
    args = [sys.executable, 'tools/performance/density.py', '--output', str(root/name),
            '--clients', '256', '--seconds', '120', '--repeats', '1', '--seed', str(seed),
            '--adapters', 'docker', '--language', 'python', '--traffic', 'population',
            '--calls-per-minute', '1', '--memory-mib', str(budget), '--pristine', '2',
            '--max-owned-rss-mib', '6144']
    phases = json.loads((root/'final-phases.json').read_text())
    row = {'phase': name, 'start': time.time(), 'args': args}
    phases.append(row)
    (root/'final-phases.json').write_text(json.dumps(phases, indent=2))
    with (root/(name+'.log')).open('w') as log:
        result = subprocess.run(args, stdout=log, stderr=subprocess.STDOUT)
    row.update(end=time.time(), exitCode=result.returncode)
    (root/'final-phases.json').write_text(json.dumps(phases, indent=2))
    print(name, result.returncode, flush=True)
    if result.returncode:
        raise SystemExit(result.returncode)
