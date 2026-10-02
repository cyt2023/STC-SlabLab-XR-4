#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
python="$root/.venv/bin/python"
runtime="$root/.runtime/backend"
logs="$runtime/logs"
pids="$runtime/pids"
supervise=0
for argument in "$@"; do
  case "$argument" in
    --supervise) supervise=1 ;;
    # Hybrid data source: keep the live Wave gateway as the first choice, but
    # never block on it — the bundled dataset under For_VR/WaveCache answers
    # instead. --offline goes further and skips the gateway entirely.
    --offline) export S4D_OFFLINE=1 ;;
  esac
done

# Where a pre-hydrated dataset can be shipped inside the package.
export S4D_WAVE_CACHE="${S4D_WAVE_CACHE:-$root/For_VR/WaveCache}"

# Keep configuration identical whether the user starts the command launcher or
# the packaged Unity player starts this script itself.
if [[ -f "$root/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$root/.env"
  set +a
fi

if [[ ! -x "$python" ]]; then
  echo "Missing $python. Create the virtual environment and install dependencies first." >&2
  exit 1
fi

mkdir -p "$logs" "$pids"

start_service() {
  local name="$1" port="$2" workdir="$3" pidfile="$4"
  shift 4
  if curl -fsS --max-time 2 "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
    local actual_root expected_root
    actual_root="$(curl -fsS --max-time 2 "http://127.0.0.1:$port/health" | \
      "$python" -c 'import json,sys; print(json.load(sys.stdin).get("workspaceRoot", ""))')"
    expected_root="$(cd "$workdir" && pwd -P)"
    if [[ "$actual_root" == "$expected_root" ]]; then
      # A service for this workspace is already listening. If it is not the
      # process we recorded it is a leftover from an earlier launch (the pid
      # file is gone or stale), and the app would keep talking to that older
      # build. Replace it so the running service always matches the code on
      # disk.
      local recorded=""
      [[ -f "$pidfile" ]] && recorded="$(tr -cd '0-9' < "$pidfile")"
      local listening
      listening="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null | head -1)"
      if [[ -n "$listening" && "$listening" == "$recorded" ]]; then
        echo "$name is already healthy on port $port (PID $listening)."
        return
      fi
      echo "$name on port $port is a leftover process (PID ${listening:-unknown}); restarting it."
      if [[ -n "$listening" ]]; then
        kill "$listening" 2>/dev/null || true
        for _ in {1..20}; do
          kill -0 "$listening" 2>/dev/null || break
          sleep 0.25
        done
        kill -9 "$listening" 2>/dev/null || true
      fi
      rm -f "$pidfile"
    fi
    if [[ "$actual_root" != "$expected_root" ]]; then
      echo "Port $port is serving a different STC SlabLab copy: $actual_root" >&2
      echo "Stop that backend before starting this package: $expected_root" >&2
      exit 1
    fi
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

if [[ "$supervise" == "1" ]]; then
  echo "Backend supervisor active. Keep this process running while SlabLab is open."
  while true; do
    if ! curl -fsS --max-time 2 "http://127.0.0.1:8010/health" >/dev/null 2>&1 || \
       ! curl -fsS --max-time 2 "http://127.0.0.1:8020/health" >/dev/null 2>&1; then
      echo "A backend service stopped. See $logs for details." >&2
      exit 1
    fi
    sleep 5
  done
fi
