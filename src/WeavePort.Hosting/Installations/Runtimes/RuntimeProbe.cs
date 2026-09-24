using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WeavePort.Hosting;
internal static class RuntimeProbe
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    internal const int MaximumOutput = 65536;
    internal static string Run(string executable, string ecosystem)
    {
        using var probe = new Probe(executable, ecosystem);
        if (!probe.Process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            throw new InvalidDataException("Runtime probe timed out.");
        }

        return probe.Finish();
    }

    internal static async Task<string> RunAsync(string executable, string ecosystem, CancellationToken token)
    {
        using var probe = new Probe(executable, ecosystem);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Timeout);
        try
        {
            await probe.Process.WaitForExitAsync(deadline.Token);
            await probe.ReadersComplete.Task.WaitAsync(deadline.Token);
            return probe.Finish();
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new InvalidDataException("Runtime probe timed out.", error);
        }
    }

    private sealed class Probe : IDisposable
    {
        internal Process Process { get; }
        internal TaskCompletionSource ReadersComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Thread _stdout;
        private readonly Thread _stderr;
        private readonly StringBuilder _output = new();
        private int _remaining = 2;
        private int _count;
        private Exception? _error;
        internal Probe(string executable, string ecosystem)
        {
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            {
                throw new InvalidDataException("Approved runtime executable is missing or not absolute.");
            }

            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                CreateNoWindow = true
            };
            info.Environment.Clear();
            if (OperatingSystem.IsWindows())
            {
                info.Environment["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            }

            string[] arguments = ecosystem switch
            {
                "dotnet" => ["--list-runtimes"],
                "python" => ["-I", "-S", "--version"],
                "node" => ["--version"],
                _ => throw new InvalidDataException("Unknown runtime probe ecosystem.")};
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            try
            {
                Process = Process.Start(info) ?? throw new IOException("Runtime probe did not start.");
            }
            catch (Win32Exception error)
            {
                throw new InvalidDataException("Runtime executable could not start.", error);
            }

            _stdout = new Thread(() => Read(Process.StandardOutput, true))
            {
                IsBackground = true
            };
            _stderr = new Thread(() => Read(Process.StandardError, false))
            {
                IsBackground = true
            };
            _stdout.Start();
            _stderr.Start();
        }

        private void Read(StreamReader reader, bool output)
        {
            try
            {
                char[] buffer = new char[1024];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (Interlocked.Add(ref _count, count) > MaximumOutput)
                    {
                        throw new InvalidDataException("Runtime probe output exceeds 64 KiB.");
                    }

                    if (!output)
                    {
                        continue;
                    }

                    _output.Append(buffer, 0, count);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                Interlocked.CompareExchange(ref _error, error, null);
                Stop();
            }
            finally
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    ReadersComplete.TrySetResult();
                }
            }
        }

        internal string Finish()
        {
            if (!_stdout.Join(TimeSpan.FromSeconds(1)) || !_stderr.Join(TimeSpan.FromSeconds(1)))
            {
                throw new InvalidDataException("Runtime probe output did not close.");
            }

            if (_error is not null)
            {
                throw new InvalidDataException("Runtime probe output failed.", _error);
            }

            if (Process.ExitCode != 0)
            {
                throw new InvalidDataException($"Runtime probe exited with code {Process.ExitCode}.");
            }

            return _output.ToString().Trim();
        }

        private void Stop()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            { /* The process already exited. */
            }
            catch (Win32Exception error)
            {
                Interlocked.CompareExchange(ref _error, error, null);
            }
        }

        public void Dispose()
        {
            Stop();
            Process.StandardOutput.Dispose();
            Process.StandardError.Dispose();
            _stdout.Join(TimeSpan.FromSeconds(1));
            _stderr.Join(TimeSpan.FromSeconds(1));
            Process.Dispose();
        }
    }
}
