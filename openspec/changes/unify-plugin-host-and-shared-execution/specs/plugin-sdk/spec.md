## ADDED Requirements

### Requirement: Concurrent authoring and live stream delivery
All three author SDKs SHALL support approved concurrent unary execution with isolated contexts and cleanup, correlated callbacks, atomic frames and cooperative per-invocation cancellation. Concurrent workers SHALL use a protocol revision rejected by serial-only hosts. Exclusive result streams SHALL flush available items promptly and permit empty unfinished heartbeat batches. Stream exchange and total deadlines SHALL be configured together separately from unary deadlines. Callback count SHALL be configurable with a default of eight.

#### Scenario: Slow stream and bounded callbacks
- **WHEN** a stream first yields after twelve seconds under a sufficient stream deadline and the same binding has a five-second unary deadline
- **THEN** the stream completes, a hung unary call times out at five seconds, and an explicitly approved callback budget of 64 allows forty callbacks while the default rejects the ninth
