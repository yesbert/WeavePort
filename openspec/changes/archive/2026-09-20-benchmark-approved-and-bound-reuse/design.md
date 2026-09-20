# Design

Use the existing pinned Docker fixture and direct Engine transport. Approved calls run serially per container with cooperative cleanup. Bound calls route to an idle matching customer/plugin/version container, otherwise use a pristine slot or evict the least-recently-used idle slot. Never transfer a used bound container. Requests sharing a binding are serialized. Fixed pool sizes are experimental comparison points, not product limits.

Periodic replay uses one call per customer per period at seeded random phases, repeated across two periods. Compare resident and over-capacity populations. Independent Poisson traffic with unique customers measures approved-session throughput. Latency includes scheduling and container replacement. Pool preparation is separate. Full customer completion means every planned call succeeded; call throughput and distinct fully served customers per second must remain separate.

Registered-session fixture writes customer data into a registered cache and file, changes the environment and closes resources. An intentionally unregistered global remains a negative control for accepted risk. Callback permissions remain a cooperative stub, not production authorization. No new sandbox dependencies or hostile-code safety certification.
