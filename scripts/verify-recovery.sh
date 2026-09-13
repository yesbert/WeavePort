#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Build first: ./scripts/appointment-desk.sh --build --verify
exec python3 tests/recovery/verify-crash.py "$(command -v dotnet)"
