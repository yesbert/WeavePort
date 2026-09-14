## Context

Baseline: GitHub SonarQube run 34842081350, revision e9a0f491e487b5729975618077d3bfeddf13bdb7. Raw reports retained as local evidence. SDK 10.0.401, net10.0, C# 14.0 verified. The earlier lifetime-only draft informed this broader remediation.

## Decisions

Close admission before source disposal, cancel and await source callbacks, and drain pool starts/session invocation before release. Local clients synchronize linked-source creation against shutdown; their paused enumerators retain their own cancelled sources and must never dispatch after shutdown. Cleanup attempts remain independent, failures visible and repeated disposal shares one completion. Test missing disposal with source token access, races with barriers, and exceptions with existing doubles.

Use Directory.CreateTempSubdirectory for exclusive 0700 Unix directory creation. Keep socket paths short and fail clearly if the deployment temp path is too long. Resolve Docker to an absolute deployment-selected executable; conventional installation paths are defaults and custom installations use an explicit property. Never search an untrusted PATH to start Docker.

Keep serialization in the bounded memory stream until complete before transport output, using SerializeAsync with the invocation token. Refactor protocol field detection into cohesive helpers preserving escaped-name and duplicate-field semantics. Move manifest Compatibility into its deserialization constructor; do not remove this security/compatibility input as allegedly unused.

Public API convention changes await the owner's compatibility choice; default is compatibility. No rules, gates or exclusions are weakened. Read-only/static analyzer review findings must be justified individually with code/test evidence.

## References

- https://learn.microsoft.com/dotnet/api/system.threading.cancellationtokensource?view=net-10.0 — Dispose must follow other source operations.
- https://learn.microsoft.com/dotnet/api/system.threading.cancellationtokensource.cancelasync?view=net-10.0 — cancellation callbacks are awaited, not arbitrary business work.
- https://learn.microsoft.com/dotnet/api/system.io.directory.createtempsubdirectory?view=net-10.0 — exclusive temporary directory creation, Unix owner-only permissions.

- https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0 — direct execution still searches PATH unless FileName is absolute.
- https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializer.serializeasync?view=net-10.0 — bounded Stream, JsonTypeInfo and CancellationToken overload.

## Risks and verification

Use deterministic failure-before/fix-after tests for lifetime cleanup, blocked startup, paused streams, and private socket allocation. Preserve tenant-B behavior when disposing A. Run existing envelope, transport, installation, SDK and packed-consumer checks plus code style and documentation gates. Verify fixes against a fresh GitHub-exported server analysis after protected merge. No native sandbox expansion or escaped-child guarantee is claimed.

### Local evidence

New lifetime-source checks failed before the fix for host, session, pool and client. The socket allocation check failed before listening on the old implementation. Corrected hosting checks cover delayed startup shutdown, throwing cancellation callbacks, repeated disposal failures, and tenant B retaining the same worker while A shuts down. Gateway checks cover suspended consumers and owned binding cleanup failure. Existing framing tests preserve maximum-size atomic output and escaped UTF-8 ownership. Docker command tests use a private fake CLI and do not contact or reconfigure the Docker daemon.

Worker startup admission and drainage live in a cohesive partial WorkerPool file so the shutdown lifecycle remains reviewable within the enforced method/file size limits.

The packed API drift gate correctly failed for the additive DockerExecutable property. Reviewed actual-vs-baseline output contains only its property, getter and init setter; the DockerProfile constructor and all existing signatures remain unchanged. Updated those three baseline entries explicitly. The negative API/package drift controls remain enabled.
