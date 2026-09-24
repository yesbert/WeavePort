---
title: Frequently asked questions
description: Decide whether WeavePort fits your .NET application, understand plugin trust and language support, and find the right starting point.
---

# Is WeavePort right for my product?

Start here if you are deciding how to make your .NET backend extensible. These answers describe the current 0.8.0 release and link to the detailed contracts.

## What would I use it for?

A replaceable part of an application workflow: a document reader, a scoring strategy or a scheduling rule. WeavePort runs the plugin; your application decides which plugin to use, what it may access and which results to save.

The [three example applications](getting-started.md#choose-your-next-example) show those patterns end to end.

## Does my whole application have to use WeavePort?

You embed the libraries where you need plugin execution. Your existing services, domain types and data stores remain application-owned. The [coordinator template](../docs/embedded-coordinator.md) is copyable application source for sharing a host and execution budgets.

## Can I use a Python plugin in a C# application?

Yes. WeavePort has author SDKs for C#, Python and TypeScript and a shared .NET client interface. Your application defines the function name and JSON schema, then selects a compatible artifact. Changing language is not a promise that arbitrary implementations have the same business behavior.

C# packages are public on NuGet. Python and TypeScript SDKs accompany the GitHub release as qualified downloads and can also be built from a matching checkout; they are not currently published to PyPI or npm. See [author SDKs](../docs/plugin-sdk.md).

## Does it sandbox plugins?

Native workers are trusted processes running with the application's OS-user rights. They are suitable for code controlled by the operator. They do not prevent hostile code from accessing that user's files or network.

Callback grants govern access through the WeavePort callback API. They are not an OS sandbox. Read [security and trust boundaries](../docs/security-architecture.md) before choosing an execution profile.

## Is it a workflow engine or database?

Your application owns workflows, persistence and external-action semantics. WeavePort provides the execution layer, including binding, invocation, callbacks, admission and worker lifecycle. The examples demonstrate application-owned journals and recovery policies; these do not become a universal storage guarantee.

## Can I run it on Windows or Linux?

Yes. Windows, Linux and macOS are supported targets for trusted plugin execution over standard input/output (stdio), with .NET 10 and the required plugin runtimes installed. Windows is explicitly handled in process startup, environment setup and workspace creation. The optional Unix-socket mode is available on Linux and macOS only.

Support describes the implemented execution path, not completed testing on every OS: current release validation covers macOS arm64; Windows and Linux release validation is pending. Historical Linux tests cover specific VM/container environments. The example Bash scripts assume Unix paths and are not native Windows launchers. See the [platform support and validation matrix](../docs/platform-qualification.md).

## What happens when a plugin crashes?

The host observes worker failure and manages restart and cleanup under its lifecycle policy. Worker-local memory is temporary. The application decides whether a request can be repeated and whether an external effect is uncertain. The [recovery runbook](../docs/native-operations.md) and [worker lifecycle](../docs/worker-lifecycle.md) explain the limits.

## Is it free to use?

The seven public NuGet packages are released under the [MIT license](../LICENSE). This documentation targets 0.8.0; see [release status](../docs/status.md) for publication progress. Review [packages and compatibility](packages.md) before adopting or upgrading the evolving API.

## Where should I start?

[Run Decision Room](getting-started.md). It builds the C# and Python plugins, executes a complete application workflow and gives you an expected result to check. For questions or reproducible problems, [open a GitHub issue](https://github.com/yesbert/WeavePort/issues).

## Can I reuse an existing MCP server?

Yes, if it offers the supported local stdio tools subset. Hosting 0.8.0 can discover and call tools using MCP 2025-11-25 or 2026-07-28, with the same lifecycle and tenant budgets as native plugins. You deploy the trusted server and select the protocol explicitly. One server may offer several tools; no AI model is required. See [MCP tools](../docs/mcp-plugins.md).

This does not turn every plugin into an MCP server. Native callbacks and streams remain native; remote HTTP, resources, prompts and interactive continuations are outside this release.

## Can several tenants share one loaded model?

Yes. An explicitly approved Shared installation keeps resident workers and serves concurrent unary calls through tenant-bound clients. Each invocation retains its own callback authority, while all calls in one worker share process memory and its failure boundary. Use the same host for exclusive connectors and shared computation. See the [shared execution guide](../docs/shared-execution.md) and [runnable catalog-to-client example](../examples/shared/README.md).
