"""Frame-level dataset verification used before analysis jobs start."""
from __future__ import annotations


from pathlib import Path

from fastapi import HTTPException

from .models import VolumeManifest

def _verify_dataset_frames(manifest_path: Path, dataset_id: str) -> None:
    """Fail early, and actionably, when a dataset's frames are not on disk.

    RAW folders can be moved, deleted or only partially copied between runs.
    Without this check the reader raises deep inside the Grid build with a
    message that does not say which dataset or which frame is at fault.
    """
    try:
        manifest = VolumeManifest.load(manifest_path)
    except Exception as exc:
        raise HTTPException(
            status_code=422,
            detail=f"Dataset {dataset_id} has an unreadable manifest: {exc}",
        ) from exc
    problems: list[str] = []
    for variable_id, series in manifest.variables.items():
        for frame in series.frames:
            path = manifest_path.parent / frame.path
            if not path.is_file():
                problems.append(
                    f"{variable_id}/{frame.frameId} ({frame.path}): file missing"
                )
                continue
            if frame.expectedBytes:
                size = path.stat().st_size
                if size != frame.expectedBytes:
                    problems.append(
                        f"{variable_id}/{frame.frameId} ({frame.path}): "
                        f"{size} bytes on disk, {frame.expectedBytes} expected"
                    )
            if len(problems) >= 5:
                break
        if len(problems) >= 5:
            break
    if problems:
        raise HTTPException(
            status_code=422,
            detail=(
                f"Dataset {dataset_id} is incomplete ({len(problems)} frame "
                "problems, first few: " + "; ".join(problems) + "). "
                "Re-import the dataset, or restore the RAW folder, then retry."
            ),
        )
