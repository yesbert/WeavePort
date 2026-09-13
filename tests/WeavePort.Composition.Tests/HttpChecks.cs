using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal static class HttpChecks
{
    internal static async Task RunAsync(string config, string output)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        await using var scenario = await BulkScenario.CreateAsync(config, Path.Combine(output, "http-store"), "fixed-authenticated-fixture-tenant");
        string key = Guid.NewGuid().ToString("N");
        using var admission = new SemaphoreSlim(1);
        app.MapPost("/results", async (HttpContext context) =>
        {
            if (context.Request.Headers["X-Test-Key"] != key) { context.Response.StatusCode = 401; return; }
            if (!int.TryParse(context.Request.Query["kib"], out int kib) || kib is < 256 or > 131072 || kib % 64 != 0) { context.Response.StatusCode = 400; return; }
            if (!await admission.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            try
            {
                bool fan = context.Request.Query["fan"] == "true";
                context.Response.ContentType = "application/x-ndjson";
                context.Response.ContentLength = (long)kib * 1024 * (fan ? 3 : 1);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                await scenario.RunAsync(kib * 1024, fan, 262144, token: deadline.Token, destination: context.Response.Body);
            }
            finally { admission.Release(); }
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromMinutes(3) };
            using var denied = await client.PostAsync("/results?kib=256", null);
            if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidOperationException("Fixture endpoint must reject absent key.");
            client.DefaultRequestHeaders.Add("X-Test-Key", key);
            var rows = new List<object>();
            foreach (int kib in new[] { 256, 8192, 32768, 131072 }) foreach (bool fan in new[] { false, true })
            {
                long start = Stopwatch.GetTimestamp();
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/results?kib={kib}&fan={fan.ToString().ToLowerInvariant()}");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                double headersMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                await using Stream body = await response.Content.ReadAsStreamAsync();
                using var caller = new CheckingSink(BulkScenario.Pattern(true));
                await body.CopyToAsync(caller);
                long expected = (long)kib * 1024 * (fan ? 3 : 1);
                if (caller.Count != expected) throw new InvalidDataException("HTTP caller length mismatch.");
                rows.Add(new { inputKiB = kib, fanOut = fan, deliveredBytes = caller.Count, headersMs, completeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds, correctness = "all HTTP response bytes verified" });
                Console.WriteLine($"HTTP {kib} KiB fan={fan}: {Stopwatch.GetElapsedTime(start).TotalSeconds:F2}s");
            }
            await File.WriteAllTextAsync(Path.Combine(output, "http.json"), JsonSerializer.Serialize(new { transport = "loopback HTTP/1.1", tenant = "fixed test identity, not a production authentication scheme", unauthorizedRejected = true, rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { await app.StopAsync(); }
    }
}
