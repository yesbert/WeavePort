#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Requires the current Document Workshop releases built from packed libraries.
if [[ -d artifacts/installation-tests/packages ]]; then
  mv artifacts/installation-tests/packages "artifacts/installation-tests/packages-previous-$(date +%s)-$$"
fi
dotnet restore tests/installations --force --no-cache
dotnet run --project tests/installations -c Release --no-restore -- "$PWD" "$(command -v dotnet)"
python3 tests/installations/verify-sample-pins.py "$(command -v dotnet)"
