# Packages and support

Install the runtime in your .NET application and the author SDK in each C# plugin project. The package version in this source is **0.7.0**, licensed under MIT. Use the same exact version across the selected .NET packages. The optional packages can be installed independently of one another.

| Package | Use it for |
|---|---|
| [WeavePort.Abstractions](https://www.nuget.org/packages/WeavePort.Abstractions/0.7.0) | Shared host, session and execution contracts |
| [WeavePort.Hosting](https://www.nuget.org/packages/WeavePort.Hosting/0.7.0) | Binding, execution profiles, callbacks, admission and worker lifecycle |
| [WeavePort.Sdk](https://www.nuget.org/packages/WeavePort.Sdk/0.7.0) | Authoring C# plugin functions and streams |
| [WeavePort.Sdk.Client](https://www.nuget.org/packages/WeavePort.Sdk.Client/0.7.0) | Typed application calls over an authorized local session |
| [WeavePort.Composition](https://www.nuget.org/packages/WeavePort.Composition/0.7.0) | Bounded local and remote result composition |
| [WeavePort.Sdk.Gateway.Client](https://www.nuget.org/packages/WeavePort.Sdk.Gateway.Client/0.7.0) | Remote HTTPS client without ASP.NET Core |
| [WeavePort.Sdk.Gateway](https://www.nuget.org/packages/WeavePort.Sdk.Gateway/0.7.0) | ASP.NET Core gateway hosting |

In the application project:

```sh
dotnet add package WeavePort.Hosting --version 0.7.0
dotnet add package WeavePort.Sdk.Client --version 0.7.0
```

In a C# plugin project:

```sh
dotnet add package WeavePort.Sdk --version 0.7.0
```

Package installation supplies libraries. The application still composes a host, selects installed artifacts and binds authorized contexts. The [first example](getting-started.md) shows the complete wiring.

## Language and deployment scope

| Area | Current boundary |
|---|---|
| C# | Public NuGet author SDK; .NET 10 |
| Python | Repository-built `weaveport-sdk` wheel; no PyPI publication |
| TypeScript | Repository-packed `@weaveport/sdk` archive; no npm publication |
| Windows, Linux and macOS | Supported targets for trusted stdio execution with .NET 10 and required plugin runtimes |
| Release validation | macOS arm64 validated; Windows and Linux release validation pending |
| Optional Unix-socket transport | Linux and macOS only; unavailable on Windows |
| Example scripts / offline bundle | Bash scripts assume Unix paths; the historical offline bundle is macOS arm64-specific |
| Container deployment | Separate execution profile; historical experiments do not qualify every topology |
| Optional Gateway, Composition, Testing | Outside the four-package public release |
| NativeAOT / remote production | Not qualified by this release |

See [platform support and validation](../docs/platform-qualification.md) for the distinction between supported execution paths and tested releases.

The API is pre-1.0 and evolving. The host API, wire protocol, package version and plugin artifact version are separate identities. Read [exact compatibility](../docs/package-compatibility.md), [release procedure](../docs/releases.md) and [current status](../docs/status.md) before choosing a deployment.

## MCP support is included in Hosting

`WeavePort.Hosting` 0.7.0 includes optional local MCP tools with no additional MCP runtime dependency. Use the low-level session contract for `tools/list` and `tools/call`; `WeavePort.Sdk.Client` remains the native typed/streaming client. Deploy the trusted server and its language dependencies separately. [Complete MCP guide](../docs/mcp-plugins.md).

## Optional package family

Version 0.7.0 includes `WeavePort.Composition`, `WeavePort.Sdk.Gateway` and `WeavePort.Sdk.Gateway.Client`. The remote client requires only .NET 10; the gateway server requires ASP.NET Core. See [release status](../docs/releases.md), [HTTPS gateway](../docs/gateway.md) and [composition](../docs/bulk-composition.md). Testing remains internal.
