"""Resolve published stable dependency versions without installing tools."""

import json
import re
from urllib.parse import quote
from urllib.request import Request, urlopen


def read_json(url):
    request = Request(url, headers={"User-Agent": "WeavePort-dependency-update"})
    with urlopen(request, timeout=30) as response:
        return json.load(response)


def version_key(value):
    if not re.fullmatch(r"\d+(?:\.\d+){1,3}", value):
        raise ValueError("Expected a stable numeric version: " + value)
    return tuple(int(part) for part in value.split("."))


def latest(registry, package):
    escaped = quote(package, safe="")
    if registry == "nuget":
        data = read_json(f"https://api.nuget.org/v3-flatcontainer/{escaped.lower()}/index.json")
        stable = [v for v in data["versions"] if re.fullmatch(r"\d+(?:\.\d+){1,3}", v)]
        return max(stable, key=version_key)
    if registry == "npm":
        value = read_json(f"https://registry.npmjs.org/{escaped}/latest")["version"]
        version_key(value)
        return value
    if registry == "pypi":
        value = read_json(f"https://pypi.org/pypi/{escaped}/json")["info"]["version"]
        version_key(value)
        return value
    raise ValueError("Unsupported registry: " + registry)
