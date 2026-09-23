#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export WP_DOCUMENT_ROOT="$PWD/artifacts/document-workshop"
export WP_DOCUMENT_DOTNET="$(command -v dotnet)"
if [[ "${1:-}" == "--build" ]]; then
  shift
  mkdir -p "$WP_DOCUMENT_ROOT" artifacts/packages
  if [[ -d "$WP_DOCUMENT_ROOT/packages" ]]; then
    mv "$WP_DOCUMENT_ROOT/packages" "$WP_DOCUMENT_ROOT/packages-previous-$(date +%s)-$$"
  fi
  ./scripts/prepare-core-packages.sh
  for wp_project in Worker Host; do
    dotnet restore "samples/DocumentWorkshop/$wp_project" --force --no-cache
    wp_target="$(printf '%s' "$wp_project" | tr '[:upper:]' '[:lower:]')"
    if [[ "$wp_project" == Worker ]]; then
      for wp_version in 1 2; do
        dotnet publish "samples/DocumentWorkshop/Worker" -c Release --no-restore --self-contained false -p:DefineConstants="RELEASE_V$wp_version" -o "$WP_DOCUMENT_ROOT/releases/$wp_version" --nologo
        ./scripts/seal-installation.sh "$WP_DOCUMENT_ROOT" document-workshop "$wp_version" document-workshop/v1 DocumentWorkshop.Worker.dll "$WP_DOCUMENT_DOTNET"
      done
    else
      dotnet publish "samples/DocumentWorkshop/Host" -c Release --no-restore --self-contained false -o "$WP_DOCUMENT_ROOT/host" --nologo
    fi
  done
  if [[ ! -f "$WP_DOCUMENT_ROOT/active-version.txt" ]]; then printf '1\n' > "$WP_DOCUMENT_ROOT/active-version.txt"; fi
fi
if [[ ! -f "$WP_DOCUMENT_ROOT/host/DocumentWorkshop.Host.dll" ]]; then
  echo 'Build first: ./scripts/document-workshop.sh --build [options]' >&2
  exit 1
fi
if [[ "${1:-}" == "--build-only" ]]; then exit 0; fi
exec "$WP_DOCUMENT_DOTNET" "$WP_DOCUMENT_ROOT/host/DocumentWorkshop.Host.dll" "$@"
