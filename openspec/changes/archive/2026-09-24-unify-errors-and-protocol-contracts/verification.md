# Verification

## Qualified final candidate

- Source snapshot: `2f858d909a09759aa0753b168381c209932dd59d`.
- Evidence: `artifacts/candidates/20260924-094229-b6d49e62/result.json`.
- Result: passed, 3,581 recorded assertions, 66 stages and 438 frozen artifact files.
- Production source, SDKs, tests, tooling, protocol definitions, CI and API baselines were compared byte-for-byte with the final snapshot before archival. Only specification clarification, completion notes and archival followed qualification.
- Snapshot commits used a temporary Git index; the working branch and user's staging area were not committed or changed.

## Coverage

The isolated native candidate rebuilt packages and exercised source and packed hosting, scheduling, gateway, approved reuse, shared workers, concurrent author SDKs, collection, streaming, binary sources, installation sealing, recovery, package compatibility and all three product examples. Independent fixtures retain literal wire values. Generated protocol drift, architecture, all three control-flow gates, C# formatting/size, documentation and package-mutation negative controls passed.

Focused additions cover SDK code/cleanup/correlation propagation, malformed frames, unrelated cancellation, an independently expired startup deadline, startup primary plus cleanup causes, protected diagnostic delivery and throwing diagnostic destinations, local/remote failure metadata, seven gRPC status mappings with sensitive descriptions, and primary/multiple cleanup causes. Python: 24 unittest cases. TypeScript: four Node test cases. These SDK test counts are separate from the candidate's recorded assertion total.

Core and optional API candidates were reviewed for additive changes; no existing reflected signature was removed. Existing status/ErrorCode and MayHaveExecuted remain available. Registered cleanup retention and host teardown are the explicitly specified cause-preservation boundaries. Exception messages remain readable locally; raw remote plugin stack traces are not transported.

## Iteration evidence and limits

Earlier complete snapshots passed at `20260924-093344-78221872` (3,577 assertions), `20260924-093828-4c02edb5` (3,579) and `20260924-094044-da4180bb` (3,581). Final qualification includes the Python constructor-compatibility regression and all production corrections.

An initial focused stream test exposed expected stream-close callback cancellation; an explicit internal cancellation type fixed it without swallowing unrelated iterator errors. A transient local parallel MSBuild child failure was followed by successful sequential builds and isolated candidate runs. A temporary API-review project on another volume failed path resolution; running the same review on the project volume succeeded. None was accepted as qualification evidence.

Evidence is native macOS on the repository-pinned .NET SDK. Docker services were not stopped, restarted or reconfigured. No benchmark, Windows or hostile-plugin isolation guarantee is inferred from these tests.
