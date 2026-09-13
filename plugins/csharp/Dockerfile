FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc AS build
WORKDIR /src
COPY Directory.Build.props .
COPY plugins/csharp plugins/csharp
RUN dotnet publish plugins/csharp -c Release -o /out --nologo
FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:6a40d375e9c8432fcf4adebae05d7e0a276e9a90dd01174df6709a090771bebc
WORKDIR /plugin
COPY --from=build /out .
ENV DOTNET_EnableDiagnostics=0 DOTNET_GCHeapHardLimitPercent=50
ENTRYPOINT ["dotnet", "/plugin/WeavePort.SamplePlugin.dll"]
