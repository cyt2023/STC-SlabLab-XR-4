"""Resolution between duplicate imports of the same Wave window.

Re-running a Wave import leaves the previous copy of the same window beside
the fresh one. Shape, variable name and frame count are identical, so the
review stage used to die on an ambiguity error and lose its matrix panel.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient

from Services.S4DAnalysisService import app as app_module
from Services.S4DAnalysisService import manifests as manifests_module
from Services.S4DAnalysisService.app import app

DIM_X = 4
DIM_Y = 3
DIM_Z = 2
FRAME_COUNT = 2
VARIABLE = "Wave_HS"


def _write_wave_manifest(root: Path, dataset_id: str, version: str) -> Path:
    variable_dir = root / dataset_id / "UnityRaw" / VARIABLE
    variable_dir.mkdir(parents=True)
    frames = []
    for index in range(FRAME_COUNT):
        name = f"event_01_time_{index:04d}.raw"
        (variable_dir / name).write_bytes(b"\x00" * (DIM_X * DIM_Y * DIM_Z))
        frames.append(
            {
                "frameId": f"{VARIABLE}_t{index:04d}",
                "timeIndex": index,
                "temporalMeaning": "instantaneous",
                "path": f"UnityRaw/{VARIABLE}/{name}",
                "expectedBytes": DIM_X * DIM_Y * DIM_Z,
                "sha256": None,
            }
        )
    manifest = {
        "schemaVersion": "1.0",
        "datasetId": dataset_id,
        "datasetVersion": version,
        "dimensions": {"x": DIM_X, "y": DIM_Y, "z": DIM_Z},
        "storageOrder": "ZYX",
        "defaultVoxelType": "uint8",
        "coordinates": {
            "coordinateReference": "EPSG:4326",
            "renderProjection": "WebMercator",
            "x": {
                "kind": "regular_geographic_grid",
                "axis": "longitude",
                "unit": "degree_east",
                "start": 113.65,
                "step": 0.01,
            },
            "y": {
                "kind": "regular_geographic_grid",
                "axis": "latitude",
                "unit": "degree_north",
                "start": 22.03,
                "step": 0.01,
            },
            "depth": {
                "kind": "ordinal_index",
                "unit": "display_extrusion_layer",
                "start": 0,
                "step": 1,
                "positive": "down",
                "excludedIndices": [],
            },
        },
        "variables": {
            VARIABLE: {
                "displayName": VARIABLE,
                "unit": "source unit (unverified)",
                "valueSemantics": "physical",
                "voxelType": "uint8",
                "scale": 1.0,
                "offset": 0.0,
                "missingRawValues": [0],
                "frames": frames,
            }
        },
        "assumptions": [],
    }
    path = root / dataset_id / "manifest.json"
    path.write_text(json.dumps(manifest), encoding="utf-8")
    return path


def _client(tmp_path: Path, monkeypatch) -> tuple[TestClient, Path, Path]:
    imports = tmp_path / "wave-imports"
    old = _write_wave_manifest(
        imports, "wave_20191130T0000Z_20191201T0000Z_hs_elev", "2026-09-22T13:07:27Z"
    )
    new = _write_wave_manifest(
        imports,
        "wave_live_20191130T0000Z_20191201T0000Z_hs_elev",
        "2026-09-23T10:24:53Z",
    )
    empty_datasets = tmp_path / "datasets"
    empty_datasets.mkdir()
    monkeypatch.setenv("S4D_DATASET_ROOT", str(empty_datasets))
    # The scan lives in manifests.py since the routers were split out, so the
    # patch has to land where the registry actually reads the root.
    monkeypatch.setattr(manifests_module, "WAVE_IMPORT_ROOT", imports)
    return TestClient(app), new.parent, old.parent


def _query(**overrides) -> dict[str, object]:
    params: dict[str, object] = {
        "variable": VARIABLE,
        "x": DIM_X,
        "y": DIM_Y,
        "z": DIM_Z,
        "timeCount": FRAME_COUNT,
    }
    params.update(overrides)
    return params


def test_duplicate_imports_resolve_to_the_opened_copy(tmp_path, monkeypatch) -> None:
    client, _, oldest = _client(tmp_path, monkeypatch)

    response = client.get(
        "/datasets/resolve",
        params=_query(
            sourceRoot=str(oldest / "UnityRaw" / VARIABLE),
            sourcePath=str(oldest / "UnityRaw" / VARIABLE / "event_01_time_0000.raw"),
        ),
    )

    assert response.status_code == 200
    assert response.json()["datasetId"] == oldest.name


def test_duplicate_imports_without_a_hint_prefer_the_newest_manifest(
    tmp_path, monkeypatch
) -> None:
    client, newest, _ = _client(tmp_path, monkeypatch)

    response = client.get("/datasets/resolve", params=_query())

    assert response.status_code == 200
    assert response.json()["datasetId"] == newest.name
    assert response.json()["valueSemantics"] == "physical"


def test_manifest_preflight_reports_missing_frames(tmp_path) -> None:
    root = tmp_path / "wave-imports"
    manifest_path = _write_wave_manifest(
        root, "wave_live_20191130T0000Z_20191201T0000Z_hs_elev",
        "2026-09-23T10:24:53Z",
    )
    missing = manifest_path.parent / "UnityRaw" / VARIABLE / "event_01_time_0001.raw"
    missing.unlink()

    with pytest.raises(HTTPException) as error:
        app_module._verify_dataset_frames(manifest_path, manifest_path.parent.name)

    assert error.value.status_code == 422
    detail = str(error.value.detail)
    assert "incomplete" in detail
    assert "event_01_time_0001" in detail
    assert "Re-import" in detail


def test_manifest_preflight_reports_truncated_frames(tmp_path) -> None:
    root = tmp_path / "wave-imports"
    manifest_path = _write_wave_manifest(
        root, "wave_live_20191130T0000Z_20191201T0000Z_hs_elev",
        "2026-09-23T10:24:53Z",
    )
    frame = manifest_path.parent / "UnityRaw" / VARIABLE / "event_01_time_0000.raw"
    frame.write_bytes(b"\x00")

    with pytest.raises(HTTPException) as error:
        app_module._verify_dataset_frames(manifest_path, manifest_path.parent.name)

    assert "bytes on disk" in str(error.value.detail)


def test_health_reports_unreadable_manifests(tmp_path, monkeypatch) -> None:
    """A corrupt manifest must be visible, not silently skip the dataset."""
    root = tmp_path / "datasets"
    _write_wave_manifest(root, "wave_live_ok", "2026-09-23T10:24:53Z")
    broken = root / "wave_live_broken"
    broken.mkdir(parents=True)
    (broken / "manifest.json").write_text("{ not json", encoding="utf-8")
    monkeypatch.setenv("S4D_DATASET_ROOT", str(root))
    monkeypatch.setattr(manifests_module, "WAVE_IMPORT_ROOT", tmp_path / "none")

    client = TestClient(app)
    health = client.get("/health").json()

    assert health["datasets"] == 1
    assert len(health["unreadableManifests"]) == 1
    assert "wave_live_broken" in health["unreadableManifests"][0]
