# Security and capacity matrix

This is a source-built Python/Docker experiment on the MacBook Air, separate from the delivered SDK benchmark and from the production host path. It performs real alternating Python plugin modules, but its permission callback is a local stub. It does not include the production .NET protocol, host callbacks, application database, or network ingress. A distinct synthetic customer identity per successful call is the default. With `callsPerCustomer`, a customer counts as fully served only after that many correct calls.

## Execution profiles

- `fresh`: destroy the container after each invocation.
- `process`: retain the container, execute a fresh Python interpreter per invocation.
- `forkserver`: retain the container and a pristine interpreter template, fork a new child for each invocation. Only approved standard libraries are preloaded; customer plugin imports happen after dropping UID/GID/capabilities. The template is created before traffic and never receives request/result bodies. Child results are bounded JSON bytes, never untrusted pickle.
- `trusted`: retain the interpreter and run cooperative cleanup. Negative controls deliberately demonstrate retained globals, mutable defaults, context variables, logger state and cache entries. This profile does not provide a cross-customer memory boundary.

The experimental fork supervisor runs as container-root with only SETUID, SETGID and KILL added to an otherwise empty capability set. Plugin children run as UID/GID 65532 without effective capabilities. The template socket directory is private to root. These are testable defenses, not certification against hostile code or shared-kernel vulnerabilities. Unreviewed code must retain the stronger execution policy. Host authorization must be checked outside plugin memory in every profile: the local permission stub is deliberately forgeable.

