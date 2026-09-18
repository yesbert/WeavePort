#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/packages artifacts/local
for project in Abstractions Hosting Sdk.Client Testing Composition; do
  dotnet pack "src/WeavePort.$project" -c Release -o artifacts/packages --nologo
done
wp_cache_stamp="$(date -u +%Y%m%d-%H%M%S)-$$"
for wp_cache in artifacts/local-consumer-packages artifacts/consumer-packages; do
  for wp_package in weaveport.abstractions weaveport.hosting weaveport.sdk.client weaveport.testing weaveport.composition; do
    if [ -d "$wp_cache/$wp_package" ]; then
      mv "$wp_cache/$wp_package" "$wp_cache/$wp_package-previous-$wp_cache_stamp"
    fi
  done
done
dotnet publish plugins/csharp -c Release --self-contained false -o artifacts/local/csharp --nologo
dotnet restore tests/WeavePort.Local.Tests --force --no-cache
dotnet build tests/WeavePort.Local.Tests -c Release --no-restore --nologo
dotnet tests/WeavePort.Local.Tests/bin/Release/net10.0/WeavePort.LocalDemo.dll configure "$PWD" artifacts/local/config.json
