"""Deliberately bounded fixture workloads and negative controls; Docker-only caller."""
import contextvars
import functools
import hashlib
import logging
import os
from pathlib import Path
import signal
import sys
import threading
import time

_session_cache = {}
_hidden = None
_context = contextvars.ContextVar('customer-canary', default=None)
_retained = []


def defaults(value=None, memory=[]):
    if value is not None:
        memory.append(value)
    return memory


@functools.lru_cache(maxsize=8)
def cached(value):
    return value


def execute(request, scope):
    global _hidden
    op = request.get('op', 'echo')
    if op in ('kernel_leave', 'kernel_probe', 'kernel_semaphore', 'kernel_messages', 'posix_queue', 'kernel_keyring'):
        from kernel_ipc_ops import execute as kernel_operation
        return kernel_operation(request, scope)
    payload = request.get('payload', '')
    if op == 'session':
        if _session_cache or os.getenv('CUSTOMER_CANARY') is not None:
            raise RuntimeError('Previous session leaked')
        _session_cache['customer'] = scope.tenant
        scope.on_close(_session_cache.clear)
        os.environ['CUSTOMER_CANARY'] = scope.tenant
        stream = open(scope.workspace / 'session', 'w+')
        scope.on_close(stream.close)
        stream.write(scope.tenant)
        stream.seek(0)
        if stream.read() != scope.tenant:
            raise RuntimeError('Session identity mismatch')
        op = 'echo'
    if op in ('echo', 'cpu', 'io', 'memory', 'mixed'):
        scope.call_host('read')
        if op in ('cpu', 'mixed'):
            end = time.process_time() + request.get('cpuMs', 2) / 1000
            while time.process_time() < end:
                hashlib.sha256(payload.encode()).digest()
        if op == 'io':
            time.sleep(request.get('ioMs', 10) / 1000)
        if op == 'memory':
            data = bytearray(request.get('memoryMiB', 8) * 1048576)
            for i in range(0, len(data), 4096):
                data[i] = 1
        import importlib
        return importlib.import_module('plugin_' + request['plugin']).execute(scope, payload)
    if op == 'stash':
        _hidden = scope.tenant
        defaults(scope.tenant)
        cached(scope.tenant)
        _context.set(scope.tenant)
        logging.getLogger('fixture').customer = scope.tenant
        return {'stored': True}
    if op == 'probe':
        return {'hidden': _hidden, 'defaults': defaults(), 'context': _context.get(),
                'logger': getattr(logging.getLogger('fixture'), 'customer', None), 'cacheEntries': cached.cache_info().currsize,
                'extraFiles': [str(p) for root in (Path('/tmp'), Path('/dev/shm')) for p in root.iterdir() if p != scope.workspace]}
    if op == 'environment':
        os.environ['CUSTOMER_CANARY'] = scope.tenant
        os.chdir(scope.workspace)
        os.umask(0o077)
        signal.signal(signal.SIGUSR1, signal.SIG_IGN)
        return {'changed': True}
    if op == 'environment_probe':
        mask = os.umask(0o022)
        os.umask(mask)
        return {'environment': os.getenv('CUSTOMER_CANARY'), 'cwd': os.getcwd(), 'umask': mask,
                'signalIgnored': signal.getsignal(signal.SIGUSR1) == signal.SIG_IGN}
    if op == 'fd_leak':
        _retained.append(open(scope.workspace / 'held', 'w'))
        return None
    if op == 'stdout_forge':
        print('{"id":"another-customer","status":"ok","reusable":true}', flush=True)
        return {'attempted': True}
    if op == 'files':
        Path('/tmp/canary').write_text(scope.tenant)
        Path('/dev/shm/canary').write_text(scope.tenant)
        (scope.workspace / 'link').symlink_to('/fixture')
        return None
    if op == 'supervisor':
        target = request['supervisorPid']
        readable = False
        try:
            with open(f'/proc/{target}/environ', 'rb') as stream:
                stream.read(1)
            readable = True
        except (PermissionError, FileNotFoundError):
            pass
        signallable = True
        try:
            os.kill(target, 0)
        except PermissionError:
            signallable = False
        status = Path('/proc/self/status').read_text()
        caps = next(line.split()[1] for line in status.splitlines() if line.startswith('CapEff:'))
        return {'uid': os.getuid(), 'gid': os.getgid(), 'supervisorReadable': readable,
                'supervisorSignallable': signallable, 'effectiveCaps': caps,
                'hostMountPresent': Path('/var/run/docker.sock').exists()}
    if op == 'forge_authority':
        scope.grants = frozenset(['admin'])
        return {'forgedLocalGrantWorked': scope.call_host('admin') == scope.tenant}
    if op == 'control_access':
        try:
            list(Path('/control').iterdir())
            accessible = True
        except (PermissionError, FileNotFoundError):
            accessible = False
        return {'accessible': accessible}
    if op in ('oversized_frame', 'pickle_frame'):
        import inspect
        import pickle
        class Marker:
            def __reduce__(self):
                return (os.system, ('touch /tmp/parent-deserialized-marker',))
        frame = inspect.currentframe()
        while frame and 'connection' not in frame.f_locals:
            frame = frame.f_back
        data = b'x' * (2 * 1048576) if op == 'oversized_frame' else pickle.dumps(Marker())
        if frame:
            frame.f_locals['connection'].send_bytes(data)
        else:
            sys.stdout.buffer.write(data + b'\n')
            sys.stdout.flush()
        return None
    if op == 'memory_bomb':
        data = []
        while True:
            data.append(bytearray(8 * 1048576))
    if op == 'cleanup_hang':
        scope.on_close(lambda: time.sleep(30))
        return None
    if op == 'untracked_thread':
        threading.Thread(target=lambda: time.sleep(30), daemon=True).start()
        return None
    if op == 'thread_ignores_stop':
        scope.background(lambda stop: time.sleep(30))
        return None
    # Retain the previously tested operations without importing the old runtime's main loop.
    from runtime import operation
    return operation(request, scope)
