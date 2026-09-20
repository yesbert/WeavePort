import json
from pathlib import Path
import subprocess
import sys
import time

root = Path('artifacts/reuse-matrix')
phases = json.loads((root / 'final-phases.json').read_text())
def run(name, args):
    row = {'phase': name, 'start': time.time(), 'args': args}
    phases.append(row)
    (root / 'final-phases.json').write_text(json.dumps(phases, indent=2))
    with (root / (name + '.log')).open('w') as log:
        result = subprocess.run([sys.executable, *args], stdout=log, stderr=subprocess.STDOUT)
    row.update(end=time.time(), exitCode=result.returncode)
    (root / 'final-phases.json').write_text(json.dumps(phases, indent=2))
    print(name, result.returncode, flush=True)
    if result.returncode:
        raise SystemExit(result.returncode)

for name, config in [('serialized-ready-final', 'serialized-final'), ('workloads-final', 'workloads-final'), ('faults-final', 'faults-engine')]:
    run(name, ['tools/performance/reuse_matrix.py', str(root / 'configs' / (config + '.json')), str(root / name)])
for language in ['python', 'typescript', 'csharp']:
    run('production-' + language, ['tools/performance/density.py', '--output', str(root / ('production-' + language)),
        '--clients', '100,500,1000', '--seconds', '60', '--repeats', '1', '--adapters', 'docker', '--language', language,
        '--traffic', 'population', '--calls-per-minute', '1', '--memory-mib', '4096', '--pristine', '2'])
soaks = []
for mode in ['trusted', 'forkserver']:
    bracket = json.loads((root / ('capacity-' + mode) / 'bracket.json').read_text())
    if not bracket['qualifiedLow']:
        raise RuntimeError('Choose a passing capacity bracket before soak: ' + mode)
    rate = max(50, int(bracket['qualifiedLow'] * .75 / 50) * 50)
    soaks.append(dict(mode=mode, workers=16, cpus='1', transport='engine', seconds=1800, arrival='poisson', rate=rate, seed=1731))
(root / 'configs' / 'soaks-final.json').write_text(json.dumps(soaks, indent=2))
run('soaks-final', ['tools/performance/reuse_matrix.py', str(root / 'configs' / 'soaks-final.json'), str(root / 'soaks-final')])
