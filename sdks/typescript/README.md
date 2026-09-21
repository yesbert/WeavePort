# WeavePort TypeScript author SDK

Register functions, async result streams and binary sources. The SDK owns transport, invocation identity and cleanup. Customer-bound serial execution is the default.

```typescript
import { PluginApplication } from '@weaveport/sdk';

const app = new PluginApplication('1', { concurrentCalls: true });
app.function('score', async (value: { number: number }, context, signal) => {
    signal.throwIfAborted();
    return { tenant: context.tenant, score: value.number * 2 };
});
await app.run();
```

`concurrentCalls: true` opts into protocol 2. The host explicitly approves shared execution and configures the degree before the first call. Each call gets its own context and `AbortSignal`. Async I/O overlaps on the event loop; CPU-bound handlers need author-managed worker threads. Shared globals remain the author's responsibility. Streams and binary sources require an exclusive worker.

Use `await context.callHost(operation, input)` for host callbacks. Denial fails the call. Register cleanup using `context.onClose(action)` or `context.own(resource)`. Cleanup runs in reverse order; cleanup failure retires the channel. Cancellation aborts the signal, but capacity remains occupied until the handler and cleanup actually finish. Logging goes to stderr; host forwarding is explicit because logs may contain confidential data.

Use `app.stream(name, async function* (input, context, signal) { ... })` for JSON results. Available items are flushed promptly; empty heartbeats are possible while a single retained iterator advancement is pending. Limits remain 128 KiB per item, 256 KiB per batch and 64 MiB per JSON stream. Observe the signal in generators so closing can finish promptly.

Use `app.source(name, handler)` for binary output. Return a `BinarySource` with `read(maxBytes): Uint8Array | Promise<Uint8Array>` and `close(): void | Promise<void>`. The SDK owns its lifetime, accepts at most 256 KiB per read and treats an empty read as EOF. Short reads are legal. Host Composition collection commits a result only after complete transfer; no partial result is exposed.

Build with `npm ci && npm run build`. Cross-language wire tests run from the repository root with `python3 -m unittest discover -s sdks/python/tests -v`.

The package includes the repository's MIT license. Python/TypeScript packages are distributed as qualified GitHub release artifacts; registry publication is separate. See the repository's `docs/reusable-plugins.md` and `examples/reuse` for exclusive reuse and its trust boundary.
