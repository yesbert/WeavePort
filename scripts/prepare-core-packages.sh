#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -n "${WEAVEPORT_PACKAGE_SET:-}" ]]; then
  exec python3 scripts/check-package-set.py "$WEAVEPORT_PACKAGE_SET"
fi
mkdir -p artifacts/packages
for wp_project in Abstractions Hosting Sdk Sdk.Client; do
  dotnet pack "src/WeavePort.$wp_project" -c Release -o artifacts/packages --nologo
done
python3 scripts/copy-package-dependencies.py
