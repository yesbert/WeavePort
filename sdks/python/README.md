# WeavePort Python author SDK

Register functions, async result streams and binary sources. The SDK owns transport, invocation identity and cleanup. Customer-bound serial execution is the default.

```python
from weaveport_sdk import PluginApplication

app = PluginApplication("1", concurrent_calls=True)

@app.function("score")
def score(value, context):
    # Synchronous functions run in a bounded worker thread pool.
    if context.cancelled:
        return None
    return {"tenant": context.tenant, "score": value["number"] * 2}

app.run()
```

`concurrent_calls=True` opts into protocol 2. The host must explicitly approve shared execution and configures the degree before the first call. Do not set a worker count in the plugin. Each call owns its context and registered cleanup; shared globals remain the author's responsibility. Streams and binary sources require an exclusive worker.

Async functions use `await context.call_host(operation, input)`. Synchronous concurrent functions use `context.call_host_sync(operation, input)`; calling the synchronous helper from an async function is rejected to avoid deadlock. Async handlers must yield to the event loop. Synchronous handlers can overlap blocking or native work; Python's GIL still limits pure Python CPU throughput.

Cancellation is cooperative: check `context.cancelled`. The SDK acknowledges only after the handler and all registered cleanup finish; cancellation does not forcibly stop Python threads. Register cleanup with `context.on_close(action)` or transfer an object with `context.own(resource)`. Cleanup runs in reverse order; failure retires the channel. Callback denial fails the call. Logging goes to stderr; it may contain confidential data, so host forwarding is an explicit operator choice.

For a JSON stream, decorate an async generator with `@app.stream("items")`. Available items are returned promptly. The SDK retains at most one outstanding iterator advancement and may return an empty heartbeat while waiting. Per-item, batch and total JSON limits remain 128 KiB, 256 KiB and 64 MiB.

For larger binary output, decorate a function with `@app.source("download")` and return an object exposing `read(size)` and `close()` or `aclose()` (for example `io.BytesIO` or a binary file). Either read/open may be async. The SDK owns closing the object and enforces a maximum 256 KiB read. Return empty bytes only at EOF; short reads do not imply EOF. Host Composition collection commits the final result only after the complete transfer.

Run cross-language wire checks after building TypeScript:

```sh
python3 -m unittest discover -s sdks/python/tests -v
```

The package includes the repository's MIT license. Python/TypeScript packages are distributed as qualified GitHub release artifacts; registry publication is separate. See the repository's `docs/reusable-plugins.md` and `examples/reuse` for exclusive reuse and its trust boundary.
