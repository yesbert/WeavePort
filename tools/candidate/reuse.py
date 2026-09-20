"""Build and qualify source/packed reuse consumers without changing frozen packages."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile


def build(checkout, stage):
    stage('reuse-source-build', ['dotnet', 'publish', 'tests/WeavePort.ReuseTests', '-c', 'Release',
          '-p:UsePackedCore=false', '-o', 'artifacts/reuse-source'])
    stage('reuse-packed-build', ['dotnet', 'publish', 'tests/WeavePort.ReuseTests', '-c', 'Release',
          '-p:UsePackedCore=true', '-o', 'artifacts/reuse-packed'])
    stage('reuse-example-build', ['dotnet', 'publish', 'examples/reuse/csharp', '-c', 'Release',
          '-o', 'artifacts/reuse-example'])
    version = json.loads((checkout / 'compatibility/local-v1.json').read_text())['HostPackages']['WeavePort.Hosting']
    for name in ['WeavePort.Hosting', 'WeavePort.Sdk']:
        with zipfile.ZipFile(checkout / 'artifacts/packages' / f'{name}.{version}.nupkg') as package:
            expected = hashlib.sha256(package.read(f'lib/net10.0/{name}.dll')).digest()
        for mode in ['source', 'packed']:
            actual = hashlib.sha256((checkout / f'artifacts/reuse-{mode}/{name}.dll').read_bytes()).digest()
            assert actual == expected, f'{name}: {mode} consumer did not load the qualified package bytes'


def verify(checkout, stage):
    installed = checkout / 'artifacts/sdk-version-tests'
    for mode in ['source', 'packed']:
        stage(f'{mode}-reuse-regressions', [shutil.which('dotnet'),
              str(checkout / f'artifacts/reuse-{mode}/WeavePort.ReuseTests.dll'), str(checkout),
              str(installed / 'python/bin/python'), shutil.which('node'), '-',
              str(installed / 'node_modules/@weaveport/sdk/dist/index.js')])
    stage('reuse-author-examples', ['python3', 'tests/WeavePort.ReuseTests/examples.py',
          str(checkout), shutil.which('dotnet'), shutil.which('node')])
