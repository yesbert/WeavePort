import json
from pathlib import Path
import sys
sys.path.insert(0,'tools/performance')
import docker_memory

root=Path('artifacts/reuse-matrix')
known=set()
for file in list(root.rglob('summary.json'))+list(root.rglob('cleanup.json')):
    value=json.loads(file.read_text())
    for row in value if isinstance(value,list) else [value]:
        if isinstance(row,dict): known.update(row.get('known',[]))
for file in root.glob('production-*/*/resources.jsonl'):
    for line in file.read_text().splitlines(): known.update(json.loads(line).get('instances',[]))
engine=docker_memory.Engine()
info=engine.get('/info')
names={name.lstrip('/') for container in engine.get('/containers/json?all=true') for name in container['Names']}
report={'observedOwnedNames':len(known),'remainingOwnedNames':sorted(known & names),
        'otherContainerCount':len(names-known),'docker':{key:info.get(key) for key in ['ServerVersion','KernelVersion','OperatingSystem','Architecture','NCPU','MemTotal','SecurityOptions','DefaultRuntime']}}
(root/'environment-cleanup.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
if known & names: raise RuntimeError('Exact benchmark-owned containers remain')
