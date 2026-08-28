from __future__ import annotations

import hashlib
import json
import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np

from .models import FacetGridRequest, VolumeManifest
from .raw_reader import GridNumericResult, RawVolumeReader


class SnapshotStore:
    """Durable, immutable analysis metadata plus lazy verified Ground volumes."""

    def __init__(self, root: str | Path):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def create(
        self,
        snapshot_id: str,
        manifest_path: Path,
        request: FacetGridRequest,
        result: GridNumericResult,
        local_job_id: str,
        remote_job_id: str,
    ) -> dict[str, Any]:
        destination = self.root / snapshot_id
        if destination.exists():
            raise ValueError(f"snapshot already exists: {snapshot_id}")
        destination.mkdir(parents=True)
        manifest_bytes = manifest_path.read_bytes()
        manifest = VolumeManifest.model_validate_json(manifest_bytes)
        metadata: dict[str, Any] = {
            "snapshotId": snapshot_id,
            "status": "pending",
            "createdAt": datetime.now(timezone.utc).isoformat(),
            "datasetId": manifest.datasetId,
            "datasetVersion": manifest.datasetVersion,
            "variableId": request.variableId,
            "manifestPath": str(manifest_path.resolve()),
            "manifestSha256": hashlib.sha256(manifest_bytes).hexdigest(),
            "request": request.model_dump(mode="json"),
            "transform": {
                "timeAggregation": "valid-value arithmetic mean; equal frame weight",
                "depthAggregation": "valid-value arithmetic mean; equal layer weight",
                "missingPolicy": request.missingPolicy,
                "scalePolicy": request.scalePolicy,
            },
            "sharedScale": {
                "minimum": result.shared_minimum,
                "maximum": result.shared_maximum,
                "unit": result.unit,
            },
            "jobs": {
                "s4dJobId": local_job_id,
                "matPlotAgentJobId": remote_job_id,
            },
            "sourceFiles": self._source_fingerprints(manifest_path, manifest, request),
            "cells": [
                {
                    "cellId": cell.cell_id,
                    "variableId": cell.variable_id or request.variableId,
                    "validFraction": cell.valid_fraction,
                    "framesUsed": list(cell.frames_used),
                    "depthIndices": list(cell.depth_indices),
                    **self._cell_statistics(cell.values),
                }
                for cell in result.cells
            ],
        }
        (destination / "manifest.json").write_bytes(manifest_bytes)
        self._atomic_json(destination / "snapshot.json", metadata)
        return metadata

    @staticmethod
    def _cell_statistics(values: np.ndarray) -> dict[str, float | int | bool]:
        finite = values[np.isfinite(values)]
        if finite.size == 0:
            # Keep numeric fields for older Unity clients, but explicitly mark
            # the cell as empty.  Treating an empty footprint as a real zero
            # made it eligible for "LOWEST" and produced unsupported findings.
            return {
                "minimum": 0.0,
                "mean": 0.0,
                "maximum": 0.0,
                "validCount": 0,
                "hasData": False,
            }
        return {
            "minimum": float(finite.min()),
            "mean": float(finite.mean()),
            "maximum": float(finite.max()),
            "validCount": int(finite.size),
            "hasData": True,
        }

    def get(self, snapshot_id: str) -> dict[str, Any]:
        path = self.root / snapshot_id / "snapshot.json"
        if not path.is_file():
            raise FileNotFoundError(f"unknown snapshot: {snapshot_id}")
        return json.loads(path.read_text(encoding="utf-8"))

    def set_status(self, snapshot_id: str, status: str, error: str | None = None) -> None:
        metadata = self.get(snapshot_id)
        metadata["status"] = status
        metadata["updatedAt"] = datetime.now(timezone.utc).isoformat()
        if error:
            metadata["error"] = error
        elif "error" in metadata:
            del metadata["error"]
        self._atomic_json(self.root / snapshot_id / "snapshot.json", metadata)

    def aggregate_volume(
        self,
        snapshot_id: str,
        cell_id: str,
    ) -> tuple[np.ndarray, dict[str, Any]]:
        metadata = self.get(snapshot_id)
        if metadata.get("status") != "completed":
            raise ValueError(
                f"snapshot {snapshot_id} is not completed: {metadata.get('status')}"
            )
        cell = next(
            (item for item in metadata["cells"] if item["cellId"] == cell_id),
            None,
        )
        if cell is None:
            raise ValueError(f"unknown cell in snapshot {snapshot_id}: {cell_id}")

        cache_path = self.root / snapshot_id / "ground" / f"{cell_id}.npy"
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        if cache_path.is_file():
            return np.load(cache_path, allow_pickle=False), cell

        manifest_path = Path(metadata["manifestPath"])
        self._verify_sources(metadata, manifest_path)
        request = FacetGridRequest.model_validate(metadata["request"])
        volume = RawVolumeReader(manifest_path).materialize_aggregate_volume(
            request, cell_id
        )
        with tempfile.NamedTemporaryFile(
            dir=cache_path.parent, suffix=".npy", delete=False
        ) as stream:
            temporary = Path(stream.name)
            np.save(stream, volume.values, allow_pickle=False)
        os.replace(temporary, cache_path)
        cell["groundValidFraction"] = volume.valid_fraction
        self._atomic_json(
            self.root / snapshot_id / "snapshot.json",
            metadata,
        )
        return volume.values, cell

    @staticmethod
    def _source_fingerprints(
        manifest_path: Path,
        manifest: VolumeManifest,
        request: FacetGridRequest,
    ) -> list[dict[str, Any]]:
        used = {
            index
            for bucket in request.timeBuckets
            for index in bucket.indices
        }
        result = []
        variable_ids = {
            bucket.variableId or request.variableId
            for bucket in request.depthBuckets
        }
        for variable_id in sorted(variable_ids):
            variable = manifest.variables[variable_id]
            for frame in variable.frames:
                if frame.timeIndex not in used:
                    continue
                path = (manifest_path.parent / frame.path).resolve()
                stat = path.stat()
                result.append(
                    {
                        "variableId": variable_id,
                        "frameId": frame.frameId,
                        "timeIndex": frame.timeIndex,
                        "path": str(path),
                        "size": stat.st_size,
                        "modifiedNs": stat.st_mtime_ns,
                        "declaredSha256": frame.sha256,
                    }
                )
        return result

    @staticmethod
    def _verify_sources(metadata: dict[str, Any], manifest_path: Path) -> None:
        manifest_bytes = manifest_path.read_bytes()
        actual_manifest = hashlib.sha256(manifest_bytes).hexdigest()
        if actual_manifest != metadata["manifestSha256"]:
            raise ValueError(
                "snapshot source manifest changed; refusing to Ground old results "
                "against new metadata"
            )
        for source in metadata["sourceFiles"]:
            path = Path(source["path"])
            if not path.is_file():
                raise ValueError(f"snapshot source file is missing: {path}")
            stat = path.stat()
            if stat.st_size != source["size"] or stat.st_mtime_ns != source["modifiedNs"]:
                raise ValueError(
                    f"snapshot source file changed; refusing stale Ground: {path}"
                )

    @staticmethod
    def _atomic_json(path: Path, value: dict[str, Any]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix(path.suffix + ".tmp")
        temporary.write_text(
            json.dumps(value, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        os.replace(temporary, path)
