"""Qualify concurrent SDKs and real shared hosts against source and frozen packages."""
import json
import shutil


def build(checkout, stage):
    for mode in ('source', 'packed'):
        for project, directory in (('WeavePort.Shared.Tests', 'shared'), ('WeavePort.ConcurrentSdkTests', 'concurrent-sdk')):
            stage(f'{directory}-{mode}-build', ['dotnet', 'publish', f'tests/{project}', '-c', 'Release',
                  f'-p:UsePackedCore={str(mode == "packed").lower()}', '-o', f'artifacts/{directory}-{mode}'])
    sample = checkout / 'artifacts/shared-example/plugins/shared-example'
    release = sample / 'releases/1'
    stage('shared-sample-build', ['dotnet', 'publish', 'examples/shared', '-c', 'Release', '-o', str(release)])
    (release / 'launch.json').write_text(json.dumps(dict(Runtime='dotnet', Arguments=['--worker'],
        MemoryMiB=256, Ownership=['Shared'], MaximumDegree=4)))
    stage('shared-sample-seal', ['python3', 'scripts/seal-installation.py', str(sample),
        'shared-example', '1', 'shared-example/v1', 'SharedExample.dll', shutil.which('dotnet')])
    (sample / 'active.txt').write_text('1\n')
    stage('sources-sample-build', ['dotnet', 'build', 'examples/sources', '-c', 'Release', '-p:UsePackedCore=true'])


def verify(checkout, env, stage):
    installed = checkout / 'artifacts/sdk-version-tests'
    original_path = env['PATH']
    env['PATH'] = str(installed / 'python/bin') + ':' + original_path
    env['WP_SHARED_PYTHON_SDK'] = '-'
    env['WP_SHARED_NODE_SDK'] = str(installed / 'node_modules/@weaveport/sdk/dist/index.js')
    for mode in ('source', 'packed'):
        env['WP_SHARED_CSHARP_FIXTURE'] = str(checkout / f'artifacts/concurrent-sdk-{mode}/WeavePort.ConcurrentSdkTests.dll')
        stage(f'shared-{mode}-regressions', ['dotnet', str(checkout / f'artifacts/shared-{mode}/WeavePort.Shared.Tests.dll'),
              str(checkout), str(installed / 'python/bin/python'), shutil.which('node')])
        dll = checkout / f'artifacts/concurrent-sdk-{mode}/WeavePort.ConcurrentSdkTests.dll'
        env['WP_CONCURRENT_SDK_DLL'] = str(dll)
        stage(f'concurrent-sdk-{mode}-wire', ['python3', 'tests/WeavePort.ConcurrentSdkTests/check.py'])
        stage(f'concurrent-sdk-{mode}-collection', ['dotnet', str(dll), 'collect'])
        stage(f'concurrent-sdk-{mode}-streams', ['dotnet', str(dll), 'streams', str(checkout)])
    for key in ('WP_SHARED_PYTHON_SDK', 'WP_SHARED_NODE_SDK', 'WP_CONCURRENT_SDK_DLL', 'WP_SHARED_CSHARP_FIXTURE'):
        env.pop(key, None)
    env['PATH'] = original_path
    stage('sources-sample-run', ['dotnet', str(checkout / 'examples/sources/bin/Release/net10.0/SourceCollector.dll')])
    stage('shared-sample-run', ['dotnet', str(checkout / 'artifacts/shared-example/plugins/shared-example/releases/1/SharedExample.dll'),
        str(checkout / 'artifacts/shared-example/plugins'), shutil.which('dotnet')])
    stage('multilingual-concurrent-wire', ['python3', 'sdks/python/tests/test_protocol.py'])
