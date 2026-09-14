# Use WeavePort with AI assistants

Give your coding assistant WeavePort's guides, examples and reviewed API signatures so it can help build an integration against the actual contracts.

## Start with the documentation

- [llms.txt](https://weaveport.dev/llms.txt): compact index for finding the right guide.
- [llms-full.txt](https://weaveport.dev/llms-full.txt): combined guides, verified specifications and reviewed .NET API signatures.

The index follows the [llms.txt proposal](https://llmstxt.org/). Its project heading, summary and grouped Markdown links support targeted retrieval. The full file is a companion convention, not a separately standardized schema. Each documentation page also has a Markdown version: replace `.html` with `.md`. HTML discovery links identify both the Markdown version and `/llms.txt`.

Website retrieval files and Markdown pages come from the same build. Repository copies use GitHub source URLs. Neither includes unfinished OpenSpec changes or historical reports as implemented guarantees. For installation details, read [status](status.md), [platform support](platform-qualification.md) and [compatibility](package-compatibility.md) first.

Paste this into an assistant with web-fetch access:

```text
Use https://weaveport.dev/llms.txt to help me integrate WeavePort into my
.NET application. Read status, platform support and package compatibility
first, then the author SDK and coordinator guides. Use the documented API
signatures and a matching sample. Separate implemented behavior from pending
validation. Ask about my plugin contract before generating the integration.
```

These URLs are documents, not MCP endpoints. A fetch-capable client can read them without GitHub authentication. Loading documentation alone does not execute plugins.

## Connect the official GitHub MCP server

GitHub MCP lets an assistant retrieve files and search the `yesbert/WeavePort` repository. Use GitHub's hosted service; no local server installation is needed. Start with repository tools in read-only mode. Configuration and authentication depend on your MCP client. See the [official setup guide](https://github.com/github/github-mcp-server) and [remote toolset documentation](https://github.com/github/github-mcp-server/blob/main/docs/remote-server.md).

For **VS Code**, add this to `.vscode/mcp.json` in your application workspace (merge it with any existing servers):

```json
{
  "servers": {
    "github-weaveport": {
      "type": "http",
      "url": "https://api.githubcopilot.com/mcp/x/repos/readonly"
    }
  }
}
```

Start the server in VS Code and complete GitHub sign-in when prompted. Use a client version supporting remote HTTP MCP and GitHub OAuth. If your client needs a personal access token, follow its secure credential-input flow; do not commit tokens. Other clients use their own configuration format with the same endpoint. Read-only mode limits available tools; it does not restrict the connection to a single repository.

Then give the assistant this task:

```text
Use GitHub MCP to inspect owner "yesbert", repository "WeavePort".
Read docs/status.md, docs/package-compatibility.md,
docs/platform-qualification.md and compatibility/public-api.txt.
For a package-based integration, use the tag matching my installed package
version (for 0.2.1: v0.2.1), and keep all source reads on that revision.
Use get_file_contents to inspect docs/plugin-sdk.md,
docs/embedded-coordinator.md and samples/DecisionRoom/README.md.
Find the corresponding host and plugin implementations before writing code.
Build a minimal integration for my application using these exact contracts.
```

Verify the connection by requesting `get_file_contents` with `owner: yesbert`, `repo: WeavePort`, `path: docs/status.md` and `ref: refs/tags/v0.2.1`. The result should contain the file from that revision. Tool prefixes vary by client. A missing tool usually means the server is not started or its repository tools are disabled; authentication failures need the client's sign-in or token setup. These setup instructions are based on GitHub's documentation; they are not a recorded authenticated client test.

GitHub MCP supplies repository context. To build or run an integration, your assistant additionally needs a local checkout, .NET and the required plugin runtimes, plus terminal access you authorize. WeavePort does not expose a plugin-execution MCP endpoint through this setup.

## Keep the context current

Edit canonical Markdown or the reviewed API baseline, then regenerate the repository copies:

```sh
python3 scripts/generate-llms.py
python3 scripts/generate-llms.py --check
python3 scripts/build-website.py
```

CI rejects stale repository copies. Every website build generates its own index, full reference and Markdown pages from the current sources, validates local retrieval targets and includes them in the same deployment artifact. Publish the whole artifact so pages and AI context move together. The server serves that published snapshot; it does not independently pull GitHub changes, and assistants may need to refresh their own caches.

Add new consumer guides to the generator's curated `GUIDES` list; specifications are discovered under `openspec/specs/`. Regression tests cover index structure, source changes and website retrieval links. Keep the repository copies in the same commit as their source updates. For reproducible source work, pin all GitHub MCP reads to the installed release tag or commit rather than mixing `main` with a released package.
