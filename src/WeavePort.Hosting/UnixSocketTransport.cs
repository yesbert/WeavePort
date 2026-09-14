namespace WeavePort.Hosting;
/// <summary>Opt-in Linux transport. Both trusted paths must address the same private directory
/// from the coordinator and Docker daemon respectively. Plugins receive only one read-only leaf mount.</summary>
/// <param name = "LocalDirectory">Short, absolute, coordinator-owned private directory (0700).</param>
/// <param name = "DockerDirectory">Absolute path to the same directory as seen by the Docker daemon.</param>
public sealed record UnixSocketTransport(string LocalDirectory, string DockerDirectory)
{
    internal void Validate()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Unix worker transport requires a Linux coordinator.");
        }

        ValidatePath(LocalDirectory);
        ValidatePath(DockerDirectory);
        if (System.Text.Encoding.UTF8.GetByteCount(Path.Combine(LocalDirectory, "weaveport-" + new string ('0', 32), "p.sock")) > 100)
        {
            throw new ArgumentException("Coordinator socket directory is too long for a Unix endpoint.");
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains(',') || path.Contains('\n') || path.Contains('\0'))
        {
            throw new ArgumentException("Socket directories must be absolute paths without mount separators.");
        }
    }
}
