from __future__ import annotations

import json
import os
import re
from pathlib import Path


TIME_PATTERN = re.compile(r"_time_(\d+)_")


def generate(workspace_root: Path, output_path: Path) -> None:
    data_root = workspace_root / "OneDrive_1_4-30-2026"
    expected_bytes = 400 * 441 * 92
    variables: dict[str, object] = {}
    display_names = {
        "chlorophyll": "Chlorophyll",
        "NO3": "NO3",
        "salt": "Salinity",
    }
    for variable_id in ("chlorophyll", "NO3", "salt"):
        raw_files = sorted(
            (data_root / variable_id).glob("*.raw"),
            key=lambda path: int(TIME_PATTERN.search(path.name).group(1)),
        )
        frames = []
        for raw_path in raw_files:
            time_index = int(TIME_PATTERN.search(raw_path.name).group(1))
            frames.append(
                {
                    "frameId": f"{variable_id}_t{time_index:03d}",
                    "timeIndex": time_index,
                    "temporalMeaning": "instantaneous",
                    "path": Path(
                        os.path.relpath(raw_path, output_path.parent)
                    ).as_posix(),
                    "expectedBytes": expected_bytes,
                    "sha256": None,
                }
            )
        variables[variable_id] = {
            "displayName": display_names[variable_id],
            "unit": "encoded_intensity",
            "valueSemantics": "encoded_intensity",
            "voxelType": "uint8",
            "scale": 1.0,
            "offset": 0.0,
            "missingRawValues": [0],
            "frames": frames,
        }

    manifest = {
        "schemaVersion": "1.0",
        "datasetId": "hong_kong_ocean_encoded_v1",
        "datasetVersion": "local-raw-2026-07-24",
        "dimensions": {"x": 400, "y": 441, "z": 92},
        "storageOrder": "ZYX",
        "defaultVoxelType": "uint8",
        "coordinates": {
            "x": {"kind": "ordinal_index", "unit": "x_index", "start": 0, "step": 1},
            "y": {"kind": "ordinal_index", "unit": "y_index", "start": 0, "step": 1},
            "depth": {
                "kind": "ordinal_index",
                "unit": "depth_index",
                "start": 0,
                "step": 1,
                "positive": "down",
                "excludedIndices": [91],
            },
        },
        "variables": variables,
        "assumptions": [
            {
                "id": "encoded-values",
                "statement": "Values 1-254 are encoded intensities, not physical units.",
                "evidence": "All files are uint8; no scale, offset, or physical unit metadata exists.",
                "status": "measured",
            },
            {
                "id": "zero-invalid",
                "statement": "Raw value 0 is treated as invalid/masked.",
                "evidence": "All variables share the same zero mask at each ordinal time; valid values begin at 5.",
                "status": "inferred",
            },
            {
                "id": "ordinal-time",
                "statement": "Files are ordered by time index 0-29 without claiming timestamps.",
                "evidence": "File names contain a complete unique _time_N_ sequence and no timestamps.",
                "status": "measured",
            },
            {
                "id": "depth-order",
                "statement": "Depth indices 0-90 increase downward; index 91 is excluded.",
                "evidence": "Valid horizontal coverage decreases from index 0 through 90; index 91 breaks that pattern.",
                "status": "inferred",
            },
            {
                "id": "storage-order",
                "statement": "RAW is interpreted as C-order ZYX.",
                "evidence": "This reproduces coherent XY layers and matches the existing Unity reader.",
                "status": "inferred",
            },
        ],
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    service_dir = Path(__file__).resolve().parent
    root = service_dir.parents[1]
    generate(root, root / "datasets" / "hong_kong_encoded" / "manifest.json")
