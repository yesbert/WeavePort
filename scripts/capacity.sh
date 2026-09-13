#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/build.sh
dotnet build tests/WeavePort.CapacityTests -c Release --nologo
dotnet tests/WeavePort.CapacityTests/bin/Release/net10.0/WeavePort.CapacityTests.dll --self-test
dotnet tests/WeavePort.CapacityTests/bin/Release/net10.0/WeavePort.CapacityTests.dll "$PWD" "$@"
