"""Wave API gateway endpoints: register, import and stream frame patches."""
from __future__ import annotations


from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..config import WAVE_IMPORT_ROOT, WORKSPACE_ROOT
from ..wave_importer import WaveDatasetImporter, WaveImportError

router = APIRouter()

class WaveImportRequest(BaseModel):
    start: str
    end: str
    fields: list[str] = ["hs", "elev"]


class WaveFrameBatchRequest(BaseModel):
    time: str
    field: str
    nodeIds: list[int]


class WaveTimelineBatchRequest(BaseModel):
    start: str
    end: str
    field: str
    nodeIds: list[int]

@router.get("/wave/status")
def wave_status() -> dict[str, object]:
    """Report Wave API configuration without returning the API key."""
    try:
        return WaveDatasetImporter(WORKSPACE_ROOT, WAVE_IMPORT_ROOT).status()
    except WaveImportError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc


@router.post("/wave/import")
def import_wave_dataset(request: WaveImportRequest) -> dict[str, object]:
    """Download a Wave API subset and convert it into the app's native format."""
    try:
        return WaveDatasetImporter(WORKSPACE_ROOT, WAVE_IMPORT_ROOT).import_dataset(
            request.start,
            request.end,
            request.fields,
        )
    except WaveImportError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.post("/wave/live-dataset")
def prepare_live_wave_dataset(request: WaveImportRequest) -> dict[str, object]:
    """Register a lazy Wave dataset without downloading any field frames."""
    try:
        return WaveDatasetImporter(
            WORKSPACE_ROOT, WAVE_IMPORT_ROOT
        ).prepare_live_dataset(request.start, request.end, request.fields)
    except WaveImportError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.post("/wave/frame-batch")
def fetch_live_wave_frame_batch(
    request: WaveFrameBatchRequest,
) -> dict[str, object]:
    """Return one visible-frame patch of at most 100 geographic nodes."""
    try:
        return WaveDatasetImporter(
            WORKSPACE_ROOT, WAVE_IMPORT_ROOT
        ).fetch_frame_batch(request.time, request.field, request.nodeIds)
    except WaveImportError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.post("/wave/timeline-batch")
def fetch_live_wave_timeline_batch(
    request: WaveTimelineBatchRequest,
) -> dict[str, object]:
    """Return one node patch across the visible live timeline."""
    try:
        return WaveDatasetImporter(
            WORKSPACE_ROOT, WAVE_IMPORT_ROOT
        ).fetch_timeline_batch(
            request.start, request.end, request.field, request.nodeIds
        )
    except WaveImportError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
