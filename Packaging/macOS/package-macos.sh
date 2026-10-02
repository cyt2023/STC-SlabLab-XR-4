#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
build_root="$repo_root/RenderingModule/Builds"
source_app="$build_root/SlabLab-Flat.app"
package_dir="$build_root/STC-SlabLab-macOS"
archive="$build_root/STC-SlabLab-macOS.zip"
with_env=0
if [[ "${1:-}" == "--with-env" ]]; then
  with_env=1
fi

if [[ ! -d "$source_app" ]]; then
  echo "Missing $source_app. Run the Unity macOS desktop build first." >&2
  exit 1
fi

rm -rf "$package_dir"
mkdir -p "$package_dir/Services/MatPlotAgent" \
  "$package_dir/Services/S4DAnalysisService" "$package_dir/For_VR"
ditto "$source_app" "$package_dir/STC SlabLab.app"
ditto "$repo_root/For_VR/UnityRaw" "$package_dir/For_VR/UnityRaw"
ditto "$repo_root/datasets" "$package_dir/datasets"
# Optional offline bundle: "Prepare Offline Data.command" hydrates a Wave window
# into For_VR/WaveCache; shipping it lets the app open with no tailnet.
if [[ -d "$repo_root/For_VR/WaveCache" ]]; then
  ditto "$repo_root/For_VR/WaveCache" "$package_dir/For_VR/WaveCache"
fi

for file in api_server.py local_run.py; do
  cp "$repo_root/Services/MatPlotAgent/$file" \
    "$package_dir/Services/MatPlotAgent/$file"
done
ditto "$repo_root/Services/S4DAnalysisService" \
  "$package_dir/Services/S4DAnalysisService"
find "$package_dir/Services" -type d -name __pycache__ -prune -exec rm -rf {} +

cp "$repo_root/Start-Backend.sh" "$package_dir/Start-Backend.sh"
cp "$repo_root/Stop-Backend.sh" "$package_dir/Stop Backend.command"
cp "$repo_root/Packaging/macOS/requirements-desktop.txt" "$package_dir/"
cp "$repo_root/Packaging/macOS/Setup Backend.command" "$package_dir/"
cp "$repo_root/Packaging/macOS/Start STC SlabLab.command" "$package_dir/"
cp "$repo_root/Packaging/macOS/Prepare Offline Data.command" "$package_dir/"
cp "$repo_root/Packaging/macOS/README-START-HERE.txt" "$package_dir/"
cp "$repo_root/.env.example" "$package_dir/.env.example"
mkdir -p "$package_dir/tools"
cp "$repo_root/tools/prepare_wave_cache.py" "$package_dir/tools/"
# Live mode needs the Wave API key. Passing --with-env bakes .env into the
# package (anyone holding it can call the Wave API), so it stays opt-in.
if [[ "$with_env" == "1" && -f "$repo_root/.env" ]]; then
  cp "$repo_root/.env" "$package_dir/.env"
  # The key travels with the package by request, so at least keep it
  # owner-only: other accounts on the same machine must not be able to read it.
  chmod 600 "$package_dir/.env"
  echo "Included .env (with WAVE_API_KEY) in the package."
fi
chmod +x "$package_dir/Start-Backend.sh" \
  "$package_dir/Stop Backend.command" \
  "$package_dir/Setup Backend.command" \
  "$package_dir/Start STC SlabLab.command" \
  "$package_dir/Prepare Offline Data.command"

rm -f "$archive"
ditto -c -k --sequesterRsrc --keepParent "$package_dir" "$archive"
echo "Distribution ready: $archive"
