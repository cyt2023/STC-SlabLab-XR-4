"""Job status, chart artifacts and findings generation."""

from __future__ import annotations

import uuid
import json
import threading
from io import BytesIO

import httpx
from fastapi import APIRouter, HTTPException, Response
from PIL import Image, ImageDraw, ImageOps

from ..config import JOB_ROOT, MATPLOT_URL
from ..digest import build_deterministic_digest, build_llm_digest
from ..matplot_gateway import MatPlotAgentGateway
from .. import state

router = APIRouter()

def require_submitted_job(job_id: str) -> state.SubmittedJob:
    submitted = state._submitted_jobs.get(job_id)
    if submitted is not None:
        return submitted
    job_path = JOB_ROOT / job_id / "s4d_job.json"
    if job_path.is_file():
        payload = json.loads(job_path.read_text(encoding="utf-8"))
        remote_jobs = payload.get("remoteJobs")
        if not remote_jobs and payload.get("remoteJobId"):
            remote_jobs = {"grid": str(payload["remoteJobId"])}
        submitted = state.SubmittedJob(
            remote_jobs={
                str(cell_id): str(remote_id)
                for cell_id, remote_id in dict(remote_jobs or {}).items()
            },
            snapshot_id=str(payload["snapshotId"]),
            cell_order=tuple(payload.get("cellOrder", remote_jobs or {"grid": ""})),
            columns=int(payload.get("columns", 1)),
            rows=int(payload.get("rows", 1)),
        )
        state._submitted_jobs[job_id] = submitted
        return submitted
    if submitted is None:
        raise HTTPException(status_code=404, detail=f"Unknown analysis job: {job_id}")
    return submitted


@router.get("/jobs/{job_id}")
def job_status(job_id: str) -> dict[str, object]:
    submitted = require_submitted_job(job_id)
    gateway = MatPlotAgentGateway(MATPLOT_URL)
    try:
        remote_cells = {
            cell_id: gateway.status(remote_id)
            for cell_id, remote_id in submitted.remote_jobs.items()
        }
    except httpx.HTTPError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    statuses = [
        str(remote.get("status", "")).lower()
        for remote in remote_cells.values()
    ]
    if statuses and all(status == "completed" for status in statuses):
        status = "completed"
        state.snapshot_store.set_status(submitted.snapshot_id, "completed")
    elif any(status == "failed" for status in statuses):
        status = "failed"
        errors = [
            str(remote.get("error", "MatPlotAgent cell generation failed"))
            for remote in remote_cells.values()
            if str(remote.get("status", "")).lower() == "failed"
        ]
        state.snapshot_store.set_status(
            submitted.snapshot_id,
            "failed",
            "; ".join(errors),
        )
    elif any(status == "running" for status in statuses):
        status = "running"
    else:
        status = "queued"
    cell_states = []
    for cell_id in submitted.cell_order:
        remote = remote_cells[cell_id]
        remote_status = str(remote.get("status", "queued")).lower()
        cell_states.append(
            {
                "cellId": cell_id,
                "status": remote_status,
                "stage": remote.get("stage", remote_status),
                "progress": float(remote.get("progress", 0.0)),
                "error": remote.get("error", ""),
                "panelUrl": (
                    f"/jobs/{job_id}/cells/{cell_id}/panel"
                    if remote_status == "completed"
                    else ""
                ),
            }
        )
    progress = (
        sum(float(remote.get("progress", 0.0)) for remote in remote_cells.values())
        / max(1, len(remote_cells))
    )
    return {
        "jobId": job_id,
        "matplotAgentJobId": next(iter(submitted.remote_jobs.values()), ""),
        "snapshotId": submitted.snapshot_id,
        "status": status,
        "stage": (
            f"{sum(state['status'] == 'completed' for state in cell_states)}"
            f"/{len(cell_states)} cell panels ready"
        ),
        "progress": progress,
        "error": next(
            (state["error"] for state in cell_states if state["error"]), ""
        ),
        "cells": cell_states,
    }


