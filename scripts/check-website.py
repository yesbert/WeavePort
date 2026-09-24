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
        if "id" in attrs:
            self.ids.add(attrs["id"])
        self.links.extend(attrs[key] for key in ("href", "src") if key in attrs)


def check_page(root, path, page, pages, errors):
    count = 0
    for href in page.links:
        url = urlsplit(href)
        if url.scheme or url.netloc:
            continue
        count += 1
        check_local_href(root, path, href, url, pages, errors)
    return count


def resolve_local_path(root, path, url):
    if not url.path:
        return path
    if url.path.startswith("/"):
        return (root / unquote(url.path).lstrip("/")).resolve()
    return (path.parent / unquote(url.path)).resolve()


def check_local_href(root, path, href, url, pages, errors):
    target = resolve_local_path(root, path, url)
    if target.is_dir():
        target /= "index.html"
    if not target.is_relative_to(root) or not target.exists():
        errors.append(f"{path.relative_to(root)}: missing {href}")
    elif (
        url.fragment
        and target in pages
        and unquote(url.fragment) not in pages[target].ids
    ):
        errors.append(f"{path.relative_to(root)}: missing anchor {href}")


def check_retrieval(root, document, errors):
    for href in re.findall(
        r"\]\((https://weaveport\.dev/[^)]+)\)", document.read_text()
    ):
        target = (root / unquote(urlsplit(href).path).lstrip("/")).resolve()
        if target.is_relative_to(root) and target.is_file():
            continue
        errors.append(f"{document.relative_to(root)}: missing retrieval target {href}")


def check_public_asset(root, asset, errors):
    if not asset.is_file() or asset.suffix not in {
        ".html",
        ".json",
        ".xml",
        ".yml",
        ".txt",
        ".md",
        ".js",
        ".css",
    }:
        return
    text = asset.read_text()
    if "/Volumes/" in text or "/Users/" in text:
        errors.append(
            "Machine-local path in public artifact: " + str(asset.relative_to(root))
        )


def check(root):
    root = root.resolve()
    pages = {p: Page(p.read_text()) for p in root.rglob("*.html")}
    errors = []
    count = 0
    for path, page in pages.items():
        count += check_page(root, path, page, pages, errors)
    # Validate same-origin Markdown/index references as well as rendered navigation.
    for document in [*root.rglob("*.md"), root / "llms.txt", root / "llms-full.txt"]:
        check_retrieval(root, document, errors)
    for asset in root.rglob("*"):
        check_public_asset(root, asset, errors)
    for required in (
        "index.html",
        "docs/getting-started.html",
        "docs/api.html",
        "docs/optional-api.html",
        "docs/failure-codes.html",
        "compatibility/optional-api.txt",
        "docs/introduction.html",
        "docs/toc.html",
        "assets/logo.png",
        "public/main.css",
        "public/main.js",
        "index.json",
        "sitemap.xml",
        "llms.txt",
        "llms-full.txt",
        "robots.txt",
    ):
        if (root / required).is_file():
            continue
        errors.append("Missing required file: " + required)
    index = json.loads((root / "index.json").read_text())
    if len(index) < 30:
        errors.append("Search index does not cover the documentation")
    sitemap = ET.parse(root / "sitemap.xml")
    if not all(
        e.text.startswith("https://weaveport.dev/")
        for e in sitemap.findall(".//{*}loc")
    ):
        errors.append("Sitemap includes a foreign origin")
    if errors:
        raise SystemExit("\n".join(errors))
    print(
        f"PASS: {len(pages)} HTML files, {count} local references, search index, sitemap and AI retrieval files"
    )


if __name__ == "__main__":
    check(
        Path(sys.argv[1]) if len(sys.argv) > 1 else Path("artifacts/documentation/site")
    )
