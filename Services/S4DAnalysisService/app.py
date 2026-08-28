from __future__ import annotations

import os
import tempfile
import uuid
import json
import threading
from concurrent.futures import ThreadPoolExecutor
from io import BytesIO
from dataclasses import dataclass
from pathlib import Path

import httpx
import numpy as np
from fastapi import FastAPI, File, HTTPException, Query, Response, UploadFile
from PIL import Image, ImageDraw, ImageOps
from pydantic import BaseModel

from .matplot_contract import (
    build_matplotagent_cell_packages,
    build_matplotagent_grid_package,
)
from .matplot_gateway import MatPlotAgentGateway
from .digest import build_deterministic_digest, build_llm_digest
from .models import (
    FacetGridRequest,
    IntentResolution,
    IntentResolutionRequest,
    VolumeManifest,
)
from .preview_renderer import render_preview_atlas
from .raw_reader import RawVolumeReader, validate_manifest_files
from .snapshot_store import SnapshotStore


WORKSPACE_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DATASET_ROOT = WORKSPACE_ROOT / "datasets"
JOB_ROOT = Path(
    os.getenv(
        "S4D_JOB_ROOT",
        str(WORKSPACE_ROOT / ".runtime" / "s4d-analysis" / "jobs"),
    )
)
MATPLOT_URL = os.getenv("S4D_MATPLOT_URL", "http://127.0.0.1:8010")
MATPLOT_SUBMIT_CONCURRENCY = max(
    1, int(os.getenv("S4D_MATPLOT_SUBMIT_CONCURRENCY", "9"))
)
SNAPSHOT_ROOT = Path(
    os.getenv(
        "S4D_SNAPSHOT_ROOT",
        str(WORKSPACE_ROOT / ".runtime" / "s4d-analysis" / "snapshots"),
    )
)


@dataclass(frozen=True)
class SubmittedJob:
    remote_jobs: dict[str, str]
    snapshot_id: str
    cell_order: tuple[str, ...]
    columns: int
    rows: int


_submitted_jobs: dict[str, SubmittedJob] = {}
_digest_jobs: dict[str, dict[str, object]] = {}
_digest_lock = threading.Lock()
snapshot_store = SnapshotStore(SNAPSHOT_ROOT)

_voice_model = None
_voice_model_lock = threading.Lock()


def _transcribe_locally(audio: bytes, suffix: str) -> tuple[str, str]:
    """Run a CPU Whisper fallback when the configured cloud service is unavailable."""
    global _voice_model
    try:
        from faster_whisper import WhisperModel
    except ImportError as exc:
        raise RuntimeError(
            "Local speech recognition is not installed (pip install faster-whisper)."
        ) from exc

    with _voice_model_lock:
        if _voice_model is None:
            model_name = os.getenv("VOICE_LOCAL_MODEL", "base")
            _voice_model = WhisperModel(model_name, device="cpu", compute_type="int8")
        model = _voice_model

    temp_path = ""
    try:
        with tempfile.NamedTemporaryFile(delete=False, suffix=suffix or ".wav") as temp_audio:
            temp_audio.write(audio)
            temp_path = temp_audio.name
        segments, _ = model.transcribe(
            temp_path,
            beam_size=5,
            vad_filter=True,
            condition_on_previous_text=False,
        )
        text = " ".join(segment.text.strip() for segment in segments).strip()
        return text, f"faster-whisper/{os.getenv('VOICE_LOCAL_MODEL', 'base')}"
    finally:
        if temp_path:
            try:
                os.unlink(temp_path)
            except OSError:
                pass

app = FastAPI(title="S4D Canvas Analysis Service", version="0.1.0")


