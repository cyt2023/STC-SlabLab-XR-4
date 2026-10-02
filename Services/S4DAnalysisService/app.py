from __future__ import annotations

import os
import tempfile
import uuid
import json
import threading
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
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
from .wave_importer import WaveDatasetImporter, WaveImportError

from .config import (
    DEFAULT_DATASET_ROOT,
    JOB_ROOT,
    MATPLOT_SUBMIT_CONCURRENCY,
    MATPLOT_URL,
    SERVICE_CODE_MODIFIED_AT,
    SERVICE_STARTED_AT,
    SNAPSHOT_ROOT,
    WAVE_IMPORT_ROOT,
    WORKSPACE_ROOT,
)
from .manifests import manifest_registry, require_manifest
from . import state
from .matching import (
    _normalize_match_path,
    _path_contains,
    _source_match_score,
    _version_sort_key,
)
from .routers import datasets as dataset_routes
from .routers import snapshots as snapshot_routes
from .verification import _verify_dataset_frames
from .routers import health as health_routes
from .routers import wave as wave_routes
from .routers import analysis as analysis_routes
from .routers import jobs as job_routes
from .routers import speech as speech_routes





app = FastAPI(title="S4D Canvas Analysis Service", version="0.1.0")

app.include_router(dataset_routes.router)
app.include_router(snapshot_routes.router)

app.include_router(health_routes.router)
app.include_router(wave_routes.router)
app.include_router(speech_routes.router)
app.include_router(analysis_routes.router)
app.include_router(job_routes.router)
