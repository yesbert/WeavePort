---
_layout: landing
title: Turn your .NET app into a platform.
description: Add C#, Python and TypeScript plugins to your .NET product. WeavePort handles execution, callbacks and workers while you own the data and permissions.
---

<section class="wp-hero">
<a class="wp-release" href="packages.md"><span class="wp-dot" aria-hidden="true"></span> Meet WeavePort 0.1.0 <span aria-hidden="true">↗</span></a>
<h1>Turn your .NET app<br> <em>into a platform.</em></h1>
<p class="wp-lead">Add document readers, scoring strategies and customer-specific rules in <strong>C#, Python or TypeScript.</strong> WeavePort runs the plugins. Your application keeps control of data and permissions.</p>
<div class="wp-actions"><a class="btn btn-primary" href="getting-started.md">Run your first example <span aria-hidden="true">→</span></a><a class="wp-text-link" href="introduction.md">Discover WeavePort <span aria-hidden="true">↗</span></a></div>
<p class="wp-hero-note">Open source · MIT licensed · Built for .NET 10</p>
</section>

<section class="wp-showcase" aria-labelledby="language-heading">
<div class="wp-showcase-heading"><div><p class="wp-eyebrow">ONE CONTRACT. THREE LANGUAGES.</p><h2 id="language-heading">Write the function.<br> WeavePort connects it.</h2></div><p>Start with a small <code>echo</code> plugin. Add your own logic, result streams and approved callbacks as your product grows.</p></div>
<div class="wp-demo">
<div class="wp-code-side" data-language-example>
<div class="wp-demo-bar"><span class="wp-file-label">Plugin</span><div class="wp-tabs" role="tablist" aria-label="Plugin language" hidden><button id="tab-python" type="button" role="tab" aria-controls="example-python" aria-selected="true" tabindex="0" data-language="python">Python</button><button id="tab-csharp" type="button" role="tab" aria-controls="example-csharp" aria-selected="false" tabindex="-1" data-language="csharp">C#</button><button id="tab-typescript" type="button" role="tab" aria-controls="example-typescript" aria-selected="false" tabindex="-1" data-language="typescript">TypeScript</button></div></div>
<div id="example-python" class="wp-code-panel" data-language-panel="python"><h3 class="wp-fallback-label">Python</h3>

```python
from weaveport_sdk import PluginApplication

app = PluginApplication()

@app.function("echo")
async def echo(value, context):
    return value

app.run()
```

</div>
<div id="example-csharp" class="wp-code-panel" data-language-panel="csharp"><h3 class="wp-fallback-label">C#</h3>

```csharp
using System.Text.Json;
using WeavePort.Sdk;

await new PluginApplication()
    .Function<JsonElement, JsonElement>("echo",
        (input, _, _) => ValueTask.FromResult(input))
    .RunAsync();
```

</div>
<div id="example-typescript" class="wp-code-panel" data-language-panel="typescript"><h3 class="wp-fallback-label">TypeScript</h3>

```typescript
import { PluginApplication } from '@weaveport/sdk';

const app = new PluginApplication();
app.function('echo', async input => input);

await app.run();
```

</div>
<div class="wp-code-footer"><a href="../docs/plugin-sdk.md">Explore the author SDKs <span aria-hidden="true">↗</span></a><span>Minimal provider excerpts</span></div>
</div>
<div class="wp-contract"><p class="wp-eyebrow">YOUR .NET APPLICATION</p><h3>The same call.<br> Whichever language you choose.</h3><div class="wp-contract-row"><span>Function</span><code>echo</code></div><div class="wp-contract-row"><span>Input</span><code>{ "message": "hello" }</code></div><div class="wp-contract-row"><span>Result</span><code>{ "message": "hello" }</code></div><p class="wp-caption">Illustrative contract. Your host selects the artifact and authorizes the binding before calling it.</p><a href="getting-started.md">Run the complete integration <span aria-hidden="true">→</span></a></div>
</div>
<p class="wp-support-line">Windows, Linux and macOS. Trusted plugins over standard input/output. Release validation: macOS arm64; Windows and Linux validation pending. The 0.1.0 API is evolving; native execution is not a hostile-code sandbox. <a href="packages.md">See support details.</a></p>
</section>

