from __future__ import annotations

import json
from pathlib import Path
from urllib.parse import parse_qs

import httpx
import numpy as np
import pytest

from Services.S4DAnalysisService.wave_importer import (
    WaveDatasetImporter,
    WaveImportError,
)


def _write_static_mesh(workspace: Path) -> None:
    geo = workspace / "For_VR" / "UnityRaw" / "GeoSurface"
    geo.mkdir(parents=True)
    np.array([1, 4, 9], dtype="<u4").tofile(geo / "node_ids_u32.bin")
    np.tile(np.array([113.8, 22.2], dtype="<f4"), 3).tofile(
        geo / "lon_lat_f32.bin"
    )
    np.array([0, 1, 2], dtype="<u4").tofile(geo / "faces_u32.bin")
    np.ones(96 * 64, dtype=np.uint8).tofile(geo / "raster_valid_u8.bin")
    np.tile(np.array([0, 1, 2], dtype="<u4"), (96 * 64, 1)).tofile(
        geo / "raster_vertices_u32.bin"
    )
    np.tile(np.array([1.0, 0.0, 0.0], dtype="<f4"), (96 * 64, 1)).tofile(
        geo / "raster_weights_f32.bin"
    )
    manifest = {
        "boundsEPSG4326": {
            "lonMin": 113.6,
            "lonMax": 114.7,
            "latMin": 22.0,
            "latMax": 22.7,
        },
        "geographicSurface": {
            "coordinateReference": "EPSG:4326",
            "projection": "WebMercator",
            "nodeCount": 3,
            "faceCount": 1,
            "coordinateFile": "GeoSurface/lon_lat_f32.bin",
            "faceFile": "GeoSurface/faces_u32.bin",
        },
    }
    (workspace / "For_VR" / "UnityRaw" / "conversion_manifest.json").write_text(
        json.dumps(manifest), encoding="utf-8"
    )


def test_wave_import_builds_native_dataset(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)

    def handler(request: httpx.Request) -> httpx.Response:
        assert request.headers["X-API-Key"] == "test-key"
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        query = parse_qs(request.url.query.decode())
        nodes = [int(value) for value in query["node_ids"][0].split(",")]
        rows = []
        for hour in range(2):
            for node_id in nodes:
                rows.append({
                    "time": f"2019-11-30T0{hour}:00:00",
                    "node_id": str(node_id),
                    "hs": node_id + hour / 10.0,
                    "elev": -node_id - hour / 10.0,
                })
        return httpx.Response(200, json=rows)

    importer = WaveDatasetImporter(
        workspace,
        tmp_path / "imports",
        transport=httpx.MockTransport(handler),
    )
    result = importer.import_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )

    root = Path(result["datasetRoot"])
    assert result["hours"] == 2
    assert result["requests"] == 1
    assert (root / "Wave_HS" / "event_01_time_0000.raw").stat().st_size == 96 * 64 * 4
    assert (root / "Wave_Water_Level" / "event_01_time_0001.raw.ini").is_file()
    exact = np.fromfile(
        root / "GeoSurface" / "Wave_HS_nodes_f32.bin", dtype="<f4"
    ).reshape(2, 3)
    np.testing.assert_allclose(exact, [[1.0, 4.0, 9.0], [1.1, 4.1, 9.1]])
    manifest = json.loads(Path(result["manifestPath"]).read_text(encoding="utf-8"))
    assert set(manifest["variables"]) == {"Wave_HS", "Wave_Water_Level"}


def test_live_wave_dataset_is_lazy_and_batches_visible_frame(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        query = parse_qs(request.url.query.decode())
        field = query["fields"][0]
        hours = 2 if query["end"][0].startswith("2019-11-30T02") else 1
        return httpx.Response(200, json=[
            {"time": f"2019-11-30T0{hour}:00:00", "node_id": node_id,
             field: float(node_id) + hour / 10.0}
            for hour in range(hours)
            for node_id in query["node_ids"][0].split(",")
        ])

    importer = WaveDatasetImporter(
        workspace,
        tmp_path / "imports",
        transport=httpx.MockTransport(handler),
    )
    result = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )
    assert result["datasetId"].startswith("wave_live_")
    assert result["datasetVersion"]
    assert result["variables"] == ["Wave_HS", "Wave_Water_Level"]
    root = Path(result["datasetRoot"])
    values = np.fromfile(
        root / "GeoSurface" / "Wave_HS_nodes_f32.bin", dtype="<f4"
    )
    assert values.size == 6
    assert np.isnan(values).all()
    conversion = json.loads(
        (root / "conversion_manifest.json").read_text(encoding="utf-8")
    )
    assert conversion["datasets"][0]["liveWave"] is True

    batch = importer.fetch_frame_batch(
        "2019-11-30T00:00:00Z", "hs", [1, 4, 9], api_key="test-key"
    )
    assert batch["items"] == [
        {"nodeId": 1, "value": 1.0},
        {"nodeId": 4, "value": 4.0},
        {"nodeId": 9, "value": 9.0},
    ]

    timeline = importer.fetch_timeline_batch(
        "2019-11-30T00:00:00Z", "2019-11-30T02:00:00Z",
        "hs", [1, 4], api_key="test-key",
    )
    assert timeline["hours"] == 2
    assert timeline["items"] == [
        {"frameIndex": 0, "nodeId": 1, "value": 1.0},
        {"frameIndex": 0, "nodeId": 4, "value": 4.0},
        {"frameIndex": 1, "nodeId": 1, "value": 1.1},
        {"frameIndex": 1, "nodeId": 4, "value": 4.1},
    ]


