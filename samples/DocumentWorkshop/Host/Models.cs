using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;

internal sealed record WorkshopConfiguration(string[] InstalledReaders);
internal sealed record ImportOptions(string File, string Store, string Tenant = "demo", string Profile = "default", string? Reader = null);
internal sealed record ImportResult(string Path, string Reader, int Sections, int Fragments, long SourceBytes, int ReadCalls, int MaximumRead, string[] DeclinedReaders);
internal sealed record TestHooks(Func<IPluginSession, SourceLease, Task>? Bound = null, Func<ExtractionPage, ExtractionPage>? TransformPage = null, Func<IPluginSession, ExtractionPage, Task>? PageReceived = null, bool DenyRead = false);
