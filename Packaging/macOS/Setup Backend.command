#!/usr/bin/env bash
set -euo pipefail

package_root="$(cd "$(dirname "$0")" && pwd)"
cd "$package_root"

if ! command -v python3 >/dev/null 2>&1; then
  echo "Python 3 is required. Install it from https://www.python.org/downloads/ and run this file again."
  read -r -p "Press Return to close..." _
  exit 1
fi

if [[ ! -x "$package_root/.venv/bin/python" ]]; then
  python3 -m venv "$package_root/.venv"
fi

"$package_root/.venv/bin/python" -m pip install --upgrade pip
"$package_root/.venv/bin/python" -m pip install \
  -r "$package_root/requirements-desktop.txt"
touch "$package_root/.venv/.stc-slablab-ready"

echo
echo "Backend setup complete. Double-click Start STC SlabLab.command."
read -r -p "Press Return to close..." _
