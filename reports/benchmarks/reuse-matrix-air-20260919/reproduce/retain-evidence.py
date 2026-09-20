"""Run after final phases. Preserve compact, sanitized reproducible evidence."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil

source=Path('artifacts/reuse-matrix')
target=Path('reports/benchmarks/reuse-matrix-air-20260919')
target.mkdir(parents=True,exist_ok=True)
root=str(Path.cwd())
home_prefix=str(Path.home())+'/'
phases=['scaling-v1','profile-v1','profile-preload','scaling-optimized','workloads','engine-scaling','engine-comparison','arrivals','canonical-scaling','canonical-final','capacity-trusted','capacity-forkserver','serialized-final','serialized-ready-final','workloads-final','faults-final','faults-arrivals-final','soaks-final','soaks-validated','production-python','production-typescript','production-csharp','production-python-fixed','production-typescript-fixed','production-csharp-fixed','security-v1','security-optimized','security-engine','security-kernel-audit','security-final','security-final-cli','security-final-cli-normalized','security-exit-fixed','security-bookend-cli','kernel-before','kernel-after']
manifest=[]
phases += ['production-python-repeat-4096', 'production-python-repeat-8192']
phases += [f'production-python-confirm-{budget}-{seed}' for budget,seed in [(8192,1731),(4096,1731),(4096,1732),(8192,1732)]]
def sanitize(value):
    if isinstance(value,dict):
        return {('existingContainerCount' if k=='existingContainers' else k): (len(v) if k=='existingContainers' else sanitize(v)) for k,v in value.items() if not (k=='rows' and 'allContainersRawMiB' in value)}
    if isinstance(value,list): return [sanitize(v) for v in value]
    if isinstance(value,str): return value.replace(root,'<repo>').replace(home_prefix,'<home>/')
    return value

def save(src,dst,compressed=False):
    data=src.read_text()
    if src.suffix=='.jsonl':
        data=''.join(json.dumps(sanitize(json.loads(line)))+'\n' for line in data.splitlines())
    elif src.suffix=='.json': data=json.dumps(sanitize(json.loads(data)),indent=2)+'\n'
    else: data=data.replace(root,'<repo>').replace(home_prefix,'<home>/')
    dst.parent.mkdir(parents=True,exist_ok=True)
    if compressed: dst.write_bytes(gzip.compress(data.encode(),mtime=0))
    else: dst.write_text(data)
    manifest.append({'path':str(dst.relative_to(target)),'sha256':hashlib.sha256(dst.read_bytes()).hexdigest()})

for phase in phases:
    folder=source/phase
    if not folder.exists(): continue
    for name in ['identity.json','summary.json','security.json','cleanup.json','bracket.json']:
        if (folder/name).exists(): save(folder/name,target/phase/name)
    for file in folder.glob('*/summary.json'): save(file,target/phase/file.parent.name/file.name)
    for file in folder.glob('*/headroom.json'): save(file,target/phase/file.parent.name/file.name)
    if phase.startswith('production-'):
        for name in ['result.json','config.json','supervisor.json']:
            for file in folder.glob('*/'+name):
                destination=target/phase/file.parent.name/file.name
                if name=='supervisor.json':
                    save(file,destination.with_suffix('.json.gz'),True)
                    # Remove only our superseded generated copy; the original evidence remains intact.
                    destination.unlink(missing_ok=True)
                else:
                    save(file,destination)
    if phase.startswith('capacity-') or phase in ('soaks-final','soaks-validated'):
        for file in folder.glob('*/samples.jsonl'): save(file,target/phase/file.parent.name/'samples.jsonl.gz',True)
    if phase.startswith('kernel-'):
        for file in folder.glob('*.json'):
            if file.name not in ['identity.json','summary.json','cleanup.json']: save(file,target/phase/file.name)
for name in ['registry-before','registry-after','registry-final']:
    for file in (source/name).glob('*/summary.json'): save(file,target/name/file.parent.name/file.name)
    for file in (source/name).glob('*hashes.json'): save(file,target/name/file.name)
for file in source.glob('production-*-headroom.json'): save(file,target/'verification'/file.name)
for name in ['packed-identity-check.json','packed-identity-check-final.json','scheduler-reserve-fixed.log','scheduler-reserve-packed.log','hosting-reserve-packed.log','density-observer-check.log','density-histogram-check.json','code-style.json','python-tests-expanded.log','python-matrix-final.log','python-matrix-expanded.log','hosting-packed.log','scheduler-source-expanded.log','scheduler-packed.log','exit-before.log','final-phases.json','final-verification.log','soak-analysis.json','environment-cleanup.json']:
    file=source/name
    if file.exists(): save(file,target/'verification'/name)
for file in (source/'configs').glob('*.json'): save(file,target/'configs'/file.name)
for file in (source/'reserve-race-before').iterdir():
    if file.is_file(): save(file,target/'verification'/'reserve-race-before'/file.name)
for name in ['python-tests-final.log','code-style-final.json','final-postchecks.log','candidate-process-cleanup.json']:
    file=source/name
    if file.exists(): save(file,target/'verification'/name)
for name in ['run-final-phases.py','resume-final-phases.py','run-after-reserve-fix.py','run-production-repeat.py','run-production-confirmation.py','retain-evidence.py','analyze-soaks.py','audit-cleanup.py','write-report.py']:
    save(source/name,target/'reproduce'/name)
(target/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Retained',len(manifest),'files',sum((target/v['path']).stat().st_size for v in manifest),'bytes')
