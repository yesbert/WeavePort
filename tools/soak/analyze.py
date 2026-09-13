"""Summarize completed or interrupted soak evidence without changing its outcome."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    args = parser.parse_args()
    result = json.loads((args.run / 'result.json').read_text())
    if result['status'] == 'running':
        print('Run is still active. Inspect resources.jsonl and k6.log; k6 summary is not available yet.')
        return
    summary = args.run / 'k6-summary.json'
    metrics = json.loads(summary.read_text())['metrics'] if summary.exists() else {}
    lines = ['# k6 soak result', '', f"Status: **{result['status']}**; stop reason: **{result.get('stopReason', 'running')}**.", '',
             f"Elapsed: {result.get('elapsedSeconds', 0):.1f} seconds. Source: `{result['sourceCommit']}`. See result.json for exact artifacts and configuration.", '',
             result['scope'], '', '| Metric | Value |', '| --- | ---: |']
    for name in ('completed_operations', 'unexpected_errors', 'expected_faults', 'operation_ms'):
        values = metrics.get(name, {}).get('values', {})
        for key in ('count', 'rate', 'avg', 'p(95)', 'p(99)', 'max'):
            if key in values:
                rendered = f'{values[key]:.8f}' if key == 'rate' else f'{values[key]:.3f}'
                lines.append(f'| {name} {key} | {rendered} |')
        if name == 'unexpected_errors' and 'passes' in values:
            lines.append(f"| unexpected error samples | {values['passes']:.0f} |")
    resources = result.get('resources', {})
    lines += ['', f"Resource samples: {resources.get('samples', 0)}; peak summed RSS: {resources.get('peakSummedRssMiB', 0):.1f} MiB.", '',
              '| Tenant | Completed | Unexpected error rate | p99 ms |', '| --- | ---: | ---: | ---: |']
    for i in range(result['settings']['clients']):
        suffix = f'{{tenant:tenant-{i}}}'
        def value(name, key): return metrics.get(name + suffix, {}).get('values', {}).get(key, 0)
        lines.append(f"| tenant-{i} | {value('completed_operations', 'count'):.0f} | {value('unexpected_errors', 'rate'):.8f} | {value('operation_ms', 'p(99)'):.2f} |")
    first, last = resources.get('first'), resources.get('last')
    if first and last:
        lines += ['', '| Coordinator observation | First | Last |', '| --- | ---: | ---: |']
        for key in ('managedBytes', 'allocatedBytes', 'handles', 'gen0', 'gen1', 'gen2'):
            lines.append(f"| {key} | {first['gateway'][key]} | {last['gateway'][key]} |")
    lines += ['', 'Use resources.jsonl for chronological RSS, CPU, GC and worker-accounting trends. k6.log contains per-tenant minute windows (completed operations and maximum latency). Whole-run percentiles can conceal transient stalls; sampled RSS growth alone is not proof of a leak.', '',
              'Latency covers complete validated operations; explicit cancellation includes drain/confirmation. Expected injected provider errors are separate from unexpected errors. A short successful pilot does not qualify multi-hour stability.', '']
    text = '\n'.join(lines)
    (args.run / 'report.md').write_text(text)
    print(text)


if __name__ == '__main__':
    main()
