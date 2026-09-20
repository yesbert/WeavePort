## ADDED Requirements

### Requirement: Shared clean-session scheduling
Approved unary SDK sessions SHALL integrate with global fair admission and release binding residency when returning a clean worker. Multiple registered customers SHALL be served without requiring one resident worker per customer. Bound sessions SHALL retain their existing affinity and eviction rules.

#### Scenario: Many registrations and a small pool
- **WHEN** many approved customer-plugin registrations invoke sequentially or concurrently within a small shared budget
- **THEN** clean workers are reused within that budget, identities remain correct and abandoned binding residency does not block admission

## MODIFIED Requirements

### Requirement: Reconstructible residency
The scheduler SHALL release reconstructible customer-bound workers on idle expiry or pressure by destroying their used environment. Approved clean unary sessions SHALL release binding residency into the compatible reusable pool. A separate pristine reserve SHALL use only recently requested registered profiles and remain bounded independently of customer count.

#### Scenario: Cold customer replaces idle customer
- **WHEN** capacity is full of customer-bound workers and a customer without a resident worker is selected
- **THEN** an idle used worker is destroyed before assigning fresh capacity

#### Scenario: Profile demand disappears
- **WHEN** a profile has no remaining registration or its recent demand expires
- **THEN** its pristine target is removed during maintenance
