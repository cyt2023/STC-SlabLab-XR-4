"""Offline release regressions; no model calls or running services required."""
import asyncio
import io
import json
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np
import httpx
import pytest

from fastapi import HTTPException, UploadFile
from Services.MatPlotAgent import api_server as api
from Services.S4DAnalysisService.models import FacetGridRequest, VolumeManifest


def test_intent_endpoint_accepts_json_body():
    """The routed request model must remain a JSON body, not a query field."""
    from fastapi.testclient import TestClient
    from Services.S4DAnalysisService.app import app

    client = TestClient(app)
    response = client.post("/analysis/resolve-intent", json={
        "text": "Find anomalies", "variableId": "hs",
        "variableDisplayName": "Wave HS", "unit": "m",
    })
    assert response.status_code == 200, response.text
    assert response.json()["analyticTask"] == "find_anomalies"
    assert client.get("/openapi.json").status_code == 200


def _mesh_workspace(tmp_path: Path) -> Path:
    """The minimum a workspace needs before WaveDatasetImporter.status() runs."""
    workspace = tmp_path / "workspace"
    geo = workspace / "For_VR" / "UnityRaw" / "GeoSurface"
    geo.mkdir(parents=True, exist_ok=True)
    np.array([1, 4, 9], dtype="<u4").tofile(geo / "node_ids_u32.bin")
    np.ones(96 * 64, dtype=np.uint8).tofile(geo / "raster_valid_u8.bin")
    np.tile(np.array([0, 1, 2], dtype="<u4"), (96 * 64, 1)).tofile(
        geo / "raster_vertices_u32.bin")
    np.tile(np.array([1.0, 0.0, 0.0], dtype="<f4"), (96 * 64, 1)).tofile(
        geo / "raster_weights_f32.bin")
    return workspace


def test_unknown_ids_answer_404_not_500():
    """Splitting the routers moved the lookups; an unknown id must still be a
    clean 404 from every endpoint that resolves one."""
    from fastapi.testclient import TestClient
    from Services.S4DAnalysisService.app import app

    client = TestClient(app)
    for path in ("/jobs/does-not-exist",
                 "/jobs/does-not-exist/panel",
                 "/jobs/does-not-exist/chart-result",
                 "/digest-jobs/does-not-exist",
                 "/snapshots/does-not-exist"):
        response = client.get(path)
        assert response.status_code == 404, (path, response.status_code,
                                             response.text[:200])


class InputValidationTests(unittest.TestCase):
    def test_reject_duplicate_and_ambiguous_cell_ids(self):
        for times, depths in [(["a", "a"], ["b"]), (["a", "a__b"], ["b__c", "c"])]:
            with self.subTest(times=times), self.assertRaises(ValueError):
                FacetGridRequest(datasetId="d", variableId="v",
                    timeBuckets=[dict(id=i, label=i, indices=[0]) for i in times],
                    depthBuckets=[dict(id=i, label=i, indices=[0]) for i in depths])

    def test_reject_invalid_dataset_metadata(self):
        source = next((Path(__file__).resolve().parents[3] / "datasets").glob("*/manifest.json"))
        original = json.loads(source.read_text())
        for failure in ["empty_variables", "empty_frames", "nan_scale", "infinite_offset"]:
            data = json.loads(json.dumps(original))
            variable = next(iter(data["variables"].values()))
            if failure == "empty_variables": data["variables"] = {}
            elif failure == "empty_frames": variable["frames"] = []
            elif failure == "nan_scale": variable["scale"] = float("nan")
            else: variable["offset"] = float("inf")
            with self.subTest(failure=failure), self.assertRaises(ValueError):
                VolumeManifest.model_validate(data)


class AdmissionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.slots = threading.BoundedSemaphore(1)
        self.patches = [patch.object(api, "UPLOADS", root / "uploads"),
                        patch.object(api, "JOBS", root / "jobs"),
                        patch.object(api, "_admission_slots", self.slots),
                        patch.object(api, "_jobs", {})]
        for item in self.patches: item.start()

    def tearDown(self):
        for item in reversed(self.patches): item.stop()
        self.temp.cleanup()

    def submit(self, data=b"x,y\n1,2", contract=None):
        return asyncio.run(api.create_job("plot", UploadFile(filename="x.csv", file=io.BytesIO(data)),
            UploadFile(filename="contract.json", file=io.BytesIO(contract)) if contract is not None else None))

    def test_full_queue_rejects_without_creating_files(self):
        self.slots.acquire()
        with self.assertRaises(HTTPException) as caught: self.submit()
        self.assertEqual(429, caught.exception.status_code)
        self.assertFalse(api.UPLOADS.exists())

    def test_bad_uploads_release_slot_and_remove_partial_files(self):
        for data, contract, limit, status in [(b"12345", None, 4, 413), (b"1", b"{", 4, 400)]:
            with self.subTest(status=status), patch.object(api, "_max_upload_bytes", limit):
                with self.assertRaises(HTTPException) as caught: self.submit(data, contract)
                self.assertEqual(status, caught.exception.status_code)
                self.assertTrue(self.slots.acquire(blocking=False))
                self.slots.release()
                self.assertEqual([], list(api.UPLOADS.iterdir()))
                self.assertEqual([], list(api.JOBS.iterdir()))

    def test_worker_failure_releases_capacity(self):
        self.slots.acquire()
        with patch.object(api, "_run", side_effect=RuntimeError("worker failed")):
            with self.assertRaises(RuntimeError): api._run_admitted()
        self.assertTrue(self.slots.acquire(blocking=False))

    def test_thread_start_failure_cleans_up(self):
        with patch.object(api.threading.Thread, "start", side_effect=RuntimeError("no thread")):
            with self.assertRaises(RuntimeError): self.submit()
        self.assertEqual({}, api._jobs)
        self.assertTrue(self.slots.acquire(blocking=False))
        self.assertEqual([], list(api.JOBS.iterdir()))
def test_the_wave_api_key_never_leaves_the_process(tmp_path, monkeypatch):
    """
    The packaged app ships a .env with a real key. Nothing the app can read back
    may quote it: not /wave/status, not /health, not an error message.
    """
    from Services.S4DAnalysisService.wave_importer import WaveDatasetImporter

    secret = "test-key-that-must-not-be-echoed-0123456789"
    monkeypatch.setenv("WAVE_API_KEY", secret)
    seen: dict[str, str] = {}

    def fake_get_json(self, path, key, **kwargs):
        # It still has to reach the gateway, otherwise this test proves nothing.
        seen["key"] = key
        return {"status": "ok"}

    monkeypatch.setattr(WaveDatasetImporter, "_get_json", fake_get_json)
    importer = WaveDatasetImporter(_mesh_workspace(tmp_path),
        tmp_path / "imports", cache_root=tmp_path / "cache")

    assert secret not in json.dumps(importer.status())
    assert seen.get("key") == secret


def test_an_echoing_gateway_cannot_leak_the_key(tmp_path):
    """
    Upstream error bodies are quoted into our own messages, so a gateway that
    repeats the request (header included) would otherwise hand the key back —
    through /wave/status, and through the exception the operator reads.
    """
    from Services.S4DAnalysisService.wave_importer import (
        WaveDatasetImporter,
        WaveImportError,
    )

    secret = "echoed-secret-0123456789"

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            401, json={"detail": "rejected X-API-Key " + secret})

    importer = WaveDatasetImporter(
        _mesh_workspace(tmp_path),
        tmp_path / "imports",
        transport=httpx.MockTransport(handler),
    )

    reported = importer.status(api_key=secret)
    assert secret not in json.dumps(reported)
    assert "<redacted>" in str(reported.get("error", "")), reported

    with pytest.raises(WaveImportError) as error:
        importer.fetch_frame_batch(
            "2019-11-30T03:00:00Z", "hs", [5], api_key=secret)
    assert secret not in str(error.value)
    assert "<redacted>" in str(error.value)
