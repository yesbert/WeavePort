import json
import sys
from pathlib import Path

R=Path('artifacts/reuse-matrix')
P=Path('reports/benchmarks/reuse-matrix-air-20260919')
O=Path(sys.argv[1]) if len(sys.argv)>1 else R/'user-report'

def read(path): return json.loads((R/path).read_text())
# Metrics are numeric; never pass arbitrary report input through the formatter.
def fmt(v,d=1): return '—' if v is None else f'{float(v):,.{d}f}'
def table(headers,rows):
    return '\n'.join(['| '+' | '.join(headers)+' |','| '+' | '.join(['---']*len(headers))+' |']+['| '+' | '.join(map(str,row))+' |' for row in rows])

data={name:read(name+'/summary.json') for name in ['canonical-final','capacity-trusted','capacity-forkserver','serialized-ready-final','workloads-final','faults-final','faults-arrivals-final','soaks-validated','production-python','production-typescript','production-csharp','production-python-fixed','production-typescript-fixed','production-csharp-fixed','production-python-repeat-4096','production-python-repeat-8192']}
confirmation_specs=[(8192,1731),(4096,1731),(4096,1732),(8192,1732)]
for budget,seed in confirmation_specs:
    name=f'production-python-confirm-{budget}-{seed}'
    data[name]=read(name+'/summary.json')
brackets={mode:read('capacity-'+mode+'/bracket.json') for mode in ['trusted','forkserver']}
soaks=read('soak-analysis.json')
security={name:read(name+'/security.json') for name in ['security-exit-fixed','security-bookend-cli']}
sec_counts={name:dict(passed=sum(c['passed'] for g in groups for c in g['checks']),total=sum(len(g['checks']) for g in groups)) for name,groups in security.items()}
identity=read('canonical-final/identity.json')
registry={size:read(f'registry-final/{size}/summary.json') for size in [4,10000,100000]}
import re
scheduler_checks=int(re.search(r'PASS (\d+) public scheduler', (R/'scheduler-reserve-packed.log').read_text())[1])

pool=table(['Mode','Containers','Customers/s','p99 ms','Container peak MiB'],[[r['config']['mode'],r['config']['workers'],fmt(r['customersPerSecond']),fmt(r['latency']['responseMs']['p99'],2),fmt(r['memoryPeakMiB'],2) if r['config']['mode']!='fresh' else 'transient / undersampled'] for r in data['canonical-final']])
capacity=table(['Mode','Offered/s','Repeat','Correct customers/s','p99 ms','Drops','Qualified'],[[mode,r['config']['rate'],r['config']['repeat'],fmt(r['customersPerSecond']),fmt(r['latency']['responseMs']['p99'],2),r['counts']['dropped'],r['stability']['qualified']] for mode in brackets for r in data['capacity-'+mode]])
soak_table=table(['Mode','Duration','Offered/s','Correct customers/s','Correct distinct customers','p99 ms','Peak MiB','Errors / drops'],[[r['config']['mode'],'30 min',r['config']['rate'],fmt(r['customersPerSecond'],2),r['counts']['customersCompleted'],fmt(r['latency']['responseMs']['p99'],2),fmt(r['memoryPeakMiB'],2),str(r['counts']['failed'])+' / '+str(r['counts']['dropped'])] for r in data['soaks-validated']])
grouped=table(['Mode','Calls/customer','Plugins/customer','Arrival','Customers/s','Calls/s','p99 ms','Drops'],[[r['config']['mode'],r['config'].get('callsPerCustomer',1),r['config'].get('pluginsPerCustomer',2),r['config'].get('arrival','saturated'),fmt(r['customersPerSecond']),fmt(r['requestsPerSecond']),fmt(r['latency']['responseMs']['p99'],2),r['counts']['dropped']] for r in data['serialized-ready-final']])
workloads=table(['Mode','Containers','Workload','Customers/s','p99 ms','Peak MiB'],[[r['config']['mode'],r['config']['workers'],(('burst '+str(r['config']['rate'])+'/s') if r['config'].get('arrival')=='burst' else r['config'].get('workload','payload 256 KiB')),fmt(r['customersPerSecond']),fmt(r['latency']['responseMs']['p99'],2),fmt(r['memoryPeakMiB'],2)] for r in data['workloads-final']])
faults=table(['Mode','Arrival','Offered/s','Drops','Correct customers/s','Expected faults contained','Unexpected errors','Normal echo p99 ms','Container starts'],[[r['config']['mode'],r['config'].get('arrival','saturated'),r['config'].get('rate','—'),r['counts']['dropped'],fmt(r['customersPerSecond']),str(r['counts']['faultsContained'])+'/'+str(r['counts']['faultsExpected']),r['counts']['failed'],fmt(r['byWorkload']['echo']['p99'],2),r['starts']] for r in data['faults-final']+data['faults-arrivals-final']])
production=table(['Language','Candidate','Customers','Offered calls/s','Correct calls','Observed calls/s','p99 ms','Peak workers','Container MiB','Passed'],[[lang,('after reserve fix' if phase.endswith('-fixed') else 'before reserve fix'),r['config']['Clients'],fmt(r['offeredRate'],2),r['totals']['Success'],fmt(r['throughput'],2),fmt(r['totals']['latency']['p99'],2),r['resources']['peakWorkers'],fmt(r['supervisor']['peakSampledContainerMiB'],2),r['passed']] for lang in ['python','typescript','csharp'] for phase in ['production-'+lang,'production-'+lang+'-fixed'] for r in data[phase]])
repeated=table(['Budget MiB','Customers','Seed','Offered calls/s','Correct calls','Cold dispatches','Observed calls/s','p99 ms','Peak workers','Container MiB','Passed'], [[budget,r['config']['Clients'],r['config']['Seed'],fmt(r['offeredRate'],2),r['totals']['Success'],r['totals']['Cold'],fmt(r['throughput'],2),fmt(r['totals']['latency']['p99'],2),r['resources']['peakWorkers'],fmt(r['supervisor']['peakSampledContainerMiB'],2),r['passed']] for budget in [4096,8192] for r in data['production-python-repeat-'+str(budget)]])
population_bounds={}
for budget in [4096,8192]:
    rows=data['production-python-repeat-'+str(budget)]+[r for b,seed in confirmation_specs if b==budget for r in data[f'production-python-confirm-{b}-{seed}']]
    populations=sorted({r['config']['Clients'] for r in rows})
    confirmed=[n for n in populations if len([r for r in rows if r['config']['Clients']==n])>=2 and all(r['passed'] for r in rows if r['config']['Clients']==n)]
    rejected=next((r for n in populations for r in rows if r['config']['Clients']==n and not r['passed']),None)
    invalid=rejected is not None and (rejected['supervisor']['reason'] is not None or rejected.get('failure') is not None)
    lower=max(confirmed,default=None)
    population_bounds[budget]={'confirmedCustomers':lower,'trialsAtConfirmedPopulation':len([r for r in rows if r['config']['Clients']==lower]),'callsPerSecond':lower/60 if lower else None,
        'firstRejectedCustomers':rejected['config']['Clients'] if rejected and not invalid else None,
        'firstInvalidCustomers':rejected['config']['Clients'] if invalid else None,
        'rejectionObserverReason':rejected['supervisor']['reason'] if rejected else None,
        'rejectedFailedCalls':rejected['totals']['Failed'] if rejected else None}
