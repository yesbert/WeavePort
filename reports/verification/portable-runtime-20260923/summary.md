# Portable runtime qualification

Implementation verification in progress on 2026-09-23. Final committed-HEAD qualification is recorded below when complete; this report does not allocate or publish a package version.

- A packed .NET consumer sealed a managed-only bundle on macOS and invoked it on macOS and Ubuntu 24.04.4 in a runtime-only container with separate read-only host and plugin-root mounts. No resealing occurred. Manifest/pin digest was identical while muxer hashes differed.
- Python and Node ran real SDK echo plugins through CustomerBound, ApprovedSessions and Shared installed bindings. Compatible runtime selection, optional strict hashes and incompatible-version refusal passed for each combination.
- Controlled runtime wrappers changed their reported version after catalog resolution and after a real worker crash. Initial and replacement execution were refused under all three ownership policies; no second plugin process was started.
- Python packaging 25.0 generated 676 comparison cases, and npm semver 7.7.2 generated 180 cases. Committed fixtures are independent expected results, not generated from the WeavePort implementation. Eleven .NET comparisons exercised actual host resolution for roll-forward policies, exact/missing versions and multiple frameworks.
- Probe tests cover nonzero exit, excessive output and timeout. Installation checks cover deterministic manifests, relocation, an independently authored manifest/digest, external SDK mutation, changed bundle/declaration metadata, missing approved runtime, cancellation and selector preservation.

The first container check recorded 84.7 ms for macOS resolution and 91.8 ms for Linux resolution; binding plus the first call took 124.6 ms and 85.1 ms respectively. These are single observations including parsing and file hashing, not a throughput benchmark or a general latency guarantee. Subsequent runs retain their own values. The new probe executes at resolution and worker creation, never for each ordinary call on a live worker.

Scope is managed-only .NET portability on the tested macOS/Linux deployment and the locally exercised Python/Node versions. Matching fixtures do not qualify every runtime version or native dependency. Windows, hostile plugin containment, self-contained/AOT .NET and .NET prerelease framework resolution remain outside this qualification.

Probe-only observations used five warmups and twenty retained samples per runtime on this macOS host: median .NET 11.84 ms, Python 17.78 ms and Node 24.92 ms. See [raw samples summary](probe-measurements.json). These include process creation and bounded output collection; they are not isolation or throughput guarantees. [Local](local.json) and [Linux](linux.json) records retain the same pin and differing runtime hashes; [language checks](languages.txt) retain all ownership outcomes.
