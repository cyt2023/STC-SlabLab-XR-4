"""Dataset matching helpers shared by the dataset and speech routers."""
from __future__ import annotations

from datetime import datetime
import json
import os
import re
from pathlib import Path

def _normalize_match_path(value: str | Path | None) -> str:
    if value is None:
        return ""
    return str(value).replace("\\", "/").rstrip("/").casefold()


def _path_contains(container: str, target: str) -> bool:
    """True when target is container itself or lives below it.

    Segment-aware on purpose: ``wave-imports/wave_live_x`` must not be treated
    as living inside ``wave-imports/wave_x`` just because the text overlaps.
    """
    if not container or not target:
        return False
    return target == container or target.startswith(container + "/")


def _version_sort_key(version: str) -> tuple[int, str]:
    """Sort ISO timestamps ahead of opaque build labels, newest last."""
    text = (version or "").strip()
    if text:
        try:
            return (1, datetime.fromisoformat(text.replace("Z", "+00:00")).isoformat())
        except ValueError:
            pass
    return (0, text)


def _source_match_score(
    manifest_path: Path,
    frame_path: str,
    requested_source_root: str,
    requested_source_path: str,
) -> int:
    """Score how well one manifest answers the caller's local dataset path.

    Unity opens a RAW folder that sits inside the dataset directory, such as
    ``<dataset>/UnityRaw/Wave_HS``. Re-running a Wave import leaves an older
    copy of the same window beside the fresh one, so shape, variable name and
    frame count are all identical and the directory relationship is the only
    reliable discriminator.
    """
    manifest_root = _normalize_match_path(manifest_path.parent)
    resolved_frame = _normalize_match_path(manifest_path.parent / frame_path)
    frame_directory = _normalize_match_path(
        (manifest_path.parent / frame_path).parent
    )
    score = 0
    if requested_source_path:
        if resolved_frame == requested_source_path:
            score = max(score, 200)
        elif _path_contains(manifest_root, requested_source_path):
            score = max(score, 180)
        elif _path_contains(frame_directory, requested_source_path):
            score = max(score, 170)
        elif _normalize_match_path(Path(requested_source_path).parent) == frame_directory:
            score = max(score, 160)
        else:
            requested_name = requested_source_path.rsplit("/", 1)[-1]
            frame_name = resolved_frame.rsplit("/", 1)[-1]
            if requested_name and requested_name == frame_name:
                score = max(score, 10)
    if requested_source_root:
        if manifest_root == requested_source_root:
            score = max(score, 120)
        elif _path_contains(manifest_root, requested_source_root):
            score = max(score, 110)
        elif _path_contains(requested_source_root, manifest_root):
            score = max(score, 100)
        else:
            root_name = requested_source_root.rsplit("/", 1)[-1]
            manifest_name = manifest_root.rsplit("/", 1)[-1]
            if root_name and root_name == manifest_name:
                score = max(score, 10)
    return score