population_table=table(['Budget MiB','Confirmed customers (all trials)','Offered calls/s','First rejected customers','Rejected-stage failed calls'],[[b,v['confirmedCustomers'] or '—',fmt(v['callsPerSecond'],2),v['firstRejectedCustomers'] or '—',v['rejectedFailedCalls'] if v['rejectedFailedCalls'] is not None else '—'] for b,v in population_bounds.items()])
confirmation_table=table(['Order','Budget MiB','Seed','Correct calls','p99 ms','Maximum ms','Passed'],[[i+1,budget,seed,r['totals']['Success'],fmt(r['totals']['latency']['p99'],2),fmt(r['totals']['latency']['max'],2),r['passed']] for i,(budget,seed) in enumerate(confirmation_specs) for r in data[f'production-python-confirm-{budget}-{seed}']])
mem=table(['Mode','Container median after warmup → end MiB','Measuring-host median MiB','Starts / final retirements','Backlog/latency qualified'],[[s['mode'],fmt(s['steadyContainerEarlyMedianMiB'],2)+' → '+fmt(s['steadyContainerLateMedianMiB'],2),fmt(s['steadyHostEarlyMedianMiB'],2)+' → '+fmt(s['steadyHostLateMedianMiB'],2),str(s['starts'])+' / '+str(s['retirements']),s['stability']['qualified']] for s in soaks])