@app.post("/speech/transcribe")
async def transcribe_speech(file: UploadFile = File(...)) -> dict[str, str]:
    """Transcribe a short Quest microphone recording.

    Quest headsets do not consistently ship an Android RecognitionService, so
    the XR client records locally and sends a WAV clip here.  Speech is kept
    separate from MatPlot generation and returns only editable task text.
    """
    audio = await file.read()
    if len(audio) < 64:
        raise HTTPException(status_code=400, detail="The audio recording is empty.")
    if len(audio) > 12 * 1024 * 1024:
        raise HTTPException(status_code=413, detail="The audio recording is too large.")

    key = os.getenv("VOICE_OPENAI_API_KEY") or os.getenv("OPENAI_API_KEY")
    base_url = os.getenv("VOICE_OPENAI_BASE_URL", "https://api.openai.com/v1")
    model = os.getenv("VOICE_TRANSCRIBE_MODEL", "gpt-4o-mini-transcribe")
    text = ""
    used_model = model
    cloud_error = ""
    if key and os.getenv("VOICE_LOCAL_FIRST", "0") != "1":
        try:
            from openai import OpenAI

            client = OpenAI(api_key=key, base_url=base_url)
            result = client.audio.transcriptions.create(
                model=model,
                file=(file.filename or "quest-voice.wav", audio, file.content_type or "audio/wav"),
            )
            text = str(getattr(result, "text", "") or "").strip()
        except Exception as exc:
            cloud_error = str(exc)[:240]

    if not text:
        try:
            suffix = Path(file.filename or "quest-voice.wav").suffix or ".wav"
            text, used_model = _transcribe_locally(audio, suffix)
        except Exception as exc:
            detail = f"Local speech recognition failed: {str(exc)[:220]}"
            if cloud_error:
                detail += f" Cloud service also failed: {cloud_error}"
            raise HTTPException(status_code=502, detail=detail) from exc
    if not text:
        raise HTTPException(status_code=422, detail="No speech was recognized.")
    return {"text": text, "model": used_model}


class ValidationRequest(BaseModel):
    verifyHashes: bool = False


_INTENT_RULES: tuple[tuple[str, str, tuple[str, ...]], ...] = (
    (
        "find_anomalies",
        "FIND ANOMALIES",
        (
            "anomaly", "anomalies", "outlier", "outliers", "extreme", "hotspot",
            "异常", "离群", "极值", "热点",
        ),
    ),
    (
        "determine_range",
        "DETERMINE RANGE",
        (
            "range", "minimum", "maximum", "min ", "max ", "bounds",
            "范围", "区间", "最大", "最小", "上下限",
        ),
    ),
    (
        "characterize_trend",
        "CHARACTERIZE TREND",
        (
            "trend", "change over time", "increase", "decrease", "evolution",
            "趋势", "随时间", "变化", "上升", "下降", "演变",
        ),
    ),
    (
        "correlate",
        "CORRELATE",
        (
            "correlation", "correlate", "relationship", "association",
            "相关", "关系", "关联",
        ),
    ),
    (
        "cluster",
        "CLUSTER",
        (
            "cluster", "group similar", "pattern groups",
            "聚类", "分组", "相似模式",
        ),
    ),
    (
        "characterize_distribution",
        "CHARACTERIZE DISTRIBUTION",
        (
            "distribution", "compare", "spatial pattern", "where", "spread",
            "分布", "比较", "空间格局", "哪里", "扩散",
        ),
    ),
)


