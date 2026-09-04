#!/usr/bin/env bash
set -euo pipefail

package_root="$(cd "$(dirname "$0")" && pwd)"
cd "$package_root"

if [[ ! -f "$package_root/.venv/.stc-slablab-ready" ]]; then
  echo "Backend dependencies are not installed yet."
  echo "Run Setup Backend.command once, then start the application again."
  read -r -p "Press Return to close..." _
  exit 1
fi

if [[ -f "$package_root/.env" ]]; then
  set -a
  source "$package_root/.env"
  set +a
fi

"$package_root/Start-Backend.sh"
open "$package_root/STC SlabLab.app"

echo "STC SlabLab is running. Backend logs are in .runtime/backend/logs."
