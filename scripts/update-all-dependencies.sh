#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Adapted to WeavePort from the NextPA-V2/HiveWeaver-V2 update workflow:
# resolve first, honor explicit holds, refresh locks, then build/test/audit.
export PYTHONDONTWRITEBYTECODE=1
exec python3 tools/dependencies/run.py "$@"
