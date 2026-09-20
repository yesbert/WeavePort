"""Collect real coverage from WeavePort's executable regression suites."""
import json
import os
import shutil
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / 'artifacts/analysis'
SUITES = ('WeavePort.Hosting.Tests', 'WeavePort.Gateway.Tests', 'WeavePort.Scheduling.Tests', 'WeavePort.ReuseTests')


def execute():
    results = []
    for name in SUITES:
        with (OUTPUT / (name + '.log')).open('w') as log:
            command = ['dotnet', str(ROOT / 'tests' / name / 'bin/Debug/net10.0' / (name + '.dll'))]
            if name == 'WeavePort.ReuseTests':
                command += [str(ROOT), sys.executable, shutil.which('node')]
            result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
        print((OUTPUT / (name + '.log')).read_text(), flush=True)
        results.append({'suite': name, 'exitCode': result.returncode})
    (OUTPUT / 'tests.json').write_text(json.dumps(results, indent=2))
    return int(any(item['exitCode'] != 0 for item in results))


def collect():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'tests.json').unlink(missing_ok=True)
    os.environ['DOTNET_COVERAGE_TELEMETRY_OPTOUT'] = '1'
    subprocess.run(['npm', 'ci', '--prefix', 'sdks/typescript', '--ignore-scripts'], check=True)
    subprocess.run(['npm', 'run', 'build', '--prefix', 'sdks/typescript'], check=True)
    for name in SUITES:
        subprocess.run(['dotnet', 'build', str(ROOT / 'tests' / name), '-c', 'Debug', '--nologo'], check=True)
    report = OUTPUT / 'coverage.xml'
    tool = ROOT / 'artifacts/analysis-tools/dotnet-coverage'
    subprocess.run([str(tool), 'collect', '-f', 'xml', '-o', str(report), '--',
                    sys.executable, str(Path(__file__).resolve()), '--execute'], check=True)
    results = json.loads((OUTPUT / 'tests.json').read_text())
    if len(results) != len(SUITES) or any(item['exitCode'] != 0 for item in results):
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
