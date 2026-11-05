using System.IO.Compression;
using System.Text.Json;
using WeavePort.Sdk;

// C# side of the cross-language function file contract; files.py and files.mjs register the same functions.
internal static class FileFixture
{
    private static Stream? _retained;

    internal static Task RunAsync(string[] args)
    {
        var app = new PluginApplication
        {
            FileBlockBytes = args.Contains("--block-bytes") ? int.Parse(args[Array.IndexOf(args, "--block-bytes") + 1], System.Globalization.CultureInfo.InvariantCulture) : 65536
        };
        RegisterReads(app);
        RegisterOutputs(app);
        return app.RunAsync();
    }

    private static void RegisterReads(PluginApplication app) => app
        .Function<JsonElement, object>("stats", (_, _, _) => ValueTask.FromResult<object>(new { pid = Environment.ProcessId }))
        .Function<JsonElement, object>("zip-summary", (_, context, _) =>
        {
            using Stream sheet = context.OpenInput("sheet");
            using var archive = new ZipArchive(sheet, ZipArchiveMode.Read, leaveOpen: true);
            return ValueTask.FromResult<object>(new
            {
                names = archive.Entries.Select(entry => entry.FullName).ToArray(),
                inputs = context.Inputs.Select(input => new { name = input.Name, length = input.Length, mediaType = input.MediaType }).ToArray()
            });
        })
;

    private static void RegisterOutputs(PluginApplication app) => app
        .Function<JsonElement, object>("copy", async (_, context, token) =>
        {
            await using Stream source = context.OpenInput("sheet");
            await using PluginOutputStream copy = await context.CreateOutputAsync("copy.bin", "application/octet-stream", token);
            await source.CopyToAsync(copy, token);
            await copy.CompleteAsync(token);
            return new
            {
                length = copy.Position
            };
        })
        .Function<JsonElement, object>("leave-open", async (_, context, token) =>
        {
            PluginOutputStream open = await context.CreateOutputAsync("open.bin", "text/plain", token);
            await open.WriteAsync(new byte[] { 1 }, token);
            return new
            {
                done = true
            };
        })
        .Function<JsonElement, object>("refusals", async (_, context, token) => new
        {
            codes = new[] { await CodeAsync(() => Task.FromResult(context.OpenInput("nope"))), await CodeAsync(() => context.CreateOutputAsync("x.bin", "text/plain", token)) }
        })
        .Function<JsonElement, object>("cached-reads", async (_, context, token) =>
        {
            await using Stream sheet = context.OpenInput("sheet");
            byte[] buffer = new byte[100];
            for (int index = 0; index < 10; index++)
            {
                sheet.Position = index * 1000;
                await sheet.ReadExactlyAsync(buffer, token);
            }

            return new
            {
                done = true
            };
        })
        .Function<JsonElement, object>("late-store", (_, context, _) =>
        {
            _retained = context.OpenInput("sheet");
            return ValueTask.FromResult<object>(new
            {
                stored = true
            });
        })
        .Function<JsonElement, object>("late-read", async (_, _, token) => new { refused = await CodeAsync(async () => await _retained!.ReadAsync(new byte[16], token)) != "" });

    private static async Task<string> CodeAsync<T>(Func<Task<T>> action)
    {
        try
        {
            await action();
            return "";
        }
        catch (PluginFileException error)
        {
            return error.Code;
        }
        catch (InvalidOperationException)
        {
            return "expired";
        }
    }
}
