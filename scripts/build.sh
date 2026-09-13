#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
./scripts/prepare-core-packages.sh
for wp_app in decision-room document-workshop appointment-desk; do
  "./scripts/$wp_app.sh" --build --build-only
done
