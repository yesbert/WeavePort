"""Build the static documentation site from canonical repository sources."""
import argparse
import json
import importlib.util
from pathlib import Path
import posixpath
import re
import shutil
import subprocess
from urllib.parse import quote, unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / 'artifacts/documentation/content'
OUTPUT = ROOT / 'artifacts/documentation/site'
REPOSITORY = 'https://github.com/yesbert/WeavePort/blob/main/'
GROUPS = {
    'Getting started': [('website/introduction.md', 'Introduction'), ('website/getting-started.md', 'Quickstart'), ('website/concepts.md', 'Core concepts'), ('website/packages.md', 'Packages'), ('website/faq.md', 'FAQ')],
    'Build plugins': [('docs/mcp-plugins.md', 'MCP tools'), ('docs/plugin-sdk.md', 'Language SDKs'), ('docs/gateway.md', 'HTTPS gateway'), ('docs/installed-plugins.md', 'Installed artifacts'), ('docs/package-compatibility.md', 'Compatibility')],
    'Integration': [('docs/embedded-coordinator.md', 'Shared coordinator'), ('docs/v1-integration-contract.md', 'Integration contract'), ('docs/local-execution.md', 'Local execution'), ('docs/bulk-composition.md', 'Bulk composition')],
    'Operate': [('docs/continuous-integration.md', 'CI and deployment'), ('docs/native-operations.md', 'Recovery runbook'), ('docs/worker-lifecycle.md', 'Worker lifecycle'), ('docs/runtime-diagnostics.md', 'Diagnostics'), ('docs/internal-distribution.md', 'Offline distribution')],
    'Platform': [('docs/architecture.md', 'Architecture'), ('docs/security-architecture.md', 'Security'), ('docs/protocol.md', 'Protocol'), ('docs/status.md', 'Current status'), ('docs/platform-qualification.md', 'Platform qualification'), ('docs/benchmarking.md', 'Benchmarks'), ('docs/soak-testing.md', 'Soak testing')],
    'Examples': [('samples/DecisionRoom/README.md', 'Decision Room'), ('samples/DocumentWorkshop/README.md', 'Document Workshop'), ('samples/AppointmentDesk/README.md', 'Appointment Desk')],
    'Reference': [('docs/api.md', 'Public API'), ('docs/releases.md', 'Releases'), ('docs/ai-documentation.md', 'AI documentation'), ('docs/history.md', 'Historical evidence'), ('docs/specifications.md', 'Behavioral specifications')],
}
LINK = re.compile(r'\[([^\]\n]*)\]\(([^)\n]+)\)')
HTML_LINK = re.compile(r'((?:href|src)=")([^"\n]+)(")')


def sources():
    mapping = {p.relative_to(ROOT).as_posix(): 'docs/' + p.name for p in sorted((ROOT / 'docs').glob('*.md'))}
    mapping.update({p.relative_to(ROOT).as_posix(): (p.name if p.name in {'index.md', 'imprint.md', 'privacy.md'} else 'docs/' + p.name) for p in sorted((ROOT / 'website').rglob('*.md')) if p.name != 'README.md'})
    mapping.update({f'samples/{name}/README.md': f'docs/examples/{name}.md' for name in ('DecisionRoom', 'DocumentWorkshop', 'AppointmentDesk')})
    return mapping


def rewrite_href(source, destination, href, mapping):
    parsed = urlsplit(href.strip('<>'))
    if parsed.scheme or parsed.netloc or not parsed.path:
        return href
    target = posixpath.normpath(posixpath.join(posixpath.dirname(source), unquote(parsed.path)))
    local = ROOT / target
    if not local.resolve().is_relative_to(ROOT) or not local.exists():
        raise ValueError(f'Missing source target: {source}: {href}')
    if target in mapping:
        result = posixpath.relpath(mapping[target], posixpath.dirname(destination) or '.')
    elif target.startswith('website/assets/'):
        result = posixpath.relpath(target.removeprefix('website/'), posixpath.dirname(destination) or '.')
    else:
        base = REPOSITORY.replace('/blob/', '/tree/') if local.is_dir() else REPOSITORY
        result = base + quote(target, safe='/')
    return result + (('?' + parsed.query) if parsed.query else '') + (('#' + parsed.fragment) if parsed.fragment else '')


