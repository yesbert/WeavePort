internal static class ObserverChecks
{
    internal static void Run()
    {
        string first = "weaveport-" + new string('a', 32), second = "weaveport-" + new string('b', 32);
        string commands = $"/opt/homebrew/bin/docker run -i --name {first} image\n/docker run --name={second} image\n/docker run --name other-service image\n/docker rm {first}\n/docker run --name weaveport-invalid image\n";
        if (!DensityResources.ExtractDockerInstances(commands).ToHashSet().SetEquals(new[] { first, second }))
        {
            throw new InvalidDataException("Owned pristine Docker name extraction failed");
        }

        Console.WriteLine("PASS owned Docker name extraction includes pristine/startup names and excludes unrelated names");
    }
}
