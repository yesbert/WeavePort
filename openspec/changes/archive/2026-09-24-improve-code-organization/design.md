## Context

See proposal.md. The repository resolves .NET SDK 10.0.401, net10.0 and C# 14. Hosting already uses LoggerMessage source generation and IDs 1001–1007. Existing package dependencies mostly follow inward boundaries; public namespaces and SDK imports are consumer contracts.

## Goals / Non-Goals

Make responsibilities discoverable and changes local. Preserve lifecycle ordering and error categories. Avoid a mediator dependency, artificial domain layers, duplicate adapter contracts, package upgrades or namespace breaks merely to imitate an application architecture.

## Decisions

1. Use root Central Package Management for maintained repository projects. Standalone copyable samples retain explicit versions and opt out via a nearby props file. Preserve all resolved versions, PrivateAssets, lock files and conditional project/package references. Do not enable transitive pinning, which can change published dependency closure.
2. Keep existing packages as dependency boundaries. Within Hosting use feature folders for Installations, Scheduling, Sessions, Workers and Diagnostics, with explicit Adapters and Protocol areas. Keep public namespaces stable for binary/source consumers; document this intentional directory/namespace difference. Split public contract types into named files. Keep related partial implementations together; do not split a method merely to meet a numeric limit.
3. TypeScript index.ts and Python __init__.py become export-only facades. Registration and runtime move to separate modules; shared session and protocol responsibilities have one owner. Expand compressed control flow and use descriptive identifiers. Preserve public signatures, wire values and invocation/cleanup ordering. SDK tests continue exercising real child processes.
4. Name generated log IDs centrally and retain current numeric values, templates and safe fields. Keep logging internal, caller-owned and payload-free by default. A catalogue maps events and errors to causes and operator actions.
5. Add ErrorCode to the two public .NET platform exceptions, retaining their IOException inheritance and existing fields. PluginCallException codes retain the existing Status vocabulary (including an unknown peer status unchanged); version mismatch uses version-mismatch. Add cleanup-error to SDK cleanup exceptions. Standard argument, cancellation and IO exceptions remain standard rather than losing their semantic types under a universal wrapper. Error codes are diagnosis, never retry authorization or proof of no side effects.
6. Add a structural check for the architecture boundaries, export-only entry points, central package references and unique documented log IDs. Use existing lifecycle, source, stream, concurrent and package-consumer suites for behavioral evidence.

## Risks / Trade-offs

- File moves can break linked compilation and documentation → inspect all literal paths and run build, link and generated-document checks.
- Module extraction can introduce import cycles or state duplication → direct internal imports, one invocation scope instance, compiled SDK and cross-language process tests.
- Package changes can qualify stale local artifacts → repack and use isolated consumer caches for verification.
- Broad refactoring is difficult to review → keep behavior changes additive and isolate migration mechanics; retain feature ownership notes.
- A directory layout is not dependency enforcement → check actual project references and forbid internal imports through public SDK facades.

## Migration Plan

Apply package configuration, then C# organization, then SDK extraction and readability, then diagnostic additions. Run focused checks during each step and repository verification at the end. Rollback consists of reverting source/configuration changes; no persisted data or wire migration is required. A clean-HEAD candidate requires committing the final tree; working-tree checks and candidate qualification must be reported distinctly.

## Sources checked on 2026-09-24

- https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management
- https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation
- https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
- https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures
- https://www.typescriptlang.org/docs/handbook/2/modules.html
- https://docs.python.org/3/tutorial/modules.html

HiveWeaver's central versions and diagnostics extension layout were inspected as design references; no sibling implementation or event ranges are copied.

## Implementation review notes

- Checked official Node AsyncLocalStorage and Python get_running_loop documentation during extraction. Invocation scope remains singular; synchronous callback checks no longer parse RuntimeError messages.
- Used Prettier 3.6.2 and Black 26.5.1 as isolated development tools; no runtime dependencies added. Sources: https://prettier.io/docs/options and https://black.readthedocs.io/en/stable/the_black_code_style/current_style.html.
- SDK builds clear TypeScript dist before compilation so removed modules cannot remain in packed artifacts.

The first isolated candidate found flat JavaScript copying in portable/shared test bundle construction. Those bundlers now preserve relative paths recursively, and mixed-installation runtime inventories include nested modules. The focused portable suite subsequently passed Python and Node in CustomerBound, ApprovedSessions and Shared modes, including incompatible runtime refusal. The failed candidate is retained at `artifacts/candidates/20260924-081314-161fa8cb` for diagnosis; it is not passing qualification evidence.

Public API review found only the intended two ErrorCode getters/properties; no existing signature changed. The reviewed baseline records those four reflection entries. All pre-existing resolved lockfile package versions remain unchanged.

## Verified outcome

The complete isolated native candidate passed with 3,555 recorded assertions and 428 frozen files. See [verification evidence](verification.md) for source identity, checks, the corrected bundle-copy regression and qualification limits. The diagnostic delta is synchronized into the main specification.
