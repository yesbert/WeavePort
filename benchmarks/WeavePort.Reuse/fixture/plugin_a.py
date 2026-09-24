"""Example: customer data belongs to the invocation, not a module-global cache."""

# Deliberately unsafe cache used ONLY by the negative isolation control.
_hidden = None


def execute(scope, payload):
    scope.call_host("read")
    return {"tenant": scope.tenant, "plugin": "a", "value": payload.upper()}


def leave_hidden_canary(value):
    global _hidden
    _hidden = value
