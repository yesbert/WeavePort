#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/prepare-core-packages.sh
wp_portable_run="$PWD/artifacts/portable-check-$(date +%s)-$$"
mkdir -p "$wp_portable_run"
dotnet publish tests/portable -c Release --force -p:RestorePackagesPath="$wp_portable_run/packages" -o "$wp_portable_run/host" --nologo
dotnet "$wp_portable_run/host/Portable.dll" seal "$wp_portable_run/plugins" | tee "$wp_portable_run/seal.json"
dotnet "$wp_portable_run/host/Portable.dll" run "$wp_portable_run/plugins" | tee "$wp_portable_run/local.json"
dotnet "$wp_portable_run/host/Portable.dll" languages "$PWD" "$wp_portable_run/languages" "$(command -v python3)" "$(command -v node)" | tee "$wp_portable_run/languages.txt"
if [[ "${1:-}" == "--docker" ]]; then
  docker run --rm --network none --memory 512m --cpus 1 \
    --mount "type=bind,src=$wp_portable_run/host,dst=/host,readonly" \
    --mount "type=bind,src=$wp_portable_run/plugins,dst=/plugins,readonly" \
    mcr.microsoft.com/dotnet/runtime:10.0 \
    dotnet /host/Portable.dll run /plugins /usr/share/dotnet/dotnet | tee "$wp_portable_run/linux.json"
fi
printf 'Evidence: %s\n' "$wp_portable_run"
