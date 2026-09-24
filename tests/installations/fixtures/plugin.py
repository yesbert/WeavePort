import sys

sys.path.insert(0, sys.argv[1])
from weaveport_sdk import PluginApplication

app = PluginApplication(concurrent_calls=True)


@app.function("who")
def who(value, context):
    return {"tenant": context.tenant, "language": "python"}


app.run()
