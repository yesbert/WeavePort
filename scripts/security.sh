#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
wp_output="${1:?new evidence directory}"
[ ! -e "$wp_output" ] || exit 2
# Builds only the dedicated image. Existing daemon/settings/services are untouched.
docker build -f plugins/security-extended/Dockerfile -t weaveport-poc-security-extended:1 .
dotnet restore tests/WeavePort.SecurityTests --locked-mode
dotnet build tests/WeavePort.SecurityTests -c Release --no-restore --nologo
set +e
dotnet tests/WeavePort.SecurityTests/bin/Release/net10.0/WeavePort.SecurityTests.dll "$wp_output"
wp_exit=$?
set -e
if [ -d "$wp_output" ]; then
 printf '%s\n' "$wp_exit" > "$wp_output/exit-code.txt"
 git rev-parse HEAD > "$wp_output/source-revision.txt"
 git diff --stat > "$wp_output/source-diff-stat.txt"
fi
exit "$wp_exit"
