#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# First build the samples and run scripts/sdk-versions.sh against the current source.
if [[ -d artifacts/compatibility/packages ]]; then
  mv artifacts/compatibility/packages "artifacts/compatibility/packages-previous-$(date +%s)-$$"
fi
python3 tests/compatibility/check-packages.py
dotnet restore tests/compatibility --force --no-cache
dotnet run --project tests/compatibility -c Release --no-restore -- "$PWD"
python3 tests/compatibility/verify-review-gates.py "$(command -v dotnet)"
./scripts/verify-installations.sh
