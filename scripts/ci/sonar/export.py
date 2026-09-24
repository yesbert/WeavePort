"""Publish SonarQube findings to GitHub without changing their review status."""

import html
import json
import os
from pathlib import Path
import re
import sys
from datetime import datetime, timezone
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlparse
from urllib.request import Request, build_opener, HTTPRedirectHandler

PROJECT = "weaveport"
MAXIMUM_PAGES = 100
PAGE_SIZE = 100
MAXIMUM_SUMMARY_BYTES = 900_000
METRICS = (
    "coverage,new_coverage,duplicated_lines_density,new_duplicated_lines_density,ncloc"
)


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Api:
    def __init__(self, url, token):
        if urlparse(url).scheme != "https" or not token:
            raise ValueError("HTTPS SonarQube URL and report credential are required")
        self.url, self.token = url.rstrip("/"), token

    def __call__(self, endpoint, **params):
        request = Request(
            f"{self.url}/api/{endpoint}?{urlencode(params)}",
            headers={"Authorization": f"Bearer {self.token}"},
        )
        try:
            with build_opener(NoRedirect()).open(request, timeout=30) as response:
                return json.load(response)
        except HTTPError as error:
            raise ValueError(
                f"{endpoint}: HTTP {error.code}; verify report Browse permission"
            ) from None
        except (URLError, TimeoutError, json.JSONDecodeError):
            raise ValueError(f"{endpoint}: request failed or invalid JSON") from None


def pages(api, endpoint, field, **params):
    items, components, total = [], {}, None
    for page in range(1, MAXIMUM_PAGES + 1):
        data = api(endpoint, **params, p=page, ps=PAGE_SIZE)
        expected = data["paging"]["total"]
        if total is not None and total != expected:
            raise ValueError(f"{endpoint}: results changed during pagination")
        total = expected
        batch = data[field]
        items.extend(batch)
        components.update(page_components(data, field))
        if len(items) == total:
            return completed_pages(endpoint, field, items, components, total)
        if not batch or len(items) > total:
            break
    raise ValueError(f"{endpoint}: incomplete pagination")


def page_components(data, field):
    if field == "components":
        return {}
    return {item["key"]: item for item in data.get("components", [])}


def completed_pages(endpoint, field, items, components, total):
    keys = [item["key"] for item in items]
    if len(set(keys)) != total:
        raise ValueError(f"{endpoint}: duplicate results")
    if field == "components":
        return {field: items}
    return {field: items, "components": list(components.values())}


def safe(value):
    text = html.escape(str(value), quote=False).replace("\n", " ").replace("\r", " ")
    return re.sub(r"([\\`*_[\]{}()|#!])", r"\\\1", text)


def location(item, components, repository, revision):
    component = item.get("component", "")
    path = components.get(component, {}).get("path")
    path = path or component.removeprefix(PROJECT + ":")
    line = item.get("line", item.get("textRange", {}).get("startLine"))
    label = safe(f"{path}:{line}" if line else path)
    if revision and path and not path.startswith("/") and ".." not in path.split("/"):
        anchor = f"#L{line}" if isinstance(line, int) and line > 0 else ""
        return f'[{label}](https://github.com/{repository}/blob/{revision}/{quote(path, safe="/")}{anchor})'
    return label


def render(data, errors, repository, revision, url):
    lines = [
        "# SonarQube findings",
        "",
        f"Exported at {datetime.now(timezone.utc).isoformat()}. Scope: all open findings and unreviewed hotspots.",
        "",
    ]
    lines += (
        ["**Export incomplete. Do not interpret missing data as zero findings.**", ""]
        if errors
        else ["**Export complete.**", ""]
    )
    lines.extend(f"- {safe(error)}" for error in errors)
    lines += [
        "",
        (
            f"Analyzed source: `{revision}`"
            if revision
            else "Source revision unverified; file links omitted."
        ),
        "",
    ]
    gate = (
        data.get("quality-gate", {})
        .get("projectStatus", {})
        .get("status", "UNAVAILABLE")
    )
    lines += [
        f"**Quality gate: {safe(gate)}** (independent of export status)",
        "",
        "| Metric | Value |",
        "| --- | --- |",
    ]
    for metric in data.get("measures", {}).get("component", {}).get("measures", []):
        lines.append(
            f'| {safe(metric["metric"])} | {safe(metric.get("value", metric.get("period", {}).get("value", "unavailable")))} |'
        )
    for key, field in [("issues", "issues"), ("hotspots", "hotspots")]:
        report = data.get(key)
        if report is None:
            lines += ["", f"## {key.title()}: unavailable"]
            continue
        components = {item["key"]: item for item in report.get("components", [])}
        lines += [
            "",
            f"## {key.title()} ({len(report[field])})",
            "",
            "| Impact / priority | Rule | Source | Finding |",
            "| --- | --- | --- | --- |",
        ]
        lines.extend(finding_rows(report[field], components, repository, revision))
    lines += [
        "",
        "## Duplication by file",
        "",
        "| Source | Measures |",
        "| --- | --- |",
    ]
    for item in data.get("duplications", {}).get("components", []):
        measures = item.get("measures", [])
        if not any(float(measure.get("value", 0)) > 0 for measure in measures):
            continue
        text = ", ".join(
            f'{measure["metric"]}: {measure.get("value", "unavailable")}'
            for measure in measures
        )
        lines.append(
            f'| {location({"component": item["key"]}, {item["key"]: item}, repository, revision)} | {safe(text)} |'
        )
    lines += [
        "",
        "Full data: issues.json, hotspots.json, quality-gate.json, measures.json, duplications.json and metadata.json in the sonar-report artifact.",
        "",
        f"[SonarQube dashboard]({url}/dashboard?id={PROJECT})",
        "",
    ]
    return "\n".join(lines)


