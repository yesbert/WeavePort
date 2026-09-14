"""Collect real coverage from WeavePort's executable regression suites."""
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / 'artifacts/analysis'
SUITES = ('WeavePort.Hosting.Tests', 'WeavePort.Gateway.Tests')


def execute():
    results = []
    for name in SUITES:
        with (OUTPUT / (name + '.log')).open('w') as log:
            result = subprocess.run(['dotnet', str(ROOT / 'tests' / name / 'bin/Debug/net10.0' / (name + '.dll'))], stdout=log, stderr=subprocess.STDOUT)
        print((OUTPUT / (name + '.log')).read_text(), flush=True)
        results.append({'suite': name, 'exitCode': result.returncode})
    (OUTPUT / 'tests.json').write_text(json.dumps(results, indent=2))
    return int(any(item['exitCode'] != 0 for item in results))


def collect():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'tests.json').unlink(missing_ok=True)
    os.environ['DOTNET_COVERAGE_TELEMETRY_OPTOUT'] = '1'
    for name in SUITES:
        subprocess.run(['dotnet', 'build', str(ROOT / 'tests' / name), '-c', 'Debug', '--nologo'], check=True)
    report = OUTPUT / 'coverage.xml'
    tool = ROOT / 'artifacts/analysis-tools/dotnet-coverage'
    command = f'"{sys.executable}" "{Path(__file__).resolve()}" --execute'
    subprocess.run([str(tool), 'collect', command, '-f', 'xml', '-o', str(report)], check=True)
    results = json.loads((OUTPUT / 'tests.json').read_text())
    if len(results) != len(SUITES) or any(item['exitCode'] != 0 for item in results):
        raise SystemExit('Regression execution failed')
    tree = ET.parse(report)
    covered = sum(1 for element in tree.iter() if element.get('covered') == 'yes')
    if covered == 0:
        raise SystemExit('Coverage report has no covered ranges')
    print(f'PASS: {len(results)} executable suites, {covered} covered ranges')


if __name__ == '__main__':
    os.chdir(ROOT)
    if '--execute' in sys.argv:
        raise SystemExit(execute())
    collect()
