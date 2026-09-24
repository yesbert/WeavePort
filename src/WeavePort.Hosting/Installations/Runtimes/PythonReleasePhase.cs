namespace WeavePort.Hosting;

// Numeric ordering implements Python's public-version phase ordering.
internal enum PythonReleasePhase
{
    Development = -1,
    Alpha = 0,
    Beta = 1,
    ReleaseCandidate = 2,
    Final = 3
}
