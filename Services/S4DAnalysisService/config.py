"""Configuration and process identity for the S4D analysis service."""
from __future__ import annotations


import os
from datetime import datetime
from pathlib import Path

WORKSPACE_ROOT = Path(__file__).resolve().parents[2]
# Running a service whose code on disk is newer than the process is the failure
# mode that has bitten this project more than once ("why is it still doing the
# old thing?"). Report both stamps so an operator can see it at a glance.
SERVICE_STARTED_AT = datetime.utcnow().isoformat(timespec="seconds") + "Z"


def _service_code_modified_at() -> str:
    latest = 0.0
    for path in Path(__file__).resolve().parent.rglob("*.py"):
        try:
            latest = max(latest, path.stat().st_mtime)
        except OSError:
            continue
    if latest <= 0.0:
        return ""
    return datetime.utcfromtimestamp(latest).isoformat(timespec="seconds") + "Z"


SERVICE_CODE_MODIFIED_AT = _service_code_modified_at()
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
WAVE_IMPORT_ROOT = Path(
    os.getenv(
        "S4D_WAVE_IMPORT_ROOT",
        str(WORKSPACE_ROOT / ".runtime" / "wave-imports"),
    )
)
