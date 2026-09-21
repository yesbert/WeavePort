# WeavePort.Sdk.Gateway.Client

Call preconfigured WeavePort gateway bindings using RemotePluginClient. Requires .NET 10, not the ASP.NET Core shared framework. Supports bounded calls, streams, authenticated binding identity and TLS. Server certificates are validated by default.

Version 0.6.0 targets .NET 10. MIT licensed; third-party packages retain their own licenses. Use a coherent 0.6.0 WeavePort package family. Optional packages are not additional mandatory entries in installation declarations.

See https://github.com/yesbert/WeavePort for examples, qualification evidence and full documentation. Cancellation or connection failure does not prove absence of external effects. There are no automatic operation retries. Applications own domain semantics, authentication, storage and resource budgets. Native plugins remain trusted code.