<section class="wp-section" aria-labelledby="benefits-heading">
<div class="wp-section-heading"><p class="wp-eyebrow">BUILD WHAT MAKES YOUR PRODUCT DIFFERENT</p><h2 id="benefits-heading">More room for ideas.<br> Less runtime plumbing.</h2><p>A new reader. A different algorithm. A customer’s own rules. Give each one a defined place in your application.</p></div>
<div class="wp-benefits">
<div class="wp-benefit"><span class="wp-feature-icon" aria-hidden="true">{ }</span><h3>Let the language fit the job.</h3><p>Use Python for a strategy, TypeScript for a rule or C# for a reader. Your .NET application calls them through the same client interface.</p><a href="../docs/plugin-sdk.md">Functions and streams →</a></div>
<div class="wp-benefit"><span class="wp-feature-icon" aria-hidden="true">↗</span><h3>Open the right doors.</h3><p>Plugins reach your services through explicit callback grants. The application supplies the tenant identity and checks access to each object.</p><a href="../docs/security-architecture.md">How access works →</a></div>
<div class="wp-benefit"><span class="wp-feature-icon" aria-hidden="true">⌘</span><h3>Give workers one home.</h3><p>Share startup, deadlines, worker budgets and cleanup through one host. The coordinator template connects that budget to application operations.</p><a href="../docs/embedded-coordinator.md">Embed the runtime →</a></div>
<div class="wp-benefit"><span class="wp-feature-icon" aria-hidden="true">≡</span><h3>Keep your application's architecture.</h3><p>Your contracts, database and workflows stay yours. WeavePort is a set of NuGet libraries embedded in your backend.</p><a href="introduction.md">Where WeavePort fits →</a></div>
</div>
</section>

<section class="wp-section" aria-labelledby="examples-heading">
<div class="wp-section-heading wp-heading-inline"><div><p class="wp-eyebrow">IDEAS YOU CAN RUN</p><h2 id="examples-heading">Start closer to your use case.</h2></div><p>Three complete applications show the decisions around the plugin, from access to committed results.</p></div>
<div class="wp-examples">
<a class="wp-example" href="../samples/DecisionRoom/README.md"><span class="wp-example-kind">STRATEGIES</span><span class="wp-example-title" role="heading" aria-level="3">Let customers choose<br> how decisions are scored.</span><p>Compare C# and Python evaluators with scoped knowledge and replayable results.</p><span class="wp-example-link">Explore Decision Room <span aria-hidden="true">↗</span></span></a>
<a class="wp-example" href="../samples/DocumentWorkshop/README.md"><span class="wp-example-kind">DOCUMENT PROCESSING</span><span class="wp-example-title" role="heading" aria-level="3">Add a reader.<br> Keep your workflow.</span><p>Swap document readers while the application controls source access and staged commits.</p><span class="wp-example-link">Explore Document Workshop <span aria-hidden="true">↗</span></span></a>
<a class="wp-example" href="../samples/AppointmentDesk/README.md"><span class="wp-example-kind">BUSINESS RULES</span><span class="wp-example-title" role="heading" aria-level="3">Make scheduling<br> rules replaceable.</span><p>Follow booking strategies, idempotent actions and recovery in one shared coordinator.</p><span class="wp-example-link">Explore Appointment Desk <span aria-hidden="true">↗</span></span></a>
</div>
</section>

<section class="wp-section wp-adoption" aria-labelledby="adoption-heading">
<div><p class="wp-eyebrow">SMALL ENOUGH TO TRY. OPEN ENOUGH TO INSPECT.</p><h2 id="adoption-heading">Take it for a run.</h2><p>Start with Decision Room and see a plugin participate in a real application. Then adapt the host and contracts to your product.</p><p class="wp-caption">The example runs on the qualified macOS arm64 path with .NET 10 and Python 3.11+. Initial package restore needs network access.</p><a class="btn btn-primary" href="getting-started.md">Start the walkthrough →</a></div>
<div class="wp-terminal"><div class="wp-terminal-bar"><span aria-hidden="true">● ● ●</span><span>Terminal</span></div>

```sh
git clone https://github.com/yesbert/WeavePort.git
cd WeavePort
./scripts/decision-room.sh --build
```

<div class="wp-terminal-result"><span class="wp-dot" aria-hidden="true"></span> Expected: proposal B wins with a score of 11.</div>
</div>
</section>

<section class="wp-section wp-faq" aria-labelledby="faq-heading">
<div><p class="wp-eyebrow">BEFORE YOU BUILD</p><h2 id="faq-heading">A few useful answers.</h2><a href="faq.md">Read the full FAQ →</a></div>
<div><details><summary>Is this a good fit for my application?</summary><p>WeavePort fits a .NET backend that needs owner-approved extension code: strategies, readers and domain rules. You define each contract, choose the artifacts and decide which application services they can call.</p></details><details><summary>Can I run untrusted plugins?</summary><p>The native execution path runs with your application’s OS-user rights. It is for trusted code. Running arbitrary untrusted uploads needs a stronger, separately qualified execution boundary.</p></details><details><summary>What can I install today?</summary><p>The four core NuGet packages are public at 0.1.0 under MIT. Python and TypeScript author SDKs are built from this repository. The API is evolving. Trusted stdio execution supports Windows, Linux and macOS; Windows and Linux release validation is pending. Remote production deployment is not qualified by this release.</p></details><details><summary>Do I need to change my database or workflows?</summary><p>Your application keeps its own domain contracts, durable state and recovery policy. WeavePort manages the execution layer; callbacks connect plugins to services you already own.</p></details></div>
</section>
