#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export WP_DOCUMENT_ROOT="$PWD/artifacts/document-workshop"
export WP_DOCUMENT_DOTNET="$(command -v dotnet)"
rotate_package_cache() {
  if [[ ! -d "$WP_DOCUMENT_ROOT/packages" ]]; then
    return
  fi
  mv "$WP_DOCUMENT_ROOT/packages" "$WP_DOCUMENT_ROOT/packages-previous-$(date +%s)-$$"
}

publish_workers() {
  for wp_version in 1 2; do
    dotnet publish "samples/DocumentWorkshop/Worker" -c Release --no-restore --self-contained false \
      -p:DefineConstants="RELEASE_V$wp_version" -o "$WP_DOCUMENT_ROOT/releases/$wp_version" --nologo
    ./scripts/seal-installation.sh "$WP_DOCUMENT_ROOT" document-workshop "$wp_version" document-workshop/v1 DocumentWorkshop.Worker.dll "$WP_DOCUMENT_DOTNET"
  done
}

build_sample() {
  mkdir -p "$WP_DOCUMENT_ROOT" artifacts/packages
  rotate_package_cache
  ./scripts/prepare-core-packages.sh
  for wp_project in Worker Host; do
    dotnet restore "samples/DocumentWorkshop/$wp_project" --force --no-cache
  done
  publish_workers
  dotnet publish "samples/DocumentWorkshop/Host" -c Release --no-restore --self-contained false -o "$WP_DOCUMENT_ROOT/host" --nologo
  if [[ ! -f "$WP_DOCUMENT_ROOT/active-version.txt" ]]; then
    printf '1\n' > "$WP_DOCUMENT_ROOT/active-version.txt"
  fi
}

if [[ "${1:-}" == "--build" ]]; then
  shift
  build_sample
fi
if [[ ! -f "$WP_DOCUMENT_ROOT/host/DocumentWorkshop.Host.dll" ]]; then
  echo 'Build first: ./scripts/document-workshop.sh --build [options]' >&2
  exit 1
fi
if [[ "${1:-}" == "--build-only" ]]; then exit 0; fi
exec "$WP_DOCUMENT_DOTNET" "$WP_DOCUMENT_ROOT/host/DocumentWorkshop.Host.dll" "$@"
