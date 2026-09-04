#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
build_root="$repo_root/RenderingModule/Builds"
source_app="$build_root/SlabLab-Flat.app"
package_dir="$build_root/STC-SlabLab-macOS"
archive="$build_root/STC-SlabLab-macOS.zip"

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
cp "$repo_root/Packaging/macOS/README-START-HERE.txt" "$package_dir/"
cp "$repo_root/Services/MatPlotAgent/.env.example" "$package_dir/.env.example"
chmod +x "$package_dir/Start-Backend.sh" \
  "$package_dir/Stop Backend.command" \
  "$package_dir/Setup Backend.command" \
  "$package_dir/Start STC SlabLab.command"

rm -f "$archive"
ditto -c -k --sequesterRsrc --keepParent "$package_dir" "$archive"
echo "Distribution ready: $archive"
