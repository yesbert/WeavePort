# Call local MCP tools from C#

This executable consumer uses the packed Hosting library to discover and call an ordinary text-normalization tool. It checks both explicitly selected MCP revisions, host status, tool-level errors and the expected result. No AI model, account, network listener or Docker service is needed.

From the repository root, with the SDK in `global.json` and Node installed:

```sh
./scripts/prepare-core-packages.sh
npm ci --prefix tests/mcp --ignore-scripts
dotnet run --project examples/mcp -c Release -- "$(command -v node)" "$PWD/tests/mcp/server.mjs"
```

Expected output:

```text
Mcp20251125: hello world
Mcp20260728: hello world
```

The server is the repository's [official TypeScript SDK fixture](../../tests/mcp/server.mjs), reused so documentation and interoperability tests exercise the same implementation. Its dependencies are development-only. In your application, deploy your own reviewed MCP server and replace the absolute executable/script paths; Hosting never installs server dependencies for you.

The host starts a separate server process for each binding and disposes it deterministically. One server can expose many tools. Existing native Python/TypeScript plugins continue to use their own SDK and can coexist in the same host. MCP bindings do not accept native callback grants. `structuredContent.text` is this example's output contract; other servers may return only `content`.

This is a local tools subset, validated on macOS arm64 against the official TypeScript SDK. It does not qualify arbitrary MCP SDKs, remote HTTP or untrusted-code containment. See the [complete guide](../../docs/mcp-plugins.md) for versions, limits, pagination and cancellation.
