#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
wp_output="${1:?new output directory}"
[ ! -e "$wp_output" ] || exit 2
mkdir -p "$wp_output"
git rev-parse HEAD > "$wp_output/source-revision.txt"
for wp_case in sdk runtime-control; do
 set +e
 if [ "$wp_case" = runtime-control ]; then
  dotnet tests/WeavePort.GatewayRegression/bin/Release/net10.0/WeavePort.GatewayRegression.dll "$wp_output/$wp_case.json" --runtime-control > "$wp_output/$wp_case.log" 2>&1
 else
  dotnet tests/WeavePort.GatewayRegression/bin/Release/net10.0/WeavePort.GatewayRegression.dll "$wp_output/$wp_case.json" > "$wp_output/$wp_case.log" 2>&1
 fi
 wp_exit=$?
 set -e
 printf '%s\n' "$wp_exit" > "$wp_output/$wp_case-exit-code.txt"
 [ "$wp_exit" -eq 0 ] || exit "$wp_exit"
done
