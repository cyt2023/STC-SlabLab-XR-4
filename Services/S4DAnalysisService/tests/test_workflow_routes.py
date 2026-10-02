"""Exercise router boundaries after the service split, without paid model calls.

Only the remote job transport and model answer are replaced. Numeric RAW
reading, contracts, MatPlotAgent's deterministic renderer, durable snapshots,
assembled PNGs, digest routing and Ground reconstruction use production code.
"""
from __future__ import annotations

import json
import os
import threading
import time
from io import BytesIO
from types import SimpleNamespace

import numpy as np
import pytest
from fastapi.testclient import TestClient
from PIL import Image

from Services.MatPlotAgent.local_run import render_contract_fallback
from Services.S4DAnalysisService import manifests, state
from Services.S4DAnalysisService import config
from Services.S4DAnalysisService.app import app
from Services.S4DAnalysisService.digest import build_deterministic_digest
from Services.S4DAnalysisService.routers import analysis, datasets, jobs
from Services.S4DAnalysisService.snapshot_store import SnapshotStore


@pytest.fixture
def workflow(tmp_path, monkeypatch):
    source = tmp_path / "datasets" / "route-test"
    source.mkdir(parents=True)
    frames = []
    for i in range(2):
        values = np.array([0, 10, 20, 30, 0, 30, 40, 50], dtype=np.uint8) + i * 10
        values[[0, 4]] = 0
        filename = f"frame-{i}.raw"
        (source / filename).write_bytes(values.tobytes())
        frames.append(dict(frameId=f"f{i}", timeIndex=i, temporalMeaning="instantaneous",
                           path=filename, expectedBytes=8))
    manifest = dict(
        schemaVersion="1.0", datasetId="route-test", datasetVersion="1",
        dimensions=dict(x=2, y=2, z=2), storageOrder="ZYX", defaultVoxelType="uint8",
        coordinates=dict(x=dict(kind="ordinal_index", unit="x_index"),
                         y=dict(kind="ordinal_index", unit="y_index"),
                         depth=dict(kind="ordinal_index", unit="depth_index", positive="down")),
        variables={"v":dict(displayName="V", unit="encoded_intensity",
                            valueSemantics="encoded_intensity", voxelType="uint8",
                            scale=1, offset=0, missingRawValues=[0], frames=frames)},
    )
    path = source / "manifest.json"
    path.write_text(json.dumps(manifest))
    monkeypatch.setenv("S4D_DATASET_ROOT", str(source.parent))
    monkeypatch.setattr(manifests, "WAVE_IMPORT_ROOT", tmp_path / "no-wave")
    monkeypatch.setattr(state, "snapshot_store", SnapshotStore(tmp_path / "snapshots"))
    monkeypatch.setattr(state, "_submitted_jobs", {})
    monkeypatch.setattr(state, "_digest_jobs", {})
    for module in (analysis, jobs):
        monkeypatch.setattr(module, "JOB_ROOT", tmp_path / "jobs")
    rendered = {}
    lock = threading.Lock()

    class LocalGateway:
        remote_status = "completed"

        def __init__(self, *_args, **_kwargs):
            pass

        def submit(self, package):
            with lock:  # Matplotlib is not thread-safe; S4D submits concurrently.
                assert render_contract_fallback(package.data_csv.parent, "final.png")
                job_id = package.data_csv.parent.name
                rendered[job_id] = package.data_csv.parent
            return SimpleNamespace(job_id=job_id)

        def status(self, job_id):
            return dict(status=self.remote_status, progress=1, stage=self.remote_status,
                        error="fixture failure" if self.remote_status == "failed" else "")

        def artifact(self, job_id, kind):
            filename = "final.png" if kind == "image" else "chart_result.json"
            return (rendered[job_id] / filename).read_bytes(), (
                "image/png" if kind == "image" else "application/json")

    for module in (analysis, jobs):
        monkeypatch.setattr(module, "MatPlotAgentGateway", LocalGateway)
    digest_calls = []

    def model_digest(snapshot, image):
        assert snapshot["status"] == "completed"
        with Image.open(BytesIO(image)) as montage:
            montage.verify()
        digest_calls.append(snapshot["snapshotId"])
        return {**build_deterministic_digest(snapshot), "generatedBy": "fixture-model"}

    monkeypatch.setattr(jobs, "build_llm_digest", model_digest)
    request = dict(datasetId="route-test", variableId="v", rawIntent="Compare distribution",
                   timeBuckets=[dict(id=f"t{i}", label=f"T{i}", indices=[i]) for i in range(2)],
                   depthBuckets=[dict(id=f"z{i}", label=f"Z{i}", indices=[i]) for i in range(2)])
    with TestClient(app) as client:
        yield SimpleNamespace(client=client, request=request, source=source,
                              gateway=LocalGateway, digest_calls=digest_calls)


def submit(w):
    response = w.client.post("/analysis/materialize", json=w.request)
    assert response.status_code == 200, response.text
    return response.json()


