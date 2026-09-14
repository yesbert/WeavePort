---
title: Introduction
description: Make your .NET product extensible with owner-approved C#, Python and TypeScript plugins. Learn where WeavePort fits and run a complete example.
---

# Build a product others can extend

WeavePort adds a plugin execution layer to your .NET application. Use it to run document readers, evaluation strategies and business rules written in C#, Python or TypeScript. You define the extension points; WeavePort handles the workers behind them.

<div class="wp-start-links"><a href="getting-started.md"><strong>Run your first example →</strong> <span>See C# and Python strategies work inside a complete application.</span></a> <a href="../docs/plugin-sdk.md"><strong>Write a plugin →</strong> <span>Expose functions and streams in the language that fits your task.</span></a></div>

## When WeavePort helps

Your application already has a workflow, but part of it needs to vary. One customer needs a different scheduling rule. A team wants to add a document reader. An evaluator is easier to implement in Python while the product is written in .NET.

Make that part a plugin. The application selects the approved artifact, supplies its context and calls a function. Plugins can request data or actions through callbacks that the host explicitly grants.

## What you gain

| Application need | WeavePort's part | Your part |
|---|---|---|
| Support more than one plugin language | C#, Python and TypeScript SDKs with one .NET client interface | Define function names and request/result schemas |
| Connect plugins to application data | Host-bound context and callback grants | Authenticate callers and authorize individual objects |
| Run many application operations | Shared worker management, deadlines and admission | Choose budgets and handle overload |
| Recover after interrupted work | Worker restart, cancellation and cleanup accounting | Persist state and reconcile uncertain effects |

## How it fits

1. **Define the contract.** Choose an operation such as reading a document or scoring a proposal, along with its JSON request and result.
2. **Write the plugin.** Register an ordinary asynchronous function or result stream through a language SDK.
3. **Bind it in your application.** Select an installed artifact, trusted execution profile, tenant context and callback grants.
4. **Call it and use the result.** Validate the response and commit application state under your own rules.

The [core concepts](concepts.md) explain bindings, callbacks and ownership. The [integration guide](../docs/embedded-coordinator.md) shows how operations share one host.

## Start with the right expectations

WeavePort's native execution is for **owner-controlled code**. Workers run with the application's OS-user rights; separate processes are not a hostile-plugin sandbox. The current public release is 0.2.1 under MIT, with an evolving API. Windows, Linux and macOS are supported targets for trusted stdio execution. Current release validation covers macOS arm64; Windows and Linux release validation is pending. See [platform support and validation](../docs/platform-qualification.md) for transport and tooling differences.

Your application keeps its database and workflow architecture. It also keeps responsibility for durable state, retries and the meaning of external effects. A cancelled call does not prove an action never happened.

See [packages and support](packages.md) and the [FAQ](faq.md), then choose a [running example](getting-started.md).
