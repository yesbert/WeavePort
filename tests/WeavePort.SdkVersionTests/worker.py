import sys
from weaveport_sdk import PluginApplication

app = PluginApplication() if sys.argv[1] == "default" else PluginApplication(plugin_version=sys.argv[1])

@app.function("echo")
async def echo(value, context):
    return value

app.run()
