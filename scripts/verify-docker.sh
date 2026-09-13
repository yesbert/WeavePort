#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/build-docker-tests.sh
openspec validate --all --strict
dotnet run --project tests/WeavePort.Hosting.Tests -c Release
dotnet tools/WeavePort.Runner/bin/Release/net10.0/WeavePort.Runner.dll verify "$PWD"
dotnet tools/WeavePort.Runner/bin/Release/net10.0/WeavePort.Runner.dll load "$PWD"
