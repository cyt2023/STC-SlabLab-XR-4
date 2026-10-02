"""Mutable service state shared by the routers.

These containers are imported (not copied) so every router mutates the same
objects the application was built with.
"""

from __future__ import annotations

import threading
from dataclasses import dataclass

from .config import SNAPSHOT_ROOT
from .snapshot_store import SnapshotStore

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
