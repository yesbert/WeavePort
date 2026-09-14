"""Restricted SSH archive receiver; install root-owned outside the deploy account."""
import hashlib
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tarfile
import tempfile

MAX_ARCHIVE = 32 * 1024 * 1024
MAX_FILES = 4000
MAX_CONTENT = 128 * 1024 * 1024
ALLOWED = {'.html', '.md', '.txt', '.json', '.xml', '.yml', '.png', '.svg', '.ico', '.css', '.js', '.map', '.woff', '.woff2', '.ttf'}


def extract(archive, destination):
    with tarfile.open(archive, 'r:gz') as tar:
        members = tar.getmembers()
        if len(members) > MAX_FILES or sum(m.size for m in members) > MAX_CONTENT:
            raise ValueError('Site exceeds deployment limits')
        seen = set()
        for member in members:
            path = Path(member.name)
            if path.is_absolute() or '..' in path.parts or not (member.isfile() or member.isdir()):
                raise ValueError('Unsafe archive member')
            if path.as_posix() in seen:
                raise ValueError('Duplicate archive member')
            seen.add(path.as_posix())
            if any(part.startswith('.') for part in path.parts) or (member.isfile() and path.suffix not in ALLOWED):
                raise ValueError('Unexpected site file')
        # Never preserve archive permissions, owners, links or special files.
        for member in members:
            target = destination / member.name
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with tar.extractfile(member) as source, target.open('wb') as output:
                    shutil.copyfileobj(source, output)
                target.chmod(0o644)
    for required in ('index.html', 'llms.txt', 'llms-full.txt', 'docs/ai-documentation.md', 'deployment.json'):
        if not (destination / required).is_file():
            raise ValueError('Incomplete website')
    metadata = json.loads((destination / 'deployment.json').read_text())
    if not re.fullmatch('[0-9a-f]{40}', metadata.get('revision', '')) or type(metadata.get('run')) is not int or metadata['run'] < 1:
        raise ValueError('Invalid deployment identity')
    return metadata


def receive(root, stream):
    os.umask(0o022)
    with (root / 'deploy.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        with tempfile.TemporaryDirectory(dir=root) as temporary:
            temporary = Path(temporary)
            archive = temporary / 'site.tar.gz'
            total = 0
            with archive.open('wb') as target:
                while chunk := stream.read(1024 * 1024):
                    total += len(chunk)
                    if total > MAX_ARCHIVE:
                        raise ValueError('Archive too large')
                    target.write(chunk)
            candidate = temporary / 'site'
            candidate.mkdir()
            metadata = extract(archive, candidate)
            current = root / 'current'
            if (current / 'deployment.json').is_file():
                previous = json.loads((current / 'deployment.json').read_text())
                if metadata['run'] < previous['run']:
                    raise ValueError('Refusing an older workflow run')
            # The server owns cache policy; archives cannot upload .htaccess.
            (candidate / '.htaccess').write_text('<IfModule mod_headers.c>\nHeader set Cache-Control "no-cache"\n</IfModule>\nAddType text/markdown .md\n')
            release = root / 'releases' / (str(metadata['run']) + '-' + metadata['revision'])
            release.parent.mkdir(exist_ok=True)
            if release.exists():
                def digest(directory):
                    return {p.relative_to(directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                            for p in directory.rglob('*') if p.is_file()}
                if digest(release) != digest(candidate):
                    raise ValueError('Existing release differs from retry')
            else:
                candidate.rename(release)
            next_link = root / 'next'
            next_link.unlink(missing_ok=True)
            next_link.symlink_to(release)
            next_link.replace(current)
            print('Published ' + metadata['revision'])


if __name__ == '__main__':
    receive(Path(sys.argv[1]), sys.stdin.buffer)