def finding_rows(items, components, repository, revision):
    lines = []
    for item in items:
        impact = ", ".join(
            f'{i["softwareQuality"]}: {i["severity"]}' for i in item.get("impacts", [])
        )
        impact = impact or item.get(
            "vulnerabilityProbability", item.get("severity", "unknown")
        )
        lines.append(
            f'| {safe(impact)} | {safe(item.get("rule", item.get("securityCategory", "")))} | {location(item, components, repository, revision)} | {safe(item.get("message", ""))} |'
        )
    return lines


def export(api, output, repository, sha, task_id, url, summary_path=None):
    output.mkdir(parents=True, exist_ok=True)
    data, errors = {}, []
    calls = {
        "issues": lambda: pages(
            api,
            "issues/search",
            "issues",
            componentKeys=PROJECT,
            issueStatuses="OPEN,CONFIRMED",
        ),
        "hotspots": lambda: pages(
            api, "hotspots/search", "hotspots", projectKey=PROJECT, status="TO_REVIEW"
        ),
        "quality-gate": lambda: api("qualitygates/project_status", projectKey=PROJECT),
        "measures": lambda: api(
            "measures/component", component=PROJECT, metricKeys=METRICS
        ),
        "duplications": lambda: pages(
            api,
            "measures/component_tree",
            "components",
            component=PROJECT,
            qualifiers="FIL",
            metricKeys="duplicated_lines_density,duplicated_lines,duplicated_blocks",
        ),
    }
    revision = None
    try:
        task = api("ce/task", id=task_id)["task"]
        current = api("project_analyses/search", project=PROJECT, ps=1)["analyses"][0]
        if (
            task["status"] != "SUCCESS"
            or task["analysisId"] != current["key"]
            or current.get("revision") != sha
        ):
            raise ValueError(
                "Current server analysis does not match this run; source revision unverified"
            )
        revision = sha
    except (ValueError, KeyError, IndexError, TypeError) as error:
        errors.append(
            str(error)
            if isinstance(error, ValueError)
            else "Analysis identity unavailable"
        )
    for name, call in calls.items():
        try:
            data[name] = call()
        except (ValueError, KeyError, TypeError) as error:
            errors.append(
                str(error)
                if isinstance(error, ValueError)
                else f"{name}: invalid API response"
            )
    # Recheck after pagination so concurrent analyses cannot silently change source links.
    revision = confirm_revision(api, current if revision else None, revision, errors)
    for name, value in data.items():
        (output / f"{name}.json").write_text(json.dumps(value, indent=2) + "\n")
    (output / "metadata.json").write_text(
        json.dumps(
            {
                "complete": not errors,
                "revision": revision,
                "taskId": task_id,
                "errors": errors,
            },
            indent=2,
        )
        + "\n"
    )
    summary = render(data, errors, repository, revision, url)
    (output / "summary.md").write_text(summary)
    append_summary(summary_path, summary)
    print(
        f'SonarQube export: {"incomplete" if errors else "complete"}; see sonar-report/summary.md'
    )
    return int(bool(errors))


def confirm_revision(api, current, revision, errors):
    if revision is None:
        return None
    try:
        latest = api("project_analyses/search", project=PROJECT, ps=1)["analyses"][0]
        if latest["key"] != current["key"]:
            raise ValueError("Analysis changed during export; source links omitted")
        return revision
    except (ValueError, KeyError, IndexError, TypeError):
        errors.append("Analysis identity changed or could not be verified after export")
        return None


def append_summary(path, summary):
    if not path:
        return
    if len(summary.encode()) > MAXIMUM_SUMMARY_BYTES:
        summary = (
            "\n".join(summary.splitlines()[:15])
            + "\n\nSummary too large; download sonar-report for the complete findings.\n"
        )
    with Path(path).open("a") as stream:
        stream.write(summary)


def main():
    output = Path("artifacts/sonar-report")
    url = os.environ.get("SONAR_HOST_URL", "").rstrip("/")
    token = os.environ.get("SONAR_REPORT_TOKEN") or os.environ.get("SONAR_TOKEN", "")
    try:
        api = Api(url, token)
        task_file = Path(".sonarqube/out/.sonar/report-task.txt")
        task = dict(
            line.split("=", 1)
            for line in task_file.read_text().splitlines()
            if "=" in line
        )
        return export(
            api,
            output,
            os.environ["GITHUB_REPOSITORY"],
            os.environ["GITHUB_SHA"],
            task["ceTaskId"],
            url,
            os.environ.get("GITHUB_STEP_SUMMARY"),
        )
    except (ValueError, OSError, KeyError):
        output.mkdir(parents=True, exist_ok=True)
        message = "# SonarQube export incomplete\n\nMissing credential, valid HTTPS URL, or scanner submission metadata. No findings count is available.\n"
        (output / "summary.md").write_text(message)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as stream:
                stream.write(message)
        print("SonarQube export unavailable; see summary. Credentials are not logged.")
        return 1


if __name__ == "__main__":
    sys.exit(main())
