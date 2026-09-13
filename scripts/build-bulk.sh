#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/packages
for wp_project in Abstractions Hosting Composition; do
  dotnet pack "src/WeavePort.$wp_project" -c Release -o artifacts/packages --nologo
done
wp_stamp="$(date -u +%Y%m%d-%H%M%S)-$$"
for wp_package in weaveport.abstractions weaveport.hosting weaveport.composition; do
  wp_cache="artifacts/bulk-consumer-packages/$wp_package"
  if [ -d "$wp_cache" ]; then mv "$wp_cache" "$wp_cache-previous-$wp_stamp"; fi
done
dotnet publish plugins/csharp -c Release --self-contained false -o artifacts/local/csharp --nologo
dotnet restore tests/WeavePort.Composition.Tests --force --no-cache
dotnet build tests/WeavePort.Composition.Tests -c Release --no-restore --nologo
