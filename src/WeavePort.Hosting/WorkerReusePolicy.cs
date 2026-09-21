namespace WeavePort.Hosting;
/// <summary>Operator-authorized ownership policy; never accept this choice from plugin input.</summary>
public enum WorkerReusePolicy
{
    /// <summary>Keep a used worker exclusively bound until it is destroyed. This is the default.</summary>
    CustomerBound,
    /// <summary>Allow compatible reviewed plugins to share a worker after SDK cleanup acknowledgement. Hidden state remains a cooperative trust risk.</summary>
    ApprovedSessions,
    /// <summary>Explicitly approved resident concurrent execution across tenants.</summary>
    Shared
}
