#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/packages artifacts/sdk-packages-js artifacts/sdk-wheel artifacts/sdk-python-example
wp_stamp="$(date -u +%Y%m%d-%H%M%S)-$$"
for wp_package in abstractions hosting sdk sdk.client sdk.gateway.client sdk.gateway; do
  wp_cache="artifacts/sdk-packages/weaveport.$wp_package"
  if [ -d "$wp_cache" ]; then mv "$wp_cache" "$wp_cache-previous-$wp_stamp"; fi
done
./scripts/prepare-core-packages.sh
dotnet restore examples/sdk/csharp --force --force-evaluate --no-cache
dotnet publish examples/sdk/csharp -c Release --no-restore --self-contained false -o artifacts/sdk-csharp --nologo
npm ci --prefix sdks/typescript --ignore-scripts
npm run build --prefix sdks/typescript
npm pack ./sdks/typescript --pack-destination artifacts/sdk-packages-js
(cd examples/sdk/typescript && npm install --ignore-scripts --force ../../../artifacts/sdk-packages-js/weaveport-sdk-0.1.0.tgz)
npm run build --prefix examples/sdk/typescript
python3 -m venv artifacts/sdk-python
artifacts/sdk-python/bin/python -m pip wheel ./sdks/python --no-deps -w artifacts/sdk-wheel
artifacts/sdk-python/bin/python -m pip install --no-deps --force-reinstall artifacts/sdk-wheel/weaveport_sdk-0.1.0-py3-none-any.whl
cp examples/sdk/python/plugin.py artifacts/sdk-python-example/plugin.py
dotnet restore tests/WeavePort.SdkFixture --force --force-evaluate --no-cache
dotnet publish tests/WeavePort.WorkerHost -c Release --self-contained false -o artifacts/sdk-worker-host --nologo

dotnet build tests/WeavePort.SdkTests -c Release --nologo
dotnet restore benchmarks/WeavePort.Benchmarks --force --force-evaluate --no-cache
dotnet build benchmarks/WeavePort.Benchmarks -c Release --no-restore --nologo
dotnet restore tests/WeavePort.GatewayRegression --force --force-evaluate --no-cache
dotnet build tests/WeavePort.GatewayRegression -c Release --no-restore --nologo
