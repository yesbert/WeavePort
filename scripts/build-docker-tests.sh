#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/packages
for project in Abstractions Hosting Testing; do
  dotnet pack "src/WeavePort.$project" -c Release -o artifacts/packages --nologo
done
rm -rf artifacts/consumer-packages/WeavePort.* artifacts/consumer-packages/weaveport.*
dotnet restore tests/WeavePort.Docker.Tests --force --no-cache
dotnet build tests/WeavePort.Docker.Tests -c Release --no-restore --nologo
dotnet build tests/WeavePort.LoadTests -c Release --nologo
dotnet build tools/WeavePort.Runner -c Release --nologo
for language in csharp python typescript; do
  docker build -f "plugins/$language/Dockerfile" -t "weaveport-poc-$language:1" .
done
docker build -f plugins/python/Dockerfile --build-arg PLUGIN_VERSION=2 -t weaveport-poc-python:2 .
docker build -f plugins/security/Dockerfile -t weaveport-poc-security:1 .
docker build -f plugins/security/Dockerfile --build-arg READY_PROTOCOL=1e999 -t weaveport-poc-security-badready:1 .
