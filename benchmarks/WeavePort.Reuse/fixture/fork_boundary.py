"""Experimental pristine fork server + UID boundary; JSON-only child results."""
import ctypes
import json
import multiprocessing
import os
from pathlib import Path
import resource
import signal
import threading
import time
from runtime_helpers import invoke, processes, wipe


def worker(connection, request):
    os.setsid()
    os.setgroups([])
    os.setgid(65532)
    os.setuid(65532)
    import tempfile
    os.environ['TMPDIR'] = '/tmp'
    tempfile.tempdir = '/tmp'
    ctypes.CDLL(None).prctl(38, 1, 0, 0, 0)  # PR_SET_NO_NEW_PRIVS
    resource.setrlimit(resource.RLIMIT_AS, (192 * 1048576, 192 * 1048576))
    # No access to the coordinator's Docker-facing stdio stream.
    fd = os.open('/dev/null', os.O_RDWR)
    for target in (0, 1, 2):
        os.dup2(fd, target)
    if fd > 2:
        os.close(fd)
    try:
        before = time.perf_counter()
        result = {'status': 'ok', 'reusable': True} if request is None else invoke(request)
        result['childWorkMs'] = (time.perf_counter() - before) * 1000
        connection.send_bytes(json.dumps(result, separators=(',', ':')).encode())
        if request and request.get('exitDelayMs'):
            time.sleep(request['exitDelayMs'] / 1000)  # Controlled exit/reap deadline fixture.
    finally:
        connection.close()


class ForkBoundary:
    def __init__(self):
        if os.getuid() != 0:
            raise RuntimeError('Privileged fixture supervisor required')
        libc = ctypes.CDLL(None)
        if libc.prctl(4, 0, 0, 0, 0) != 0:  # PR_SET_DUMPABLE
            raise RuntimeError('Cannot protect supervisor')
        # Keep the fork-server control socket outside plugin-writable directories.
        Path('/control').mkdir(mode=0o700, exist_ok=True)
        os.environ['TMPDIR'] = '/control'
        import tempfile
        tempfile.tempdir = '/control'
        # Only audited, customer-independent standard libraries enter the pristine template.
        multiprocessing.set_forkserver_preload(['fork_boundary', 'hashlib', 'logging', 'contextvars'])
        self.context = multiprocessing.get_context('forkserver')
        warmup = self._run(None)  # Launch pristine template before any customer payload exists.
        if warmup.get('status') != 'ok' or not warmup.get('reusable'):
            raise RuntimeError('Pristine template warmup failed')
        self.baseline = processes()

    def _run(self, request):
        parent, child_end = self.context.Pipe(duplex=False)
        child = self.context.Process(target=worker, args=(child_end, request))
        result = []
        def receive():
            try:
                # Never deserialize child-controlled pickle.
                result.append(json.loads(parent.recv_bytes(1048576)))
            except Exception:
                result.append({'status': 'failed', 'reusable': False, 'error': 'invalid-child-frame'})
        start = time.perf_counter()
        deadline = start + 1.5
        child.start()
        started = time.perf_counter()
        child_end.close()
        reader = threading.Thread(target=receive, daemon=True)
        reader.start()
        reader.join(max(0, deadline - time.perf_counter()))
        received = time.perf_counter()
        child.join(max(0, deadline - time.perf_counter()))
        forced_termination = child.is_alive()
        if forced_termination:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                child.kill()
            child.join(.5)
        reader.join(.2)
        parent.close()
        exitcode = child.exitcode
        child.close()
        value = result[0] if result and not reader.is_alive() else {'status': 'failed', 'reusable': False, 'error': 'timeout'}
        if not isinstance(value, dict) or value.get('status') not in ('ok', 'failed') or type(value.get('reusable')) is not bool:
            value = {'status': 'failed', 'reusable': False, 'error': 'invalid-child-schema'}
        if exitcode != 0:
            value['reusable'] = False
            value['status'] = 'failed'
            value['error'] = 'child-deadline' if forced_termination else 'child-exit'
        value['childExitCode'] = exitcode
        value['forcedTermination'] = forced_termination
        value['childStartMs'] = (started - start) * 1000
        value['childResponseWaitMs'] = (received - started) * 1000
        value['childExitWaitMs'] = (time.perf_counter() - received) * 1000
        value['processTurnoverMs'] = (time.perf_counter() - start) * 1000
        return value

    def invoke(self, request):
        request = {**request, 'supervisorPid': os.getpid()}
        result = self._run(request)
        if processes() - self.baseline:
            result['reusable'] = False
            result['error'] = 'descendants-remain'
        result['untrustedDeserializeMarkerPresent'] = Path('/tmp/parent-deserialized-marker').exists()
        wipe()
        return result
