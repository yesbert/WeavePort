#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
wp_mode="${1:?verify, load or http}"
wp_output="${2:?new evidence directory}"
shift 2
case "$wp_mode" in verify|load|http) ;; *) exit 2;; esac
if [ -e "$wp_output" ]; then echo 'Use a new evidence directory' >&2; exit 2; fi
mkdir -p "$wp_output"
export WEAVEPORT_BULK_OUTPUT="$(cd "$wp_output" && pwd)"
export WEAVEPORT_LOCAL_CONFIG="${WEAVEPORT_LOCAL_CONFIG:-$PWD/artifacts/local/config.json}"
shasum -a 256 tests/WeavePort.Composition.Tests/bin/Release/net10.0/WeavePort.*.dll > "$WEAVEPORT_BULK_OUTPUT/assembly-hashes.txt"
dotnet tests/WeavePort.Composition.Tests/bin/Release/net10.0/WeavePort.BulkDemo.dll "$wp_mode" "$@"
