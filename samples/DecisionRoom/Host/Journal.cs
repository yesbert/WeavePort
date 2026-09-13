using System.Text.Json;
using DecisionRoom.Contracts;

namespace DecisionRoom.Host;
internal sealed record JournalData(int Schema, string Configuration, Dictionary<string, string> Artifacts, Evaluation[] Events);
internal sealed class Journal : IDisposable
{
    private readonly string _path;
    private readonly FileStream _lock;
    internal JournalData Data { get; private set; }
    internal RunConfiguration Configuration { get; }

    internal Journal(string path, RunConfiguration config, RuntimePaths runtime, bool resume)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _lock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            JournalData? saved = resume ? JsonSerializer.Deserialize<JournalData>(File.ReadAllText(_path), Wire.Json) ?? throw new InvalidDataException("Empty journal.") : null;
            if (resume && saved!.Schema != 2)
            {
                throw new InvalidDataException("Journal schema is incompatible; use the original build or a new journal.");
            }

            string? pinned = saved is null ? null : JsonSerializer.Deserialize<RunConfiguration>(saved.Configuration, Wire.Json)?.PluginVersion;
            if (resume && pinned is null)
            {
                throw new InvalidDataException("Journal lacks a pinned plugin version.");
            }

            Configuration = config with
            {
                PluginVersion = config.PluginVersion ?? pinned ?? runtime.CurrentVersion()
            };
            var expected = new JournalData(2, Wire.Serialize(Configuration), runtime.Identity(Configuration.PluginVersion!), []);
            if (saved is not null)
            {
                if (saved.Configuration != expected.Configuration || Wire.Serialize(saved.Artifacts) != Wire.Serialize(expected.Artifacts) || saved.Events.Length > 2)
                {
                    throw new InvalidDataException("Journal configuration, schema or artifacts do not match.");
                }

                Data = saved;
            }
            else
            {
                if (File.Exists(_path))
                {
                    throw new InvalidDataException("Journal already exists; use --resume or a new --journal path.");
                }

                Data = expected;
            }
        }
        catch
        {
            _lock.Dispose();
            throw;
        }
    }

    internal void VerifyArtifacts(RuntimePaths runtime)
    {
        if (Wire.Serialize(Data.Artifacts) != Wire.Serialize(runtime.Identity(Configuration.PluginVersion!)))
        {
            throw new InvalidDataException("Pinned plugin artifacts changed; refusing to continue.");
        }
    }

    internal async Task CommitAsync(Evaluation[] events, CancellationToken token)
    {
        var next = Data with
        {
            Events = events
        };
        string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, Wire.Serialize(next), token);
            File.Move(temporary, _path, overwrite: true);
            Data = next;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public void Dispose() => _lock.Dispose();
}