def test_live_wave_dataset_hydrates_placeholder_frames(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        query = parse_qs(request.url.query.decode())
        hours = 2 if query["end"][0].startswith("2019-11-30T02") else 1
        fields = query["fields"][0].split(",")
        return httpx.Response(
            200,
            json=[
                {
                    "time": f"2019-11-30T0{hour}:00:00",
                    "node_id": node_id,
                    **{field: float(node_id) + hour for field in fields},
                }
                for hour in range(hours)
                for node_id in query["node_ids"][0].split(",")
            ],
        )

    importer = WaveDatasetImporter(
        workspace,
        tmp_path / "imports",
        transport=httpx.MockTransport(handler),
    )
    registered = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )
    dataset_root = Path(registered["datasetRoot"]).parent
    frame = dataset_root / "UnityRaw" / "Wave_HS" / "event_01_time_0000.raw"

    # The analysis service reads RAW from disk, so a lazily registered dataset
    # is invisible to it until the placeholders become real frames.
    assert not np.any(np.fromfile(frame, dtype=np.uint8))

    result = importer.hydrate_dataset(dataset_root, api_key="test-key")

    assert result["hydrated"] is True
    assert result["hours"] == 2
    assert sorted(result["variables"]) == ["Wave_HS", "Wave_Water_Level"]
    for index in range(2):
        volume = np.fromfile(
            dataset_root / "UnityRaw" / "Wave_HS" / f"event_01_time_{index:04d}.raw",
            dtype=np.uint8,
        )
        assert volume.size == 96 * 64 * 4
        assert volume.max() > 0
    nodes = np.fromfile(
        dataset_root / "UnityRaw" / "GeoSurface" / "Wave_HS_nodes_f32.bin",
        dtype="<f4",
    )
    assert nodes.size == 6
    assert np.isfinite(nodes).all()
    assert nodes.tolist() == [1.0, 4.0, 9.0, 2.0, 5.0, 10.0]

    # Hydration is idempotent: the frames are local now, so the next analysis
    # request must not hit the Wave API again.
    assert importer.hydrate_dataset(dataset_root, api_key="test-key")["hydrated"] is False


def test_repeated_live_registration_keeps_hydrated_frames(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)
    calls = {"count": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/wave":
            calls["count"] += 1
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        query = parse_qs(request.url.query.decode())
        hours = 2 if query["end"][0].startswith("2019-11-30T02") else 1
        fields = query["fields"][0].split(",")
        return httpx.Response(
            200,
            json=[
                {
                    "time": f"2019-11-30T0{hour}:00:00",
                    "node_id": node_id,
                    **{field: float(node_id) + hour for field in fields},
                }
                for hour in range(hours)
                for node_id in query["node_ids"][0].split(",")
            ],
        )

    importer = WaveDatasetImporter(
        workspace, tmp_path / "imports", transport=httpx.MockTransport(handler)
    )
    registered = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )
    importer.hydrate_dataset(Path(registered["datasetRoot"]).parent, api_key="test-key")
    calls_after_hydration = calls["count"]

    # Launching the app again registers the same window. It must reuse the
    # hydrated dataset instead of re-downloading the range (and tripping the
    # gateway rate limit).
    again = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )

    assert again["reused"] is True
    assert again["hydrated"] is True
    # The Unity opening screen prints "DATA SOURCE · LIVE" versus
    # "DATA SOURCE · LOCAL CACHE (NO NETWORK)" from this field, so both paths
    # have to declare themselves instead of leaving the label to guess. The
    # second registration is deliberately answered from the local store (no
    # gateway call), so it reports "cache" even though the first one was live.
    assert registered["source"] == "live"
    assert again["source"] == "cache"
    assert again["datasetVersion"] == registered["datasetVersion"]
    assert calls["count"] == calls_after_hydration


