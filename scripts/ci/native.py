"""Run the existing packaged native adapter fixtures without Bash dependencies."""
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
os.chdir(ROOT)
output = ROOT / 'artifacts/native-ci'
output.mkdir(parents=True, exist_ok=True)


def run(*args):
    subprocess.run([str(arg) for arg in args], check=True)


for project in ('Abstractions', 'Sdk.Client', 'Hosting', 'Testing'):
    run('dotnet', 'pack', f'src/WeavePort.{project}', '-c', 'Release', '-o', 'artifacts/packages', '--nologo')
run(sys.executable, 'scripts/copy-package-dependencies.py')
run('dotnet', 'publish', 'plugins/csharp', '-c', 'Release', '--self-contained', 'false', '-o', 'artifacts/local/csharp', '--nologo')
run('dotnet', 'restore', 'tests/WeavePort.Local.Tests', '--force', '--no-cache')
run('dotnet', 'build', 'tests/WeavePort.Local.Tests', '-c', 'Release', '--no-restore', '--nologo')
configuration = {
    'Dotnet': shutil.which('dotnet'), 'Python': sys.executable, 'Node': shutil.which('node'),
    'Csharp': str(ROOT / 'artifacts/local/csharp/WeavePort.SamplePlugin.dll'),
    'PythonScript': str(ROOT / 'plugins/python/worker.py'),
    'TypeScriptScript': str(ROOT / 'plugins/typescript/worker.ts'),
    'SelfContained': False,
}
(output / 'platform.json').write_text(json.dumps({'os': platform.platform(), 'architecture': platform.machine(),
    'revision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()}, indent=2))
for transport in (('stdio',) if os.name == 'nt' else ('stdio', 'socket')):
    evidence = output / transport
    evidence.mkdir()
    config = {**configuration, 'WorkspaceRoot': str(evidence / 'workspaces'), 'UseUnixSocket': transport == 'socket'}
    config_path = evidence / 'config.json'
    config_path.write_text(json.dumps(config, indent=2))
    run('dotnet', 'tests/WeavePort.Local.Tests/bin/Release/net10.0/WeavePort.LocalDemo.dll', 'verify', config_path, evidence)
    report = json.loads((evidence / 'verification.json').read_text())
    if report['failures'] or not report['checks'] or not all(check['passed'] for check in report['checks']):
        raise SystemExit('Native verification did not pass')
