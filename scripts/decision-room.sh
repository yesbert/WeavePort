#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export WP_DECISION_ROOT="$PWD/artifacts/decision-room"
export WP_DECISION_DOTNET="$(command -v dotnet)"
export WP_DECISION_PYTHON="$WP_DECISION_ROOT/python/bin/python"
if [[ "${1:-}" == "--build" ]]; then
  shift
  mkdir -p "$WP_DECISION_ROOT" artifacts/packages
  if [[ -d "$WP_DECISION_ROOT/packages" ]]; then
    mv "$WP_DECISION_ROOT/packages" "$WP_DECISION_ROOT/packages-previous-$(date +%s)-$$"
  fi
  ./scripts/prepare-core-packages.sh
  for wp_project in Plugin Host; do
    dotnet restore "samples/DecisionRoom/$wp_project" --force --force-evaluate --no-cache
  done
  dotnet publish samples/DecisionRoom/Host -c Release --no-restore --self-contained false -o "$WP_DECISION_ROOT/host" --nologo
  for wp_version in 1 2; do
    wp_release="$WP_DECISION_ROOT/releases/$wp_version"
    dotnet publish samples/DecisionRoom/Plugin -c Release --no-restore --self-contained false \
      -p:DefineConstants="DECISION_V$wp_version" -o "$wp_release" --nologo
    cp samples/DecisionRoom/Python/pyproject.toml "$wp_release/pyproject.toml"
    cp samples/DecisionRoom/Python/plugin.py "$wp_release/plugin.py"
    wp_risk=1
    if [[ "$wp_version" == 2 ]]; then wp_risk=10; fi
    printf 'VERSION = "%s"\nRISK_MULTIPLIER = %s\n' "$wp_version" "$wp_risk" > "$wp_release/release.py"
  done
  python3 -m venv "$WP_DECISION_ROOT/python"
  if [[ -n "${WEAVEPORT_PYTHON_WHEEL:-}" ]]; then
    mkdir -p "$WP_DECISION_ROOT/wheel"
    cp "$WEAVEPORT_PYTHON_WHEEL" "$WP_DECISION_ROOT/wheel/"
  else
    "$WP_DECISION_PYTHON" -m pip wheel ./sdks/python --no-deps -w "$WP_DECISION_ROOT/wheel"
  fi
  "$WP_DECISION_PYTHON" -m pip install --no-deps --force-reinstall "$WP_DECISION_ROOT"/wheel/weaveport_sdk-*.whl
  for wp_version in 1 2; do
    ./scripts/seal-installation.sh "$WP_DECISION_ROOT" decision-room "$wp_version" decision-room/v1 DecisionRoom.Plugin.dll "$WP_DECISION_DOTNET" "$WP_DECISION_PYTHON"
  done
  if [[ ! -f "$WP_DECISION_ROOT/active-version.txt" ]]; then
    printf '1\n' > "$WP_DECISION_ROOT/active-version.txt"
  fi
fi
if [[ ! -f "$WP_DECISION_ROOT/host/DecisionRoom.Host.dll" || ! -x "$WP_DECISION_PYTHON" ]]; then
  echo 'Build first: ./scripts/decision-room.sh --build [options]' >&2
  exit 1
fi
if [[ "${1:-}" == "--build-only" ]]; then exit 0; fi
exec "$WP_DECISION_DOTNET" "$WP_DECISION_ROOT/host/DecisionRoom.Host.dll" "$@"
