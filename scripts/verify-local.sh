#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/build-local.sh
openspec validate --all --strict
dotnet run --project tests/WeavePort.Hosting.Tests -c Release
wp_local_output="artifacts/runs/local/$(date -u +%Y%m%d-%H%M%S)-verification"
mkdir -p "$wp_local_output"
# Give this verification its own ownership root; older experiments may retain theirs.
python3 - "$wp_local_output" <<'PYCONFIG'
import json
from pathlib import Path
import sys
output = Path(sys.argv[1]).resolve()
config = json.loads(Path('artifacts/local/config.json').read_text())
config['WorkspaceRoot'] = str(output / 'workspaces')
(output / 'config.json').write_text(json.dumps(config, indent=2))
PYCONFIG
dotnet tests/WeavePort.Local.Tests/bin/Release/net10.0/WeavePort.LocalDemo.dll verify "$wp_local_output/config.json" "$wp_local_output"