eng=f'''# Reuse security and many-customer capacity — MacBook Air

Measured on 2026-09-19: Apple M4 Air, 10 cores, 32 GiB RAM, Docker Desktop ~24 GiB. Other project services stayed running; their sampled raw Docker memory was about 11.4 GiB before the expanded I/O probes, so the entire 24-GiB VM was not free for this test. This is a source-built, uncommitted candidate; not release qualification and not comparable to the earlier M2 Ultra benchmark host. Primary image: `{identity['image']}`.

## What these numbers mean

The reuse prototype executes two small, preinstalled Python plugin modules through local Docker stdio. Each one-call case uses a distinct synthetic customer identity per invocation and validates tenant, plugin and returned value. It does not provision a production customer per identity or include the production .NET protocol, host authorization callbacks, ingress, database or real customer dependencies. Grouped cases count a customer only after all three/ten correct calls. The production-host population controls are separate below.

`trusted` retains an interpreter and relies on cooperation; it demonstrably does not isolate arbitrary customer heap state. `process` starts a fresh interpreter in the container. `forkserver` uses a pristine customer-independent template and a new unprivileged child per call, with external cleanup checks. `fresh` replaces the entire container after each call. Unreviewed artifacts must retain stronger customer separation; these tests do not certify a hostile-code sandbox.

## Final pool scaling

One CPU quota per container; 128 MiB ceiling each, except 64-container cases use 64 MiB. Default payload 64 bytes. One invocation per container. Preparation is excluded from throughput and retained separately; fresh-container retirement is included. Twenty-second saturated trials are peaks, not sustainable offered-rate claims.

{pool}

Actual container memory is sampled cgroup working set, not the configured memory ceiling, whole Docker VM footprint or macOS RSS. Short-lived fresh containers are undersampled. More containers do not imply more tiny-call throughput; I/O demand has a different curve. Initial scaling stops at 64 containers/4 GiB; later I/O probes expand to 128 containers/8 GiB only after a Docker headroom check and 2-GiB margin. Neither is a production worker cap.

## Arrival-rate boundary and repeated trials

Poisson arrivals, independent of completion, 16 containers, two 60-second seeds per point. Qualification requires all calls correct, no drops, overall and sufficiently populated time-window p99 ≤1 second, and bounded backlog growth. Last-third mean backlog may exceed first-third mean by at most max(two calls/worker, 100 ms of offered work). This is an explicit finite engineering criterion, not proof of unlimited stability. The highest passing and first rejected tested rates are:

```json
{json.dumps(brackets,indent=2)}
```

{capacity}

The resource observer streams its history to disk and retains scalar peaks/counts only. The measuring process uses substantially less than a whole CPU core in inspected trusted boundary trials; retained generator-lag histograms expose its own delays. Results still describe this complete experimental path on a shared laptop, not a hardware-independent plugin maximum.

## Thirty-minute operating-point validation

Each mode runs below its passing boundary, with a separate seed. There are no per-customer reserved containers. Every invocation uses a different customer identity within its run.

{soak_table}

{mem}

Memory comparison excludes the first five minutes and compares the first/last up-to-100 steady-state resource samples. Final retirements are normal pool shutdown. Live samples, queue windows, hashes and cleanup are retained; finite memory observations are not a general proof against leaks. The cooperative pool median increases by about 7.1 MiB over this comparison, while the fork-server median is effectively unchanged.

## Few calls per customer, serial same-plugin execution

The p99 values below are invocation latency, not full multi-call customer-workflow latency. Same customer/plugin jobs cannot occupy multiple containers concurrently; waiting jobs do not consume a lane. Two modules permit fan-out across different plugins; one-module cases prohibit that intra-customer fan-out. Earlier unconstrained grouped pilot cases and the interrupted undersized-group-queue probe remain historical evidence. Saturated grouping now queues up to two complete customer groups per worker (bounded by 10,000 calls), avoiding artificial idle lanes when one customer has ten sequential calls.

{grouped}

## Workloads and injected faults

CPU=10 ms CPU time; I/O=100 ms simulated wait; memory=32 MiB touched allocation; payload=256 KiB; blend=1% 500-ms I/O, 9% 10-ms CPU, remainder short calls. Each case lasts 30 seconds. Burst rows offer a complete second of short calls at once. These are workload models, not measurements of a database or network service.

{workloads}

Closed-loop fault cases last 90 seconds; additional independent Poisson-arrival cases last 60 seconds at 250/500 offered calls per second. They inject a hang/crash/cleanup failure every hundred calls across four containers, and require retirement before reuse. Closed-loop latency alone does not show the waiting experienced by independent arrivals when all lanes block. Fault containment counts only actually executed faults; admission drops are separately reported. Intentional failed customers are not included in successful customer throughput. A fault campaign is not a zero-error service-capacity point.

{faults}

## Security findings and corrections

Final expected-behavior checks: Engine {sec_counts['security-exit-fixed']['passed']}/{sec_counts['security-exit-fixed']['total']}; CLI bookend {sec_counts['security-bookend-cli']['passed']}/{sec_counts['security-bookend-cli']['total']}. Deliberate negative controls are included in these passes.

1. **Heap state can cross customers in the cooperative mode.** Five seeded patterns—module global, mutable default argument, cache, context variable, logger—remain readable by the next customer. The seed/probe is deliberate, but the retention pattern can be an ordinary author mistake. A generic SDK reset is not a confidentiality boundary.
2. **Process exit did not remove kernel IPC state.** A later customer read the previous customer's canary from System V shared memory in both fresh-interpreter and fork-server modes. External audits of System V shared memory/semaphores/message queues and POSIX queues now mark any residue non-reusable. The exact container is destroyed; the next customer cannot access it. Both transports explicitly create a private IPC namespace. In trusted mode the audit shares the plugin interpreter and can itself be subverted; only the fork-server design places it in the separate privilege-separated supervisor. Process-keyring creation was denied under the tested Docker policy.
3. **Explicit hostile probes are distinct from accidental retention.** Tests attempt forged stdout/results, oversized frames, pickle execution, parent access/signals and access to the private template socket. Fork child results are bounded JSON, not deserialized pickle. Children have UID/GID 65532 and no effective capabilities; supervisor capabilities are limited to SETUID/SETGID/KILL. No host mounts, network or Docker socket enter a plugin. Local grant mutation intentionally succeeds: the local scope is a stub, so real authorization must remain in the host.
4. **Timeout handling had a false failure.** A separate 100-ms post-response exit wait killed four otherwise correct results at 64-way saturation. A controlled 250-ms exit reproduced it. Response and process exit now share a 1.5-second deadline; 250 ms succeeds, 2 seconds retires, and the corrected 64-way repeat has zero unexpected errors. Cleanup that cannot safely remove child-owned files retires the container; privileges were not broadened to conceal this condition.

The two public fixture modules also do not qualify confidentiality between private customer code bundles: readable files in a shared image are shared with its plugins. Customer secrets and private artifacts must not be baked into that bundle.

The production scheduler's existing prohibition on transferring used workers between customers is unchanged. The kernel leakage finding concerns the reuse experiment. Finite controls do not establish protection against arbitrary native code, kernel vulnerabilities or side channels. See [method and source](../../../benchmarks/WeavePort.Reuse/MATRIX.md) and [author guidance](../../../benchmarks/WeavePort.Reuse/AUTHOR-GUIDE.md).

## Actual host and other languages

The following source-built .NET scheduler tests use Python/TypeScript/C# Docker workers, seeded customers with one call/minute, no warmup, a pristine reserve ceiling of two slots and a 4-GiB shared reservation. Used workers remain customer-bound. Each language's staircase stops at its first failure. Every customer must receive a result within the configured one-second criterion; this is stricter than checking only global p99. The fixture calls `context` and validates the customer identity plus 64-byte configuration. Production-control containers use 0.5 CPU and 64 MiB each, unlike the one-CPU reuse trials. Registration is measured separately before traffic. The diagnostic allows a five-second admission wait and ten-second execution deadline so overload remains observable; these are not one-second production timeout recommendations.

{production}

The initial 100-customer rows contain 6/7 spurious busy responses, not a real throughput boundary. A deterministic startup-gate regression reproduced the reserve/foreground race. Foreground acquisition now waits for a pending shared candidate, rechecks readiness after launch-slot acquisition and takes priority over refill. The corrected rows rerun the same arrival seed. The corrected observer also includes unbound pristine/startup instance names from owned CLI processes; earlier memory samples could omit these. At 500 Python/TypeScript customers the remaining busy results spend about five seconds in the queue, matching admission expiry under overload. C# returns all 100 results correctly but misses the one-second latency criterion in this control. These measurements expose production startup/eviction/protocol costs. They must not be substituted with the much higher reuse-prototype rates.

### Repeated low-frequency customers and memory budget

Python controls below span two minutes: each registered customer offers two calls, one per minute. Two arrival seeds are tested per population unless a failure stops the staircase. Admission budgets of 4/8 GiB imply up to 64/128 workers for the configured 64-MiB profile; no separate worker-count cap is supplied. The same customer can appear in both minutes, so observed calls/s is not a rate of newly provisioned customers. Cold dispatch counts expose scheduler-classified startup/reattachment; only passing rows allow the remaining successful calls to be interpreted as resident reuse. Registration alone does not reserve a container. Container memory is separate from host/CLI RSS; a 6-GiB host/CLI guard protects this probe. A preflight additionally reserves 2 GiB beyond measured background Docker usage.

{population_table}

These are tested population brackets at one call/customer/minute, not a universal account limit. Qualification uses every available repeat, with at least two seeds; the 256-customer follow-ups below are included. An observer guard would invalidate a stage rather than establish a capacity boundary; its reason is retained in the JSON. The detailed repetitions follow.

{repeated}

The 8-GiB, 256-customer second seed returned every call correctly but crossed the one-second criterion. Four additional 120-second controls use new seeds and counterbalanced budget order (8/4 GiB, then 4/8 GiB). Every per-customer p99 must pass; with two calls/customer that tests the slower call, so a passing aggregate p99 alone is insufficient. An occasional over-limit result is retained and prevents qualifying that population across all repeats. At 256 customers each budget passes three of four runs; all requests return correctly, but one run per budget misses latency. The conservative confirmed population is therefore 128 for both budgets under this finite protocol, not 256. These controls do not establish a monotonic capacity gain from additional memory.

{confirmation_table}

## Improvements retained in the candidate

- Lifecycle-maintained active/resident indexes replace dormant-registry scans in dispatch. With four warmed native C# workers, 10,000 registrations improve from ~6,486 to ~{fmt(registry[10000]['requestsPerSecond'],0)} calls/s on the final candidate. At 100,000 registrations: ~{fmt(registry[100000]['requestsPerSecond'],0)} calls/s for **four hot customers**, ~{fmt(registry[100000]['managedRegistrationDeltaMiB'],2)} MiB incremental managed registration memory, ~{fmt(registry[100000]['disposalSeconds'],3)}-second disposal. The memory figure is the difference in GC-reported live managed bytes after forced collections, not total process/container memory. This diagnostic is not 100,000 distinct customers/s. Both source and locally packed scheduler consumers pass {scheduler_checks} assertions; packed hosting regressions pass with the same Hosting DLL SHA.
- Immutable Python bytecode precompilation and approved standard-library template preload remove repeated compilation/import cost. Customer modules/data never enter the template.
- Direct Engine attach removes one CLI helper per retained container. Matched reversed-order runs retain CLI/Engine comparisons. Summed CLI RSS can include shared pages and is not a physical-memory saving guarantee.
- Sparse per-customer benchmark histograms replace six preallocated arrays: 1,000 two-sample records allocate 1,264,176 bytes in the verified control, rather than roughly 115 MB of initially empty arrays.

## Evidence and reproduction

[Manifest](manifest.json) hashes compact retained data. Capacity/soak windows are retained as compressed JSON Lines; production observer histories are compressed JSON (`supervisor.json.gz`). Identities redact unrelated container names while preserving their count, machine settings, image identity and source hashes. Original overloads, the 64-way false timeout, deliberate leaks and the CLI CapAdd normalization false failure are retained rather than discarded. The latter was a test representation mismatch (`CAP_` prefix), not a capability change.

For a fresh checkout, prepare images and the source-built density harness before timing, then copy the retained configurations into a new artifact root:

```sh
env -u WEAVEPORT_PACKAGE_SET dotnet build benchmarks/WeavePort.Density -c Release
env -u WEAVEPORT_PACKAGE_SET dotnet build benchmarks/WeavePort.Registry -c Release
docker build -t weaveport-poc-python:1 -f plugins/python/Dockerfile .
docker build -t weaveport-poc-typescript:1 -f plugins/typescript/Dockerfile .
docker build -t weaveport-poc-csharp:1 -f plugins/csharp/Dockerfile .
docker build -t weaveport-reuse-matrix:1 -f benchmarks/WeavePort.Reuse/Matrix.Dockerfile benchmarks/WeavePort.Reuse
mkdir -p artifacts/reuse-matrix
cp -R reports/benchmarks/reuse-matrix-air-20260919/configs artifacts/reuse-matrix/configs
python3 tools/performance/matrix_security.py artifacts/reuse-matrix/security-exit-fixed --engine
python3 tools/performance/reuse_matrix.py artifacts/reuse-matrix/configs/canonical-scaling.json artifacts/reuse-matrix/canonical-final
python3 reports/benchmarks/reuse-matrix-air-20260919/reproduce/run-final-phases.py
python3 reports/benchmarks/reuse-matrix-air-20260919/reproduce/run-production-repeat.py
python3 reports/benchmarks/reuse-matrix-air-20260919/reproduce/run-production-confirmation.py
python3 tools/performance/reuse_matrix.py artifacts/reuse-matrix/configs/faults-arrivals-final.json artifacts/reuse-matrix/faults-arrivals-final
python3 tools/performance/matrix_security.py artifacts/reuse-matrix/security-bookend-cli
for count in 4 10000 100000; do
  dotnet benchmarks/WeavePort.Registry/bin/Release/net10.0/WeavePort.Registry.dll "$count" "artifacts/reuse-matrix/registry-final/$count"
done
```

These commands are for empty output paths; do not overwrite a prior run. They reproduce the methods against the current candidate. The sequential script retains the original names `production-*` and `soaks-final`; in a fresh run those contain current-code results. Original before-fix measurements and interrupted phases are historical evidence, not regenerated by running corrected code. The report writer requires the retained before/after evidence layout, so it is not a generic renderer for an arbitrary fresh campaign. Use the [matrix commands](../../../benchmarks/WeavePort.Reuse/MATRIX.md), [configurations](configs/canonical-scaling.json) and [sequential run script](reproduce/run-final-phases.py) with fresh artifact directories; never run benchmarks alongside builds or other load tests. The script's known brackets are observations for this machine and must be adjusted when moving machines. Runtime/source/package identities distinguish all series. `scripts/verify.sh` qualification of committed HEAD, publication and production cross-customer reuse rollout were not performed.
'''
P.mkdir(parents=True,exist_ok=True);(P/'README.md').write_text(eng)

