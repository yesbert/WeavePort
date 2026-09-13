from weaveport_sdk import PluginApplication
from release import VERSION, RISK_MULTIPLIER

app = PluginApplication(plugin_version=VERSION)

@app.function("artifact.identity")
async def identity(request, context):
    return dict(version=VERSION)

@app.function("strategy.evaluate")
async def evaluate(request, context):
    weights = context.configuration["priorities"]
    knowledge = await context.call_host("knowledge.read", {})
    scores = {
        p["id"]: p["benefit"] * weights["benefit"] - p["cost"] * weights["cost"]
        - knowledge["risks"][p["id"]] * weights["risk"] * RISK_MULTIPLIER
        for p in request["snapshot"]["definition"]["proposals"]
    }
    return dict(participant=request["participant"], source=knowledge["source"],
                risks=knowledge["risks"], scores=scores, pluginVersion=VERSION)

app.run()
