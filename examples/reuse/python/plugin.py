"""A cooperative reusable plugin. Only registration metadata lives at module scope."""
import io
from weaveport_sdk import PluginApplication

app = PluginApplication()

@app.function("transform")
async def transform(value, context):
    # Keep customer input, authority and caches inside this invocation.
    data = bytearray(value["text"].encode("utf-8"))
    context.on_close(lambda: data.__setitem__(slice(None), b"\0" * len(data)))
    buffer = context.own(io.BytesIO())
    buffer.write(data)
    # Return independent JSON data; cleanup must not mutate the returned object.
    return dict(tenant=context.tenant, text=value["text"].upper(), bytes=buffer.tell())

app.run()