def german_table(value):
    import re
    labels={'Correct distinct customers':'Korrekte unterschiedliche Kunden','Calls/customer':'Aufrufe/Kunde','Plugins/customer':'Plugins/Kunde','Correct customers/s':'Korrekte Kunden/s','Correct calls':'Korrekte Aufrufe','Customers/s':'Kunden/s','Calls/s':'Aufrufe/s','Offered calls/s':'Angebotene Aufrufe/s','Observed calls/s':'Gemessene Aufrufe/s','Offered/s':'Angeboten/s','Container peak MiB':'Container-Spitze MiB','Peak MiB':'Spitze MiB','Peak workers':'Max. Worker','Errors / drops':'Fehler / verworfen','Expected faults contained':'Absichtliche Fehler abgefangen','Unexpected errors':'Unerwartete Fehler','Normal echo p99 ms':'p99 normaler Aufruf ms','Container starts':'Containerstarts','Container median after warmup → end MiB':'Container-Median nach Aufwärmen → Ende MiB','Measuring-host median MiB':'Messprogramm-Median MiB','Starts / final retirements':'Starts / Abbau am Ende','Backlog/latency qualified':'Rückstau/Latenz bestanden','Order':'Reihenfolge','Maximum ms':'Maximum ms','Language':'Sprache','Customers':'Kunden','Containers':'Container','Workload':'Arbeitslast','Duration':'Dauer','Repeat':'Wiederholung','Qualified':'Bestanden','Passed':'Bestanden','Arrival':'Ankunft','Drops':'Verworfen','Confirmed customers (all trials)':'Bestätigte Kunden (alle Folgen)','First rejected customers':'Erste abgelehnte Kundenzahl','Rejected-stage failed calls':'Aufrufe mit Fehlerstatus dort','Budget MiB':'Budget MiB','Cold dispatches':'Kalte Ausführungen','Mode':'Variante','Candidate':'Stand','after reserve fix':'nach Reserve-Korrektur','before reserve fix':'vor Reserve-Korrektur','True':'ja','False':'nein','saturated':'gesättigt','transient / undersampled':'zu kurz für verlässliche Stichprobe'}
    value='\n'.join('| '+' | '.join(labels.get(cell.strip(),cell.strip()) for cell in line.strip().strip('|').split('|'))+' |' for line in value.splitlines())
    return re.sub(r'(?<= )([0-9][0-9,]*\.[0-9]+)(?= |$)',lambda m:m[1].replace(',','X').replace('.',',').replace('X','.'),value)

