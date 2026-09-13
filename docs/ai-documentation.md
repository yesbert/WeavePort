# AI documentation

[llms.txt](../llms.txt) is the compact retrieval index. [llms-full.txt](../llms-full.txt) combines maintained guides, verified specifications and the reviewed .NET API signatures. Both are generated from this checkout; planned OpenSpec changes and historical reports are not included as current guarantees.

## Read through an existing MCP tool

Give a fetch-capable MCP tool or documentation indexer this entry URL:

```text
https://raw.githubusercontent.com/yesbert/WeavePort/main/llms.txt
```

Ask the assistant to read the status and compatibility guide first, then retrieve the linked guide for the task. Use the full text only if the client can handle its size. A filesystem MCP tool can instead read the same files from a local checkout. Repository MCP tools can retrieve them by repository, path and revision.

These are documentation files, not a Streamable HTTP or stdio MCP server. Do not put the raw URL into a client's MCP server configuration. Fetch/index support and refresh behavior depend on the selected tool; no service is automatically registered by adding these files. This setup exposes documentation, not plugin execution or administrative tools.

For reproducible answers, replace `main` with the commit or release tag matching the installed packages in the raw URLs, including links retrieved from the document. The full reference contains sources from one checkout, while its links use the current main branch for discovery. Check package compatibility before combining guidance from different revisions.

## Maintain

Update canonical Markdown or the reviewed API baseline, then regenerate:

```sh
python3 scripts/generate-llms.py
python3 scripts/generate-llms.py --check
```

The generator rejects missing local targets and rewrites relative links for remote retrieval. CI checks that both files match their sources. Add new consumer guides to its curated list; specifications are discovered under `openspec/specs/`. The API signature baseline is not a replacement for XML documentation or packed-consumer behavior tests.

The index follows the [llms.txt proposal](https://llmstxt.org/).