def resolve_intent_text(request: IntentResolutionRequest) -> IntentResolution:
    """Resolve free text to one validated Amar-style analytic task.

    This deterministic resolver is deliberately kept behind the service API so
    an LLM resolver can replace it later without changing the Unity contract.
    Ambiguous text falls back to distribution comparison and is explicitly
    marked as a fallback for user confirmation.
    """
    text = " ".join(request.text.strip().split())
    empty_input = not text
    if empty_input:
        text = "Compare the distribution across all cells."
    folded = text.casefold()
    matches: list[tuple[int, int, str, str]] = []
    for priority, (task, label, keywords) in enumerate(_INTENT_RULES):
        score = sum(1 for keyword in keywords if keyword.casefold() in folded)
        if score:
            matches.append((score, -priority, task, label))
    if matches:
        # Distribution words such as "compare" and "where" are generic. A
        # sentence such as "compare where the hotspots occur" must retain its
        # more specific anomaly intent even when it contains several generic
        # comparison words.
        specific_matches = [
            match for match in matches
            if match[2] != "characterize_distribution"
        ]
        score, _, task, label = max(specific_matches or matches)
        used_fallback = empty_input
        confidence = 0.45 if empty_input else min(
            0.98, 0.68 + 0.10 * (score - 1)
        )
    else:
        task = "characterize_distribution"
        label = "CHARACTERIZE DISTRIBUTION"
        used_fallback = True
        confidence = 0.45

    variable = request.variableDisplayName or request.variableId or "the selected variable"
    unit_suffix = f" ({request.unit})" if request.unit else ""
    focus = f"{variable}{unit_suffix} across every Time x Depth cell"
    normalized = (
        f"{label.title()}: {text}. Compare all cells with identical spatial "
        "encoding, missing-value handling, and one shared color scale."
    )
    return IntentResolution(
        rawText=text,
        analyticTask=task,
        displayLabel=label,
        focus=focus,
        confidence=confidence,
        usedFallback=used_fallback,
        normalizedInstruction=normalized,
    )


def manifest_registry() -> dict[str, Path]:
    root = Path(os.getenv("S4D_DATASET_ROOT", str(DEFAULT_DATASET_ROOT)))
    registry: dict[str, Path] = {}
    if not root.is_dir():
        return registry
    for path in root.glob("*/manifest.json"):
        try:
            manifest = VolumeManifest.load(path)
        except Exception:
            continue
        registry[manifest.datasetId] = path.resolve()
    return registry


def require_manifest(dataset_id: str) -> Path:
    path = manifest_registry().get(dataset_id)
    if path is None:
        raise HTTPException(status_code=404, detail=f"Unknown dataset: {dataset_id}")
    return path


@app.get("/health")
def health() -> dict[str, object]:
    dataset_root = Path(
        os.getenv("S4D_DATASET_ROOT", str(DEFAULT_DATASET_ROOT))
    ).resolve()
    return {
        "status": "ok",
        "datasets": len(manifest_registry()),
        "matplotAgentRequired": True,
        # These absolute paths deliberately identify the running checkout.  A
        # developer may have several copies of STC on the same PC; without an
        # identity check Unity can silently connect to an older service that
        # happens to own port 8020.
        "workspaceRoot": str(WORKSPACE_ROOT.resolve()),
        "datasetRoot": str(dataset_root),
        "matplotUrl": MATPLOT_URL,
    }


@app.get("/datasets")
def datasets() -> list[dict[str, object]]:
    result = []
    for path in manifest_registry().values():
        manifest = VolumeManifest.load(path)
        result.append(
            {
                "datasetId": manifest.datasetId,
                "datasetVersion": manifest.datasetVersion,
                "variables": list(manifest.variables),
                "dimensions": manifest.dimensions.model_dump(),
                "valueSemantics": {
                    key: value.valueSemantics
                    for key, value in manifest.variables.items()
                },
            }
        )
    return result


@app.get("/datasets/resolve")
def resolve_dataset(
    variable: str = Query(min_length=1),
    x: int = Query(gt=0),
    y: int = Query(gt=0),
    z: int = Query(gt=0),
    time_count: int = Query(alias="timeCount", gt=0),
) -> dict[str, object]:
    """Match Unity's locally opened RAW series to a validated manifest entry."""
    candidates: list[dict[str, object]] = []
    requested = variable.casefold()
    for path in manifest_registry().values():
        manifest = VolumeManifest.load(path)
        if (
            manifest.dimensions.x != x
            or manifest.dimensions.y != y
            or manifest.dimensions.z != z
        ):
            continue
        for variable_id, series in manifest.variables.items():
            if len(series.frames) != time_count:
                continue
            if requested not in {
                variable_id.casefold(),
                series.displayName.casefold(),
            }:
                continue
            candidates.append(
                {
                    "datasetId": manifest.datasetId,
                    "datasetVersion": manifest.datasetVersion,
                    "variableId": variable_id,
                    "displayName": series.displayName,
                    "unit": series.unit,
                    "valueSemantics": series.valueSemantics,
                }
            )
    if not candidates:
        raise HTTPException(
            status_code=404,
            detail=(
                f"No validated manifest matches variable={variable!r}, "
                f"shape={x}x{y}x{z}, timeCount={time_count}"
            ),
        )
    if len(candidates) > 1:
        raise HTTPException(
            status_code=409,
            detail={"message": "Dataset match is ambiguous", "candidates": candidates},
        )
    return candidates[0]


