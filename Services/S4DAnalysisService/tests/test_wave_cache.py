"""Hybrid data source: a bundled hydrated dataset must answer with no network.

The packaged app prefers the live Wave gateway and falls back to the dataset
shipped inside the package. These tests pin that fallback down: it must serve
real values from disk, never touch the gateway, and explain itself when the
requested window is not bundled.
"""

from __future__ import annotations

import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient

from Services.S4DAnalysisService.app import app
from Services.S4DAnalysisService.wave_importer import (
    WaveDatasetImporter,
    WaveImportError,
    wave_mode,
)

NODES = 7364
START = datetime(2019, 11, 30, tzinfo=timezone.utc)
HOURS = 24


def _iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _bundle(tmp_path: Path, dataset_id: str) -> Path:
    """A minimal hydrated live dataset: geo node ids + one float frame set."""
    cache_root = tmp_path / "For_VR" / "WaveCache"
    root = cache_root / dataset_id
    geo = root / "UnityRaw" / "GeoSurface"
    geo.mkdir(parents=True)
    (root / "manifest.json").write_text(
        json.dumps({"datasetVersion": "test-2026-09-24"}), encoding="utf-8"
    )
    (root / ".hydrated").write_text("1", encoding="utf-8")
    node_ids = np.arange(1, NODES + 1, dtype="<u4")
    node_ids.tofile(geo / "node_ids_u32.bin")
    values = np.zeros((HOURS, NODES), dtype="<f4")
    for hour in range(HOURS):
        values[hour] = hour * 1000.0 + np.arange(NODES)
    values.tofile(geo / "Wave_HS_nodes_f32.bin")
    conversion = {
        "schemaVersion": "1.0",
        "grid": {"x": 96, "y": 64, "z": 4},
        "datasets": [
            {
                "name": "Wave_HS",
                "sourceField": "hs",
                "liveWave": True,
                "geographicFrameStrideBytes": NODES * 4,
                "timeUTC": [
                    _iso(START + timedelta(hours=index)) for index in range(HOURS)
                ],
            }
        ],
    }
    (root / "UnityRaw" / "conversion_manifest.json").write_text(
        json.dumps(conversion), encoding="utf-8"
    )
    return cache_root


def _static_mesh(workspace: Path) -> None:
    """The mapping files status() insists on, for tests that register a dataset."""
    geo = workspace / "For_VR" / "UnityRaw" / "GeoSurface"
    geo.mkdir(parents=True, exist_ok=True)
    np.arange(1, NODES + 1, dtype="<u4").tofile(geo / "node_ids_u32.bin")
    np.ones(96 * 64, dtype=np.uint8).tofile(geo / "raster_valid_u8.bin")
    np.zeros((96 * 64, 3), dtype="<u4").tofile(geo / "raster_vertices_u32.bin")
    np.zeros((96 * 64, 3), dtype="<f4").tofile(geo / "raster_weights_f32.bin")


def _importer(tmp_path: Path, cache_root: Path) -> WaveDatasetImporter:
    return WaveDatasetImporter(
        tmp_path,
        tmp_path / ".runtime" / "wave-imports",
        cache_root=cache_root,
    )


def _no_network(monkeypatch) -> None:
    def explode(*args, **kwargs):
        raise AssertionError("the bundled cache must answer without the gateway")

    monkeypatch.delenv("WAVE_API_KEY", raising=False)
    monkeypatch.setattr(WaveDatasetImporter, "_get_json", explode)


def test_timeline_batch_is_served_from_the_bundle(tmp_path, monkeypatch) -> None:
    cache_root = _bundle(tmp_path, "wave_live_20191130T0000Z_20191201T0000Z_hs")
    importer = _importer(tmp_path, cache_root)
    _no_network(monkeypatch)

    result = importer.fetch_timeline_batch(
        _iso(START), _iso(START + timedelta(hours=2)), "hs", [1, 2]
    )

    assert result["source"] == "cache"
    assert result["hours"] == 2
    values = {
        (item["frameIndex"], item["nodeId"]): item["value"]
        for item in result["items"]
    }
    assert values[(0, 1)] == 0.0
    assert values[(0, 2)] == 1.0
    assert values[(1, 1)] == 1000.0
    assert values[(1, 2)] == 1001.0
    assert wave_mode() == "cache"


def test_frame_batch_is_served_from_the_bundle(tmp_path, monkeypatch) -> None:
    cache_root = _bundle(tmp_path, "wave_live_20191130T0000Z_20191201T0000Z_hs")
    importer = _importer(tmp_path, cache_root)
    _no_network(monkeypatch)

    result = importer.fetch_frame_batch("2019-11-30T03:00:00Z", "hs", [5])

    assert result["source"] == "cache"
    assert result["items"] == [{"nodeId": 5, "value": 3000.0 + 4.0}]
    assert wave_mode() == "cache"


def test_live_registration_seeds_from_the_bundle(tmp_path, monkeypatch) -> None:
    dataset_id = "wave_live_20191130T0000Z_20191201T0000Z_hs"
    cache_root = _bundle(tmp_path, dataset_id)
    importer = _importer(tmp_path, cache_root)
    _no_network(monkeypatch)

    result = importer.prepare_live_dataset(
        _iso(START), _iso(START + timedelta(hours=HOURS)), ["hs"]
    )

    assert result["source"] == "cache"
    assert result["hydrated"] is True
    assert result["variables"] == ["Wave_HS"]
    seeded = Path(str(result["datasetRoot"]))
    assert (seeded / "conversion_manifest.json").is_file()
    assert (seeded.parent / ".hydrated").is_file()


def test_registration_without_key_or_bundle_explains_both(
    tmp_path, monkeypatch
) -> None:
    """
    Step 1 of a packaged app asks for a window it cannot reach. The operator has
    to learn both halves — no key *and* nothing bundled covers this window — or
    they will go looking for the wrong problem. This used to be a bare
    "Wave API key is missing."
    """
    _no_network(monkeypatch)
    _static_mesh(tmp_path)
    # A cache that exists but holds other windows: the message must name it.
    cache_root = _bundle(tmp_path, "wave_live_20191130T0000Z_20191201T0000Z_hs")
    importer = _importer(tmp_path, cache_root)

    with pytest.raises(WaveImportError) as error:
        importer.prepare_live_dataset(
            "2020-01-01T00:00:00Z", "2020-01-02T00:00:00Z", ["hs"]
        )

    detail = str(error.value)
    assert "no bundled dataset covers" in detail
    assert "hs" in detail
    assert str(cache_root) in detail or "Bundled windows" in detail


def test_offline_request_explains_what_is_missing(tmp_path, monkeypatch) -> None:
    cache_root = _bundle(tmp_path, "wave_live_20191130T0000Z_20191201T0000Z_hs")
    importer = _importer(tmp_path, cache_root)
    _no_network(monkeypatch)

    with pytest.raises(WaveImportError) as error:
        importer.fetch_timeline_batch(
            "2020-01-01T00:00:00Z", "2020-01-01T02:00:00Z", "hs", [1]
        )

    detail = str(error.value)
    assert "no bundled dataset covers" in detail
    assert "2019-11-30T00:00:00Z" in detail


def test_health_reports_the_wave_mode() -> None:
    payload = TestClient(app).get("/health").json()
    assert "waveMode" in payload
    assert isinstance(payload["waveCache"], list)
