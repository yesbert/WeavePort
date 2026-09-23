#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export WP_APPOINTMENT_ROOT="$PWD/artifacts/appointment-desk"
export WP_APPOINTMENT_DOTNET="$(command -v dotnet)"
if [[ "${1:-}" == "--build" ]]; then
  shift
  mkdir -p "$WP_APPOINTMENT_ROOT" artifacts/packages
  if [[ -d "$WP_APPOINTMENT_ROOT/packages" ]]; then
    mv "$WP_APPOINTMENT_ROOT/packages" "$WP_APPOINTMENT_ROOT/packages-previous-$(date +%s)-$$"
  fi
  ./scripts/prepare-core-packages.sh
  for wp_project in Worker Host; do
    dotnet restore "samples/AppointmentDesk/$wp_project" --force --no-cache
    wp_target="$(printf '%s' "$wp_project" | tr '[:upper:]' '[:lower:]')"
    if [[ "$wp_project" == Worker ]]; then
      for wp_version in 1 2; do
        dotnet publish "samples/AppointmentDesk/Worker" -c Release --no-restore --self-contained false -p:DefineConstants="RELEASE_V$wp_version" -o "$WP_APPOINTMENT_ROOT/releases/$wp_version" --nologo
        ./scripts/seal-installation.sh "$WP_APPOINTMENT_ROOT" appointment-desk "$wp_version" appointment-desk/v1 AppointmentDesk.Worker.dll "$WP_APPOINTMENT_DOTNET"
      done
    else
      dotnet publish "samples/AppointmentDesk/Host" -c Release --no-restore --self-contained false -o "$WP_APPOINTMENT_ROOT/host" --nologo
    fi
  done
  if [[ ! -f "$WP_APPOINTMENT_ROOT/active-version.txt" ]]; then printf '1\n' > "$WP_APPOINTMENT_ROOT/active-version.txt"; fi
fi
if [[ ! -f "$WP_APPOINTMENT_ROOT/host/AppointmentDesk.Host.dll" ]]; then
  echo 'Build first: ./scripts/appointment-desk.sh --build [options]' >&2
  exit 1
fi
if [[ "${1:-}" == "--build-only" ]]; then exit 0; fi
exec "$WP_APPOINTMENT_DOTNET" "$WP_APPOINTMENT_ROOT/host/AppointmentDesk.Host.dll" "$@"
