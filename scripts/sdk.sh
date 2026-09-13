#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
wp_output="${1:?new output directory}"; shift
[ ! -e "$wp_output" ] || { echo 'Output exists' >&2; exit 2; }
mkdir -p "$wp_output"
export WP_SDK_OUTPUT="$(cd "$wp_output" && pwd)"
export WP_SDK_ROOT="$PWD"
export WP_SDK_DOTNET="$(command -v dotnet)"
export WP_SDK_NODE="$(command -v node)"
export NUGET_PACKAGES="$PWD/artifacts/sdk-packages"
git rev-parse HEAD > "$WP_SDK_OUTPUT/source-revision.txt"
git diff --stat > "$WP_SDK_OUTPUT/source-diff-stat.txt"
dotnet --info > "$WP_SDK_OUTPUT/dotnet-info.txt"
python3 --version > "$WP_SDK_OUTPUT/python-version.txt"
node --version > "$WP_SDK_OUTPUT/node-version.txt"
if docker info > "$WP_SDK_OUTPUT/docker-state.txt" 2>&1; then echo running > "$WP_SDK_OUTPUT/docker-status.txt"; else echo unavailable > "$WP_SDK_OUTPUT/docker-status.txt"; fi
if [ "$(uname -s)" = Darwin ]; then sysctl vm.swapusage > "$WP_SDK_OUTPUT/swap-before.txt"; fi
set +e
if [ "${1:-}" = verify ]; then
 dotnet tests/WeavePort.SdkTests/bin/Release/net10.0/WeavePort.SdkTests.dll "${@:2}" > "$WP_SDK_OUTPUT/run.log" 2>&1
else
 (cd benchmarks/WeavePort.Benchmarks && dotnet bin/Release/net10.0/WeavePort.Benchmarks.dll "$@") > "$WP_SDK_OUTPUT/run.log" 2>&1
fi
wp_exit=$?
set -e
printf '%s\n' "$wp_exit" > "$WP_SDK_OUTPUT/exit-code.txt"
if [ "$(uname -s)" = Darwin ]; then sysctl vm.swapusage > "$WP_SDK_OUTPUT/swap-after.txt"; fi
exit "$wp_exit"