@app.get("/datasets/{dataset_id}/manifest")
def dataset_manifest(dataset_id: str) -> dict[str, object]:
    return VolumeManifest.load(require_manifest(dataset_id)).model_dump(mode="json")


@app.post("/datasets/{dataset_id}/validate")
def validate_dataset(
    dataset_id: str,
    request: ValidationRequest,
) -> dict[str, object]:
    report = validate_manifest_files(
        require_manifest(dataset_id),
        verify_hashes=request.verifyHashes,
    )
    return report.model_dump()


@app.post("/analysis/resolve-intent", response_model=IntentResolution)
def resolve_analysis_intent(
    request: IntentResolutionRequest,
) -> IntentResolution:
    return resolve_intent_text(request)


@app.post("/analysis/prepare-matplot-job")
def prepare_matplot_job(request: FacetGridRequest) -> dict[str, object]:
    """Build the numeric grid and one mandatory grid-level MatPlotAgent package."""
    manifest_path = require_manifest(request.datasetId)
    try:
        result = RawVolumeReader(manifest_path).materialize_grid(request)
        job_id = uuid.uuid4().hex
        package = build_matplotagent_grid_package(JOB_ROOT / job_id, request, result)
    except (KeyError, OSError, ValueError) as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return {
        "jobId": job_id,
        "status": "prepared_for_matplotagent",
        "matplotAgentRequired": True,
        "dataCsv": str(package.data_csv),
        "contractJson": str(package.contract_json),
        "promptText": str(package.prompt_txt),
        "sharedScale": {
            "minimum": result.shared_minimum,
            "maximum": result.shared_maximum,
            "unit": result.unit,
        },
        "cells": [
            {
                "cellId": cell.cell_id,
                "validFraction": cell.valid_fraction,
                "framesUsed": list(cell.frames_used),
                "depthIndices": list(cell.depth_indices),
                "variableId": cell.variable_id or request.variableId,
            }
            for cell in result.cells
        ],
    }


@app.post("/analysis/preview-atlas")
def preview_atlas(request: FacetGridRequest) -> Response:
    """Return the same interval means as the final Grid, without invoking MatPlotAgent."""
    manifest_path = require_manifest(request.datasetId)
    try:
        result = RawVolumeReader(manifest_path).materialize_grid(request)
        content = render_preview_atlas(
            result,
            column_count=len(request.timeBuckets),
            row_count=len(request.depthBuckets),
        )
    except (KeyError, OSError, ValueError) as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return Response(
        content=content,
        media_type="image/png",
        headers={
            "X-S4D-Aggregation": "valid-value-mean",
            "X-S4D-Scale-Min": str(result.shared_minimum),
            "X-S4D-Scale-Max": str(result.shared_maximum),
        },
    )


