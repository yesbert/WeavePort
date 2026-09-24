"""Independent cleanup failure and primary-cause regression checks."""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from weaveport_sdk.cleanup import cleanup_all, execute_with_cleanup
from weaveport_sdk.context import PluginContext, SessionCleanupError


class CleanupTests(unittest.IsolatedAsyncioTestCase):
    def test_exception_constructor_retains_runtime_error_arguments(self):
        self.assertEqual(SessionCleanupError().args, ())
        self.assertEqual(SessionCleanupError("one", "two").args, ("one", "two"))
        self.assertEqual(
            SessionCleanupError("message", errors=[ValueError()]).args, ("message",)
        )

    async def test_all_cleanup_failures_survive(self):
        first, second = ValueError("private first"), RuntimeError("private second")

        async def a():
            raise first

        async def b():
            raise second

        with self.assertRaises(SessionCleanupError) as caught:
            await cleanup_all(a, b)
        self.assertEqual(caught.exception.errors, (first, second))

    async def test_primary_and_cleanup_survive(self):
        primary, cleanup = ValueError("primary"), RuntimeError("cleanup")

        async def action():
            raise primary

        async def release():
            raise cleanup

        with self.assertRaises(SessionCleanupError) as caught:
            await execute_with_cleanup(action, release)
        self.assertEqual(caught.exception.errors, (primary, cleanup))
        self.assertIs(caught.exception.__cause__, primary)

    async def test_successful_cleanup_preserves_original_exception(self):
        primary = ValueError("primary")

        async def action():
            raise primary

        async def release():
            pass

        with self.assertRaises(ValueError) as caught:
            await execute_with_cleanup(action, release)
        self.assertIs(caught.exception, primary)

    async def test_context_retains_every_cause_in_reverse_order(self):
        context = PluginContext("tenant", {}, None)
        first, second = ValueError("first"), ValueError("second")

        def a():
            raise first

        def b():
            raise second

        context.on_close(a)
        context.on_close(b)
        with self.assertRaises(SessionCleanupError) as caught:
            await context._close()
        self.assertEqual(caught.exception.errors, (second, first))


if __name__ == "__main__":
    unittest.main()
