## Purpose

Allow applications to consume ordinary MCP tools through explicitly selected local plugins while retaining native contracts and host-owned lifecycle policies.

## Requirements

### Requirement: Optional explicit MCP protocol
Local process bindings SHALL retain the native protocol by default and SHALL support explicitly selected MCP 2025-11-25 or 2026-07-28 over stdio. Unsupported configuration or a mismatched server SHALL fail boundedly without fallback or additional unaccounted processes.

#### Scenario: Mixed plugin protocols
- **WHEN** a host binds a native plugin and a supported MCP plugin
- **THEN** both use their configured protocol and independent worker instance under the same host budget

#### Scenario: Incompatible server
- **WHEN** the server does not support the configured revision or tools capability
- **THEN** startup fails within the deadline and cleanup completes or remains accounted for

### Requirement: Bounded tool discovery and invocation
MCP bindings SHALL support tools/list with explicit caller-controlled pagination and tools/call with caller-supplied object arguments. Successful exchanges SHALL preserve complete MCP results, including tool-level isError, without executing content or following returned URLs. Unsupported methods or interaction continuations SHALL be refused explicitly.

#### Scenario: Multiple tools in one process
- **WHEN** a supported server advertises multiple tools and the caller invokes two of them
- **THEN** both execute in the same retained binding process and return their MCP results

#### Scenario: Tool-level failure
- **WHEN** a valid tool result reports isError
- **THEN** that flag and its content remain observable independently of host exchange success

### Requirement: MCP authority and traffic limits
MCP bindings SHALL NOT grant native callbacks, transmit bound context implicitly, or interpret tool metadata as authority. MCP messages SHALL obey a 1 MiB frame limit, depth 32, exact response correlation and unambiguous reserved fields. Notification processing SHALL be bounded per exchange; unsolicited server requests SHALL NOT execute host actions.

#### Scenario: Forged or excessive traffic
- **WHEN** a server sends repeated envelope fields, a wrong response ID, malformed UTF-8, excessive depth, oversized frames or excessive notifications
- **THEN** the invocation fails boundedly and the worker is removed or retained in cleanup accounting

#### Scenario: Attempted host interaction
- **WHEN** an MCP server asks for host sampling, roots, elicitation or native callbacks
- **THEN** no host action or secret disclosure occurs and the unsupported interaction is reported as failure

### Requirement: Shared MCP worker lifecycle
MCP workers SHALL obey existing global and tenant capacity, state retention, pristine assignment, restart, deadline and cleanup policies. Failed or cancelled calls SHALL NOT be replayed automatically. Trusted local execution SHALL NOT be described as a hostile-code sandbox.

#### Scenario: Tenant failure and recovery
- **WHEN** tenant A crashes or hangs while B uses the identical MCP artifact
- **THEN** A fails boundedly, B retains its instance and state, and a subsequent A call uses a fresh worker without replaying the failed call

#### Scenario: Capacity exhaustion
- **WHEN** an MCP binding requires a worker beyond the host or tenant allowance
- **THEN** it receives busy without dispatch and native bindings remain subject to the same shared accounting

#### Scenario: Callback grants supplied
- **WHEN** a consumer attempts to bind an MCP profile with native callback grants
- **THEN** binding fails before registration or process startup
