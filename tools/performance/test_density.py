"""Deterministic observer controls; run independently of performance measurements."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import density
import docker_memory


class FakeEngine:
    def __init__(self, responses):
        self.responses = responses

    def get(self, path):
        return self.responses[path]


class DensityControls(unittest.TestCase):
    def test_memory_units(self):
        self.assertEqual(density.mib("1GiB"), 1024)
        self.assertEqual(density.mib("0B"), 0)
        self.assertEqual(density.mib("1024KiB"), 1)
        with self.assertRaises(ValueError):
            density.mib("--")

    def test_missing_live_memory_fails(self):
        engine = FakeEngine({"/containers/a/stats?stream=false&one-shot=true": {},
                             "/containers/a/json": {"State": {"Running": True}}})
        with self.assertRaises(ValueError):
            docker_memory.memory_row(engine, {"Id": "a", "Names": ["/owned"]})

    def test_missing_exited_memory_is_explicit(self):
        engine = FakeEngine({"/containers/a/stats?stream=false&one-shot=true": None, "/containers/a/json": None})
        result = docker_memory.memory_row(engine, {"Id": "a", "Names": ["/owned"]})
        self.assertTrue(result["exitedDuringSample"])
        self.assertNotIn("usageBytes", result)

    def test_unrelated_memory_and_cache_not_owned(self):
        containers = [{"Id": "a", "Names": ["/owned"]}, {"Id": "b", "Names": ["/unrelated"]}]
        engine = FakeEngine({"/containers/json": containers,
            "/containers/a/stats?stream=false&one-shot=true": {"memory_stats": {"usage": 20 * 1048576, "stats": {"inactive_file": 4 * 1048576}}},
            "/containers/b/stats?stream=false&one-shot=true": {"memory_stats": {"usage": 1024 * 1048576}}})
        result = docker_memory.snapshot(engine, {"owned"})
        self.assertEqual(result["ownedMiB"], 16)
        self.assertEqual(result["ownedRawMiB"], 20)
        self.assertEqual(result["allContainersMiB"], 1040)
        self.assertEqual(result["allContainersRawMiB"], 1044)

    def test_cgroup_v1_cache_and_invalid_values(self):
        engine = FakeEngine({"/containers/a/stats?stream=false&one-shot=true":
            {"memory_stats": {"usage": 100, "stats": {"total_inactive_file": 20, "inactive_file": 80}}}})
        self.assertEqual(docker_memory.memory_row(engine, {"Id": "a", "Names": []})["workingSetBytes"], 80)
        engine.responses["/containers/a/stats?stream=false&one-shot=true"]["memory_stats"]["usage"] = -1
        with self.assertRaises(ValueError):
            docker_memory.memory_row(engine, {"Id": "a", "Names": []})

    def test_partial_resource_line_is_reread(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            resource = folder / "resources.jsonl"
            first = json.dumps({"instances": ["one"]}) + '\n'
            resource.write_text(first + '{"instances":')
            known = set()
            position = density.refresh_known(folder, known, 0)
            self.assertEqual(known, {"one"})
            resource.write_text(first + json.dumps({"instances": ["two"]}) + '\n')
            density.refresh_known(folder, known, position)
            self.assertEqual(known, {"one", "two"})

    def test_engine_timeout_is_not_zero_memory(self):
        with patch.object(FakeEngine, "get", side_effect=TimeoutError("engine timeout")):
            with self.assertRaises(TimeoutError):
                docker_memory.snapshot(FakeEngine({}), {"owned"})


if __name__ == "__main__":
    unittest.main()
