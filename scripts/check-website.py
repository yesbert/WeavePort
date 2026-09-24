"""Verify static output links, assets, anchors and required discovery files."""
from html.parser import HTMLParser
import json
import re
from pathlib import Path
import sys
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET


class Page(HTMLParser):
    def __init__(self, text):
        super().__init__()
        self.links = []
        self.ids = set()
        self.feed(text)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if 'id' in attrs:
            self.ids.add(attrs['id'])
        for key in ('href', 'src'):
            if key in attrs:
                self.links.append(attrs[key])


def check(root):
    root = root.resolve()
    pages = {p: Page(p.read_text()) for p in root.rglob('*.html')}
    errors = []
    count = 0
    for path, page in pages.items():
        for href in page.links:
            url = urlsplit(href)
            if url.scheme or url.netloc:
                continue
            count += 1
            target = ((root / unquote(url.path).lstrip('/')) if url.path.startswith('/') else path.parent / unquote(url.path)).resolve() if url.path else path
            if target.is_dir():
                target /= 'index.html'
            if not target.is_relative_to(root) or not target.exists():
                errors.append(f'{path.relative_to(root)}: missing {href}')
            elif url.fragment and target in pages and unquote(url.fragment) not in pages[target].ids:
                errors.append(f'{path.relative_to(root)}: missing anchor {href}')
    # Validate same-origin Markdown/index references as well as rendered navigation.
    for document in [*root.rglob('*.md'), root / 'llms.txt', root / 'llms-full.txt']:
        for href in re.findall(r'\]\((https://weaveport\.dev/[^)]+)\)', document.read_text()):
            target = (root / unquote(urlsplit(href).path).lstrip('/')).resolve()
            if not target.is_relative_to(root) or not target.is_file():
                errors.append(f'{document.relative_to(root)}: missing retrieval target {href}')
    for asset in root.rglob('*'):
        if asset.is_file() and asset.suffix in {'.html', '.json', '.xml', '.yml', '.txt', '.md', '.js', '.css'}:
            text = asset.read_text()
            if '/Volumes/' in text or '/Users/' in text:
                errors.append('Machine-local path in public artifact: ' + str(asset.relative_to(root)))
    for required in ('index.html', 'docs/getting-started.html', 'docs/api.html', 'docs/optional-api.html', 'docs/failure-codes.html', 'compatibility/optional-api.txt', 'docs/introduction.html', 'docs/toc.html', 'assets/logo.png', 'public/main.css', 'public/main.js', 'index.json', 'sitemap.xml', 'llms.txt', 'llms-full.txt', 'robots.txt'):
        if not (root / required).is_file():
            errors.append('Missing required file: ' + required)
    index = json.loads((root / 'index.json').read_text())
    if len(index) < 30:
        errors.append('Search index does not cover the documentation')
    sitemap = ET.parse(root / 'sitemap.xml')
    if not all(e.text.startswith('https://weaveport.dev/') for e in sitemap.findall('.//{*}loc')):
        errors.append('Sitemap includes a foreign origin')
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'PASS: {len(pages)} HTML files, {count} local references, search index, sitemap and AI retrieval files')


if __name__ == '__main__':
    check(Path(sys.argv[1]) if len(sys.argv) > 1 else Path('artifacts/documentation/site'))
