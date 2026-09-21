## Purpose

Define explicit trusted resident concurrent plugin execution with bounded admission, invocation authority and recoverable worker failure.

## ADDED Requirements

### Requirement: Approved resident concurrent execution
The host SHALL require agreement between installation declaration, operator approval and shared binding selection before starting a resident shared worker. It SHALL retain per-invocation tenant context, enforce a configured degree and bounded fair waiting, and refuse shared streams and unsupported adapters. A failed invocation SHALL NOT terminate unrelated invocations; channel failures SHALL fail all dispatched calls without replay. Cancelled work SHALL retain its slot until terminal acknowledgement or bounded worker retirement. Restart attempts SHALL be bounded and exhaustion SHALL expose disabled state.

#### Scenario: Sixteen tenants and isolated cancellation
- **WHEN** sixteen tenants invoke one approved worker and one invocation fails while another is cancelled
- **THEN** the remaining invocations complete with their own callback identity, no capacity is released for still-running cancelled work, and a process crash fails dispatched calls with uncertain execution before bounded recovery
