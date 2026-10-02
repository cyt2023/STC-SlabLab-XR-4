"""Snapshot endpoints: metadata and aggregate volumes."""
from __future__ import annotations


import numpy as np
from fastapi import APIRouter, HTTPException, Response

from .. import state

router = APIRouter()

@router.get("/snapshots/{snapshot_id}")
def snapshot(snapshot_id: str) -> dict[str, object]:
    try:
        return state.snapshot_store.get(snapshot_id)
    except FileNotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc


@router.get("/snapshots/{snapshot_id}/cells/{cell_id}/aggregate-volume")
def snapshot_aggregate_volume(snapshot_id: str, cell_id: str) -> Response:
    try:
        values, cell = state.snapshot_store.aggregate_volume(snapshot_id, cell_id)
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
