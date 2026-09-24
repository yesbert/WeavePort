using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal sealed class ExternalActionService : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, int> _ledger = [];
    private readonly List<Task> _requests = [];
    private readonly string _path;
    private readonly Task _loop;
    internal Uri Address { get; }
    internal int Count
    {
        get
        {
            lock (_ledger)
            {
                return _ledger.Count;
            }
        }
    }

    internal ExternalActionService(string path)
    {
        _path = path;
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Address = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Address.ToString());
        _listener.Start();
        _loop = ListenAsync();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext request = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                _requests.Add(HandleAsync(request));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
        }
    }

    private async Task HandleAsync(HttpListenerContext request)
    {
        try
        {
            using JsonDocument body = await JsonDocument.ParseAsync(request.Request.InputStream, cancellationToken: _stop.Token);
            JsonElement p = body.RootElement;
            string key = JsonSerializer.Serialize(new[] { p.GetProperty("tenant").GetString(), p.GetProperty("args").GetProperty("key").GetString() });
            JsonElement args = p.GetProperty("args");
            bool compensate = request.Request.Url!.AbsolutePath == "/compensate";
            bool failure = args.TryGetProperty("fail", out JsonElement fail) && fail.GetBoolean();
            string status = RecordAction(key, compensate, failure);
            if (args.TryGetProperty("loseResponse", out JsonElement lose) && lose.GetBoolean())
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token);
            }

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                status,
                count = 1
            });
            request.Response.ContentType = "application/json";
            request.Response.ContentLength64 = bytes.Length;
            await request.Response.OutputStream.WriteAsync(bytes, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
        catch (HttpListenerException) { return; }
        finally { request.Response.Close(); }
    }

    private string RecordAction(string key, bool compensate, bool failure)
    {
        lock (_ledger)
        {
            string status = ApplyAction(key, compensate, failure);
            File.WriteAllText(_path, JsonSerializer.Serialize(_ledger));
            return status;
        }
    }

    // Caller holds the ledger lock through mutation and persistence.
    private string ApplyAction(string key, bool compensate, bool failure)
    {
        if (!compensate)
        {
            _ledger.TryAdd(key, 1);
            return "committed";
        }
        if (failure)
        {
            return "compensation-failed";
        }
        _ledger.Remove(key);
        return "compensated";
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _loop;
        await Task.WhenAll(_requests);
        _listener.Close();
        _stop.Dispose();
    }
}
