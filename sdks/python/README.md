# WeavePort author SDK

Register business functions and result streams for a WeavePort host. SDK version 0.2.0 adds registered session cleanup for operator-approved worker reuse in WeavePort 0.4.0.

Customer-bound execution remains the default. Approval covers the entire deployment and its dependencies. Cleanup releases registered resources; it does not erase arbitrary globals or unregistered background work.

See [plugin author guidance](https://github.com/yesbert/WeavePort/blob/v0.4.0/docs/reusable-plugins.md) and [runnable examples](https://github.com/yesbert/WeavePort/tree/v0.4.0/examples/reuse).

The package includes the repository's MIT license. Python/TypeScript packages are distributed as qualified GitHub release artifacts; registry publication is separate.
