import json
from pathlib import Path
import statistics
import sys
sys.path.insert(0,'tools/performance')
from matrix_capacity import stability

root=Path('artifacts/reuse-matrix/soaks-validated')
results=[]
for folder in sorted(root.glob('*-*')):
    if not (folder/'summary.json').exists(): continue
    result=json.loads((folder/'summary.json').read_text())
    samples=[json.loads(x) for x in (folder/'samples.jsonl').read_text().splitlines()]
    samples=[s for s in samples if 0 < s['elapsed'] <= result['config']['seconds']]
    # Exclude the initial five-minute warmup when assessing gradual memory retention.
    steady=[s for s in samples if s['elapsed']>=300]
    first=steady[:min(100,len(steady)//3)];last=steady[-min(100,len(steady)//3):]
    def med(rows,key):return statistics.median(s[key] for s in rows)
    value={'mode':result['config']['mode'],'rate':result['config']['rate'],
           'stability':stability(folder,result),'counts':result['counts'],'customersPerSecond':result['customersPerSecond'],
           'latency':result['latency']['responseMs'],'memoryPeakMiB':result['memoryPeakMiB'],
           'steadyContainerEarlyMedianMiB':med(first,'ownedMiB'),'steadyContainerLateMedianMiB':med(last,'ownedMiB'),
           'steadyHostEarlyMedianMiB':med(first,'hostRssMiB'),'steadyHostLateMedianMiB':med(last,'hostRssMiB'),
           'starts':result['starts'],'retirements':result['retirements'],'remaining':result['remaining'],
           'sampleCount':len(samples),'swapDeltaMiB':samples[-1]['pressure']['swapUsedMiB']-samples[0]['pressure']['swapUsedMiB']}
    results.append(value)
Path('artifacts/reuse-matrix/soak-analysis.json').write_text(json.dumps(results,indent=2)+'\n')
print(json.dumps(results,indent=2))
