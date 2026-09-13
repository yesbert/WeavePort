#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/build-docker-tests.sh
dotnet build tests/WeavePort.LifecycleTests -c Release --nologo
dotnet tests/WeavePort.LifecycleTests/bin/Release/net10.0/WeavePort.LifecycleTests.dll "$PWD"
