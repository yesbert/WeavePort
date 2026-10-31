# shared-plugin-execution Specification

## Purpose

Define explicit trusted resident concurrent plugin execution with bounded admission, invocation authority and recoverable worker failure.

## Requirements

### Requirement: Approved resident concurrent execution

Shared workers SHALL require installation, operator and binding agreement; isolate invocation contexts; enforce configured concurrency and bounded fair waiting; and refuse streams and unsupported adapters. Invocation failure SHALL spare other calls; channel failure SHALL fail dispatched calls without replay. Cancellation SHALL hold slots until terminal acknowledgement or bounded retirement. Restarts SHALL be bounded; exhaustion SHALL expose disabled state.

#### Scenario: Sixteen tenants and isolated cancellation
- **WHEN** sixteen tenants invoke one approved worker and one invocation fails while another is cancelled
- **THEN** the remaining invocations complete with their own callback identity, no capacity is released for still-running cancelled work, and a process crash fails dispatched calls with uncertain execution before bounded recovery
