#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! git diff --quiet HEAD; then
  echo 'Commit tracked changes before qualifying a frozen source revision.' >&2
  exit 2
fi
exec python3 -B tools/candidate/run.py "${1:-HEAD}"
