FROM docker:29.7.2-cli AS cli
FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:6a40d375e9c8432fcf4adebae05d7e0a276e9a90dd01174df6709a090771bebc AS runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=runtime /usr/share/dotnet/shared/Microsoft.NETCore.App /usr/share/dotnet/shared/Microsoft.NETCore.App
WORKDIR /app
COPY . ./
ENTRYPOINT ["dotnet", "WeavePort.CapacityTests.dll", "--linux-probe", "/evidence"]
