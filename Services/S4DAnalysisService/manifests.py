"""Manifest discovery shared by the service routers."""
from __future__ import annotations


import os
from pathlib import Path

from fastapi import HTTPException

from .config import DEFAULT_DATASET_ROOT, WAVE_IMPORT_ROOT
from .models import VolumeManifest

_UNREADABLE_MANIFESTS: list[str] = []


def manifest_registry() -> dict[str, Path]:
    global _UNREADABLE_MANIFESTS
    root = Path(os.getenv("S4D_DATASET_ROOT", str(DEFAULT_DATASET_ROOT)))
    registry: dict[str, Path] = {}
    paths = list(root.glob("*/manifest.json")) if root.is_dir() else []
    if WAVE_IMPORT_ROOT.is_dir():
        paths.extend(WAVE_IMPORT_ROOT.glob("*/manifest.json"))
    skipped: list[str] = []
    for path in paths:
        try:
            manifest = VolumeManifest.load(path)
        except Exception as exc:
            skipped.append(f"{path.parent.name}: {exc}")
            continue
        registry[manifest.datasetId] = path.resolve()
    _UNREADABLE_MANIFESTS = skipped
    return registry


def require_manifest(dataset_id: str) -> Path:
    path = manifest_registry().get(dataset_id)
    if path is None:
        raise HTTPException(status_code=404, detail=f"Unknown dataset: {dataset_id}")
    return path



def unreadable_manifests() -> list[str]:
    """Manifests skipped by the most recent registry scan."""
    return _UNREADABLE_MANIFESTS
