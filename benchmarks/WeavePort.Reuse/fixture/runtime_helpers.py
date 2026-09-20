"""Trusted fixture helpers. Never preload customer plugin code in the fork server."""
import json
import os
from pathlib import Path
import shutil
import signal
import threading
import time
from scope import Scope


def processes():
    return {int(p.name) for p in Path('/proc').iterdir() if p.name.isdigit()}


def descriptors():
    result = set()
    for name in os.listdir('/proc/self/fd'):
        try:
            result.add((name, os.readlink('/proc/self/fd/' + name)))
        except FileNotFoundError:
            pass
    return result


def wipe():
    for root in (Path('/tmp'), Path('/dev/shm')):
        for entry in root.iterdir():
            if entry.is_dir() and not entry.is_symlink():
                shutil.rmtree(entry)
            else:
                entry.unlink()


def kernel_objects():
    # Container-private IPC resources can survive the child process and filesystem cleanup.
    result = set()
    for kind in ('shm', 'sem', 'msg'):
        for row in Path('/proc/sysvipc', kind).read_text().splitlines()[1:]:
            if row.strip():
                result.add(kind + ':' + row.split()[1])
    result.update('mqueue:' + entry.name for entry in Path('/dev/mqueue').iterdir())
    return result


def invoke(request):
    imports = time.perf_counter()
    from matrix_ops import execute
    imported = (time.perf_counter() - imports) * 1000
    start = time.perf_counter()
    environment, cwd = dict(os.environ), os.getcwd()
    before_threads, before_fds = set(threading.enumerate()), descriptors()
    mask = os.umask(0o022)
    os.umask(mask)
    before_signals = {s: signal.getsignal(s) for s in (signal.SIGUSR1, signal.SIGUSR2, signal.SIGALRM)}
    scope = Scope(request['tenant'], request.get('grants', ['read']))
    result, error = None, None
    try:
        result = execute(request, scope)
    except Exception as caught:
        error = type(caught).__name__
    executing = time.perf_counter() - start
    cleaned = time.perf_counter()
    reusable = True
    try:
        scope.close()
        os.chdir(cwd)
        os.environ.clear()
        os.environ.update(environment)
        os.umask(mask)
        for sig, handler in before_signals.items():
            signal.signal(sig, handler)
        wipe()
        if set(threading.enumerate()) - before_threads or descriptors() - before_fds:
            raise RuntimeError('Untracked live resources')
    except Exception as caught:
        reusable, error = False, 'cleanup-' + type(caught).__name__
    return {'status': 'ok' if error is None else 'failed', 'value': result, 'reusable': reusable,
            'error': error, 'pluginImportMs': imported, 'executionMs': executing * 1000, 'cleanupMs': (time.perf_counter() - cleaned) * 1000}
