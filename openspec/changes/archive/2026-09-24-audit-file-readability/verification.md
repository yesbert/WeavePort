# Readability audit verification

## Coverage and outcome

The audit records 1,771 current files plus 60 superseded paths at the qualified source revision. Every entry has a disposition in `files.json`; moved/split files name their replacements. Maintained implementations were reviewed for responsibility, naming, discoverability, branching and resource ownership. Generated files were assessed through their generators and drift checks. Historical reports, reproduction snapshots and license notices were assessed for role and content identity; they were not rewritten or claimed as newly executed evidence.

The main changes separate host/client/session lifecycle phases, SDK runtime/channel ownership, scenario suites and measurement orchestration. Test providers are ordinary source fixtures. The source organization guide maps feature folders and entry points. C#, Python and TypeScript/JavaScript control-flow gates now cover maintained tests and tools as well as production code. Loop guards remain allowed.

Concrete behavioral findings include a TypeScript callback observer confusing an ordinary payload property with an error flag, stale soak package/status expectations, and a stream test that configured the wrong timeout. These were corrected against existing contracts with regression evidence. Public API baselines, wire values, package versions and trust boundaries remain unchanged.

## Frozen candidate

- Command: `./scripts/verify.sh`
- Qualified source: `1c124a54329d768d3b883235293aa71d95d307f6`
- Candidate: `20260924-141432-3c2976f5`
- Result: **passed**, **3,581 assertion executions**, **71 stages**.
- Frozen artifacts: **442**, unchanged throughout verification.
- Artifact manifest SHA-256: `47246a0eabb6395b0fdc16d6bb69c49a7c6764e1dc3f5716b02deb603eba08d3`.
- Environment: macOS arm64, .NET SDK 10.0.401, with recorded Python, Node and shared runtime identities in the candidate result.

Raw logs, the result and artifact manifest remain under `artifacts/candidates/20260924-141432-3c2976f5/`. The final follow-up commit adds this record and archives the completed audit; it changes no qualified executable source. The ledger hashes identify the reviewed file contents, and the containing Git commit identifies the self-describing ledger itself.

Qualification includes source and packed host/scheduler/SDK/reuse/shared consumers, MCP interoperability, portable installations, the three application templates, optional HTTPS gateway/composition, recovery, API/package compatibility and changed-package rejection. The additional performance, soak, release and website controls run inside the fresh checkout. Framework and package boundaries are verified independently of source formatting.

## Additional focused evidence

- Complete Release solution build: zero warnings and errors.
- Website build: 64 HTML pages and 1,073 local references, search/sitemap/retrieval checks passed.
- Cross-language SDK wire suite: 41 tests; TypeScript runtime/context suite: four tests.
- Measurement controls: 47 tests, including bounded fake-lane scheduling and native observer/drop negative controls.
- Soak supervisor: ten tests, a five-second three-language native smoke, and payload/RSS negative controls with the expected refusal categories and owned-process cleanup.
- Historical security-check replay: all 230 retained requests and results match the original checker; no Docker daemon execution was required.
- Exact C# API comparison: six core and four optional checks passed. Real .NET runtime selection: 35 comparisons passed.
- Strict OpenSpec validation, architecture guards, generated protocol/LLM drift, public-tree checks and diff checks passed.

The first frozen attempt (`20260924-141249-37aa48a0`, source `dc6d3d3`) stopped because newly included density controls required an executable not yet built in a clean checkout. The verifier now builds it explicitly; the second complete run above passed. Failed smoke iterations and the first candidate remain retained and are not counted as successful qualification.

## Limits

These are readability and functional/package regression results on the recorded macOS environment. Docker harnesses were built and reviewed but not newly executed; no Docker services were stopped, restarted or reconfigured. The short soak is not a long-term capacity or memory qualification. Historical measurements remain historical. Windows/Linux execution and production performance require their own evidence; the separate open platform qualification change remains open. No new version, tag, package publication or release was created.
