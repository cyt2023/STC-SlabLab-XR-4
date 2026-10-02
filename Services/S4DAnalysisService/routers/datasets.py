"""Dataset listing, resolution, manifest and validation endpoints."""
from __future__ import annotations



from fastapi import APIRouter, HTTPException, Query

from ..matching import (
    _normalize_match_path,
    _source_match_score,
    _version_sort_key,
)
from ..manifests import manifest_registry, require_manifest
from ..models import ValidationRequest, VolumeManifest
from ..raw_reader import validate_manifest_files

router = APIRouter()

@router.get("/datasets")
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


@router.get("/datasets/resolve")
def resolve_dataset(
    variable: str = Query(min_length=1),
    x: int = Query(gt=0),
    y: int = Query(gt=0),
    z: int = Query(gt=0),
    time_count: int = Query(alias="timeCount", gt=0),
    source_root: str | None = Query(default=None, alias="sourceRoot"),
    source_path: str | None = Query(default=None, alias="sourcePath"),
) -> dict[str, object]:
    """Match Unity's locally opened RAW series to a validated manifest entry."""
    candidates: list[dict[str, object]] = []
    requested = variable.casefold()
    requested_source_root = _normalize_match_path(source_root)
    requested_source_path = _normalize_match_path(source_path)
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
                    "_version": manifest.datasetVersion,
                    "_score": _source_match_score(
                        path,
                        series.frames[0].path if series.frames else "",
                        requested_source_root,
                        requested_source_path,
                    ),
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
        best_score = max(int(candidate["_score"]) for candidate in candidates)
        best = [
            candidate for candidate in candidates
            if int(candidate["_score"]) == best_score
        ]
        if len(best) > 1:
            # Re-running a Wave import leaves an older copy of the same window
            # beside the fresh one. Both match on shape and variable, so take
            # the most recent manifest rather than stalling the review stage
            # with an ambiguity error.
            newest_key = max(
                _version_sort_key(str(candidate["_version"]))
                for candidate in best
            )
            newest = [
                candidate for candidate in best
                if _version_sort_key(str(candidate["_version"])) == newest_key
            ]
            if len(newest) == 1:
                best = newest
        if len(best) == 1:
            match = dict(best[0])
            match.pop("_score", None)
            match.pop("_version", None)
            return match
        for candidate in candidates:
            candidate.pop("_score", None)
            candidate.pop("_version", None)
        raise HTTPException(
            status_code=409,
            detail={"message": "Dataset match is ambiguous", "candidates": candidates},
        )
    match = dict(candidates[0])
    match.pop("_score", None)
    match.pop("_version", None)
    return match


@router.get("/datasets/{dataset_id}/manifest")
def dataset_manifest(dataset_id: str) -> dict[str, object]:
    return VolumeManifest.load(require_manifest(dataset_id)).model_dump(mode="json")


@router.post("/datasets/{dataset_id}/validate")
def validate_dataset(
    dataset_id: str,
    request: ValidationRequest,
) -> dict[str, object]:
    report = validate_manifest_files(
        require_manifest(dataset_id),
        verify_hashes=request.verifyHashes,
    )
    return report.model_dump()
