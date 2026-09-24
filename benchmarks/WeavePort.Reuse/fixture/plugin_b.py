"""A distinct plugin, not a renamed call to plugin A."""


def execute(scope, payload):
    scope.call_host("read")
    return {"tenant": scope.tenant, "plugin": "b", "value": payload[::-1]}
