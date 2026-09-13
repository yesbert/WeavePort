## Purpose
Provide an independent scheduling integration template that recovers local booking actions after lost responses without duplicating their effects.

## Requirements

### Requirement: Replaceable appointment strategies
The sample SHALL offer installed earliest/latest strategies over application-owned UTC slots and validate proposals before authorizing booking.

#### Scenario: Strategy substitution
- **WHEN** the same wish is evaluated by each strategy against available slots
- **THEN** the selected slot reflects that strategy without host code changes

#### Scenario: No available appointment
- **WHEN** no slot satisfies the wish
- **THEN** the application reports no availability without creating a booking

### Requirement: Persisted exact-command recovery
The application SHALL persist the approved command before dispatch and retain terminal outcomes by scoped request identity. Repetition SHALL return the original outcome; changed input for an existing identity SHALL be refused. Lost responses SHALL remain explicitly uncertain until reconciled.

#### Scenario: Worker loss after booking
- **WHEN** a worker dies after the booking commits but before its response arrives and the application reopens its store and repeats the request
- **THEN** the original booking is returned with no duplicate or changed slot

### Requirement: Scoped conflict-safe authority
Booking callbacks SHALL require granted capability and match host-bound tenant/profile and the approved command. Competing requests SHALL not book the same scoped slot twice.

#### Scenario: Concurrent conflicts and customer independence
- **WHEN** requests compete for one slot while a separate customer uses the same plugin artifact
- **THEN** at most one competing request books and the other customer's results remain independent

#### Scenario: Invalid authority
- **WHEN** a callback lacks its grant or changes scope or approved command
- **THEN** no booking is created by that callback

### Requirement: Recoverable bounded local storage
The sample SHALL refuse concurrent coordinators for one store, invalid persisted data and declared capacity violations without resetting valid bookings.

#### Scenario: Store reopen and refusal
- **WHEN** a store is reopened, already owned, malformed or full
- **THEN** valid outcomes remain available or the application refuses explicitly without silently replacing its history
