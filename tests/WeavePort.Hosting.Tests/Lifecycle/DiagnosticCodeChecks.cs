using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class DiagnosticCodeChecks
{
    internal static void Run()
    {
        var cases = from status in new[] { "busy", "denied", "timeout", "protocol-error", "future-peer-code" }
                    from mayHaveExecuted in new[] { false, true }
                    select (status, mayHaveExecuted);
        foreach (var (status, mayHaveExecuted) in cases)
        {
            IOException originalCategory = new PluginCallException(status, mayHaveExecuted);
            var error = (PluginCallException)originalCategory;
            if (error.ErrorCode != status || error.Status != status || error.MayHaveExecuted != mayHaveExecuted)
            {
                throw new Exception("Diagnostic code changed operation outcome semantics.");
            }
        }
        IOException mismatchCategory = new PluginVersionMismatchException("expected-secret", "advertised-secret");
        var mismatch = (PluginVersionMismatchException)mismatchCategory;
        if (mismatch.ErrorCode != "version-mismatch" || mismatch.Mismatch.Expected != "expected-secret" ||
            mismatch.Mismatch.Advertised != "advertised-secret" || mismatch.Message.Contains("secret"))
        {
            throw new Exception("Version mismatch diagnostic contract changed.");
        }

        Console.WriteLine("PASS 11 diagnostic code, exception category and uncertainty assertions");
    }
}
