using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocumentWorkshop.Contracts;

namespace DocumentWorkshop.Host;
internal static class FailureVerification
{
    internal static async Task RunAsync(RuntimePaths runtime, Importer importer, string large, string root, CancellationToken token)
    {
        string Store(string name) => Path.Combine(root, name);
        string invalid = Path.Combine(root, "invalid.txt");
        await File.WriteAllBytesAsync(invalid, [0xff, 0xfe, 0x61], token);
        await Verification.RefusedAsync(() => importer.ImportAsync(new(invalid, Store("encoding")), token));
        Verification.Clean(Store("encoding"));
        Verification.Check(true, "invalid UTF-8 fails explicitly");
        string longLine = Path.Combine(root, "long-line.txt");
        await File.WriteAllTextAsync(longLine, new string ('x', Limits.TextCharacters + 1), token);
        await Verification.RefusedAsync(() => importer.ImportAsync(new(longLine, Store("line-limit")), token));
        Verification.Clean(Store("line-limit"));
        string oversized = Path.Combine(root, "oversized.txt");
        await using (var file = File.Create(oversized))
        {
            file.SetLength(Limits.SourceBytes + 1L);
        }

        await Verification.RefusedAsync(() => importer.ImportAsync(new(oversized, Store("input-limit")), token));
        Verification.Check(!Directory.Exists(Store("input-limit")), "oversized source rejected before staging");
        string expanded = Path.Combine(root, "expanded.txt");
        await File.WriteAllTextAsync(expanded, string.Concat(Enumerable.Repeat(new string ('\0', 4000) + "\n", 1500)), new UTF8Encoding(false), token);
        await Verification.RefusedAsync(() => importer.ImportAsync(new(expanded, Store("output-limit")), token));
        Verification.Clean(Store("output-limit"));
        Verification.Check(true, "expanded JSON output quota prevents a truncated commit");
        SourceLease? denied = null;
        await Verification.RefusedAsync(() => importer.ImportAsync(new(large, Store("denied")), token, new(Bound: (_, lease) =>
        {
            denied = lease;
            return Task.CompletedTask;
        }, DenyRead: true)));
        Verification.Check(denied is { Calls: 0, Revoked: true }, "missing grant returns no bytes and revokes lease");
        Verification.Clean(Store("denied"));
        await Verification.RefusedAsync(() => importer.ImportAsync(new(large, Store("malformed")), token, new(TransformPage: page => page with { NextCursor = 999 })));
        Verification.Clean(Store("malformed"));
        await Verification.RefusedAsync(() => importer.ImportAsync(new(large, Store("truncated")), token, new(TransformPage: page => page with { Complete = true })));
        Verification.Clean(Store("truncated"));
        Verification.Check(true, "malformed cursor and premature completion refused");
        SourceLease? cancelled = null;
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            await Verification.RefusedAsync(() => importer.ImportAsync(new(large, Store("cancelled")), cancel.Token, new(Bound: (_, lease) =>
            {
                cancelled = lease;
                return Task.CompletedTask;
            }, PageReceived: (_, page) =>
            {
                if (page.Fragments.Length > 0)
                {
                    cancel.Cancel();
                }

                return Task.CompletedTask;
            })));
        }

        Verification.Check(cancelled is { Calls: > 0, Revoked: true }, "cancellation after output revokes active source");
        Verification.Clean(Store("cancelled"));
        await VerifyConcurrentFailureAsync(importer, large, root, token);
    }

    private static async Task VerifyConcurrentFailureAsync(Importer importer, string large, string root, CancellationToken token)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SourceLease? failedLease = null;
        string storeA = Path.Combine(root, "failed-a");
        var failing = importer.ImportAsync(new(large, storeA, "tenant-a", "profile-a"), token, new(Bound: async (_, lease) =>
        {
            failedLease = lease;
            reached.SetResult();
            await release.Task.WaitAsync(token);
        }, PageReceived: async (session, page) =>
        {
            if (page.Fragments.Length == 0)
            {
                return;
            }

            string instance = session.Instance;
            int index = instance.LastIndexOf("-p", StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidDataException("Owned worker identity missing.");
            }

            using var worker = Process.GetProcessById(int.Parse(instance[(index + 2)..]));
            worker.Kill();
            await worker.WaitForExitAsync(token);
        }));
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            string other = Path.Combine(root, "other.txt");
            await File.WriteAllTextAsync(other, "Only customer B sees this document.\n", token);
            var success = await importer.ImportAsync(new(other, Path.Combine(root, "success-b"), "tenant-b", "profile-b"), token);
            using var header = JsonDocument.Parse(File.ReadLines(success.Path).First());
            Verification.Check(!failing.IsCompleted && header.RootElement.GetProperty("tenant").GetString() == "tenant-b" && header.RootElement.GetProperty("profile").GetString() == "profile-b" && string.Concat(Verification.Fragments(success.Path).Select(f => f.Text)) == "Only customer B sees this document.\n", "overlapping customer B import commits only its own content");
        }
        finally
        {
            release.TrySetResult();
            await Verification.RefusedAsync(() => failing);
        }

        Verification.Check(failedLease is { Revoked: true, Calls: > 0 }, "actual worker loss revokes source after delivered output");
        Verification.Clean(storeA);
    }
}
