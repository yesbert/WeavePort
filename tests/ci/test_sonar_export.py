"""Credential-free report regression checks."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "sonar_export", Path(__file__).resolve().parents[2] / "scripts/ci/sonar/export.py"
)
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class ExportTests(unittest.TestCase):
    def test_pages_preserve_all_findings_and_components(self):
        def api(endpoint, **params):
            p = params["p"]
            return {
                "paging": {"total": 2},
                "issues": [{"key": str(p)}],
                "components": [{"key": str(p), "path": f"{p}.cs"}],
            }

        result = report.pages(api, "issues/search", "issues")
        self.assertEqual(len(result["issues"]), 2)
        self.assertEqual(len(result["components"]), 2)

    def test_empty_intermediate_page_fails(self):
        with self.assertRaisesRegex(ValueError, "incomplete"):
            report.pages(
                lambda *a, **kw: {"paging": {"total": 3}, "issues": []},
                "issues/search",
                "issues",
            )

    def test_duplicate_findings_fail(self):
        with self.assertRaisesRegex(ValueError, "duplicate"):
            report.pages(
                lambda *a, **kw: {
                    "paging": {"total": 2},
                    "issues": [{"key": "a"}, {"key": "a"}],
                },
                "issues/search",
                "issues",
            )

    @staticmethod
    def api(endpoint, **params):
        if endpoint == "ce/task":
            return {"task": {"status": "SUCCESS", "analysisId": "a"}}
        if endpoint == "project_analyses/search":
            return {"analyses": [{"key": "a", "revision": "abc123"}]}
        if endpoint == "qualitygates/project_status":
            return {"projectStatus": {"status": "ERROR"}}
        if endpoint == "measures/component":
            return {
                "component": {"measures": [{"metric": "coverage", "value": "45.3"}]}
            }
        field = {
            "issues/search": "issues",
            "hotspots/search": "hotspots",
            "measures/component_tree": "components",
        }[endpoint]
        return {"paging": {"total": 0}, field: []}

    def test_failed_gate_is_successfully_exported(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp)
            self.assertEqual(
                report.export(
                    self.api,
                    target,
                    "yesbert/WeavePort",
                    "abc123",
                    "t",
                    "https://sonar.example",
                ),
                0,
            )
            self.assertIn("Quality gate: ERROR", (target / "summary.md").read_text())
            self.assertTrue(
                json.loads((target / "metadata.json").read_text())["complete"]
            )

    def test_partial_failure_is_not_zero_issues(self):
        def api(endpoint, **params):
            if endpoint == "issues/search":
                raise ValueError("issues/search: HTTP 403")
            return self.api(endpoint, **params)

        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp)
            self.assertEqual(
                report.export(
                    api,
                    target,
                    "yesbert/WeavePort",
                    "abc123",
                    "t",
                    "https://sonar.example",
                ),
                1,
            )
            text = (target / "summary.md").read_text()
            self.assertIn("Issues: unavailable", text)
            self.assertNotIn("Issues (0)", text)
            self.assertTrue((target / "hotspots.json").exists())

    def test_safe_markdown_mqr_and_source_links(self):
        issue = {
            "key": "a",
            "component": "weaveport:src/A B.cs",
            "line": 12,
            "message": "<img> [bad](https://evil) |\ntext",
            "rule": "S1",
            "impacts": [{"softwareQuality": "RELIABILITY", "severity": "BLOCKER"}],
        }
        text = report.render(
            {"issues": {"issues": [issue]}},
            [],
            "yesbert/WeavePort",
            "abc123",
            "https://sonar.example",
        )
        self.assertIn("/blob/abc123/src/A%20B.cs#L12", text)
        self.assertIn("RELIABILITY: BLOCKER", text)
        self.assertNotIn("<img>", text)
        self.assertNotIn("[bad](https://evil)", text)
        self.assertNotIn("|\ntext", text)

    def test_changed_analysis_omits_source_links(self):
        calls = 0

        def api(endpoint, **params):
            nonlocal calls
            if endpoint == "project_analyses/search":
                calls += 1
                return {
                    "analyses": [
                        {"key": "a" if calls == 1 else "b", "revision": "abc123"}
                    ]
                }
            return self.api(endpoint, **params)

        with tempfile.TemporaryDirectory() as temp:
            self.assertEqual(
                report.export(
                    api,
                    Path(temp),
                    "yesbert/WeavePort",
                    "abc123",
                    "t",
                    "https://sonar.example",
                ),
                1,
            )
            self.assertIsNone(
                json.loads((Path(temp) / "metadata.json").read_text())["revision"]
            )


if __name__ == "__main__":
    unittest.main()
