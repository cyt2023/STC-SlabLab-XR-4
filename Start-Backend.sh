#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
python="$root/.venv/bin/python"
runtime="$root/.runtime/backend"
logs="$runtime/logs"
pids="$runtime/pids"

if [[ ! -x "$python" ]]; then
  echo "Missing $python. Create the virtual environment and install dependencies first." >&2
  exit 1
fi

mkdir -p "$logs" "$pids"

start_service() {
  local name="$1" port="$2" workdir="$3" pidfile="$4"
  shift 4
  if curl -fsS --max-time 2 "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
    echo "$name is already healthy on port $port."
    return
  fi
  if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port $port is occupied by another process." >&2
    exit 1
  fi
  (
    cd "$workdir"
    nohup "$@" >"$logs/$name.stdout.log" 2>"$logs/$name.stderr.log" &
    echo $! >"$pidfile"
  )
  for _ in {1..60}; do
    if curl -fsS --max-time 2 "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
      echo "$name is ready on http://127.0.0.1:$port"
      return
    fi
    sleep 0.5
  done
  echo "$name failed to start; see $logs/$name.stderr.log" >&2
  exit 1
}

start_service matplot 8010 "$root/Services/MatPlotAgent" "$pids/matplot.pid" \
  env MATPLOT_API_HOST=127.0.0.1 MATPLOT_API_PORT=8010 "$python" api_server.py

start_service s4d 8020 "$root" "$pids/s4d.pid" \
  env S4D_DATASET_ROOT="$root/datasets" S4D_MATPLOT_URL=http://127.0.0.1:8010 \
  VOICE_LOCAL_FIRST=1 VOICE_LOCAL_MODEL=base \
  "$python" -m uvicorn Services.S4DAnalysisService.app:app --host 127.0.0.1 --port 8020

echo "Backend ready. Logs: $logs"