@router.get("/jobs/{job_id}/panel")
def job_panel(job_id: str) -> Response:
    submitted = require_submitted_job(job_id)
    try:
        gateway = MatPlotAgentGateway(MATPLOT_URL)
        images = []
        for cell_id in submitted.cell_order:
            content, _ = gateway.artifact(
                submitted.remote_jobs[cell_id], "image"
            )
            with Image.open(BytesIO(content)) as source:
                images.append(source.convert("RGB"))
    except httpx.HTTPError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    if not images:
        raise HTTPException(status_code=404, detail="No cell panels are available")
    atlas = _compose_cell_atlas(images, submitted.columns, submitted.rows)
    stream = BytesIO()
    atlas.save(stream, format="PNG")
    return Response(content=stream.getvalue(), media_type="image/png")


def _compose_cell_atlas(
    images: list[Image.Image],
    columns: int,
    rows: int,
    *,
    cell_size: tuple[int, int] = (640, 420),
    gutter: int = 10,
) -> Image.Image:
    """Place arbitrary MatPlot outputs in stable, bordered grid cells.

    MatPlotAgent is intentionally free to choose figure dimensions.  Stretching
    every result to the largest returned width/height made a single panoramic
    result flatten the entire 3 x 3 matrix.  Each artifact is now aspect-fit in
    a canonical card so rows, columns, and cell provenance remain legible.
    """
    cell_width, cell_height = cell_size
    atlas = Image.new(
        "RGB",
        (
            columns * cell_width + (columns + 1) * gutter,
            rows * cell_height + (rows + 1) * gutter,
        ),
        (5, 15, 20),
    )
    draw = ImageDraw.Draw(atlas)
    for index, source in enumerate(images):
        fitted = ImageOps.contain(
            source.convert("RGB"),
            (cell_width - 12, cell_height - 12),
            Image.Resampling.LANCZOS,
        )
        column = index % columns
        row = index // columns
        left = gutter + column * (cell_width + gutter)
        top = gutter + row * (cell_height + gutter)
        draw.rounded_rectangle(
            (left, top, left + cell_width, top + cell_height),
            radius=8,
            fill="white",
            outline=(0, 214, 230),
            width=4,
        )
        atlas.paste(
            fitted,
            (
                left + (cell_width - fitted.width) // 2,
                top + (cell_height - fitted.height) // 2,
            ),
        )
    return atlas


@router.get("/jobs/{job_id}/cells/{cell_id}/panel")
def job_cell_panel(job_id: str, cell_id: str) -> Response:
    submitted = require_submitted_job(job_id)
    remote_id = submitted.remote_jobs.get(cell_id)
    if remote_id is None:
        raise HTTPException(status_code=404, detail=f"Unknown cell: {cell_id}")
    try:
        content, content_type = MatPlotAgentGateway(MATPLOT_URL).artifact(
            remote_id, "image"
        )
    except httpx.HTTPError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    return Response(content=content, media_type=content_type)


@router.get("/jobs/{job_id}/chart-result")
def job_chart_result(job_id: str) -> Response:
    submitted = require_submitted_job(job_id)
    try:
        gateway = MatPlotAgentGateway(MATPLOT_URL)
        cells = {}
        for cell_id in submitted.cell_order:
            content, _ = gateway.artifact(
                submitted.remote_jobs[cell_id], "metadata"
            )
            cells[cell_id] = json.loads(content.decode("utf-8"))
    except httpx.HTTPError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    snapshot = state.snapshot_store.get(submitted.snapshot_id)
    cell_statistics = []
    for cell in snapshot.get("cells", []):
        # Coverage alone is not an authoritative statistic. Some legacy
        # snapshots recorded validFraction but omitted min/mean/max; treating
        # those omitted fields as zero creates false Highest/Lowest findings.
        valid_fraction = float(cell.get("validFraction", 0.0) or 0.0)
        has_statistics = all(
            key in cell for key in ("minimum", "mean", "maximum")
        )
        legacy_has_data = has_statistics and valid_fraction > 0.0
        has_data = bool(cell.get("hasData", legacy_has_data)) and has_statistics
        valid_count = int(cell.get("validCount", 1 if has_data else 0) or 0)
        cell_statistics.append(
            {
                "cellId": cell["cellId"],
                "minimum": cell.get("minimum", 0.0),
                "mean": cell.get("mean", 0.0),
                "maximum": cell.get("maximum", 0.0),
                "validFraction": valid_fraction,
                "validCount": valid_count,
                "hasData": has_data,
            }
        )
    return Response(
        content=json.dumps(
            {
                "cellOrder": submitted.cell_order,
                "columns": submitted.columns,
                "rows": submitted.rows,
                "cells": cells,
                "cellStatistics": cell_statistics,
            }
        ),
        media_type="application/json",
    )


