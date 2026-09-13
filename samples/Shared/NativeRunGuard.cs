using System.Text.Json;

namespace WeavePort.Samples;
// Cooperative deployment gate. A marker is evidence to investigate, never process-kill authority.
internal sealed class NativeRunGuard : IDisposable
{
    private readonly string _markerPath;
    private readonly FileStream _marker;
    private bool _closed;
    internal NativeRunGuard(string directory)
    {
        Directory.CreateDirectory(directory);
        _markerPath = Path.Combine(directory, "run.json");
        Generation = Guid.NewGuid().ToString("N");
        WorkspaceRoot = Path.Combine(directory, "workers", Generation);
        try
        {
            _marker = new FileStream(_markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (IOException error)
        {
            throw new IOException("Native startup blocked. Preserve runtime/run.json and follow docs/native-operations.md before restart.", error);
        }

        try
        {
            JsonSerializer.Serialize(_marker, new { Schema = 1, Generation });
            _marker.Flush(flushToDisk: true);
        }
        catch
        {
            _marker.Dispose();
            // Even partial markers remain a conservative restart gate.
            throw;
        }
    }

    internal string Generation { get; }
    internal string WorkspaceRoot { get; }

    internal void MarkClean(CoordinatorSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!snapshot.Clean)
        {
            throw new InvalidOperationException("Unconfirmed shutdown cannot clear the native run marker.");
        }

        // Delete only an empty generation root; unexpected retained files keep the gate closed.
        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot);
        }

        Dispose();
        File.Delete(_markerPath);
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _marker.Dispose();
        _closed = true;
    // Releasing the handle must never acknowledge cleanup after an exception or crash.
    }
}
