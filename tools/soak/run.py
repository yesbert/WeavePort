"""Run a supervised, local-only k6 soak against packed multilingual plugin fixtures."""
import argparse
import json
import math
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import tempfile
import time
import uuid

from support import ROOT, digest, providers, read_line, sample, stop, process_table, summarize_resources, verify_package, cleanup_uncertain, startup_limit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=int, default=14400)
    parser.add_argument('--clients', type=int, default=24)
    parser.add_argument('--max-concurrent-starts', type=int, help='Fixture startup slots (default: client count); does not change library defaults')
    parser.add_argument('--pace-ms', type=int, default=100)
    parser.add_argument('--p99-ms', type=float, default=2000)
    parser.add_argument('--max-rss-mib', type=float, default=8192)
    parser.add_argument('--max-growth-mib', type=float, default=2048)
    parser.add_argument('--growth-after', type=int, default=300)
    parser.add_argument('--sample-seconds', type=float, default=5)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--inject-failure', action='store_true', help='Negative control: fail payload validation')
    args = parser.parse_args()
    try:
        args.max_concurrent_starts = startup_limit(args.clients, args.max_concurrent_starts)
    except ValueError as error:
        parser.error(str(error))
    if not (1 <= args.seconds <= 86400 and 3 <= args.clients <= 48 and 1 <= args.pace_ms <= 60000
            and args.p99_ms > 0 and args.max_rss_mib > 0 and args.max_growth_mib > 0
            and args.growth_after >= 0 and 0.2 <= args.sample_seconds <= 60):
        parser.error('Invalid bounded soak configuration')
    if not all(math.isfinite(value) for value in (args.p99_ms, args.max_rss_mib, args.max_growth_mib, args.sample_seconds)):
        parser.error('Resource and latency limits must be finite')
    if platform.system() not in ('Darwin', 'Linux'):
        parser.error('Supervisor requires macOS/Linux process groups; only macOS is currently qualified')
    executables = {name: shutil.which(name) for name in ('k6', 'dotnet', 'node')}
    if not all(executables.values()):
        parser.error('Install k6 and repository runtimes first')
    output = (args.output or ROOT / 'artifacts/soak' / (time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])).resolve()
    output.mkdir(parents=True, exist_ok=False)
    config = providers(output, args.clients, executables['dotnet'], executables['node'], args.max_concurrent_starts)
    record = {'status': 'running', 'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'settings': {key: str(value) if isinstance(value, Path) else value for key, value in vars(args).items()},
              'platform': platform.platform(), 'artifacts': {str(Path(p).relative_to(ROOT)): h for p, h in config['Artifacts'].items()},
              'runtimes': {name: subprocess.check_output([path, 'version' if name == 'k6' else '--version'], text=True).strip() for name, path in executables.items()},
              'scope': 'Native trusted stdio providers, loopback gRPC; sampled owned process-group RSS includes k6, shared pages may repeat; no sandbox or capacity qualification.'}
    record['packages'] = {p.name: digest(p) for p in (ROOT / 'artifacts/packages').glob('WeavePort.*.nupkg')}
    record['tooling'] = {str(p.relative_to(ROOT)): digest(p) for p in [ROOT / 'tests/soak/workload.js', Path(__file__), ROOT / 'tools/soak/support.py']}
    (output / 'source.patch').write_bytes(subprocess.check_output(['git', 'diff', 'HEAD'], cwd=ROOT))
    gateway = k6 = None
    started = time.monotonic()
    resources = output / 'resources.jsonl'
    reason = None
    baseline_rss = None
    forced = {'k6': False, 'gateway': False}
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f'Signal {signum}')
    signal.signal(signal.SIGTERM, interrupted)
    try:
        with (output / 'gateway.log').open('w') as gateway_log, (output / 'k6.log').open('w') as k6_log, resources.open('w') as samples:
            gateway = subprocess.Popen([executables['dotnet'], str(ROOT / 'artifacts/sdk-worker-host/WeavePort.WorkerHost.dll')],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=gateway_log, text=True, bufsize=1, start_new_session=True,
                env={'DOTNET_ROLL_FORWARD': 'LatestPatch'})
            gateway.stdin.write(json.dumps(config) + '\n'); gateway.stdin.flush()
            ready = read_line(gateway, 30)
            record['loaded'] = ready['Loaded']
            record['workerHostSha256'] = digest(ROOT / 'artifacts/sdk-worker-host/WeavePort.WorkerHost.dll')
            gateway.stdin.write('warmup\n'); gateway.stdin.flush()
            warm = read_line(gateway, 180)
            if warm.get('warmed') != args.clients:
                raise RuntimeError('Incomplete tenant warmup')
            record['warmedTenants'] = warm['warmed']
            # Match host/client/gateway and the C# author runtime to packed bytes.
            for name, checksum in ready['Loaded'].items():
                verify_package(ROOT / 'artifacts/packages', name, checksum)
            verify_package(ROOT / 'artifacts/packages', 'WeavePort.Sdk',
                           digest(ROOT / 'artifacts/sdk-csharp/WeavePort.Sdk.dll'))
            credentials = dict(zip((b['Tenant'] for b in config['Bindings']), ready['Credentials']))
            private = {'address': ready['Address'].removeprefix('http://'),
                       'bindings': [dict(b, Credential=credentials[b['Tenant']]) for b in config['Bindings']],
                       'seconds': args.seconds, 'paceMs': args.pace_ms, 'p99Ms': args.p99_ms, 'injectFailure': args.inject_failure}
            with tempfile.TemporaryDirectory(prefix='weaveport-soak-') as private_dir:
                secret = Path(private_dir) / 'bindings.json'
                secret.write_text(json.dumps(private)); secret.chmod(0o600)
                env = os.environ.copy()
                env.update(WP_SOAK_CONFIG=str(secret), WP_SOAK_SUMMARY=str(output / 'k6-summary.json'))
                k6 = subprocess.Popen([executables['k6'], 'run', '--quiet', '--address', '127.0.0.1:0', str(ROOT / 'tests/soak/workload.js')],
                    cwd=ROOT, env=env, stdout=k6_log, stderr=subprocess.STDOUT, start_new_session=True)
                print('Soak evidence: ' + str(output), flush=True)
                record['pids'] = {'gateway': gateway.pid, 'k6': k6.pid}
                (output / 'result.json').write_text(json.dumps(record, indent=2))
                while k6.poll() is None:
                    if (output / 'STOP').exists():
                        reason = 'operator-stop'; break
                    if gateway.poll() is not None:
                        reason = 'gateway-exited'; break
                    item = sample(gateway, k6)
                    item['elapsedSeconds'] = time.monotonic() - started
                    samples.write(json.dumps(item) + '\n'); samples.flush()
                    rss = sum(p['rssKiB'] for p in item['processes']) / 1024
                    if rss > args.max_rss_mib:
                        reason = 'rss-limit'; break
                    if item['elapsedSeconds'] >= args.growth_after:
                        if baseline_rss is None: baseline_rss = rss
                        if rss - baseline_rss > args.max_growth_mib:
                            reason = 'rss-growth-limit'; break
                    snapshot = item['gateway']['snapshot']
                    if cleanup_uncertain(snapshot):
                        reason = 'cleanup-uncertain'; break
                    if item['elapsedSeconds'] > args.seconds + 60:
                        reason = 'run-deadline'; break
                    time.sleep(args.sample_seconds)
                if reason is None and k6.poll() != 0:
                    reason = 'k6-failed'
                record['k6ExitCode'] = k6.poll()
                forced['k6'] = stop(k6)
                record['k6ExitCode'] = k6.returncode
            forced['gateway'] = stop(gateway, 'quit')
        summary_path = output / 'k6-summary.json'
        if not summary_path.exists():
            reason = reason or 'missing-k6-summary'
        else:
            summary = json.loads(summary_path.read_text())
            metrics = summary['metrics']
            for i in range(args.clients):
                count = metrics.get(f'completed_operations{{tenant:tenant-{i}}}', {}).get('values', {}).get('count', 0)
                if count <= 0: reason = reason or 'unserved-tenant'
            if any(not value['ok'] for metric in metrics.values() for value in metric.get('thresholds', {}).values()):
                reason = reason or 'failed-threshold'
        if gateway.returncode != 0:
            reason = reason or 'gateway-cleanup-failed'
        if (output / 'workers').exists() and any((output / 'workers').iterdir()):
            reason = reason or 'retained-workspace'
        if any(digest(path) != checksum for path, checksum in config['Artifacts'].items()):
            reason = reason or 'fixture-changed-during-run'
        if any(digest(ROOT / 'artifacts/packages' / name) != checksum for name, checksum in record['packages'].items()):
            reason = reason or 'package-changed-during-run'
        if digest(ROOT / 'artifacts/sdk-worker-host/WeavePort.WorkerHost.dll') != record['workerHostSha256']:
            reason = reason or 'worker-host-changed-during-run'
        record['status'] = 'passed' if reason is None else 'failed'
    except BaseException as error:
        reason = reason or type(error).__name__
        record['status'] = 'failed'
    finally:
        cleanup_errors = []
        for name, process, command in [('k6', k6, None), ('gateway', gateway, 'quit')]:
            try:
                forced[name] = stop(process, command) or forced[name]
            except Exception as error:
                forced[name] = True
                cleanup_errors.append(name + ': ' + type(error).__name__)
        record['forcedCleanup'] = forced
        record['cleanupErrors'] = cleanup_errors
        remaining = [row for row in process_table() if row['group'] in [p.pid for p in (gateway, k6) if p is not None]]
        record['remainingProcesses'] = remaining
        if remaining or any(record['forcedCleanup'].values()):
            record['status'] = 'failed'; reason = reason or 'incomplete-cleanup'
        record['stopReason'] = reason or 'completed'
        record['elapsedSeconds'] = time.monotonic() - started
        if resources.exists(): record['resources'] = summarize_resources(resources)
        (output / 'result.json').write_text(json.dumps(record, indent=2))
        print(f"Soak {record['status']}: {record['stopReason']} — {output}", flush=True)
    return 0 if record['status'] == 'passed' else 1


if __name__ == '__main__':
    raise SystemExit(main())