# German user-facing report deliberately separates measured data from deployment policy.
german=f'''# WeavePort: Sicherheit und Kundendichte auf dem MacBook Air

Die Testreihe trennt Spitzenlast, dauerhaft angebotene Last und den tatsächlichen WeavePort-Host. **Die hohen Wiederverwendungszahlen stammen aus einem Python/Docker-Prototyp; die produktive Host-Anbindung wurde dadurch noch nicht ersetzt.** Der produktive Scheduler überträgt benutzte Worker weiterhin nicht zwischen Kunden.

Hardware: M4 MacBook Air, 10 CPU-Kerne, 32 GiB RAM; Docker etwa 24 GiB. Andere Projekt-Dienste liefen weiter und belegten vor den erweiterten I/O-Proben etwa 11,4 GiB rohen Docker-Speicher. Die gesamten 24 GiB standen dem Versuch deshalb nicht frei zur Verfügung. Ergebnisse sind nicht direkt mit den früheren Messungen auf anderer Hardware vergleichbar.

## Belastbare Betriebspunkte

Je Variante 30 Minuten, 16 Container, ein anderer Kunde pro Aufruf, zwei abwechselnde kleine Plugins. Pro Container läuft genau ein Aufruf. Die angebotene Rate liegt mit Reserve unter der zuvor zweimal geprüften Lastgrenze.

{german_table(soak_table)}

`trusted`: gemeinsamer Python-Prozess mit kooperativem Aufräumen. `forkserver`: frischer, unprivilegierter Kindprozess aus einer unveränderten Vorlage pro Aufruf. **Auch der Forkserver ist mit diesen Tests nicht als Sandbox für beliebigen bösartigen Code qualifiziert.**

{german_table(mem)}

Die Speicherwerte sind gemessene Container-Arbeitssätze. Docker-VM, Messprogramm, CLI-Helfer und andere Anwendungen sind davon getrennt. Ein Container-Limit von 128 MiB bedeutet nicht, dass dieser Speicher vollständig belegt wird. Speichervergleich nach fünf Minuten Aufwärmphase; kurze Spitzen können zwischen Messpunkten liegen. Der Median des kooperativen Pools steigt dabei um etwa 7,1 MiB, der Forkserver-Median bleibt nahezu unverändert. Das ist keine Zusage unbegrenzter Langzeitstabilität.

## Wie viele Kunden pro Sekunde?

Bei einem Aufruf pro Kunde gilt hier: ein korrekt abgeschlossener Aufruf = ein vollständig bedienter, anderer Kunde. Bei drei oder zehn Aufrufen zählt der Kunde erst nach allen erfolgreichen Aufrufen. Kundennummern werden innerhalb eines Laufs unterschiedlich verwendet; es handelt sich nicht um Millionen vollständig eingerichteter Produktionskonten.

{german_table(grouped)}

p99 bezeichnet hier die Latenz einzelner Aufrufe, nicht die gesamte Dauer eines Kundenablaufs mit mehreren Aufrufen. Ein Kundenplugin läuft nicht gleichzeitig auf mehreren Workern. Zwei verschiedene Plugins desselben Kunden dürfen parallel laufen. Die Ein-Plugin-Fälle testen ausdrücklich die vollständige Serialisierung innerhalb des Kunden.

## Spitzenlast und Poolgröße

{german_table(pool)}

Diese kurzen 20-Sekunden-Läufe zeigen mögliche Spitzen. Mehr Container benötigen mehr Speicher, erhöhen aber nicht automatisch den Durchsatz. Auf diesem Rechner bringen sehr viele Container für winzige Aufrufe keinen Vorteil. Wartende I/O-Aufrufe können dagegen von mehr Ausführungsplätzen profitieren. Die ersten Versuche waren auf 64 Container begrenzt; I/O wird zusätzlich mit 128 Containern geprüft, nach Kontrolle des verfügbaren Docker-Speichers und mit 2 GiB zusätzlicher Reserve. Das sind Versuchsschutzgrenzen, keine feste Produktgrenze.

## Realistische Lastgrenze statt Schönwetterwert

Die Anfragen treffen unabhängig vom Abschluss früherer Anfragen ein. Jede Laststufe läuft zweimal mit unterschiedlicher Zufallsfolge. Bestanden bedeutet: alle Antworten korrekt, keine verworfenen Anfragen, p99 insgesamt und in Zeitfenstern höchstens eine Sekunde, kein über die festgelegte Toleranz hinaus wachsender Rückstau. Die Toleranz beträgt höchstens den größeren Wert aus zwei wartenden Aufrufen pro Worker und 100 ms angebotener Arbeit. Die Grenzwerte gelten für diese Messstrecke und diesen Rechner.

{german_table(capacity)}

Zweimal bestanden: **trusted {fmt(brackets['trusted']['qualifiedLow'],0).replace(',','.')} Kunden/s**, **forkserver {fmt(brackets['forkserver']['qualifiedLow'],0).replace(',','.')} Kunden/s**. Die erste abgelehnte getestete Last lag bei {fmt(brackets['trusted']['firstRejected'],0).replace(',','.')} beziehungsweise {fmt(brackets['forkserver']['firstRejected'],0).replace(',','.')} Kunden/s. Dazwischen liegt die noch nicht weiter aufgelöste Grenze unter diesen Bedingungen.

## Verschiedene Arbeitslasten und Fehler

CPU: 10 ms Rechenzeit. I/O: 100 ms simuliertes Warten. Speicher: 32 MiB tatsächlich beschriebene Allokation. Große Nutzdaten: 256 KiB. Mischung: 1% I/O mit 500 ms, 9% CPU mit 10 ms, sonst kurze Aufrufe. Burst-Fälle bieten eine ganze Sekunde Last gleichzeitig an.

{german_table(workloads)}

Alle hundert Aufrufe wird absichtlich ein Hänger, Absturz oder Aufräumfehler eingebaut. Neben 90 Sekunden gesättigter Last werden 60 Sekunden mit unabhängig eintreffenden 250 beziehungsweise 500 Anfragen/s geprüft. Nur die unabhängigen Ankünfte zeigen auch die Wartezeit neu ankommender Kunden, während Worker blockiert sind. Die Fehlerkontrolle zählt tatsächlich ausgeführte Fehlerfälle; vorab verworfene Anfragen werden separat ausgewiesen. Diese absichtlich scheiternden Kunden werden nicht als erfolgreich bedient gezählt. Der betroffene Container muss ersetzt werden, bevor der nächste Kunde ihn benutzt.

{german_table(faults)}

## Was wurde bei der Sicherheit tatsächlich gefunden?

**Es gab echte Datenübernahme zwischen Kunden im Prototyp.** Die Tests legten absichtlich Kundendaten ab; anschließend konnte ein anderer Kunde sie lesen. Das ist ein reproduzierter Fehlermechanismus, kein Angriff auf echte Kundendaten.

- Im gemeinsamen Python-Prozess blieben Daten in globalen Variablen, Standardargumenten, Caches, Kontextvariablen und Loggern zurück. Solche Fehler können Plugin-Autoren auch versehentlich machen. Ein generisches SDK-Aufräumen kann das nicht sicher ausschließen.
- Selbst ein frischer Prozess reichte für System-V-Shared-Memory nicht: Der Speicher überlebte das Prozessende im Container. Jetzt prüft der Koordinator Kernel-IPC-Reste und verwirft den Container bei Resten. Beim Forkserver läuft diese Prüfung im getrennten Supervisor; im kooperativen Modus teilt sie den Interpreter mit dem Plugin und ist selbst keine Grenze gegen bösartigen Code. Im Wiederholungstest konnte der nächste Kunde den Inhalt nicht mehr lesen.
- Zusätzlich gab es ausdrücklich bösartige Versuche: gefälschte Antworten, übergroße Nachrichten, Pickle-Codeausführung, Zugriff auf den Supervisor und seinen Steuerkanal. Die Forkserver-Grenzen bestanden die vorgesehenen Gegenproben. Lokale Berechtigungen im Test-SDK lassen sich dagegen absichtlich manipulieren: Berechtigungsentscheidungen müssen außerhalb des Plugins im echten Host getroffen werden.
- Ein zu kurzes Zeitlimit für das Prozessende verursachte bei 64 Containern vier falsche Fehler trotz korrekter Ergebnisse. Eine gemeinsame Aufruffrist behebt das; die Gegenprobe für echte Fristüberschreitungen bleibt wirksam.

Abschließende erwartete Sicherheitskontrollen: **{sec_counts['security-exit-fixed']['passed']}/{sec_counts['security-exit-fixed']['total']} über Engine, {sec_counts['security-bookend-cli']['passed']}/{sec_counts['security-bookend-cli']['total']} über CLI**. Dazu gehören negative Kontrollen, die bekannte Grenzen bewusst nachweisen. Diese Zahl ist keine Sicherheitszertifizierung.

Für ungeprüfte Community-Plugins bleibt die stärkere Container-Trennung zwischen Kunden bestehen. Gemeinsame Interpreter kommen nur für ausdrücklich geprüfte, kooperative Plugins und eine dazu passende Vertraulichkeitspolitik infrage. Auch eigene Plugins können versehentlich Kundendaten behalten. Lesbare Dateien eines gemeinsamen Images sind für dessen Plugins sichtbar; Kundengeheimnisse und private Kunden-Codepakete gehören nicht in ein solches gemeinsames Bundle. Getestet wurden zwei öffentliche Beispielmodule, keine Isolation privater Codepakete.

## Tatsächlicher WeavePort-Host: getrennte Kontrollmessung

Eine Anfrage pro Kunde und Minute, echte .NET-Host-Strecke, Python/TypeScript/C#, dynamische speicherbegrenzte Worker-Zuteilung. Nach der ersten nicht bestandenen Kundenzahl stoppt die jeweilige Treppe. Alle Kunden müssen bedient werden; auch einzelne langsame Kunden werden sichtbar. Gemessen wird die `context`-Operation mit Prüfung von Kundennummer und 64-Byte-Konfiguration. Diese Container verwenden jeweils 0,5 CPU und 64 MiB, während die Wiederverwendungsläufe eine CPU pro Container zulassen. Registrierung wird separat vor der Lastphase gemessen. Der Kontrolllauf erlaubt fünf Sekunden Warteschlange und zehn Sekunden Ausführungsfrist, damit Überlast sichtbar bleibt; das Qualitätsziel liegt trotzdem bei einer Sekunde. Die längeren Diagnosefristen sind keine Empfehlung für schnelle Produktivaufrufe.

{german_table(production)}

Vor der Reserve-Korrektur wurden bei nur 100 Kunden 6 beziehungsweise 7 Anfragen fälschlich als `busy` abgewiesen. Ein deterministischer Regressionstest bestätigt den Fehler: Der letzte Platz war durch einen noch startenden Reserve-Worker belegt. Echte Aufrufe warten jetzt innerhalb ihrer Frist auf passende Bereitschaft und haben Vorrang vor dem Nachfüllen. Abbruch, unpassende Profile und andere gleichzeitig startende Worker sind geprüft. Die erneuten Messungen verwenden dieselbe Ankunftsfolge. Der verbesserte Beobachter erfasst zusätzlich noch ungebundene Reserve-Worker; ältere Speicherstichproben konnten diese auslassen. Bei 500 Python-/TypeScript-Kunden liegen die verbliebenen `busy`-Antworten nach etwa fünf Sekunden Warteschlange und damit an der Aufnahmefrist unter Überlast. C# liefert alle 100 Ergebnisse korrekt, verfehlt in diesem Kontrolllauf aber das Ein-Sekunden-Latenzziel. Diese Werte enthalten weiterhin den Start-/Verdrängungsaufwand kundengebundener Worker. Die schnellere Wiederverwendung muss erst kontrolliert in diese echte Host-Strecke integriert und dort erneut qualifiziert werden.

### Wiederholte Kundenaufrufe und größeres Speicherbudget

Diese Python-Reihen laufen jeweils zwei Minuten: Jeder registrierte Kunde ruft einmal pro Minute auf, insgesamt also zweimal. Pro Kundenzahl gibt es zwei Zufallsfolgen, sofern kein Fehler die Treppe vorher beendet. Die Budgets von 4 beziehungsweise 8 GiB erlauben bei der konfigurierten Reservierung von 64 MiB pro Worker rechnerisch bis zu 64 beziehungsweise 128 Worker. Es wurde keine zusätzliche feste Workerzahl vorgegeben. Kundenregistrierungen reservieren noch keinen Container.

{german_table(population_table)}

Das sind geprüfte Kundenzahl-Intervalle bei einem Aufruf pro Kunde und Minute. Für die Bestätigung müssen alle vorhandenen Wiederholungen bestehen, mindestens zwei Zufallsfolgen; die zusätzlichen 256-Kunden-Kontrollen unten sind einbezogen. Die obere getestete Zahl hat die Qualitätsanforderungen nicht bestanden; sie ist keine zulässige Betriebsgröße. Eine ausgelöste Beobachter-Schutzgrenze würde stattdessen die Messung ungültig machen und wird in den Rohdaten gesondert ausgewiesen.

{german_table(repeated)}

Die zweite 8-GiB-Messung mit 256 Kunden lieferte alle Aufrufe korrekt, überschritt aber die Ein-Sekunden-Grenze. Deshalb folgen vier weitere Zwei-Minuten-Kontrollen mit neuen Zufallsfolgen und wechselnder Budget-Reihenfolge (8/4 GiB, dann 4/8 GiB):

{german_table(confirmation_table)}

Jeder einzelne Kunde muss das p99-Ziel bestehen. Bei zwei Aufrufen pro Kunde ist dafür der langsamere Aufruf maßgeblich; ein gutes Gesamt-p99 allein genügt nicht. Einzelne Grenzverletzungen bleiben sichtbar und verhindern eine Bestätigung über alle Wiederholungen. Bei 256 Kunden bestehen beide Budgets drei von vier Läufen; alle Antworten sind korrekt, aber je ein Lauf überschreitet die Latenzgrenze. Die konservativ bestätigte Kundenzahl liegt deshalb für beide Budgets bei 128 unter diesem endlichen Prüfverfahren. Ein größerer Speicherpool garantiert hier keinen höheren Durchsatz.

Hier können dieselben Kunden in beiden Minuten vorkommen. Aufrufe/s sind deshalb keine Zahl neuer Kunden/s. Kalte Ausführungen zeigen vom Scheduler erkannte Starts beziehungsweise erneute Bindungen. Bei bestandenen Reihen wurden die übrigen erfolgreichen Aufrufe resident ausgeführt; abgewiesene Aufrufe sind kein Nachweis einer Wiederverwendung. Neben dem Containerbudget gilt für diesen Versuch eine separate Schutzgrenze von 6 GiB für Host und Docker-CLI-Prozesse. Vor dem Start müssen außerdem 2 GiB Reserve über der gemessenen Docker-Hintergrundlast verbleiben.

## Bereits optimiert und verifiziert

- **Scheduler:** Inaktive Kundenregistrierungen werden nicht mehr bei jedem Aufruf vollständig durchsucht. Bei 10.000 Registrierungen und vier aktiven Kunden steigt der gemessene Durchsatz von rund 6.486 auf {fmt(registry[10000]['requestsPerSecond'],0).replace(',','.')} Aufrufe/s im abschließenden Kontrolllauf. Auch 100.000 Registrierungen wurden geprüft: rund {fmt(registry[100000]['managedRegistrationDeltaMiB'],0)} MiB zusätzlicher verwalteter Speicher. Das sind ausdrücklich nicht 100.000 bediente Kunden/s. Quellcode- und NuGet-Verbrauchertests bestehen jeweils {scheduler_checks} Scheduler-Prüfungen; auch die Hosting-Regressionssuite besteht.
- **Python:** Vorbereiteter Bytecode und eine unveränderte Forkserver-Vorlage vermeiden wiederholtes Übersetzen und Importieren allgemeiner Bibliotheken. Kundendaten und Kundenmodule werden nicht in die Vorlage geladen.
- **Transport:** Direkter lokaler Docker-Engine-Kanal spart den separaten Docker-CLI-Prozess pro Container. Vergleichsläufe mit umgekehrter Reihenfolge sind dokumentiert.
- **Messprogramm:** Ressourcenverläufe werden auf Platte geschrieben; der Beobachter hält nur Zähler und Spitzenwerte im Arbeitsspeicher. Sparse-Histogramme beseitigen den hohen Grundverbrauch bei vielen Kunden mit wenigen Aufrufen. Der Allokationstest für 1.000 Kunden mit je zwei Messwerten benötigt etwa 1,21 MiB statt ungefähr 110 MiB vorab belegter Histogramm-Arrays.

Das Ergebnis spricht für einen globalen Pool, dessen Größe sich nach wartenden Aufrufen, Ausführungsart, CPU, Antwortzeiten und verfügbarem Speicher richtet. Freier Speicher ist eine Obergrenze, kein Grund, beliebig weitere kurz arbeitende Container zu starten. Normale und ausdrücklich zugelassene lange Aufrufe benötigen weiterhin getrennte Budgets und faire Zuteilung.

Die Messdaten enthalten Identitäten, Konfigurationen, Fehler, verworfene Anfragen und Aufräumkontrollen. Fehlgeschlagene Vorversuche wurden nicht entfernt. Es wurde nichts veröffentlicht oder gepusht; eine Release-Qualifikation des committed HEAD wurde nicht durchgeführt.
'''
O.mkdir(parents=True,exist_ok=True)
(O/'WeavePort-Sicherheit-und-Kundendichte.md').write_text(german)
public={k:[{a:b for a,b in row.items() if a not in ['known','remaining','tenants']} for row in rows] for k,rows in data.items()}
public.update(populationBounds=population_bounds,registryDiagnostic=registry,capacityBrackets=brackets,soakAnalysis=soaks,securityControls=sec_counts,image=identity['image'],scope='Python/Docker reuse prototype; production host controls separate; shared M4 Air; no release qualification')
(O/'WeavePort-Sicherheit-und-Kundendichte.json').write_text(json.dumps(public,indent=2)+'\n')
print('Reports written')
