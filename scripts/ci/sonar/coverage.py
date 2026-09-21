"""Collect real coverage from WeavePort's executable regression suites."""
import json
import os
import shutil
import signal
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / 'artifacts/analysis'
SUITES = ('WeavePort.Hosting.Tests', 'WeavePort.Gateway.Tests', 'WeavePort.Scheduling.Tests', 'WeavePort.ReuseTests', 'WeavePort.Optional.Tests', 'WeavePort.Shared.Tests', 'WeavePort.ConcurrentSdkTests')


def run_suite(command, log, timeout=180):
    # Each suite owns its process group, including native worker children. A
    # deadlock must produce a bounded failure and retained diagnostics in CI.
    with subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True) as process:
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            print(f'FAIL: suite exceeded the {timeout}-second watchdog; stopping its owned process group.', file=log, flush=True)
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            return 124


def execute():
    results = []
    cases = [(name, None) for name in SUITES if name != 'WeavePort.ConcurrentSdkTests'] + [('WeavePort.ConcurrentSdkTests', mode) for mode in ('collect', 'streams')]
    for name, mode in cases:
        label = name + ('-' + mode if mode else '')
        with (OUTPUT / (label + '.log')).open('w') as log:
            command = ['dotnet', str(ROOT / 'tests' / name / 'bin/Debug/net10.0' / (name + '.dll'))]
            if name in ('WeavePort.ReuseTests', 'WeavePort.Shared.Tests'):
                command += [str(ROOT), sys.executable, shutil.which('node')]
            if name == 'WeavePort.Optional.Tests':
                command += [str(ROOT), shutil.which('dotnet')]
            if mode:
                command += [mode, str(ROOT)]
            exit_code = run_suite(command, log)
        print((OUTPUT / (label + '.log')).read_text(), flush=True)
        results.append({'suite': label, 'exitCode': exit_code})
        (OUTPUT / 'tests.json').write_text(json.dumps(results, indent=2))
        if exit_code == 124:
            return 1
    with (OUTPUT / 'concurrent-sdk-wire.log').open('w') as log:
        wire_exit = run_suite([sys.executable, 'tests/WeavePort.ConcurrentSdkTests/check.py'], log)
    print((OUTPUT / 'concurrent-sdk-wire.log').read_text(), flush=True)
    results.append({'suite': 'concurrent-sdk-wire', 'exitCode': wire_exit})
    (OUTPUT / 'tests.json').write_text(json.dumps(results, indent=2))
    return int(any(item['exitCode'] != 0 for item in results))


def collect():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'tests.json').unlink(missing_ok=True)
    os.environ['DOTNET_COVERAGE_TELEMETRY_OPTOUT'] = '1'
    subprocess.run(['npm', 'ci', '--prefix', 'sdks/typescript', '--ignore-scripts'], check=True)
    subprocess.run(['npm', 'run', 'build', '--prefix', 'sdks/typescript'], check=True)
    subprocess.run(['dotnet', 'publish', 'examples/gateway/Worker', '-c', 'Debug', '-p:UsePackedCore=false',
                    '-o', 'artifacts/optional/worker', '--nologo'], check=True)
    for name in SUITES:
        subprocess.run(['dotnet', 'build', str(ROOT / 'tests' / name), '-c', 'Debug', '-p:UsePackedCore=false', '--nologo'], check=True)
    os.environ['WP_SHARED_CSHARP_FIXTURE'] = str(ROOT / 'tests/WeavePort.ConcurrentSdkTests/bin/Debug/net10.0/WeavePort.ConcurrentSdkTests.dll')
    os.environ['WP_CONCURRENT_SDK_DLL'] = os.environ['WP_SHARED_CSHARP_FIXTURE']
    report = OUTPUT / 'coverage.xml'
    tool = ROOT / 'artifacts/analysis-tools/dotnet-coverage'
    subprocess.run([str(tool), 'collect', '-f', 'xml', '-o', str(report), '--',
                    sys.executable, str(Path(__file__).resolve()), '--execute'], check=True)
    results = json.loads((OUTPUT / 'tests.json').read_text())
    if len(results) != len(SUITES) + 2 or any(item['exitCode'] != 0 for item in results):
        raise SystemExit('Regression execution failed')
    tree = ET.parse(report)
    covered = sum(1 for element in tree.iter() if element.get('covered') == 'yes')
    hosting = [module for module in tree.iter('module') if module.get('name') == 'WeavePort.Hosting.dll']
    if covered == 0 or not hosting or not any(int(module.get('lines_covered', '0')) > 0 for module in hosting):
        raise SystemExit('Coverage report has no covered host implementation')
    print(f'PASS: {len(results)} executable suites, {covered} covered ranges')


if __name__ == '__main__':
    os.chdir(ROOT)
    if '--execute' in sys.argv:
        raise SystemExit(execute())
    collect()
