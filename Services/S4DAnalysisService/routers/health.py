"""Service health and identity."""
from __future__ import annotations


import os
from pathlib import Path

from fastapi import APIRouter

from ..config import (
    DEFAULT_DATASET_ROOT,
    MATPLOT_URL,
    SERVICE_CODE_MODIFIED_AT,
    SERVICE_STARTED_AT,
    WORKSPACE_ROOT,
)
from ..manifests import manifest_registry, unreadable_manifests
from ..wave_importer import WaveDatasetImporter, wave_mode
from ..config import WAVE_IMPORT_ROOT

router = APIRouter()

@router.get("/health")
def health() -> dict[str, object]:
    dataset_root = Path(
        os.getenv("S4D_DATASET_ROOT", str(DEFAULT_DATASET_ROOT))
    ).resolve()
    importer = WaveDatasetImporter(WORKSPACE_ROOT, WAVE_IMPORT_ROOT)
    bundled_windows = importer.bundled_windows()
    mode = wave_mode()
    if mode == "unknown":
        # Nothing has answered yet. Report what will answer instead of probing
        # the gateway here: the supervisor polls this endpoint every five
        # seconds and a probe would block that poll.
        mode = "cache" if bundled_windows else (
            "live" if os.getenv("WAVE_API_KEY", "").strip() else "offline"
        )
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
        # Compare codeModifiedAt with the file timestamps on disk: if the code
        # changed after startedAt, this process is running an older build and
        # should be restarted (Start-Backend.sh does that automatically when the
        # pid file no longer matches the listener).
        "startedAt": SERVICE_STARTED_AT,
        "codeModifiedAt": SERVICE_CODE_MODIFIED_AT,
        "unreadableManifests": list(unreadable_manifests()),
        # Which data source answers: the live Wave gateway, a bundled hydrated
        # dataset, or nothing (offline with no coverage).
        "waveMode": mode,
        "waveCache": bundled_windows,
    }
