#!/usr/bin/env bash
set -euo pipefail
wp_seal_project="$(cd "$(dirname "$0")/.." && pwd)/tools/WeavePort.Seal"
exec dotnet run --project "$wp_seal_project" -c Release -- "$@"
