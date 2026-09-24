"""Public handler registration and executable entry point."""

import asyncio

from .runtime import Runtime


class PluginApplication:
    def __init__(self, plugin_version="1", *, concurrent_calls=False):
        if not isinstance(plugin_version, str) or not plugin_version.strip():
            raise ValueError("Invalid plugin version")
        self._concurrent_calls = concurrent_calls
        self._plugin_version = plugin_version
        self._functions = {}
        self._streams = {}
        self._sources = {}

    def _register(self, name, target):
        if (
            not name
            or name.startswith("$")
            or name in self._functions
            or name in self._streams
            or name in self._sources
        ):
            raise ValueError("Invalid or duplicate operation")

        def register(handler):
            target[name] = handler
            return handler

        return register

    def function(self, name):
        return self._register(name, self._functions)

    def stream(self, name):
        return self._register(name, self._streams)

    def source(self, name):
        """Register a handler returning a binary readable with owned close/aclose."""
        return self._register(name, self._sources)

    def run(self):
        asyncio.run(Runtime(self).run())
