#!/usr/bin/env bash
# Bundles the Wave window the app should be able to open without the tailnet.
# Run this once while the machine still has network + WAVE_API_KEY.
set -euo pipefail

package_root="$(cd "$(dirname "$0")" && pwd)"
cd "$package_root"

if [[ ! -x "$package_root/.venv/bin/python" ]]; then
  echo "Run Setup Backend.command first."
  read -r -p "Press Return to close..." _
  exit 1
fi

"$package_root/.venv/bin/python" "$package_root/tools/prepare_wave_cache.py" "$@"

echo
read -r -p "Press Return to close..." _
