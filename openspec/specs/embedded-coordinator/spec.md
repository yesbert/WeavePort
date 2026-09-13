## Purpose

Provide an application integration template that shares a local plugin budget and exposes bounded admission and shutdown outcomes.

## Requirements

### Requirement: Application-owned bounded admission
The template SHALL share one worker budget across admitted operations, refuse excess work immediately before running its delegate, and keep each accepted operation counted until its owned work and disposal finish.

#### Scenario: Simultaneous scopes and overload
- **WHEN** two customer operations hold the configured capacity and another arrives
- **THEN** the two scopes have distinct workers within one budget and the extra operation is refused without a booking or retained intent

#### Scenario: Scoped failure
- **WHEN** one customer's worker fails after an effect while another uses the same artifact
- **THEN** the other customer can complete and the failed operation retains an uncertain outcome recoverable by its exact request

### Requirement: Observable bounded shutdown
The template SHALL close admission before draining, allow accepted operations a grace interval, request cancellation after that interval, and report whether operations and runtime cleanup actually completed within a bounded observation interval. Repeated shutdown SHALL observe the same shutdown lifecycle.

#### Scenario: Graceful completion
- **WHEN** shutdown begins with an active operation that finishes during grace
- **THEN** new work is refused and shutdown reports clean completion only after operation and worker cleanup

#### Scenario: Uncooperative work and cleanup failure
- **WHEN** accepted work ignores cancellation or cleanup cannot be confirmed
- **THEN** shutdown returns an incomplete or failed diagnostic result, preserves outstanding accounting and exposes eventual completion without declaring side effects failed

#### Scenario: Cancellation after booking
- **WHEN** shutdown cancels an invocation after its booking committed
- **THEN** the caller observes uncertainty and an identical request on a new coordinator returns the original booking
