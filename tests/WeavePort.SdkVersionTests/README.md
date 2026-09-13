# SDK artifact-version regression

Run `./scripts/sdk-versions.sh` from the repository root. It packs current NuGet libraries, builds/installs the Python wheel and TypeScript npm archive, then launches actual native workers through the existing host.

For C#, Python and TypeScript it verifies:

- Omitted version preserves version 1 and allows an ordinary echo call.
- Declared version 2 binds as version 2 and allows an ordinary echo call.
- Declared version 2 cannot bind as version 1.
- An empty declaration cannot start as a valid version-1 worker.

The C# executable is both a packed SDK consumer and the test host. Python/TypeScript fixtures import installed packages, not source SDK files. No Docker, remote host, model service or benchmark is started. The Decision Room suite separately exercises application-level release identity, concurrent versions, activation, restart and journal pinning.
