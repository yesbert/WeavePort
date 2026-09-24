"""Regressions for source links and the public landing-page examples."""

import ast
import json
import importlib.util
from pathlib import Path
import re
import unittest
import tempfile
from unittest.mock import patch
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


website = load("website", "scripts/build-website.py")
llms = load("llms", "scripts/generate-llms.py")


class WebsiteTests(unittest.TestCase):
    def test_nested_navigation_preserves_anchor(self):
        result = website.rewrite_href(
            "samples/DecisionRoom/README.md",
            "docs/examples/DecisionRoom.md",
            "../../docs/plugin-sdk.md#python",
            website.sources(),
        )
        self.assertEqual(result, "../plugin-sdk.md#python")

    def test_logo_stays_local_and_source_code_goes_to_repository(self):
        mapping = website.sources()
        self.assertEqual(
            website.rewrite_href(
                "website/index.md", "index.md", "assets/logo.png", mapping
            ),
            "assets/logo.png",
        )
        result = website.rewrite_href(
            "docs/plugin-sdk.md",
            "docs/plugin-sdk.md",
            "../examples/sdk/python/plugin.py",
            mapping,
        )
        self.assertEqual(result, website.REPOSITORY + "examples/sdk/python/plugin.py")

    def test_missing_source_is_not_silently_published(self):
        with self.assertRaises(ValueError):
            website.rewrite_href(
                "website/index.md", "index.md", "missing.md", website.sources()
            )

    def test_llm_html_assets_and_links_are_absolute(self):
        source = '<img src="website/assets/logo.png"><a href="docs/plugin-sdk.md#python">SDK</a>'
        actual = llms.rewrite("README.md", source)
        self.assertIn('src="' + llms.RAW + 'website/assets/logo.png"', actual)
        self.assertIn('href="' + llms.RAW + 'docs/plugin-sdk.md#python"', actual)
        with self.assertRaises(ValueError):
            llms.rewrite("README.md", '<img src="missing.png">')

    def test_llms_index_has_proposal_structure(self):
        index = llms.generate()["llms.txt"]
        self.assertTrue(index.startswith("# WeavePort\n\n> "))
        self.assertEqual(len(re.findall(r"^# ", index, re.M)), 1)
        sections = re.split(r"^## .+\n", index, flags=re.M)[1:]
        entries = (
            line
            for section in sections
            for line in section.splitlines()
            if line.strip()
        )
        for line in entries:
            self.assertRegex(line, r"^- \[.+\]\(https://[^)]+\): .+")

    def test_source_edits_change_full_reference(self):
        before = llms.generate()["llms-full.txt"]
        original = Path.read_text

        def changed(path, *args, **kwargs):
            content = original(path, *args, **kwargs)
            return (
                content + "\nFreshness regression sentinel\n"
                if path == ROOT / "docs/status.md"
                else content
            )

        with patch.object(Path, "read_text", changed):
            after = llms.generate()["llms-full.txt"]
        self.assertNotEqual(before, after)
        self.assertIn("Freshness regression sentinel", after)

    def test_website_index_resolves_without_github_source_publication(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "site"
            stage = Path(directory) / "stage"
            output.mkdir()
            (stage / "docs").mkdir(parents=True)
            for name in ("api", "optional-api", "specifications"):
                (stage / f"docs/{name}.md").write_text(
                    "# Reference\n\n```csharp\nT[A](System.String)\n```\n"
                )
            (output / "index.html").write_text("<head></head>")
            with patch.object(website, "OUTPUT", output), patch.object(
                website, "STAGE", stage
            ):
                website.publish_ai_resources()
            for href in re.findall(r"\]\(([^)]+)\)", (output / "llms.txt").read_text()):
                url = urlsplit(href)
                self.assertEqual(url.netloc, "weaveport.dev")
                self.assertTrue((output / url.path.lstrip("/")).is_file(), href)
            self.assertIn("T[A](System.String)", (output / "docs/api.md").read_text())
            for baseline, _ in llms.API_REFERENCES:
                self.assertEqual(
                    (output / baseline).read_bytes(), (ROOT / baseline).read_bytes()
                )
            page = (output / "index.html").read_text()
            self.assertIn('rel="describedby" href="/llms.txt"', page)
            self.assertIn('type="text/markdown" href="/index.md"', page)
            self.assertIn(
                "https://weaveport.dev/docs/status.md",
                (output / "llms-full.txt").read_text(),
            )

    def test_current_contracts_are_retrievable_and_navigable(self):
        required = {
            "docs/failure-codes.md",
            "docs/protocol.md",
            "docs/portable-installations.md",
            "docs/reusable-plugins.md",
            "docs/source-organization.md",
            "docs/engineering.md",
            "examples/gateway/README.md",
            "examples/mcp/README.md",
            "examples/reuse/README.md",
        }
        guides = {path for path, _, _ in llms.GUIDES}
        navigation = {
            path for entries in website.GROUPS.values() for path, _ in entries
        }
        self.assertLessEqual(required, guides)
        self.assertLessEqual(required, navigation)
        self.assertLessEqual(guides - {"README.md"}, set(website.sources()))
        full = llms.generate()["llms-full.txt"]
        for path, _ in llms.API_REFERENCES:
            self.assertIn((ROOT / path).read_text().rstrip(), full)
        self.assertIn("Migration from 0.7.0", full)
        self.assertIn("release-080", full)

    def test_every_wire_failure_category_has_a_documented_meaning(self):
        contract = json.loads((ROOT / "contracts/protocol.json").read_text())
        catalogue = (ROOT / "docs/failure-codes.md").read_text()
        codes = set(re.findall(r"^\| `([^`]+)` \|", catalogue, re.M))
        self.assertEqual(codes, set(contract["FailureCodes"].values()))

    def test_python_example_matches_the_maintained_provider(self):
        page = (ROOT / "website/index.md").read_text()
        code = re.search(r"```python\n(.*?)```", page, re.S)[1]
        function = next(
            node
            for node in ast.parse(code).body
            if isinstance(node, ast.AsyncFunctionDef)
        )
        fixture = ast.parse((ROOT / "examples/sdk/python/plugin.py").read_text())
        expected = next(
            node
            for node in fixture.body
            if isinstance(node, ast.AsyncFunctionDef) and node.name == "echo"
        )
        self.assertEqual(ast.dump(function), ast.dump(expected))
        self.assertIn(code.strip(), (ROOT / "README.md").read_text())

    def test_other_language_examples_use_existing_echo_handlers(self):
        page = (ROOT / "website/index.md").read_text()
        code = re.search(r"```typescript\n(.*?)```", page, re.S)[1]
        fixture = (ROOT / "examples/sdk/typescript/plugin.ts").read_text()
        # Formatting and additional type imports do not change the documented echo contract.
        for source in (code, fixture):
            self.assertRegex(
                source,
                r"import\s*\{[^}]*\bPluginApplication\b[^}]*\}\s*from\s*['\"]@weaveport/sdk['\"]",
            )
            self.assertRegex(
                source,
                r"app\.function\(['\"]echo['\"],\s*async\s*\(?input\)?\s*=>\s*input\)",
            )
            self.assertIn("await app.run();", source)
        code = re.search(r"```csharp\n(.*?)```", page, re.S)[1]
        handler = code[code.index(".Function") : code.index(".RunAsync")]
        compact = lambda text: re.sub(r"\s+", "", text)
        self.assertIn(
            compact(handler),
            compact((ROOT / "examples/sdk/csharp/Program.cs").read_text()),
        )


if __name__ == "__main__":
    unittest.main()
