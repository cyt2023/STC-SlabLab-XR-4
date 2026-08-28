#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
pids="$root/.runtime/backend/pids"

for name in s4d matplot; do
  pidfile="$pids/$name.pid"
  if [[ -f "$pidfile" ]]; then
    pid="$(tr -cd '0-9' < "$pidfile")"
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid"
      echo "Stopped $name (PID $pid)."
    fi
    rm -f "$pidfile"
  fi
done