The image precompiles immutable Python sources to bytecode. This avoids compilation on every fresh import under a read-only filesystem. It does not preload customer modules into the template. The [Python multiprocessing documentation](https://docs.python.org/3/library/multiprocessing.html) describes the fork-server model; the customer-data boundary also depends on this fixture's topology, privilege drop and result handling.

## Transport experiment

CLI transport keeps one `docker run -i` helper process per container. `transport: "engine"` uses the local Unix Docker Engine socket directly, with bounded stdout/stderr demultiplexing. The socket is accessible only to the trusted benchmark host and is never mounted in a plugin. Container settings are inspected by the security suite in addition to behavioral tests. The wire format follows the [Docker Engine attach API](https://docs.docker.com/reference/api/engine/version/v1.54/).

## Reproduction

```sh
docker build -f benchmarks/WeavePort.Reuse/Matrix.Dockerfile -t weaveport-reuse-matrix:1 benchmarks/WeavePort.Reuse
python3 tools/performance/matrix_security.py artifacts/reuse-matrix/security-new
python3 tools/performance/matrix_security.py artifacts/reuse-matrix/security-engine-new --engine
python3 tools/performance/reuse_matrix.py /path/to/configurations.json artifacts/reuse-matrix/run-new
```

Every output directory must be new. Configurations are a JSON array. Example:

```json
[
  {"mode":"forkserver","workers":8,"cpus":"1","seconds":60,"transport":"engine","arrival":"poisson","rate":500},
  {"mode":"trusted","workers":8,"cpus":"1","seconds":60,"arrival":"burst","rate":1000,"callsPerCustomer":3}
]
```

`arrival: "saturated"` measures continuously occupied serial lanes. Poisson arrivals and one-second batches are offered independently of completion, through a bounded shared queue with an admission deadline. Late/overflow calls are reported as drops. The one-second p99 quality criterion requires zero drops and zero unexpected errors; a completed overload measurement is not a successful capacity point. Latency includes queue waiting, startup when needed, transport and cleanup before the response. Container retirement is included in turnover and aggregate throughput. The default is one call per container at a time.

Workloads include small/64 KiB/256 KiB payloads, bounded CPU, simulated I/O, memory allocation, a short/CPU/I/O mixture, and injected hangs/crashes/cleanup failures. Expected faults must poison their container; the following normal customers must still return correct results. Histograms use upper bucket edges with at most 1% quantization; successful request bodies are not retained in unbounded lists.

The supervisor preserves live resource samples, configuration, image/source identity, aggregate results, bounded failure examples and exact owned container names. Memory is sampled, so short peaks can be missed. Container working set subtracts inactive file cache; raw cgroup memory is retained too. Host and Docker CLI memory are separate and must not be added to cgroup numbers as though they came from one operating system. The 32-worker CLI / 128-worker direct-Engine and 8-GiB reservation experiment guards protect this shared laptop and is not a production worker cap. A `STOP` file in the output root stops at the next observer check. Only exact containers created by that run are removed.

## Refining an operating boundary

```sh
python3 tools/performance/matrix_capacity.py artifacts/reuse-matrix/refinement-new --mode forkserver --low 650 --high 1100 --steps 3 --seconds 60
```

Every tested rate receives two independent seeded trials. A point must have no drops/unexpected errors, all offered calls correct, aggregate p99 <= 1 second, and p99 <= 1 second in sufficiently populated time windows. Mean backlog in the last third must not exceed the first third by more than the larger of two calls per worker or 100 milliseconds of offered work. This explicit engineering margin rejects a queue that is still accumulating while a short aggregate p99 looks acceptable. Bisection returns a tested passing/rejected interval, or explicitly reports that the supplied bracket did not establish a boundary. Long soak tests validate an operating rate below that interval separately.

The ready queue admits at most one in-flight invocation per customer/plugin key. Waiting work for a busy key does not take another container lane. Different plugins of the same customer can still run concurrently. Early unconstrained multiple-call pilot results remain historical measurements and must not be used to qualify this serialized workload. Production fairness and heavy admission are tested through the actual .NET scheduler; the runtime experiment's ready-key FIFO is not a replacement for that coordinator.

## Kernel IPC finding and retirement rule

A dedicated negative control reproduced cross-customer reads of System V shared memory after both fresh-interpreter and fork-server child replacement. These objects live in the container's IPC namespace, beyond Python heap and temporary-file cleanup. The runtime coordinator now checks `/proc/sysvipc/{shm,sem,msg}` and `/dev/mqueue` after the invocation. Any residue makes the container non-reusable; its exact instance is destroyed before a different customer can use the lane. Both transports explicitly request a private IPC namespace. Security checks cover shared memory, semaphores, System V/POSIX message queues, and denied process-keyring creation.

The reproduced leak is in the experimental reuse fixture, not in the production scheduler's unchanged rule that a used worker never switches customers. Pre-audit performance runs remain diagnostic history, not qualified results for the corrected boundary. Fresh-process isolation alone is insufficient when shared kernel resources survive. See the Linux manual's [shared-memory lifetime rules](https://www.man7.org/linux/man-pages/man2/shmctl.2.html) and [message-queue persistence](https://man7.org/linux/man-pages/man7/mq_overview.7.html).

The fork-child response and process exit share one 1.5-second invocation deadline. A separate 100-ms post-response exit limit produced false failures at 64 concurrent containers and was removed. Deterministic 250-ms and 2-second exit-delay controls verify acceptance within the overall budget and retirement beyond it. `pluginsPerCustomer: 1` selects a single plugin per customer in grouped workloads; the default alternates two modules.

The later 128-container I/O probes require a preflight: the full configured pool reservation must fit beneath Docker VM memory minus currently sampled raw container memory and a further 2-GiB margin. Initial scaling was bounded at 64 containers/4 GiB. These probe guards are distinct from the production memory-admission policy.

Resource history is streamed as JSON Lines. The observer retains only scalar resource peaks/counts, so a long run does not keep all per-container sample dictionaries in memory. Unit controls verify earlier peaks, missing-versus-zero memory and constant summary storage.

Grouped saturation reserves queue space for two full customer groups per worker (at most 10,000 calls), rather than only two calls per worker. Otherwise a ten-call, one-plugin customer can occupy most pending slots and artificially leave worker lanes idle. The interrupted smaller-queue run is diagnostic history; the corrected grouped series qualifies this workload.

In `trusted` mode the audit still shares the plugin interpreter and is cooperative; malicious in-process code could subvert it. The fork-server audit runs in the separate privilege-separated supervisor. Neither a plugin-reported success nor the trusted in-process audit creates a hostile-code boundary.
