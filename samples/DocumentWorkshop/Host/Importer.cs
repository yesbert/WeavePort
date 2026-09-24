using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

namespace DocumentWorkshop.Host;

internal sealed class Importer(RuntimePaths runtime, Catalog catalog, TextWriter output)
{
    internal async Task<ImportResult> ImportAsync(ImportOptions options, CancellationToken token, TestHooks? hooks = null)
    {
        if (string.IsNullOrWhiteSpace(options.Tenant) || string.IsNullOrWhiteSpace(options.Profile))
        {
            throw new ArgumentException("Tenant and profile are required.");
        }

        string media = Catalog.MediaType(options.File);
        var installation = runtime.Resolve();
        ReaderInfo[] candidates = catalog.Select(media, options.Reader).Select(r => r with { Version = installation.Identity.Version }).ToArray();
        if (new FileInfo(options.File).Length > Limits.SourceBytes)
        {
            throw new InvalidDataException("Source exceeds 8 MiB.");
        }

        string store = Path.GetFullPath(options.Store);
        string staging = Path.Combine(store, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string sourcePath = Path.Combine(staging, "source.bin");
            var (length, digest) = await CaptureAsync(options.File, sourcePath, token);
            var declined = new List<string>();
            foreach (var reader in candidates)
            {
                token.ThrowIfCancellationRequested();
                var source = new DocumentSource(Guid.NewGuid().ToString("N"), Path.GetFileName(options.File), media, length);
                var result = await ExtractAsync(new Attempt(reader, options, source, sourcePath, digest, staging, installation.Identity), token, hooks);
                if (result is not null)
                {
                    return result with
                    {
                        DeclinedReaders = declined.ToArray()
                    };
                }

                declined.Add(reader.Id);
                await output.WriteLineAsync($"Reader {reader.Id} declined this content; no result from that reader was accepted.");
            }

            throw new InvalidDataException("No reader produced document content.");
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private sealed record Attempt(ReaderInfo Reader, ImportOptions Options, DocumentSource Source, string SourcePath, string Digest, string Staging, InstallationIdentity Installation);
    private async Task<ImportResult?> ExtractAsync(Attempt attempt, CancellationToken token, TestHooks? hooks)
    {
        var (reader, options, source, sourcePath, _, staging, pin) = attempt;
        var installation = runtime.Resolve(pin);
        await using var lease = new SourceLease(sourcePath, options.Tenant, options.Profile, source);
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 0));
        var process = new ProcessProfile(runtime.Dotnet, [installation.EntryPoints["dotnet"], reader.Id], trustedCode: true, workspaceRoot: Path.Combine(runtime.Root, "workers"), timeout: TimeSpan.FromSeconds(10));
        var session = await host.BindAsync(new PluginContext(options.Tenant, reader.Id, reader.Version, options.Profile, JsonSerializer.SerializeToElement(new
        {
        })), process, lease, hooks?.DenyRead == true ? [] : ["document.read"]);
        await using var client = new LocalPluginClient(session);
        ReaderInfo actual = await client.CallAsync<object, ReaderInfo>("reader.describe", new
        {
        }, token);
        if (Json.Serialize(actual) != Json.Serialize(reader))
        {
            throw new InvalidDataException("Reader metadata does not match installation.");
        }

        await client.CallAsync<DocumentSource, ReaderInfo>("reader.open", source, token);
        if (hooks?.Bound is not null)
        {
            await hooks.Bound(session, lease);
        }

        string pending = Path.Combine(staging, "result.ndjson");
        var validator = new PageValidator(source.Length);
        await using (var destination = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await WriteDocumentHeaderAsync(destination, attempt, token);
            if (!await ReadPagesAsync(client, session, destination, validator, hooks, token))
            {
                return null;
            }

            if (validator.Fragments == 0)
            {
                throw new InvalidDataException("Reader returned an empty document.");
            }

            await WriteAsync(destination, new
            {
                kind = "complete",
                sections = validator.Sections,
                fragments = validator.Fragments
            }, token);
        }

        token.ThrowIfCancellationRequested();
        string committed = Path.Combine(Path.GetFullPath(options.Store), Guid.NewGuid().ToString("N") + ".ndjson");
        _ = runtime.Resolve(pin);
        File.Move(pending, committed);
        return new ImportResult(committed, reader.Id, validator.Sections, validator.Fragments, source.Length, lease.Calls, lease.MaximumRead, []);
    }

    private static Task WriteDocumentHeaderAsync(FileStream destination, Attempt attempt, CancellationToken token)
    {
        var (reader, options, source, _, digest, _, pin) = attempt;
        return WriteAsync(destination, new
        {
            kind = "document",
            source.FileName,
            source.MediaType,
            source.Length,
            sha256 = digest,
            reader = reader.Id,
            version = reader.Version,
            installation = pin,
            tenant = options.Tenant,
            profile = options.Profile
        }, token);
    }

    private static async Task<bool> ReadPagesAsync(IPluginClient client, IPluginSession session, FileStream destination, PageValidator validator, TestHooks? hooks, CancellationToken token)
    {
        int cursor = 0;
        while (true)
        {
            ExtractionPage page = await client.CallAsync<PageRequest, ExtractionPage>("reader.next", new PageRequest(cursor), token);
            page = hooks?.TransformPage is { } transform ? transform(page) : page;
            validator.Accept(page);
            await WriteFragmentsAsync(destination, page.Fragments, token);
            await NotifyPageReceivedAsync(hooks, session, page);
            token.ThrowIfCancellationRequested();
            cursor = page.NextCursor;
            if (page.Declined)
            {
                return false;
            }

            if (page.Complete)
            {
                break;
            }
        }

        return true;
    }

    private static async Task<(long Length, string Digest)> CaptureAsync(string original, string snapshot, CancellationToken token)
    {
        long length = 0;
        await using (var input = File.OpenRead(original))
        {
            await using (var destination = File.Create(snapshot))
            {
                var buffer = new byte[Limits.ReadBytes];
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    length += read;
                    if (length > Limits.SourceBytes)
                    {
                        throw new InvalidDataException("Source exceeds 8 MiB.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), token);
                }
            }
        }

        await using var captured = File.OpenRead(snapshot);
        return (length, Convert.ToHexString(await SHA256.HashDataAsync(captured, token)));
    }

    private static async Task WriteAsync(FileStream destination, object value, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(value) + "\n");
        if (destination.Length + bytes.Length > Limits.OutputBytes)
        {
            throw new InvalidDataException("Normalized output exceeds 32 MiB.");
        }

        await destination.WriteAsync(bytes, token);
    }

    private static async Task WriteFragmentsAsync(FileStream destination, Fragment[] fragments, CancellationToken token)
    {
        foreach (var fragment in fragments)
        {
            await WriteAsync(destination, new
            {
                kind = "fragment",
                fragment
            }, token);
        }
    }

    private static async Task NotifyPageReceivedAsync(TestHooks? hooks, IPluginSession session, ExtractionPage page)
    {
        if (hooks?.PageReceived is not null)
        {
            await hooks.PageReceived(session, page);
        }
    }
}
