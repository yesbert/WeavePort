## 1. Measurement controls

- [x] 1.1 Add bounded, recorded native memory-policy configuration and validate invalid settings.
- [x] 1.2 Implement a shared Linux process/container comparison with identical runtime artifacts and retained provenance.

## 2. Experiments

- [x] 2.1 Run native 128/256/384 growth with customer quality, headroom and recovery evidence.
- [x] 2.2 Run repeated Linux process/container controls and Linux native functional checks.
- [ ] 2.3 Execute native Windows functional and performance qualification when a local Windows environment is available.

- [x] 2.4 Run a matched native Docker-running/stopped comparison under the recorded experiment procedure, and restore previously running services.

- [x] 2.5 Attempt 512 active native customers with Docker stopped, retain guard-stop evidence and restore services. The first 512 stage hit the RSS ceiling; large-payload and repeat runs were correctly skipped.
- [x] 2.6 Repeat native 512 without a summed-RSS ceiling under the explicit measurement configuration, retaining system pressure, swap, host and quality guards.

- [x] 2.7 Extend native growth beyond 512, locate and refine an operational boundary with Docker stopped, repeat the passing level and restore services.

## 3. Review

- [x] 3.1 Document measured conclusions, resource accounting, platform status and reproduction commands.
- [x] 3.2 Run applicable functional/tooling checks, strict OpenSpec validation and diff review; retain unfinished platform work explicitly.

Windows task 2.3 remains open for capacity/performance qualification. Native functional adapter checks passed on a GitHub-hosted Windows runner in run 34828099259 (38 checks); this does not complete the remaining performance work. The change is intentionally not archived.
