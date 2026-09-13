"""Prepare offline consumer guidance and reject unresolved artifact-relative links."""
import os
from pathlib import Path, PurePosixPath
import posixpath
import re
import subprocess
from urllib.parse import unquote, quote, urlsplit
import zipfile

LINK = re.compile(r'\[([^\]\n]*)\]\(([^)\n]+)\)')
REPOSITORY = Path(__file__).resolve().parents[2]
REMOTE = 'https://github.com/yesbert/WeavePort'


def relative_target(source, href):
    parsed = urlsplit(href.strip('<>'))
    if parsed.scheme or parsed.netloc or not parsed.path:
        return None
    return posixpath.normpath(posixpath.join(posixpath.dirname(source), unquote(parsed.path)))


def check_links(contents, names):
    failures = []
    for source, text in contents.items():
        for match in LINK.finditer(text):
            target = relative_target(source, match[2])
            if target is not None and target not in names and not any(name.startswith(target + '/') for name in names):
                failures.append(f'{source}: {match[2]}')
    if failures:
        raise ValueError('Missing relative documentation targets: ' + '; '.join(failures))


def validate(root):
    names = {path.relative_to(root).as_posix() for path in root.rglob('*') if path.is_file()}
    contents = {name: (root / name).read_text() for name in names if name.endswith('.md')}
    check_links(contents, names)


def validate_package(path):
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        check_links({name: archive.read(name).decode() for name in names if name.endswith('.md')}, names)


def prepare(bundle, commit):
    tracked = set(subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', commit],
                                         cwd=REPOSITORY, text=True).splitlines())
    # Each copied templates directory has its own guidance closure. Top-level
    # guidance points at the bundled templates; copying templates stays sufficient.
    for layout in (bundle, bundle / 'templates'):
        pending = [(f'docs/{name}', Path('docs', name)) for name in
                   ('native-operations.md', 'embedded-coordinator.md', 'runtime-diagnostics.md')]
        if layout == bundle / 'templates':
            pending += [(path.relative_to(layout).as_posix(), path.relative_to(layout))
                        for path in (layout / 'samples').rglob('*.md')]
        visited = set()
        while pending:
            source, destination = pending.pop()
            if source in visited:
                continue
            visited.add(source)
            text = subprocess.check_output(['git', 'show', f'{commit}:{source}'], cwd=REPOSITORY, text=True)

            def rewrite(match):
                target = relative_target(source, match[2])
                if target is None:
                    return match[0]
                if target not in tracked and not any(name.startswith(target + '/') for name in tracked):
                    raise ValueError(f'Missing repository documentation target: {source}: {target}')
                if target.startswith('docs/') and target.endswith('.md'):
                    local = Path(target)
                    pending.append((target, local))
                elif target.startswith('samples/') and (layout / (target if layout.name == 'templates' else 'templates/' + target)).exists():
                    local = Path(target) if layout.name == 'templates' else Path('templates', target)
                else:
                    return f'[{match[1]} (repository context)]({REMOTE}/blob/{commit}/{quote(target, safe="/")})'
                href = os.path.relpath(local, destination.parent).replace(os.sep, '/')
                fragment = urlsplit(match[2]).fragment
                return f'[{match[1]}]({href}' + (f'#{fragment}' if fragment else '') + ')'

            output = layout / destination
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(LINK.sub(rewrite, text))
    validate(bundle)
    validate(bundle / 'templates')
    for path in (bundle / 'packages/nuget').glob('WeavePort.*.nupkg'):
        validate_package(path)