def test_dataset_to_matrix_findings_and_ground_round_trip(workflow):
    w = workflow
    assert w.client.get("/datasets").json()[0]["datasetId"] == "route-test"
    assert w.client.get("/datasets/route-test/manifest").status_code == 200
    resolved = w.client.get("/datasets/resolve", params=dict(variable="v", x=2, y=2, z=2,
                                                           timeCount=2))
    assert resolved.status_code == 200
    assert resolved.json()["datasetId"] == "route-test"
    intent = w.client.post("/analysis/resolve-intent", json=dict(text="Compare distribution",
                                                               variableId="v"))
    assert intent.status_code == 200
    w.request["analyticTask"] = intent.json()["analyticTask"]
    preview = w.client.post("/analysis/preview-atlas", json=w.request)
    assert preview.status_code == 200
    assert preview.content.startswith(b"\x89PNG\r\n\x1a\n")
    prepared = w.client.post("/analysis/prepare-matplot-job", json=w.request)
    assert prepared.status_code == 200
    assert len(prepared.json()["cells"]) == 4
    submitted = submit(w)
    job_id, snapshot_id = submitted["jobId"], submitted["snapshotId"]
    # A completed remote job must make the durable snapshot eligible for Ground.
    result = w.client.get(f"/jobs/{job_id}")
    assert result.status_code == 200
    assert result.json()["status"] == "completed"
    assert len(result.json()["cells"]) == 4
    state._submitted_jobs.clear()  # Recovery crosses the shared router state boundary.
    assert w.client.get(f"/jobs/{job_id}").json()["snapshotId"] == snapshot_id
    panel = w.client.get(f"/jobs/{job_id}/panel")
    assert panel.status_code == 200
    with Image.open(BytesIO(panel.content)) as image:
        image.verify()
    metadata = w.client.get(f"/jobs/{job_id}/chart-result").json()
    assert metadata["cellOrder"] == ["t0__z0", "t1__z0", "t0__z1", "t1__z1"]
    assert [c["mean"] for c in metadata["cellStatistics"]] == [20, 30, 40, 50]
    assert all(c["hasData"] for c in metadata["cellStatistics"])
    ground = w.client.get(f"/snapshots/{snapshot_id}/cells/t0__z0/aggregate-volume")
    assert ground.status_code == 200
    np.testing.assert_equal(np.frombuffer(ground.content, dtype="<f4"), [np.nan, 10, 20, 30])
    assert float(ground.headers["X-S4D-Cell-Mean"]) == 20
    digest = w.client.post(f"/jobs/{job_id}/digest")
    assert digest.status_code == 202
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        record = w.client.get(digest.json()["statusUrl"]).json()
        if record["status"] in {"completed", "failed"}:
            break
        time.sleep(0.01)
    assert record["status"] == "completed", record
    assert record["digest"]["generatedBy"] == "fixture-model"
    assert w.digest_calls == [snapshot_id]  # Missing import must not silently fall back.
    frame = w.source / "frame-0.raw"
    previous_mtime = frame.stat().st_mtime
    frame.write_bytes(b"\x01" * 8)
    os.utime(frame, (previous_mtime + 2, previous_mtime + 2))
    # An already materialized Ground cache is immutable snapshot evidence.
    # An uncached cell must refuse to reconstruct against changed RAW files.
    assert w.client.get(f"/snapshots/{snapshot_id}/cells/t0__z0/aggregate-volume").status_code == 200
    assert w.client.get(f"/snapshots/{snapshot_id}/cells/t1__z1/aggregate-volume").status_code == 409


@pytest.mark.parametrize("status", ["running", "failed"])
def test_unfinished_or_failed_grid_cannot_be_used_as_ground(workflow, status):
    w = workflow
    w.gateway.remote_status = status
    submitted = submit(w)
    assert w.client.get(f"/jobs/{submitted['jobId']}").json()["status"] == status
    path = f"/snapshots/{submitted['snapshotId']}/cells/t0__z0/aggregate-volume"
    assert w.client.get(path).status_code == 409


def test_unknown_dataset_remains_a_404_across_routers(workflow):
    w = workflow
    assert w.client.get("/datasets/unknown/manifest").status_code == 404
    w.request["datasetId"] = "unknown"
    for path in ("/analysis/preview-atlas", "/analysis/prepare-matplot-job", "/analysis/materialize"):
        assert w.client.post(path, json=w.request).status_code == 404


def test_service_code_identity_includes_split_router_modules(tmp_path, monkeypatch):
    root_file = tmp_path / "config.py"
    root_file.write_text("# root module")
    router_file = tmp_path / "routers" / "analysis.py"
    router_file.parent.mkdir()
    router_file.write_text("# split module")
    os.utime(root_file, (1_600_000_000, 1_600_000_000))
    os.utime(router_file, (1_600_000_100, 1_600_000_100))
    monkeypatch.setattr(config, "__file__", str(root_file))
    assert config._service_code_modified_at() == "2020-09-13T12:28:20Z"
