# Direct HTTPS gateway and composition

This example uses the 0.5.0 packages. Until public publication, build the repository feed with `./scripts/prepare-core-packages.sh`; the repository NuGet configuration maps WeavePort packages to that feed. External consumers can add that feed explicitly. After publication, use NuGet.org without the repository source mapping.

Publish Worker, Server and Client with `dotnet publish <project-directory> -c Release --self-contained false -o <output-directory>`. The worker and server run on the server machine; the client can run on another machine. Server requires .NET 10 and ASP.NET Core, while Client requires only .NET 10. Worker is explicitly trusted same-user code.

Configure the server through environment variables (use absolute paths):

```sh
export Kestrel__Endpoints__Gateway__Url=https://0.0.0.0:7443
export Kestrel__Endpoints__Gateway__Protocols=Http2
export Kestrel__Endpoints__Gateway__Certificate__Path=/private/certs/gateway.pfx
# Supply Kestrel__Endpoints__Gateway__Certificate__Password through your secret manager if needed.
export Worker__Dotnet=/absolute/path/to/dotnet
export Worker__Assembly=/absolute/path/to/worker/Worker.dll
export Worker__Tenant=example-tenant
export Gateway__CredentialFile=/private/bootstrap/gateway-credential.json
dotnet /absolute/path/to/server/Server.dll
```

The certificate must be valid for the DNS hostname clients use and trusted on the client machine. Restrict the network listener to intended consumers. The bootstrap parent directory must be private; on Windows provide appropriate ACLs. The server creates the credential file exclusively (Unix mode 0600), never prints its contents, and refuses to overwrite an existing file. After stopping an old server, retire its old credential file and provision a fresh one through your application-controlled secret channel.

Copy the credential securely to the authorized client, then run:

```sh
dotnet /absolute/path/to/client/Client.dll https://gateway.example.com:7443 /private/gateway-credential.json /private/results
```

Expected output: `Example bounded result`. The worker implements an identity bulk-map function. A real application must select the request tenant from independent authentication and compare it with the discovered binding, rather than adopting arbitrary caller input.

For reproducible local qualification with an ephemeral private CA and no system trust-store changes, run `python3 scripts/verify-optional.py` after preparing the feed. See [gateway contracts and limits](../../docs/gateway.md). The automated test does not attest your two-machine network or certificate deployment.
