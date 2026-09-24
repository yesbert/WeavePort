"""Deterministic supervisor boundaries without starting workers or contacting Docker."""

import argparse
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import density
from density_stage import DockerSamples, StageSupervisor


class StageControls(unittest.TestCase):
    def test_first_failure_stops_only_current_staircase(self):
        args = argparse.Namespace(clients="4,8", rates="0,2", repeats=2)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            config = {"Output": str(output / "first")}
            with patch.object(
                density, "stage_configuration", return_value=config
            ), patch.object(
                density, "run_stage", return_value={"passed": False, "supervisor": {}}
            ) as run:
                results = []
                keep_going = density.run_staircase(
                    args, output, {"headroomBefore": {}}, results, "native", "scheduled"
                )
        self.assertTrue(keep_going)
        self.assertEqual(run.call_count, 1)
        self.assertEqual(len(results), 1)

    def test_operator_stop_prevents_next_stage(self):
        args = argparse.Namespace(clients="4", rates="0", repeats=1)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            (output / "STOP").touch()
            with patch.object(density, "run_stage") as run:
                keep_going = density.run_staircase(
                    args, output, {}, [], "native", "scheduled"
                )
        self.assertFalse(keep_going)
        run.assert_not_called()

    def test_stop_grace_precedes_owned_group_termination(self):
        with tempfile.TemporaryDirectory() as directory:
            supervisor = StageSupervisor({"Output": directory}, {})
            supervisor.reason = "operator-stop"
            process = Mock(pid=123456)
            with patch(
                "density_stage.time.monotonic", side_effect=[100, 100, 131]
            ), patch("density_stage.os.killpg") as kill:
                self.assertFalse(supervisor.stop_if_required(process))
                kill.assert_not_called()
                self.assertTrue((Path(directory) / "STOP").exists())
                self.assertTrue(supervisor.stop_if_required(process))
            self.assertEqual(kill.call_args.args[0], process.pid)
            process.wait.assert_called_once_with(timeout=15)
            self.assertEqual(supervisor.reason, "operator-stop-forced-termination")

    def test_stalled_docker_sample_is_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch("density_stage.docker_memory.Engine"):
                observer = DockerSamples(Path(directory), True)
            pending = Mock()
            pending.done.return_value = False
            observer.pending = pending
            observer.started_at = 10
            try:
                with patch("density_stage.time.monotonic", return_value=26):
                    with self.assertRaises(TimeoutError):
                        observer.poll(set())
            finally:
                observer.pending = None
                observer.finish()


if __name__ == "__main__":
    unittest.main()