def _update_digest_job(digest_job_id: str, **values: object) -> None:
    with state._digest_lock:
        state._digest_jobs[digest_job_id].update(values)


def _digest_grid_montage(job_id: str) -> bytes | None:
    submitted = state._submitted_jobs.get(job_id)
    if submitted is None:
        return None
    gateway = MatPlotAgentGateway(MATPLOT_URL)
    images: list[Image.Image] = []
    for cell_id in submitted.cell_order:
        content, _ = gateway.artifact(submitted.remote_jobs[cell_id], "image")
        with Image.open(BytesIO(content)) as source:
            image = source.convert("RGB")
            image.thumbnail((360, 280), Image.Resampling.LANCZOS)
            images.append(image.copy())
    if not images:
        return None
    montage = _compose_cell_atlas(
        images,
        submitted.columns,
        submitted.rows,
        cell_size=(360, 280),
        gutter=6,
    )
    stream = BytesIO()
    montage.save(stream, format="JPEG", quality=82, optimize=True)
    return stream.getvalue()


def _run_digest_job(digest_job_id: str, snapshot_id: str, job_id: str) -> None:
    try:
        _update_digest_job(
            digest_job_id,
            status="running",
            stage="comparing cell evidence",
            progress=0.35,
        )
        snapshot_metadata = state.snapshot_store.get(snapshot_id)
        if snapshot_metadata.get("status") != "completed":
            raise RuntimeError("Facet Grid snapshot is not completed")
        _update_digest_job(
            digest_job_id,
            stage="AI comparing nine MatPlot panels",
            progress=0.58,
        )
        try:
            digest = build_llm_digest(
                snapshot_metadata,
                _digest_grid_montage(job_id),
            )
        except Exception as exc:
            digest = build_deterministic_digest(snapshot_metadata)
            digest["generatedBy"] = "deterministic-fallback: " + str(exc)[:180]
        _update_digest_job(
            digest_job_id,
            status="completed",
            stage="digest ready",
            progress=1.0,
            digest=digest,
        )
    except Exception as exc:
        _update_digest_job(
            digest_job_id,
            status="failed",
            stage="failed",
            progress=1.0,
            error=str(exc),
        )


@router.post("/jobs/{job_id}/digest", status_code=202)
def create_digest_job(job_id: str) -> dict[str, object]:
    submitted = require_submitted_job(job_id)
    digest_job_id = uuid.uuid4().hex
    record: dict[str, object] = {
        "digestJobId": digest_job_id,
        "s4dJobId": job_id,
        "snapshotId": submitted.snapshot_id,
        "status": "queued",
        "stage": "queued",
        "progress": 0.0,
        "statusUrl": f"/digest-jobs/{digest_job_id}",
    }
    with state._digest_lock:
        state._digest_jobs[digest_job_id] = record
    threading.Thread(
        target=_run_digest_job,
        args=(digest_job_id, submitted.snapshot_id, job_id),
        daemon=True,
    ).start()
    return dict(record)


@router.get("/digest-jobs/{digest_job_id}")
def digest_job_status(digest_job_id: str) -> dict[str, object]:
    with state._digest_lock:
        record = state._digest_jobs.get(digest_job_id)
        if record is None:
            raise HTTPException(status_code=404, detail="Unknown Digest job")
        return dict(record)
