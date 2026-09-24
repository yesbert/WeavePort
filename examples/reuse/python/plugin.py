"""A cooperative reusable plugin. Only registration metadata lives at module scope."""

import io
from weaveport_sdk import PluginApplication

app = PluginApplication()


@app.function("transform")
async def transform(value, context):
    # Keep customer input, authority and caches inside this invocation.
    data = bytearray(value["text"].encode("utf-8"))

    def clear_input():
        data[:] = b"\0" * len(data)

    context.on_close(clear_input)
    buffer = context.own(io.BytesIO())
    buffer.write(data)
    # Return independent JSON data; cleanup must not mutate the returned object.
    return dict(tenant=context.tenant, text=value["text"].upper(), bytes=buffer.tell())


app.run()