@app.post("/analysis/materialize")
def materialize(request: FacetGridRequest) -> dict[str, object]:
    """Prepare the Grid and submit bounded, independent MatPlotAgent cells."""
    manifest_path = require_manifest(request.datasetId)
    local_job_id = uuid.uuid4().hex
    try:
        result = RawVolumeReader(manifest_path).materialize_grid(request)
        packages = build_matplotagent_cell_packages(
            JOB_ROOT / local_job_id, request, result
        )
        # Submit every independent Facet cell together. MatPlotAgent applies its
        # own bounded generation semaphore, so this removes the avoidable
        # serial upload/start delay without letting Unity or Quest own nine
        # heavyweight generation tasks.
        package_items = list(packages.items())

        def submit_cell(
            item: tuple[str, object],
        ) -> tuple[str, object]:
            cell_id, package = item
            submitted = MatPlotAgentGateway(MATPLOT_URL).submit(package)
            return cell_id, submitted

        with ThreadPoolExecutor(
            max_workers=min(MATPLOT_SUBMIT_CONCURRENCY, len(package_items))
        ) as executor:
            submitted_cells = dict(executor.map(submit_cell, package_items))
    except httpx.HTTPError as exc:
        raise HTTPException(
            status_code=503,
            detail=f"MatPlotAgent is unavailable or rejected the Grid: {exc}",
        ) from exc
    except (KeyError, OSError, RuntimeError, ValueError) as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    snapshot_id = uuid.uuid4().hex
    snapshot_store.create(
        snapshot_id,
        manifest_path,
        request,
        result,
        local_job_id,
        ",".join(job.job_id for job in submitted_cells.values()),
    )
    remote_jobs = {
        cell_id: submitted.job_id
        for cell_id, submitted in submitted_cells.items()
    }
    cell_order = tuple(packages)
    job = SubmittedJob(
        remote_jobs=remote_jobs,
        snapshot_id=snapshot_id,
        cell_order=cell_order,
        columns=len(request.timeBuckets),
        rows=len(request.depthBuckets),
    )
    _submitted_jobs[local_job_id] = job
    job_path = JOB_ROOT / local_job_id / "s4d_job.json"
    job_path.parent.mkdir(parents=True, exist_ok=True)
    job_path.write_text(
        json.dumps(
            {
                "jobId": local_job_id,
                "remoteJobs": remote_jobs,
                "snapshotId": snapshot_id,
                "cellOrder": cell_order,
                "columns": len(request.timeBuckets),
                "rows": len(request.depthBuckets),
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    return {
        "jobId": local_job_id,
        "matplotAgentJobId": next(iter(remote_jobs.values()), ""),
        "matplotAgentJobIds": remote_jobs,
        "snapshotId": snapshot_id,
        "status": "submitted_cells_to_matplotagent",
        "statusUrl": f"/jobs/{local_job_id}",
        "sharedScale": {
            "minimum": result.shared_minimum,
            "maximum": result.shared_maximum,
            "unit": result.unit,
        },
    }


def require_submitted_job(job_id: str) -> SubmittedJob:
    submitted = _submitted_jobs.get(job_id)
    if submitted is not None:
        return submitted
    job_path = JOB_ROOT / job_id / "s4d_job.json"
    if job_path.is_file():
        payload = json.loads(job_path.read_text(encoding="utf-8"))
        remote_jobs = payload.get("remoteJobs")
        if not remote_jobs and payload.get("remoteJobId"):
            remote_jobs = {"grid": str(payload["remoteJobId"])}
        submitted = SubmittedJob(
            remote_jobs={
                str(cell_id): str(remote_id)
                for cell_id, remote_id in dict(remote_jobs or {}).items()
            },
            snapshot_id=str(payload["snapshotId"]),
            cell_order=tuple(payload.get("cellOrder", remote_jobs or {"grid": ""})),
            columns=int(payload.get("columns", 1)),
            rows=int(payload.get("rows", 1)),
        )
        _submitted_jobs[job_id] = submitted
        return submitted
    if submitted is None:
        raise HTTPException(status_code=404, detail=f"Unknown analysis job: {job_id}")
    return submitted


@app.get("/jobs/{job_id}")
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
        snapshot_store.set_status(submitted.snapshot_id, "completed")
    elif any(status == "failed" for status in statuses):
        status = "failed"
        errors = [
            str(remote.get("error", "MatPlotAgent cell generation failed"))
            for remote in remote_cells.values()
            if str(remote.get("status", "")).lower() == "failed"
        ]
        snapshot_store.set_status(
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


@app.get("/jobs/{job_id}/panel")
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


@app.get("/jobs/{job_id}/cells/{cell_id}/panel")
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


@app.get("/jobs/{job_id}/chart-result")
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
    snapshot = snapshot_store.get(submitted.snapshot_id)
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
    with _digest_lock:
        _digest_jobs[digest_job_id].update(values)


def _digest_grid_montage(job_id: str) -> bytes | None:
    submitted = _submitted_jobs.get(job_id)
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
        snapshot_metadata = snapshot_store.get(snapshot_id)
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


@app.post("/jobs/{job_id}/digest", status_code=202)
def create_digest_job(job_id: str) -> dict[str, object]:
    submitted = require_submitted_job(job_id)
    digest_job_id = uuid.uuid4().hex
    state: dict[str, object] = {
        "digestJobId": digest_job_id,
        "s4dJobId": job_id,
        "snapshotId": submitted.snapshot_id,
        "status": "queued",
        "stage": "queued",
        "progress": 0.0,
        "statusUrl": f"/digest-jobs/{digest_job_id}",
    }
    with _digest_lock:
        _digest_jobs[digest_job_id] = state
    threading.Thread(
        target=_run_digest_job,
        args=(digest_job_id, submitted.snapshot_id, job_id),
        daemon=True,
    ).start()
    return dict(state)


@app.get("/digest-jobs/{digest_job_id}")
def digest_job_status(digest_job_id: str) -> dict[str, object]:
    with _digest_lock:
        state = _digest_jobs.get(digest_job_id)
        if state is None:
            raise HTTPException(status_code=404, detail="Unknown Digest job")
        return dict(state)


@app.get("/snapshots/{snapshot_id}")
def snapshot(snapshot_id: str) -> dict[str, object]:
    try:
        return snapshot_store.get(snapshot_id)
    except FileNotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc


@app.get("/snapshots/{snapshot_id}/cells/{cell_id}/aggregate-volume")
def snapshot_aggregate_volume(snapshot_id: str, cell_id: str) -> Response:
    try:
        values, cell = snapshot_store.aggregate_volume(snapshot_id, cell_id)
    except FileNotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except (KeyError, OSError, ValueError) as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc
    finite = values[np.isfinite(values)]
    if finite.size == 0:
        raise HTTPException(status_code=422, detail="Ground volume contains no valid values")
    contiguous = np.ascontiguousarray(values, dtype="<f4")
    valid = np.isfinite(contiguous)
    reconstructed_sum = np.where(valid, contiguous, 0.0).sum(
        axis=0, dtype=np.float64
    )
    reconstructed_count = valid.sum(axis=0)
    reconstructed = np.full(reconstructed_count.shape, np.nan, dtype=np.float64)
    np.divide(
        reconstructed_sum,
        reconstructed_count,
        out=reconstructed,
        where=reconstructed_count > 0,
    )
    reconstructed_finite = reconstructed[np.isfinite(reconstructed)]
    z, y, x = contiguous.shape
    return Response(
        content=contiguous.tobytes(order="C"),
        media_type="application/vnd.s4d.volume-f32",
        headers={
            "X-S4D-Dim-X": str(x),
            "X-S4D-Dim-Y": str(y),
            "X-S4D-Dim-Z": str(z),
            "X-S4D-Depth-Indices": ",".join(
                str(index) for index in cell["depthIndices"]
            ),
            "X-S4D-Min": str(float(finite.min())),
            "X-S4D-Mean": str(float(finite.mean())),
            "X-S4D-Cell-Mean": str(float(cell.get("mean", 0.0))),
            "X-S4D-Reconstructed-Mean": str(
                float(reconstructed_finite.mean())
                if reconstructed_finite.size else 0.0
            ),
            "X-S4D-Max": str(float(finite.max())),
            "X-S4D-Valid-Fraction": str(
                float(cell.get("groundValidFraction", cell.get("validFraction", 0.0)))
            ),
            "X-S4D-Missing": "NaN",
        },
    )
