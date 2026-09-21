## ADDED Requirements

### Requirement: Collect plugin-originated bytes
Composition SHALL collect plugin-originated data through a tenant-bound local or remote client into the existing request-owned result scope using bounded blocks. Object and scope quotas SHALL apply without imposing the JSON-item stream total limit. Failure or cancellation SHALL close source state or retire its worker and SHALL NOT commit partial output.

#### Scenario: Large source
- **WHEN** a Python source yields a 200 MB object under a sufficient scope quota
- **THEN** the complete verified object is committed while transient transfer memory remains bounded by block size
