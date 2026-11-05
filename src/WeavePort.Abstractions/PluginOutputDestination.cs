namespace WeavePort.Abstractions;
/// <summary>Built-in output destinations. Intended for tools and tests; products with their own storage implement <see cref = "IPluginOutputDestination"/>.</summary>
public static class PluginOutputDestination
{
    /// <summary>Writes each output to a hidden temporary file in an existing directory and moves it to its output name on commit. An existing file of that name is never replaced; abort deletes the temporary file.</summary>
    public static IPluginOutputDestination ToDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The output directory must exist.");
        }

        return new DirectoryDestination(root);
    }

    private sealed class DirectoryDestination(string root) : IPluginOutputDestination
    {
        public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken)
        {
            string target = Path.Combine(root, WeavePort.Internal.FileDescriptor.Name(name, nameof(name)));
            string partial = Path.Combine(root, "." + name + "." + Guid.NewGuid().ToString("N") + ".partial");
            var stream = new FileStream(partial, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            });
            return ValueTask.FromResult<IPluginOutputWriter>(new FileWriter(stream, partial, target));
        }
    }

    private sealed class FileWriter(FileStream stream, string partial, string target) : IPluginOutputWriter
    {
        private bool _committed;

        public ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) => stream.WriteAsync(block, cancellationToken);

        public async ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            await stream.FlushAsync(cancellationToken);
            await stream.DisposeAsync();
            File.Move(partial, target, overwrite: false);
            _committed = true;
        }

        public async ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken)
        {
            await stream.DisposeAsync();
            File.Delete(partial);
        }

        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            if (!_committed)
            {
                File.Delete(partial);
            }
        }
    }
}
