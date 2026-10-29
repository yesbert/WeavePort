namespace WeavePort.Hosting;

/// <summary>An immutable trust snapshot. Replacing it does not revoke previously returned installations or workers.</summary>
public sealed class PluginPublisherPolicy
{
    private readonly Dictionary<string, TrustedPluginPublisher> _publishers;
    /// <summary>Gets the compatibility policy that validates content without claiming publisher authenticity.</summary>
    public static PluginPublisherPolicy IntegrityOnly { get; } = new(false, []);
    /// <summary>Gets whether every release must authenticate against a configured publisher.</summary>
    public bool RequiresSignature { get; }

    private PluginPublisherPolicy(bool requiresSignature, IEnumerable<TrustedPluginPublisher> publishers)
    {
        RequiresSignature = requiresSignature;
        _publishers = new(StringComparer.Ordinal);
        foreach (TrustedPluginPublisher publisher in publishers)
        {
            ArgumentNullException.ThrowIfNull(publisher);
            if (!_publishers.TryAdd(publisher.KeyId, publisher))
            {
                throw new ArgumentException("Publisher keys must have distinct fingerprints.", nameof(publishers));
            }
        }
    }

    /// <summary>Copies a nonempty set of trusted publishers. Withdraw keys by replacing the catalog/policy and re-resolving installations.</summary>
    public static PluginPublisherPolicy RequireTrustedPublisher(IEnumerable<TrustedPluginPublisher> publishers)
    {
        ArgumentNullException.ThrowIfNull(publishers);
        var policy = new PluginPublisherPolicy(true, publishers);
        if (policy._publishers.Count == 0)
        {
            throw new ArgumentException("Required publisher policy needs at least one trusted key.", nameof(publishers));
        }

        return policy;
    }

    internal TrustedPluginPublisher? Find(string keyId) => _publishers.GetValueOrDefault(keyId);
}
