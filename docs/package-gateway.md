# WeavePort.Sdk.Gateway

Host preconfigured plugin bindings through gRPC HTTP/2. Use AddWeavePortGateway and MapWeavePortGateway with an application-owned HTTPS listener. Requires ASP.NET Core. Credentials select immutable trusted tenant bindings; they never register executable paths.

Version 0.6.0 targets .NET 10. MIT licensed; third-party packages retain their own licenses. Use a coherent 0.6.0 WeavePort package family. Optional packages are not additional mandatory entries in installation declarations.

See https://github.com/yesbert/WeavePort for examples, qualification evidence and full documentation. Cancellation or connection failure does not prove absence of external effects. There are no automatic operation retries. Applications own domain semantics, authentication, storage and resource budgets. Native plugins remain trusted code.
