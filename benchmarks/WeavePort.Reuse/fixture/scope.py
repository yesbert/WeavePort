"""Experimental cooperative SDK scope. This is not a sandbox or a published SDK API."""

import os
import shutil
import signal
import subprocess
import tempfile
import threading
from pathlib import Path


class Scope:
    def __init__(self, tenant, grants):
        self.tenant = tenant
        self.grants = frozenset(grants)
        self.active = True
        self.workspace = Path(tempfile.mkdtemp(prefix="call-"))
        self._cleanup = []

    def call_host(self, operation):
        if not self.active or operation not in self.grants:
            raise PermissionError("Invocation authority expired or not granted")
        return self.tenant

    def on_close(self, action):
        self._cleanup.append(action)

    def background(self, action):
        stop = threading.Event()
        thread = threading.Thread(target=action, args=(stop,), daemon=True)
        thread.start()

        def close():
            stop.set()
            thread.join(0.2)
            if thread.is_alive():
                raise RuntimeError("Background task did not stop")

        self.on_close(close)

    def child(self, arguments):
        child = subprocess.Popen(
            arguments,
            start_new_session=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

        def close():
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait(timeout=0.5)

        self.on_close(close)
        return child.pid

    def close(self):
        self.active = False  # Revoke callbacks before running plugin cleanup.
        errors = []
        for action in reversed(self._cleanup):
            try:
                action()
            except Exception as error:
                errors.append(type(error).__name__)
        try:
            shutil.rmtree(self.workspace)
        except Exception as error:
            errors.append(type(error).__name__)
        if errors:
            raise RuntimeError("Cleanup incomplete: " + ",".join(errors))
