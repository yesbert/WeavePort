"""Artifact link validation must reject missing targets, including archive contents."""
from pathlib import Path
import sys
import subprocess
import tempfile
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/distribution'))
from documentation import check_links, validate_package

check_links({'docs/start.md': '[next](../guide.md#usage)'}, {'docs/start.md', 'guide.md'})
for href in ('missing.md', '../outside.md', 'missing%20guide.md'):
    try:
        check_links({'README.md': '[guide](' + href + ')'}, {'README.md'})
    except ValueError:
        continue
    raise AssertionError('Missing target accepted: ' + href)
with tempfile.TemporaryDirectory() as root:
    package = Path(root) / 'test.nupkg'
    with zipfile.ZipFile(package, 'w') as archive:
        archive.writestr('PACKAGE.md', '[guide](missing.md)')
    try:
        validate_package(package)
    except ValueError:
        pass
    else:
        raise AssertionError('Broken packaged readme accepted')
print('PASS: relative links resolve within artifact layouts')
print('PASS: missing plain, escaping and encoded targets fail')
print('PASS: actual NuGet readme contents are validated')

# Validate only maintained files; generated dependency caches are not documentation.
repository = Path(__file__).resolve().parents[2]
names = set(subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard'],
                                    cwd=repository, text=True).splitlines())
names = {name for name in names if (repository / name).is_file()}
contents = {name: (repository / name).read_text() for name in names
            if name.endswith('.md') and not name.startswith(('.agents/', '.claude/'))}
check_links(contents, names)
print('PASS: maintained repository documentation targets exist')
