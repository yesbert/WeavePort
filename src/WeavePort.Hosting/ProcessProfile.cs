using System.Text.Json;

namespace WeavePort.Hosting;
/// <summary>Local same-user processes for explicitly trusted code. No filesystem, network or hard-resource sandbox.</summary>
public sealed record ProcessProfile : ExecutionProfile
{
    private readonly string _arguments;
    /// <summary>Explicit wire protocol. Native is the default; MCP revisions require stdio.</summary>
    public ProcessProtocol Protocol { get; init; }
    /// <summary>Absolute executable path; no PATH lookup or shell interpolation.</summary>
    public string Executable { get; init; }
    /// <summary>A fresh copy of the frozen command arguments.</summary>
    public string[] Arguments => JsonSerializer.Deserialize<string[]>(_arguments)!;
    /// <summary>Parent of unique cooperative workspaces, not a security boundary against same-user code.</summary>
    public string WorkspaceRoot { get; init; }
    /// <summary>Explicit acknowledgement that the application trusts the launched code and its dependencies.</summary>
    public bool TrustedCode { get; init; }
    /// <summary>Use a private native Unix socket instead of stdio. Opt-in; does not provide a sandbox.</summary>
    public bool UseUnixSocket { get; init; }
    /// <summary>Optional host socket buffer request, 4 KiB–1 MiB per direction. OS accounting/limits apply; not a worker memory ceiling.</summary>
    public int? SocketBufferBytes { get; init; }
    /// <inheritdoc/>
    public override ExecutionProtections Protection => ExecutionProtections.None;

    /// <summary>Freezes arguments and declares scheduling reservations; does not enforce memory or CPU limits.</summary>
    /// <param name = "executable">Absolute executable path.</param>
    /// <param name = "arguments">Arguments copied at construction, passed without a shell.</param>
    /// <param name = "trustedCode">Must be true before this profile can be used.</param>
    /// <param name = "workspaceRoot">Optional absolute private workspace parent.</param>
    /// <param name = "reservedMemoryMiB">Admission estimate, not a hard ceiling.</param>
    /// <param name = "timeout">Total invocation deadline.</param>
    /// <param name = "idleTimeout">Optional idle release.</param>
    public ProcessProfile(string executable, IEnumerable<string> arguments, bool trustedCode = false, string? workspaceRoot = null, int reservedMemoryMiB = 256, TimeSpan? timeout = null, TimeSpan? idleTimeout = null) : base(reservedMemoryMiB, timeout, idleTimeout)
    {
        Executable = executable;
        _arguments = JsonSerializer.Serialize(arguments.ToArray());
        TrustedCode = trustedCode;
        WorkspaceRoot = workspaceRoot ?? Path.Combine(Path.GetTempPath(), "weaveport-processes");
    }

    internal override Task<ExecutionProfile> ResolveAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!TrustedCode)
        {
            throw new NotSupportedException("Local execution requires explicit trusted-code acknowledgement; it is not a sandbox.");
        }

        if (!Enum.IsDefined(Protocol) || Protocol != ProcessProtocol.Native && UseUnixSocket)
        {
            throw new ArgumentException("Unsupported process protocol or MCP channel.");
        }

        if (SocketBufferBytes is < 4096 or > 1048576 || SocketBufferBytes is not null && !UseUnixSocket)
        {
            throw new ArgumentException("Socket buffers require Unix-socket mode and a 4 KiB–1 MiB request.");
        }

        if (UseUnixSocket && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Native Unix sockets require macOS or Linux.");
        }

        if (!Path.IsPathFullyQualified(Executable) || !File.Exists(Executable))
        {
            throw new FileNotFoundException("Local executable must exist at an absolute path.");
        }

        if (!Path.IsPathFullyQualified(WorkspaceRoot) || Arguments.Any(a => a is null || a.Contains('\0')))
        {
            throw new ArgumentException("Invalid local launch configuration.");
        }

        return Task.FromResult<ExecutionProfile>(this);
    }

    internal override ExecutionProfile Normalize() => this with
    {
        Timeout = null,
        IdleTimeout = null
    };
    internal override Worker CreateWorker(string version, TimeProvider clock) => new ProcessWorker(this, version, clock);
}
