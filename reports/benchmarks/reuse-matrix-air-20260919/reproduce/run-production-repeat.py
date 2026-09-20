"""Sequential two-period customer populations, after prototype soak completion."""
import json
from pathlib import Path
import subprocess
import sys
import time

sys.path.insert(0, 'tools/performance')
import docker_memory

root = Path('artifacts/reuse-matrix')
for budget in [4096, 8192]:
    # The two budgets are separate admission policies, not hard-coded worker counts.
    name = 'production-python-repeat-' + str(budget)
    engine = docker_memory.Engine()
    baseline = docker_memory.snapshot(engine, set())
    total = engine.get('/info')['MemTotal'] / 1048576
    available = total - baseline['allContainersRawMiB'] - 2048
    preflight = {'dockerTotalMiB': total, 'backgroundRawMiB': baseline['allContainersRawMiB'],
                 'marginMiB': 2048, 'availableForReservationMiB': available, 'reservationMiB': budget}
    (root/(name+'-headroom.json')).write_text(json.dumps(preflight, indent=2))
    if budget > available:
        raise RuntimeError('Configured population probe exceeds observed Docker headroom')
    args = [sys.executable, 'tools/performance/density.py', '--output', str(root/name),
            '--clients', '64,128,256,512', '--seconds', '120', '--repeats', '2',
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
