#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export WP_VERSION_ROOT="$PWD/artifacts/sdk-version-tests"
export WP_VERSION_DOTNET="$(command -v dotnet)"
export WP_VERSION_NODE="$(command -v node)"
mkdir -p "$WP_VERSION_ROOT" artifacts/packages
if [[ -d "$WP_VERSION_ROOT/packages" ]]; then
  mv "$WP_VERSION_ROOT/packages" "$WP_VERSION_ROOT/packages-previous-$(date +%s)-$$"
fi
./scripts/prepare-core-packages.sh
python3 -m venv "$WP_VERSION_ROOT/python"
"$WP_VERSION_ROOT/python/bin/python" -m pip wheel ./sdks/python --no-deps -w "$WP_VERSION_ROOT/wheel"
"$WP_VERSION_ROOT/python/bin/python" -m pip install --no-deps --force-reinstall "$WP_VERSION_ROOT"/wheel/weaveport_sdk-*.whl
npm ci --prefix sdks/typescript --ignore-scripts
npm run build --prefix sdks/typescript
npm pack ./sdks/typescript --pack-destination "$WP_VERSION_ROOT"
npm install --prefix "$WP_VERSION_ROOT" --ignore-scripts --no-audit --no-fund "$WP_VERSION_ROOT/weaveport-sdk-0.2.0.tgz"
cp tests/WeavePort.SdkVersionTests/worker.py tests/WeavePort.SdkVersionTests/worker.mjs "$WP_VERSION_ROOT/"
dotnet publish tests/WeavePort.SdkVersionTests -c Release --self-contained false -o "$WP_VERSION_ROOT/host" --nologo
if [[ "${1:-}" == "--build-only" ]]; then exit 0; fi
exec "$WP_VERSION_DOTNET" "$WP_VERSION_ROOT/host/WeavePort.SdkVersionTests.dll"
