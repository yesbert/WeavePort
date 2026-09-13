using System.Text;
using System.Text.Json;
using DocumentWorkshop.Contracts;

namespace DocumentWorkshop.Host;
internal static class Verification
{
    internal static async Task RunAsync(RuntimePaths runtime, CancellationToken token)
    {
        string root = Path.Combine(runtime.Root, "verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        runtime = runtime with
        {
            SelectedVersion = null,
            Selector = Path.Combine(root, "active-version.txt")
        };
        runtime.Activate("1");
        await VerifyActivationAsync(runtime, root, token);
        var importer = new Importer(runtime, new Catalog(new(["markdown", "plain"])), TextWriter.Null);
        var handbook = await importer.ImportAsync(new("samples/DocumentWorkshop/fixtures/handbook.md", Path.Combine(root, "outline")), token);
        Fragment[] outline = Fragments(handbook.Path);
        Check(handbook.Reader == "markdown" && handbook.Sections == 4, "outline reader creates four sections");
        Check(outline[0].Anchor == "handbook" && outline.Any(f => f.Anchor == "installation" && f.Level == 2), "explicit anchors and levels retained");
        Check(outline.Any(f => f.Text.Contains("# This is code", StringComparison.Ordinal)), "fenced heading stays in body text");
        var plain = await importer.ImportAsync(new("samples/DocumentWorkshop/fixtures/handbook.md", Path.Combine(root, "plain"), Reader: "plain"), token);
        Check(plain.Reader == "plain" && plain.Sections == 1 && Fragments(plain.Path)[0].Text.StartsWith("# Workshop", StringComparison.Ordinal), "reader substitution changes structure without host edits");
        var fallback = await importer.ImportAsync(new("samples/DocumentWorkshop/fixtures/notes.md", Path.Combine(root, "fallback")), token);
        Check(fallback.Reader == "plain" && fallback.DeclinedReaders.SequenceEqual(["markdown"]), "explicit content decline triggers fallback");
        await RefusedAsync(() => importer.ImportAsync(new("samples/DocumentWorkshop/fixtures/notes.md", Path.Combine(root, "forced"), Reader: "markdown"), token));
        Clean(Path.Combine(root, "forced"));
        Check(true, "explicit reader never silently substitutes another reader");
        var restricted = new Importer(runtime, new Catalog(new(["plain"])), TextWriter.Null);
        await RefusedAsync(() => restricted.ImportAsync(new("samples/DocumentWorkshop/fixtures/handbook.md", Path.Combine(root, "missing"), Reader: "markdown"), token));
        await RefusedAsync(() => importer.ImportAsync(new("unsupported.pdf", Path.Combine(root, "unsupported")), token));
        Check(!Directory.Exists(Path.Combine(root, "missing")) && !Directory.Exists(Path.Combine(root, "unsupported")), "missing reader and unsupported type write nothing");
        string prefix = string.Concat(Enumerable.Repeat(new string ('a', 1023) + "\n", 31)) + new string ('b', 1023);
        string text = prefix + "😀\n" + string.Concat(Enumerable.Repeat("Grüße 日本語 " + new string ('x', 980) + "\n", 1400));
        string large = Path.Combine(root, "large.txt");
        await File.WriteAllTextAsync(large, text, new UTF8Encoding(false), token);
        var big = await importer.ImportAsync(new(large, Path.Combine(root, "large-store")), token);
        Check(big.SourceBytes > 1024 * 1024 && big.ReadCalls > 1 && big.MaximumRead <= Limits.ReadBytes, "source larger than frame uses bounded reads");
        Check(string.Concat(Fragments(big.Path).Select(f => f.Text)) == text, "UTF-8 and fragment boundary content preserved completely");
        using (var header = JsonDocument.Parse(File.ReadLines(big.Path).First()))
        {
            await using var input = File.OpenRead(large);
            string hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token));
            Check(header.RootElement.GetProperty("sha256").GetString() == hash, "committed source digest matches captured bytes");
        }

        string lines = Path.Combine(root, "lines.txt");
        await File.WriteAllTextAsync(lines, "\uFEFFone\r\ntwo\rthree", new UTF8Encoding(false), token);
        var normalized = await importer.ImportAsync(new(lines, Path.Combine(root, "normalized")), token);
        Check(string.Concat(Fragments(normalized.Path).Select(f => f.Text)) == "one\ntwo\nthree\n", "BOM and line endings normalized");
        await FailureVerification.RunAsync(runtime, importer, large, root, token);
        await LeaseVerification.RunAsync(root, token);
        await File.WriteAllTextAsync(Path.Combine(root, "result.txt"), "All Document Workshop checks passed. Native local execution only.\n", token);
        Console.WriteLine("Verification passed. Evidence: " + root);
    }

    internal static Fragment[] Fragments(string path) => File.ReadLines(path).Select(line => JsonDocument.Parse(line)).Select(item =>
    {
        using (item)
        {
            return item.RootElement.GetProperty("kind").GetString() == "fragment" ? item.RootElement.GetProperty("fragment").Deserialize<Fragment>(Json.Options) : null;
        }
    }).OfType<Fragment>().ToArray();
    internal static void Check(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidDataException("FAIL: " + label);
        }

        Console.WriteLine("PASS: " + label);
    }

    internal static async Task RefusedAsync(Func<Task<ImportResult>> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException)
        {
            return;
        }

        throw new InvalidDataException("Expected import refusal.");
    }

    internal static void Clean(string store)
    {
        Check(!Directory.Exists(store) || !Directory.EnumerateFiles(store, "*.ndjson").Any(), "no partial document committed");
        string staging = Path.Combine(store, ".staging");
        Check(!Directory.Exists(staging) || !Directory.EnumerateFileSystemEntries(staging).Any(), "owned staging removed");
    }

    private static async Task VerifyActivationAsync(RuntimePaths runtime, string root, CancellationToken token)
    {
        var importer = new Importer(runtime, new Catalog(new(["markdown", "plain"])), TextWriter.Null);
        string fixture = "samples/DocumentWorkshop/fixtures/handbook.md";
        var old = await importer.ImportAsync(new(fixture, Path.Combine(root, "activation-old")), token, new(Bound: async (_, _) =>
        {
            runtime.Activate("2");
            var next = await importer.ImportAsync(new(fixture, Path.Combine(root, "activation-new")), token);
            using var header = JsonDocument.Parse(File.ReadLines(next.Path).First());
            Check(header.RootElement.GetProperty("installation").GetProperty("version").GetString() == "2", "new import selects activated release while old reader is live");
        }));
        using var original = JsonDocument.Parse(File.ReadLines(old.Path).First());
        Check(original.RootElement.GetProperty("installation").GetProperty("version").GetString() == "1", "live import commits its pinned installation after activation");
        runtime.Activate("1");
    }
}
