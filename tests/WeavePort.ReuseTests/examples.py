"""Exercise the maintained author examples against freshly packed SDKs."""
from pathlib import Path
import json
import select
import shutil
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
installed = root / 'artifacts/sdk-version-tests'
example = installed / 'reuse-example.mjs'
# Same unmodified import as the maintained example; resolve the installed package.
shutil.copyfile(root / 'examples/reuse/typescript/plugin.mjs', example)
providers = {
    'csharp': [sys.argv[2], str(root / 'artifacts/reuse-example/ReusablePlugin.dll')],
    'python': [str(installed / 'python/bin/python'), '-u', str(root / 'examples/reuse/python/plugin.py')],
    'typescript': [sys.argv[3], str(example)],
}
for language, command in providers.items():
    process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        def read():
            if not select.select([process.stdout], [], [], 10)[0]:
                raise AssertionError(language + ': response timeout')
            line = process.stdout.readline()
            if not line:
                raise AssertionError(language + ': provider exited')
            return json.loads(line)
        assert read()['sessionCleanup'] == 1
        for index, tenant in enumerate(['a', 'b', 'a']):
            request = dict(type='invoke', id=str(index), operation='$sdk.call',
                           payload=dict(operation='transform', input=dict(text='hello')),
                           context=dict(tenant=tenant, configuration={}))
            process.stdin.write(json.dumps(request) + '\n')
            process.stdin.flush()
            response = read()
            assert response['type'] == 'result' and response['reusable'] is True, language
            assert response['value'] == dict(tenant=tenant, text='HELLO', bytes=5), language
            print('PASS: ' + language + ' maintained example customer ' + tenant)
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
