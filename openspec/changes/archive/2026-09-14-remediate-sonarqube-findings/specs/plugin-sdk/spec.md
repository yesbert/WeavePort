## ADDED Requirements

### Requirement: Safe local client shutdown
Local client disposal SHALL reject new operations, cancel outstanding work and attempt owned binding cleanup once. It SHALL release owned cancellation resources safely without requiring a suspended stream consumer to resume enumeration. Concurrent and repeated disposal SHALL observe the same cleanup outcome; late enumeration SHALL observe cancellation without dispatching further plugin work.

#### Scenario: Active local call during disposal
- **WHEN** a local call races disposal
- **THEN** it is either admitted before shutdown and cancelled or refused without dispatch, without accessing a disposed cancellation source

#### Scenario: Suspended stream consumer
- **WHEN** disposal starts while a consumer is suspended after receiving a stream item
- **THEN** owned binding cleanup can complete without consumer resumption and later enumeration observes cancellation without further dispatch

#### Scenario: Binding cleanup throws
- **WHEN** the owned binding fails during disposal
- **THEN** the failure remains observable to repeated disposal callers and independent cancellation resource release is still attempted
