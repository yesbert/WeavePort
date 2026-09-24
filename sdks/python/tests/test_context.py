"""Public context diagnostics and callback loop ownership."""
import asyncio
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from weaveport_sdk import PluginContext, SessionCleanupError


class ContextTests(unittest.IsolatedAsyncioTestCase):
    async def test_cleanup_keeps_cause_and_runs_every_action_in_reverse(self):
        context = PluginContext("tenant", {}, None)
        actions = []
        failure = ValueError("private cleanup detail")

        def fail():
            actions.append("failure")
            raise failure

        context.on_close(lambda: actions.append("first"))
        context.on_close(fail)
        context.on_close(lambda: actions.append("last"))
        with self.assertRaises(SessionCleanupError) as caught:
            await context._close()
        self.assertEqual(caught.exception.code, "cleanup-error")
        self.assertIs(caught.exception.__cause__, failure)
        self.assertEqual(actions, ["last", "failure", "first"])
        with self.assertRaises(RuntimeError):
            _ = context.tenant

    async def test_sync_callback_rejects_runtime_loop_but_accepts_worker_thread(self):
        async def callback(operation, value):
            return operation, value

        runtime = SimpleNamespace(loop=asyncio.get_running_loop(), callback=callback)
        context = PluginContext("tenant", {}, runtime)
        with self.assertRaisesRegex(RuntimeError, "Use await call_host"):
            context.call_host_sync("echo", 1)
        self.assertEqual(await asyncio.to_thread(context.call_host_sync, "echo", 2), ("echo", 2))


if __name__ == "__main__":
    unittest.main()