def stage():
    if STAGE.exists():
        shutil.rmtree(STAGE)
    STAGE.mkdir(parents=True)
    mapping = sources()
    for source, destination in mapping.items():
        content = (ROOT / source).read_text()
        content = LINK.sub(lambda m: '[' + m[1] + '](' + rewrite_href(source, destination, m[2], mapping) + ')', content)
        content = HTML_LINK.sub(lambda m: m[1] + rewrite_href(source, destination, m[2], mapping) + m[3], content)
        target = STAGE / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
    shutil.copytree(ROOT / 'website/assets', STAGE / 'assets')
    shutil.copytree(ROOT / 'website/templates', STAGE / 'templates')
    for name in ('llms.txt', 'llms-full.txt'):
        shutil.copy2(ROOT / name, STAGE / name)
    (STAGE / 'docs/api.md').write_text('# Reviewed public API\n\nThese are the exact reviewed signatures from `compatibility/public-api.txt`. See [package compatibility](package-compatibility.md) for the package scope and [author SDKs](plugin-sdk.md) for usage. This inventory is generated from the maintained compatibility baseline, not an additional API contract.\n\n```csharp\n' + (ROOT / 'compatibility/public-api.txt').read_text() + '\n```\n')
    specs = '# Behavioral specifications\n\nOpenSpec requirements are the source of verified product behavior. Guides explain their use; active changes describe work that may still be unfinished.\n\n'
    for p in sorted((ROOT / 'openspec/specs').glob('*/spec.md')):
        specs += f'- [{p.parent.name}]({REPOSITORY}{p.relative_to(ROOT).as_posix()})\n'
    (STAGE / 'docs/specifications.md').write_text(specs)
    toc = []
    for title, entries in GROUPS.items():
        toc.append({'name': title, 'expanded': True, 'items': [{'name': name, 'href': posixpath.relpath(mapping.get(path, path), 'docs')} for path, name in entries]})
    # JSON is a YAML subset and avoids a build-only YAML dependency.
    (STAGE / 'docs/toc.yml').write_text(json.dumps(toc, indent=2) + '\n')
    (STAGE / 'toc.yml').write_text(json.dumps([{'name': 'Docs', 'href': 'docs/'}, {'name': 'Releases', 'href': 'https://github.com/yesbert/WeavePort/releases'}], indent=2) + '\n')
    (STAGE / 'robots.txt').write_text('User-agent: *\nAllow: /\nSitemap: https://weaveport.dev/sitemap.xml\n')
    config = json.loads((ROOT / 'website/docfx.json').read_text())
    config['build']['dest'] = str(OUTPUT)
    (STAGE / 'docfx.json').write_text(json.dumps(config, indent=2) + '\n')
    print(f'Staged {len(mapping) + 2} pages from canonical sources.', flush=True)



def publish_ai_resources():
    """Export current Markdown and retrieval files with same-release website links."""
    spec = importlib.util.spec_from_file_location('weaveport_llms', ROOT / 'scripts/generate-llms.py')
    llms = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(llms)
    mapping = sources()
    mapping['README.md'] = 'overview.md'
    mapping.update({p.relative_to(ROOT).as_posix(): p.relative_to(ROOT).as_posix()
                    for p in (ROOT / 'openspec/specs').glob('*/spec.md')})
    mapping['compatibility/public-api.txt'] = 'compatibility/public-api.txt'
    urls = {source: 'https://weaveport.dev/' + destination for source, destination in mapping.items()}
    urls.update({name: 'https://weaveport.dev/' + name for name in ('llms.txt', 'llms-full.txt')})
    urls['website/assets/logo.png'] = 'https://weaveport.dev/assets/logo.png'
    for source, destination in mapping.items():
        target = OUTPUT / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        content = (ROOT / source).read_text()
        # YAML front matter belongs to the renderer, not the reader.
        content = re.sub(r'\A---\n.*?\n---\n', '', content, count=1, flags=re.S)
        target.write_text(llms.rewrite(source, content, urls) if source.endswith(".md") else content)
    for name, content in llms.generate(urls).items():
        (OUTPUT / name).write_text(content)
    # The two generated references have no canonical Markdown input.
    for name in ('api', 'specifications'):
        content = (STAGE / f'docs/{name}.md').read_text()
        prose, fence, code = content.partition('```')
        content = LINK.sub(lambda m: '[' + m[1] + '](' +
                           (m[2] if urlsplit(m[2]).scheme else 'https://weaveport.dev/docs/' + m[2]) + ')', prose) + fence + code
        (OUTPUT / f'docs/{name}.md').write_text(content)
    for page in OUTPUT.rglob('*.html'):
        markdown = page.with_suffix('.md')
        links = '<link rel="describedby" href="/llms.txt">'
        if markdown.is_file():
            links += '<link rel="alternate" type="text/markdown" href="/' + markdown.relative_to(OUTPUT).as_posix() + '">'
        page.write_text(page.read_text().replace('</head>', links + '\n</head>', 1))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage-only', action='store_true')
    args = parser.parse_args()
    subprocess.run(['python3', 'scripts/generate-llms.py', '--check'], cwd=ROOT, check=True)
    stage()
    if not args.stage_only:
        if OUTPUT.exists():
            shutil.rmtree(OUTPUT)
        subprocess.run(['dotnet', 'tool', 'run', 'docfx', 'build', str(STAGE / 'docfx.json'), '--warningsAsErrors'], cwd=ROOT, check=True)
        # DocFX's build manifest contains absolute source paths and is not needed at runtime.
        (OUTPUT / 'manifest.json').unlink(missing_ok=True)
        publish_ai_resources()
        subprocess.run(['python3', 'scripts/check-website.py', str(OUTPUT)], cwd=ROOT, check=True)


if __name__ == '__main__':
    main()
