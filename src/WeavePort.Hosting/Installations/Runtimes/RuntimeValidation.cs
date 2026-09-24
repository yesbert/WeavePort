using Chasm.SemanticVersioning;
using Chasm.SemanticVersioning.Ranges;

namespace WeavePort.Hosting;

internal sealed class RuntimeValidation(string root, IReadOnlyDictionary<string, RuntimeDeclaration> requirements, IReadOnlyDictionary<string, string> executables, IReadOnlyDictionary<string, string> files)
{
    internal void Validate()
    {
        foreach (var (alias, requirement) in requirements)
        {
            ValidateHash(alias, requirement);
            string observed = Observe(alias, requirement);
            Match(alias, requirement, observed);
        }
    }

    internal async Task ValidateAsync(CancellationToken token)
    {
        foreach (var (alias, requirement) in requirements)
        {
            token.ThrowIfCancellationRequested();
            ValidateHash(alias, requirement);
            string observed;
            try
            {
                observed = await RuntimeProbe.RunAsync(executables[alias], requirement.Ecosystem, token);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception)
            {
                throw Failure(alias, requirement, "probe failed: " + error.Message, error);
            }

            Match(alias, requirement, observed);
        }
    }

    private string Observe(string alias, RuntimeDeclaration requirement)
    {
        try
        {
            return RuntimeProbe.Run(executables[alias], requirement.Ecosystem);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            throw Failure(alias, requirement, "probe failed: " + error.Message, error);
        }
    }

    private void ValidateHash(string alias, RuntimeDeclaration requirement)
    {
        if (requirement.Sha256 is not null)
        {
            try
            {
                InstallationFiles.VerifyFile(executables[alias], requirement.Sha256);
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                throw Failure(alias, requirement, "strict executable hash mismatch", error);
            }
        }

        InstallationFiles.VerifyFile(InstallationFiles.BundlePath(root, requirement.Source), files[requirement.Source]);
    }

    private static void Match(string alias, RuntimeDeclaration requirement, string observed)
    {
        bool accepted;
        try
        {
            accepted = requirement.Ecosystem switch
            {
                "dotnet" => DotnetFrameworks.Matches(requirement.Requirement, observed),
                "python" => observed.StartsWith("Python ", StringComparison.Ordinal) && PythonSpecifier.Matches(requirement.Requirement, observed[7..]),
                "node" => observed.StartsWith('v') && VersionRange.Parse(requirement.Requirement).IsSatisfiedBy(SemanticVersion.Parse(observed[1..])),
                _ => false
            };
        }
        catch (Exception error) when (error is FormatException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            throw Failure(alias, requirement, "invalid version output or installed framework configuration", error);
        }

        if (!accepted)
        {
            throw Failure(alias, requirement, observed);
        }
    }

    private static InvalidDataException Failure(string alias, RuntimeDeclaration requirement, string observed, Exception? error = null) => new($"Runtime '{alias}' requires {requirement.Requirement}; observed {observed}.", error);
}
