"""Independent wire scenarios shared by both author SDKs; build TypeScript first."""

import base64
import time
import unittest

from worker_harness import Worker


class ProtocolScenarios:
    def worker(self, concurrent=True):
        worker = Worker(self.language, concurrent)
        self.addCleanup(worker.close)
        return worker

    def test_overlap_callbacks_and_error_isolation(self):
        worker = self.worker()
        worker.invoke("slow", operation="work", input={"delay": 0.15})
        worker.invoke("fast", operation="work", input={"callback": True})
        callback = worker.receive()
        self.assertEqual(callback["id"], "fast")
        worker.send(
            type="callback-result",
            id="fast",
            callbackId=callback["callbackId"],
            error={"code": "denied"},
        )
        self.assertEqual(worker.receive()["type"], "error")
        self.assertEqual(worker.receive()["value"], "slow")
        worker.invoke("healthy", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "healthy")

    def test_callback_payload_cannot_impersonate_an_internal_failure(self):
        worker = self.worker()
        worker.invoke("callback-data", operation="work", input={"callback": True})
        callback = worker.receive()
        value = {"callbackError": "ordinary application data"}
        worker.send(type="callback-result", id="callback-data",
                    callbackId=callback["callbackId"], value=value)
        self.assertEqual(worker.receive()["value"], value)

    def test_invalid_author_result_is_isolated(self):
        worker = self.worker()
        worker.invoke("invalid", operation="invalid", input={})
        self.assertEqual(worker.receive()["code"], "sdk-error")
        worker.invoke("next", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "next")

    def test_iterator_cleanup_failure_is_channel_fatal(self):
        worker = self.worker(False)
        worker.invoke("start", "$sdk.start", operation="brokenClose", input={})
        stream = worker.receive()["value"]["stream"]
        worker.invoke("first", "$sdk.next", stream=stream)
        self.assertEqual(worker.receive()["value"]["items"], [1])
        worker.invoke("close", "$sdk.close", stream=stream)
        self.assertEqual(worker.receive()["code"], "cleanup-error")

    def test_cancelled_callbacks_release_state_and_keep_bounded_late_identities(self):
        worker = self.worker()
        # This live callback must survive all tombstone eviction below.
        worker.invoke("active", operation="work", input={"callback": True})
        active = worker.receive()
        oldest = self.cancel_callback(worker, "cancel-0")
        newest = oldest
        for index in range(1, 4100):
            newest = self.cancel_callback(worker, f"cancel-{index}")
        worker.send(
            type="callback-result",
            id="active",
            callbackId=active["callbackId"],
            value="active-survived",
        )
        self.assertEqual(worker.receive()["value"], "active-survived")
        # A known recent cancelled callback may return once, without an active context.
        worker.send(
            type="callback-result",
            id=newest["id"],
            callbackId=newest["callbackId"],
            value="late",
        )
        worker.invoke("healthy", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "healthy")
        # Older identities must have been evicted; retaining every cancelled future
        # or closure would incorrectly accept this reply and grow indefinitely.
        worker.send(
            type="callback-result",
            id=oldest["id"],
            callbackId=oldest["callbackId"],
            value="expired",
        )
        self.assertNotEqual(worker.process.wait(timeout=3), 0)

    def cancel_callback(self, worker, identity):
        worker.invoke(identity, operation="work", input={"callback": True})
        callback = worker.receive()
        self.assertEqual(callback["type"], "callback")
        worker.send(type="cancel", id=identity)
        self.assertEqual(worker.receive(), {"type": "cancelled", "id": identity})
        return callback

    def test_late_callback_identity_is_consumed_once(self):
        worker = self.worker()
        worker.invoke("cancel", operation="work", input={"callback": True})
        callback = worker.receive()
        worker.send(type="cancel", id="cancel")
        self.assertEqual(worker.receive()["type"], "cancelled")
        worker.send(
            type="callback-result",
            id="cancel",
            callbackId=callback["callbackId"],
            value="late",
        )
        worker.invoke("healthy", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "healthy")
        worker.send(
            type="callback-result",
            id="cancel",
            callbackId=callback["callbackId"],
            value="duplicate",
        )
        self.assertNotEqual(worker.process.wait(timeout=3), 0)

    def test_missing_callback_identity_is_channel_fatal(self):
        worker = self.worker()
        worker.send(type="callback-result")
        self.assertNotEqual(worker.process.wait(timeout=3), 0)

    def test_cancel_waits_for_actual_completion(self):
        worker = self.worker()
        worker.invoke("cancel", operation="work", input={"delay": 0.15})
        started = time.monotonic()
        worker.send(type="cancel", id="cancel")
        worker.invoke("other", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "other")
        self.assertEqual(worker.receive(), {"type": "cancelled", "id": "cancel"})
        self.assertGreater(time.monotonic() - started, 0.1)

    def test_cleanup_is_owned_until_completion_and_failure_is_fatal(self):
        worker = self.worker()
        worker.invoke("cleanup", operation="cleanup", input={"delay": 0.15})
        worker.send(type="cancel", id="cleanup")
        worker.invoke("independent", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "independent")
        self.assertEqual(worker.receive()["type"], "cancelled")
        worker.invoke("failure", operation="cleanup", input={"fail": True})
        self.assertEqual(worker.receive()["code"], "cleanup-error")

    def test_shared_stream_is_rejected_without_corrupting_channel(self):
        worker = self.worker()
        worker.invoke("stream", "$sdk.start", operation="items", input={})
        self.assertEqual(worker.receive()["type"], "error")
        worker.invoke("next", operation="work", input={})
        self.assertEqual(worker.receive()["type"], "result")

    def test_late_known_cancellation_does_not_retire_channel(self):
        worker = self.worker()
        worker.invoke("completed", operation="work", input={})
        self.assertEqual(worker.receive()["type"], "result")
        worker.send(type="cancel", id="completed")
        worker.invoke("next", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "next")

    def test_buffered_item_is_delivered_before_following_callback(self):
        worker = self.worker(False)
        worker.invoke("start", "$sdk.start", operation="immediateCallback", input={})
        stream = worker.receive()["value"]["stream"]
        worker.invoke("first", "$sdk.next", stream=stream)
        first = worker.receive()
        self.assertEqual(first["type"], "result")
        self.assertEqual(first["value"], {"items": [1], "done": False})
        worker.invoke("second", "$sdk.next", stream=stream)
        callback = worker.receive()
        self.assertEqual(callback["type"], "callback")
        self.assertEqual(callback["id"], "second")
        worker.send(
            type="callback-result",
            id="second",
            callbackId=callback["callbackId"],
            value=2,
        )
        second = worker.receive()["value"]
        self.assertEqual(second["items"], [2])
        if not second["done"]:
            worker.invoke("end", "$sdk.next", stream=stream)
            self.assertTrue(worker.receive()["value"]["done"])

    def test_early_close_after_buffered_item_cancels_deferred_callback(self):
        worker = self.worker(False)
        worker.invoke("start", "$sdk.start", operation="immediateCallback", input={})
        stream = worker.receive()["value"]["stream"]
        worker.invoke("first", "$sdk.next", stream=stream)
        self.assertEqual(worker.receive()["value"]["items"], [1])
        started = time.monotonic()
        worker.invoke("close", "$sdk.close", stream=stream)
        closed = worker.receive()
        self.assertEqual(closed["type"], "result")
        self.assertTrue(closed["reusable"])
        self.assertLess(time.monotonic() - started, 2)
        worker.invoke("healthy", operation="work", input={})
        self.assertEqual(worker.receive()["value"], "healthy")

    def test_callback_before_first_item_is_not_deferred(self):
        worker = self.worker(False)
        worker.invoke(
            "start", "$sdk.start", operation="immediateCallback", input={"before": True}
        )
        stream = worker.receive()["value"]["stream"]
        worker.invoke("first", "$sdk.next", stream=stream)
        callback = worker.receive()
        self.assertEqual(callback["type"], "callback")
        worker.send(
            type="callback-result",
            id="first",
            callbackId=callback["callbackId"],
            value=0,
        )
        first = worker.receive()["value"]
        self.assertEqual(first["items"], [0, 1])

    def test_stream_heartbeat_and_binary_source(self):
        worker = self.worker(False)
        worker.invoke("start", "$sdk.start", operation="items", input={})
        stream = worker.receive()["value"]["stream"]
        worker.invoke("first", "$sdk.next", stream=stream)
        self.assertEqual(worker.receive()["value"], {"items": [1], "done": False})
        worker.invoke("heartbeat", "$sdk.next", stream=stream)
        self.assertEqual(worker.receive()["value"], {"items": [], "done": False})
        time.sleep(0.15)
        worker.invoke("last", "$sdk.next", stream=stream)
        callback = worker.receive()
        self.assertEqual(callback["type"], "callback")
        self.assertEqual(callback["id"], "last")
        worker.send(
            type="callback-result",
            id="last",
            callbackId=callback["callbackId"],
            value=2,
        )
        result = worker.receive()["value"]
        self.assertEqual(result["items"], [2])
        if not result["done"]:
            worker.invoke("end", "$sdk.next", stream=stream)
            self.assertTrue(worker.receive()["value"]["done"])
        worker.invoke("open", "$sdk.source.open", operation="bytes", input={})
        source = worker.receive()["value"]["source"]
        worker.invoke("read", "$sdk.source.read", source=source, chunkBytes=4096)
        self.assertEqual(base64.b64decode(worker.receive()["value"]["data"]), b"hello")
        worker.invoke("eof", "$sdk.source.read", source=source, chunkBytes=4096)
        eof = worker.receive()
        self.assertTrue(eof["value"]["done"])
        self.assertTrue(eof["reusable"])


class PythonProtocolTests(ProtocolScenarios, unittest.TestCase):
    language = "python"

    def test_sync_python_callback_and_cancel(self):
        worker = Worker("python")
        self.addCleanup(worker.close)
        worker.invoke("sync", operation="sync", input={"callback": True})
        callback = worker.receive()
        worker.send(
            type="callback-result",
            id="sync",
            callbackId=callback["callbackId"],
            value="ok",
            error=None,
        )
        self.assertEqual(worker.receive()["value"], "ok")
        worker.invoke("cancel", operation="sync", input={"delay": 0.15})
        worker.send(type="cancel", id="cancel")
        self.assertEqual(worker.receive()["type"], "cancelled")


class TypeScriptProtocolTests(ProtocolScenarios, unittest.TestCase):
    language = "typescript"


if __name__ == "__main__":
    unittest.main()
