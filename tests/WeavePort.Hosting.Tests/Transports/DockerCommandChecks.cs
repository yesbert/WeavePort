using WeavePort.Hosting;

internal static class DockerCommandChecks
{
    internal static async Task RunAsync()
    {
        foreach (string path in new[] { "docker", "./docker", "" })
        {
            try
            {
                DockerCommand.ResolveExecutable(path);
                throw new Exception("Relative Docker executable accepted");
            }
            catch (ArgumentException) { }
        }
        foreach (double cpu in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            try
            {
                await new DockerProfile("unused", CpuCount: cpu).ResolveAsync(default);
                throw new Exception("Invalid CPU limit accepted");
            }
            catch (ArgumentOutOfRangeException error) when (error.ParamName == "cpuCount") { }
        }
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Directory.CreateTempSubdirectory("wp-docker-").FullName;
        try
        {
            string executable = Path.Combine(directory, "trusted cli");
            try
            {
                DockerCommand.ResolveExecutable(executable);
                throw new Exception("Missing Docker executable accepted");
            }
            catch (FileNotFoundException) { }
            await File.WriteAllTextAsync(executable, "#!/bin/sh\nprintf '%s\\n' \"$@\"\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var profile = new DockerProfile("unused", Context: "context with spaces") { DockerExecutable = executable };
            string result = await DockerCommand.RunAsync(profile, ["inspect", "argument;literal"], default);
            if (result != "--context\ncontext with spaces\ninspect\nargument;literal")
            {
                throw new Exception("Explicit Docker executable or argument boundaries lost");
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS trusted Docker executable and CPU validation without daemon access");
    }
}
