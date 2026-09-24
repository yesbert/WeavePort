#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
wp_output="${1:?new output directory}"
[ ! -e "$wp_output" ] || exit 2
mkdir -p "$wp_output"
git rev-parse HEAD > "$wp_output/source-revision.txt"
run_case() {
  local wp_case="$1"
  local wp_command=(dotnet tests/WeavePort.GatewayRegression/bin/Release/net10.0/WeavePort.GatewayRegression.dll "$wp_output/$wp_case.json")
  if [[ "$wp_case" == runtime-control ]]; then
    wp_command+=(--runtime-control)
  fi
  local wp_exit=0
  "${wp_command[@]}" > "$wp_output/$wp_case.log" 2>&1 || wp_exit=$?
  printf '%s\n' "$wp_exit" > "$wp_output/$wp_case-exit-code.txt"
  return "$wp_exit"
}

for wp_case in sdk runtime-control; do
  run_case "$wp_case"
done