def test_rate_limited_request_is_retried(tmp_path: Path, monkeypatch) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)
    attempts = {"count": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        attempts["count"] += 1
        if attempts["count"] == 1:
            return httpx.Response(
                429, json={"detail": "Rate limit exceeded; retry in one minute"}
            )
        return httpx.Response(200, json={"status": "ok"})

    monkeypatch.setattr(
        "Services.S4DAnalysisService.wave_importer.time.sleep", lambda _: None
    )
    importer = WaveDatasetImporter(
        workspace, tmp_path / "imports", transport=httpx.MockTransport(handler)
    )

    assert importer._get_json("/v1/metadata", "test-key") == {"status": "ok"}
    assert attempts["count"] == 2


def _grow_static_mesh(workspace: Path, node_count: int) -> None:
    """Give the test mesh more nodes so a window spans several API batches."""
    nodes = workspace / "For_VR" / "UnityRaw" / "GeoSurface" / "node_ids_u32.bin"
    np.arange(1, node_count + 1, dtype="<u4").tofile(nodes)


def _wave_rows(request: httpx.Request) -> httpx.Response:
    query = parse_qs(request.url.query.decode())
    fields = query["fields"][0].split(",")
    hours = 2 if query["end"][0].startswith("2019-11-30T02") else 1
    return httpx.Response(
        200,
        json=[
            {
                "time": f"2019-11-30T0{hour}:00:00",
                "node_id": node_id,
                **{field: float(node_id) + hour for field in fields},
            }
            for hour in range(hours)
            for node_id in query["node_ids"][0].split(",")
        ],
    )


def test_partial_hydration_stays_retryable(tmp_path: Path, monkeypatch) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)
    _grow_static_mesh(workspace, 150)
    monkeypatch.setattr(
        "Services.S4DAnalysisService.wave_importer.time.sleep", lambda _: None
    )

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        first = int(parse_qs(request.url.query.decode())["node_ids"][0].split(",")[0])
        if first > 100:
            return httpx.Response(503, json={"detail": "temporarily unavailable"})
        return _wave_rows(request)

    importer = WaveDatasetImporter(
        workspace, tmp_path / "imports", transport=httpx.MockTransport(handler)
    )
    registered = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )
    root = Path(registered["datasetRoot"]).parent

    result = importer.hydrate_dataset(root, api_key="test-key")

    assert result["hydrated"] is True
    assert result["partial"] is True
    assert result["failedBatches"] == 1
    assert result["totalBatches"] == 2
    frame = root / "UnityRaw" / "Wave_HS" / "event_01_time_0000.raw"
    assert np.any(np.fromfile(frame, dtype=np.uint8))
    # A partial fetch must not be sealed, so the next analysis can finish it.
    assert not (root / ".hydrated").is_file()


def test_hydration_aborts_when_coverage_is_too_low(
    tmp_path: Path, monkeypatch
) -> None:
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)
    _grow_static_mesh(workspace, 150)
    monkeypatch.setattr(
        "Services.S4DAnalysisService.wave_importer.time.sleep", lambda _: None
    )

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        return httpx.Response(503, json={"detail": "down"})

    importer = WaveDatasetImporter(
        workspace, tmp_path / "imports", transport=httpx.MockTransport(handler)
    )
    registered = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )
    root = Path(registered["datasetRoot"]).parent

    with pytest.raises(WaveImportError) as error:
        importer.hydrate_dataset(root, api_key="test-key")

    assert "batches failed" in str(error.value)
    assert not (root / ".hydrated").is_file()


def test_status_probe_fails_fast_without_retrying(tmp_path: Path, monkeypatch) -> None:
    """The readiness probe must not inherit the batch retry/backoff policy.

    It used to spend ~70s (4 attempts, 5+20+45s backoff) before answering when
    the Wave host was unreachable, which stalled the app's opening screen.
    """
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)
    attempts = {"count": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        attempts["count"] += 1
        raise httpx.ConnectError("connection refused", request=request)

    monkeypatch.setattr(
        "Services.S4DAnalysisService.wave_importer.time.sleep", lambda _: None
    )
    importer = WaveDatasetImporter(
        workspace, tmp_path / "imports", transport=httpx.MockTransport(handler)
    )

    result = importer.status(api_key="test-key")

    assert result["reachable"] is False
    assert "refused" in str(result["error"])
    assert attempts["count"] == 1, "a readiness probe must not retry"


def test_hydration_reports_write_failures(tmp_path: Path) -> None:
    """A read-only dataset folder must fail loudly, never seal a partial load."""
    workspace = tmp_path / "workspace"
    _write_static_mesh(workspace)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/v1/metadata":
            return httpx.Response(200, json={"status": "ok"})
        return _wave_rows(request)

    importer = WaveDatasetImporter(
        workspace, tmp_path / "imports", transport=httpx.MockTransport(handler)
    )
    registered = importer.prepare_live_dataset(
        "2019-11-30T00:00:00Z",
        "2019-11-30T02:00:00Z",
        ["hs", "elev"],
        api_key="test-key",
    )
    root = Path(registered["datasetRoot"]).parent
    frame = root / "UnityRaw" / "Wave_HS" / "event_01_time_0000.raw"
    frame.chmod(0o444)
    try:
        with pytest.raises(WaveImportError) as error:
            importer.hydrate_dataset(root, api_key="test-key")
        assert "Could not write the hydrated frames" in str(error.value)
        assert not (root / ".hydrated").is_file()
    finally:
        frame.chmod(0o644)
