namespace WeavePort.Hosting;
/// <summary>Trusted deployment choices; never bind these from untrusted plugin input.</summary>
/// <param name = "Image">Locally available image name or digest, resolved once on binding creation.</param>
/// <param name = "Context">Optional existing Docker context name.</param>
/// <param name = "MemoryMiB">Hard memory and memory-plus-swap ceiling.</param>
/// <param name = "CpuCount">CPU ceiling.</param>
/// <param name = "Timeout">Total invocation deadline, including startup and callbacks.</param>
/// <param name = "IdleTimeout">Opt-in idle release; null preserves process state until restart/disposal. Consumers must replay any required state.</param>
/// <param name = "SocketTransport">Optional private Linux Unix-socket transport; null retains Docker CLI stdio.</param>
public sealed record DockerProfile(string Image, string? Context = null, int MemoryMiB = 256, double CpuCount = 0.5, TimeSpan? Timeout = null, TimeSpan? IdleTimeout = null, UnixSocketTransport? SocketTransport = null) : ExecutionProfile(MemoryMiB, Timeout, IdleTimeout)
{
    /// <summary>Absolute trusted Docker CLI path; null selects a conventional system installation without searching PATH.</summary>
    public string? DockerExecutable { get; init; }
    /// <inheritdoc/>
    public override ExecutionProtection Protection => ExecutionProtection.RestrictedFileSystem | ExecutionProtection.DisabledNetwork | ExecutionProtection.HardResourceLimits;

    internal override ExecutionProfile Normalize() => this with
    {
        Timeout = null,
        IdleTimeout = null
    };
    internal override Worker CreateWorker(string version, TimeProvider clock) => new DockerWorker(this, version, clock);
    internal override async Task<ExecutionProfile> ResolveAsync(CancellationToken token)
    {
        SocketTransport?.Validate();
        ValidateCpuCount(CpuCount);
        var resolved = this with
        {
            DockerExecutable = DockerCommand.ResolveExecutable(DockerExecutable)
        };
        string image = await DockerCommand.RunAsync(resolved, ["image", "inspect", "--format", "{{.Id}}", Image], token);
        if (!image.StartsWith("sha256:", StringComparison.Ordinal))
        {
            throw new IOException("Image resolution failed.");
        }

        return resolved with
        {
            Image = image
        };
    }

    private static void ValidateCpuCount(double cpuCount)
    {
        if (cpuCount <= 0 || !double.IsFinite(cpuCount))
        {
            throw new ArgumentOutOfRangeException(nameof(cpuCount));
        }
    }
}
