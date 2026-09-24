"""Invocation context and reverse-order resource cleanup."""

from .protocol import FailureCodes

import asyncio
import inspect
import threading


class SessionCleanupError(RuntimeError):
    """Registered cleanup failed; the host must retire the worker."""

    code = FailureCodes.CleanupError

    def __init__(self, *args, errors=()):
        super().__init__(*args)
        self.errors = tuple(errors)


class PluginContext:
    """Resources belong to one function call or complete result stream."""

    def __init__(self, tenant, configuration, runtime):
        self._tenant, self._configuration, self._runtime = (
            tenant,
            configuration,
            runtime,
        )
        self._active, self._cleanup = True, []
        self._cancelled = threading.Event()

    def _check(self):
        if not self._active:
            raise RuntimeError("Session completed")

    @property
    def tenant(self):
        self._check()
        return self._tenant

    @property
    def configuration(self):
        self._check()
        return self._configuration

    def on_close(self, action):
        """Register a sync/async zero-argument cleanup action, in ownership order."""
        self._check()
        if not callable(action):
            raise TypeError("Cleanup must be callable")
        self._cleanup.append(action)

    def own(self, resource):
        """Transfer a resource exposing close() or aclose() to this session."""
        self.on_close(getattr(resource, "aclose", None) or resource.close)
        return resource

    @property
    def cancelled(self):
        """Cooperative cancellation; capacity remains occupied until the handler exits."""
        return self._cancelled.is_set()

    def call_host_sync(self, operation, value):
        """Call a host callback from a synchronous concurrent worker handler."""
        self._check()
        loop = getattr(self._runtime, "loop", None)
        if loop is None:
            raise RuntimeError("Synchronous callbacks require concurrent mode")
        try:
            running_loop = asyncio.get_running_loop()
        except RuntimeError:
            running_loop = None
        if running_loop is loop:
            raise RuntimeError("Use await call_host from async handlers")
        return asyncio.run_coroutine_threadsafe(
            self.call_host(operation, value), loop
        ).result()

    async def call_host(self, operation, value):
        self._check()
        return await self._runtime.callback(operation, value)

    async def _close(self):
        self._active = False
        self._tenant = self._configuration = self._runtime = None
        actions, self._cleanup = self._cleanup, []
        errors = []
        for action in reversed(actions):
            try:
                await self._run_cleanup(action)
            except BaseException as error:
                errors.append(error)
        if errors:
            raise SessionCleanupError(
                "Registered session cleanup failed", errors=errors
            ) from errors[0]

    @staticmethod
    async def _run_cleanup(action):
        result = action()
        if inspect.isawaitable(result):
            await result
