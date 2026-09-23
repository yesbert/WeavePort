"""Generate deterministic retrieval documents from maintained repository sources."""
import argparse
from pathlib import Path
import posixpath
import re
from urllib.parse import quote, unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
RAW = "https://raw.githubusercontent.com/yesbert/WeavePort/main/"
WEB = "https://github.com/yesbert/WeavePort/tree/main/"
GUIDES = [
    ("README.md", "Overview", "Entry points and current delivery scope"),
    ("website/introduction.md", "Introduction", "Use cases and application fit"),
    ("website/getting-started.md", "Quickstart", "Run the first plugin workflow"),
    ("website/faq.md", "FAQ", "Adoption and support questions"),
    ("docs/platform-qualification.md", "Platforms", "Windows, Linux and macOS support and validation"),
    ("website/packages.md", "Packages", "Public package installation and support"),
    ("docs/status.md", "Status", "Implemented behavior and outstanding qualification"),
    ("docs/v1-integration-contract.md", "Integration contract", "Ownership and integration boundaries"),
    ("docs/mcp-plugins.md", "MCP plugins", "Optional local tools, explicit revisions and trust limits"),
    ("docs/gateway.md", "Gateway", "Separate client/server packages and direct HTTPS deployment"),
    ("docs/bulk-composition.md", "Composition", "Bounded local and remote result processing"),
    ("docs/shared-execution.md", "Shared execution", "Resident concurrent plugins and invocation isolation"),
    ("docs/installed-plugin-clients.md", "Installed clients", "Verified catalog, operator approval and one client path"),
    ("examples/shared/README.md", "Shared sample", "Executable installed concurrent plugin"),
    ("examples/sources/README.md", "Source sample", "Live streams and plugin-originated bytes"),
    ("docs/plugin-sdk.md", "Author SDKs", "C#, Python and TypeScript plugins"),
    ("docs/reusable-plugins.md", "Reusable plugins", "Operator approval, session cleanup and author best practices"),
    ("docs/embedded-coordinator.md", "Coordinator", "Embedding and shared worker ownership"),
    ("docs/portable-installations.md", "Portable installations", "Ecosystem runtime requirements and the .NET sealing API"),
    ("docs/installed-plugins.md", "Installed plugins", "Artifact identity, pinning and activation"),
    ("docs/package-compatibility.md", "Compatibility", "Exact package and protocol combinations"),
    ("docs/worker-lifecycle.md", "Lifecycle", "Retention, cleanup and worker limits"),
    ("docs/fair-scheduling.md", "Fair scheduler", "Reconstructible plugins, shared admission and heavy work"),
    ("docs/density-testing.md", "Customer density", "Bounded same-machine active-customer and arrival-rate measurements"),
    ("docs/native-operations.md", "Operations", "Trusted processes and recovery"),
    ("docs/runtime-diagnostics.md", "Diagnostics", "Logging and diagnostic boundaries"),
    ("docs/architecture.md", "Architecture", "Trust and execution boundaries"),
    ("docs/internal-distribution.md", "Internal distribution", "Offline installation"),
    ("docs/releases.md", "Releases", "GitHub and NuGet delivery"),
    ("docs/ai-documentation.md", "AI documentation", "Retrieval through existing MCP tools"),
    ("samples/DecisionRoom/README.md", "Decision Room", "Strategy plugins and replay"),
    ("samples/DocumentWorkshop/README.md", "Document Workshop", "Readers and staged commits"),
    ("samples/AppointmentDesk/README.md", "Appointment Desk", "Scheduling and idempotent actions"),
]
LINK = re.compile(r"\[([^\]\n]*)\]\(([^)\n]+)\)")


def rewrite(source, text, urls=None):
    def resolve(href):
        parsed = urlsplit(href.strip("<>"))
        if parsed.scheme or parsed.netloc:
            return href
        target = (posixpath.normpath(posixpath.join(posixpath.dirname(source), unquote(parsed.path)))
                  if parsed.path else source)
        path = ROOT / target
        if not path.resolve().is_relative_to(ROOT) or (not path.exists() and target not in {"llms.txt", "llms-full.txt"}):
            raise ValueError(f"Missing source link: {source}: {href}")
        url = (urls or {}).get(target, (WEB if path.is_dir() else RAW) + quote(target, safe="/"))
        if parsed.fragment:
            url += "#" + parsed.fragment
        return url
    text = LINK.sub(lambda m: f"[{m[1]}]({resolve(m[2])})", text)
    return re.sub(r'(\b(?:href|src)=")([^"\n]+)(")',
                  lambda m: m[1] + resolve(m[2]) + m[3], text)


def generate(urls=None):
    def link(path):
        return (urls or {}).get(path, RAW + path)
    intro = ("# WeavePort\n\n"
             "> Embedded .NET 10 backend plugin platform for owner-controlled C#, Python and TypeScript plugins.\n\n"
             "Read the current status and exact package compatibility before generating integration code. "
             "The 0.7.0 release includes four core packages plus optional Composition and Gateway server/client packages and an evolving pre-1.0 API. The historical internal distribution is separate. Native processes "
             "are trusted execution and do not sandbox hostile plugins. Applications own authorization, "
             "domain contracts and durable state.\n\n"
             "Generated by `python3 scripts/generate-llms.py`. This is retrieval documentation, not an MCP "
             "endpoint. A client must fetch these files or index them through a documentation MCP service.\n\n")
    specs = [(p.relative_to(ROOT).as_posix(), p.parent.name, "Verified behavioral specification")
             for p in sorted((ROOT / "openspec/specs").glob("*/spec.md"))]
    index = intro + "## Guides\n\n"
    index += "".join(f"- [{title}]({link(path)}): {description}\n" for path, title, description in GUIDES)
    index += "\n## Specifications\n\n"
    index += "".join(f"- [{title}]({link(path)}): {description}\n" for path, title, description in specs)
    index += (f'\n## Optional\n\n- [Full reference]({link("llms-full.txt")}): Combined guides, specifications '
              f'and reviewed .NET API signatures\n- [Public API]({link("compatibility/public-api.txt")}): Exact reviewed signatures\n')
    full = intro + "Prefer the smaller llms.txt index for targeted retrieval. Sources below belong to the same checkout.\n"
    for path, title, _ in GUIDES + specs:
        full += f"\n---\n\n## {title}\n\nSource: {link(path)}\n\n" + rewrite(path, (ROOT / path).read_text(), urls).rstrip() + "\n"
    full += (f'\n---\n\n## Reviewed .NET public API\n\nSource: {link("compatibility/public-api.txt")}\n\n```text\n'
             + (ROOT / "compatibility/public-api.txt").read_text().rstrip() + "\n```\n")
    return {"llms.txt": index, "llms-full.txt": full}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for name, content in generate().items():
        path = ROOT / name
        if args.check:
            if not path.exists() or path.read_text() != content:
                raise SystemExit(f"Stale {name}; run python3 scripts/generate-llms.py")
        else:
            path.write_text(content)
        print(f"Verified {name}" if args.check else f"Generated {name}")
