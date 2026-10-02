"""Analysis intent, previews, job preparation and materialization."""

from __future__ import annotations

import uuid
import json
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import httpx
from fastapi import APIRouter, HTTPException, Response

from ..config import (
    JOB_ROOT,
    MATPLOT_SUBMIT_CONCURRENCY,
    MATPLOT_URL,
    WAVE_IMPORT_ROOT,
    WORKSPACE_ROOT,
)
from ..intent import resolve_intent_text
from ..manifests import require_manifest
from ..matplot_contract import (
    build_matplotagent_cell_packages,
    build_matplotagent_grid_package,
)
from ..matplot_gateway import MatPlotAgentGateway
from ..models import FacetGridRequest, IntentResolution, IntentResolutionRequest
from ..preview_renderer import render_preview_atlas
from ..raw_reader import RawVolumeReader
from .. import state
from ..verification import _verify_dataset_frames
from ..wave_importer import WaveDatasetImporter, WaveImportError

router = APIRouter()

def require_analysis_manifest(dataset_id: str) -> Path:
    """Resolve a dataset and make sure its frames are readable from disk.

    Live Wave imports keep their frames as remote, on-demand values, so the
    local RAW files start out as zero-filled placeholders. Reading them would
    reject every Grid with "no valid values", so they are hydrated first.
    """
    manifest_path = require_manifest(dataset_id)
    try:
        WaveDatasetImporter(WORKSPACE_ROOT, WAVE_IMPORT_ROOT).hydrate_dataset(
            manifest_path.parent
        )
    except WaveImportError as exc:
        raise HTTPException(
            status_code=422,
            detail=f"Live Wave frames are unavailable: {exc}",
        ) from exc
    _verify_dataset_frames(manifest_path, dataset_id)
    return manifest_path




@router.post("/analysis/resolve-intent", response_model=IntentResolution)
def resolve_analysis_intent(
    request: IntentResolutionRequest,
) -> IntentResolution:
    return resolve_intent_text(request)


@router.post("/analysis/prepare-matplot-job")
def prepare_matplot_job(request: FacetGridRequest) -> dict[str, object]:
    """Build the numeric grid and one mandatory grid-level MatPlotAgent package."""
    manifest_path = require_analysis_manifest(request.datasetId)
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


@router.post("/analysis/preview-atlas")
def preview_atlas(request: FacetGridRequest) -> Response:
    """Return the same interval means as the final Grid, without invoking MatPlotAgent."""
    manifest_path = require_analysis_manifest(request.datasetId)
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


@router.post("/analysis/materialize")
def materialize(request: FacetGridRequest) -> dict[str, object]:
    """Prepare the Grid and submit bounded, independent MatPlotAgent cells."""
    manifest_path = require_analysis_manifest(request.datasetId)
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
    state.snapshot_store.create(
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
    job = state.SubmittedJob(
        remote_jobs=remote_jobs,
        snapshot_id=snapshot_id,
        cell_order=cell_order,
        columns=len(request.timeBuckets),
        rows=len(request.depthBuckets),
    )
    state._submitted_jobs[local_job_id] = job
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
