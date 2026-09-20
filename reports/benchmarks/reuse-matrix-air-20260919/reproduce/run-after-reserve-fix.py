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

for size in [4, 10000, 100000]:
    name = 'registry-final-' + str(size)
    args = ['dotnet', 'benchmarks/WeavePort.Registry/bin/Release/net10.0/WeavePort.Registry.dll', str(size), str((root / 'registry-final' / str(size)).resolve())]
    row = {'phase': name, 'start': time.time(), 'args': args}
    phases.append(row)
    with (root / (name + '.log')).open('w') as log:
        result = subprocess.run(args, stdout=log, stderr=subprocess.STDOUT)
    row.update(end=time.time(), exitCode=result.returncode)
    (root / 'final-phases.json').write_text(json.dumps(phases, indent=2))
    if result.returncode: raise SystemExit(result.returncode)
for language in ['python', 'typescript', 'csharp']:
    run('production-' + language + '-fixed', ['tools/performance/density.py', '--output', str(root / ('production-' + language + '-fixed')),
        '--clients', '100,500,1000', '--seconds', '60', '--repeats', '1', '--adapters', 'docker', '--language', language,
        '--traffic', 'population', '--calls-per-minute', '1', '--memory-mib', '4096', '--pristine', '2'])
run('soaks-validated', ['tools/performance/reuse_matrix.py', str(root / 'configs' / 'soaks-final.json'), str(root / 'soaks-validated')])
