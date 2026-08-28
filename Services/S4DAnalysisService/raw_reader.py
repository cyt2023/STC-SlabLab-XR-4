from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .models import (
    FacetGridRequest,
    GeographicCoordinate,
    ManifestValidationReport,
    VariableSeries,
    VolumeManifest,
)


@dataclass(frozen=True)
class CellNumericResult:
    cell_id: str
    values: np.ndarray
    valid_fraction: float
    frames_used: tuple[int, ...]
    depth_indices: tuple[int, ...]
    variable_id: str = ""


@dataclass(frozen=True)
class GridNumericResult:
    cells: tuple[CellNumericResult, ...]
    shared_minimum: float
    shared_maximum: float
    unit: str
    coordinate_reference: str | None = None
    render_projection: str | None = None
    x_axis: dict[str, object] | None = None
    y_axis: dict[str, object] | None = None


@dataclass(frozen=True)
class AggregateVolumeResult:
    values: np.ndarray
    valid_fraction: float
    frames_used: tuple[int, ...]
    depth_indices: tuple[int, ...]
    unit: str


def resolve_frame_path(manifest_path: Path, relative_path: str) -> Path:
    path = Path(relative_path)
    if not path.is_absolute():
        path = manifest_path.parent / path
    return path.resolve()


def sha256_file(path: Path, chunk_size: int = 4 * 1024 * 1024) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(chunk_size):
            digest.update(chunk)
    return digest.hexdigest()


def validate_manifest_files(
    manifest_path: str | Path,
    *,
    verify_hashes: bool = False,
) -> ManifestValidationReport:
    source = Path(manifest_path).resolve()
    try:
        manifest = VolumeManifest.load(source)
    except Exception as exc:
        return ManifestValidationReport(valid=False, errors=[str(exc)])

    report = ManifestValidationReport(valid=True, datasetId=manifest.datasetId)
    for variable_id, variable in manifest.variables.items():
        for frame in variable.frames:
            path = resolve_frame_path(source, frame.path)
            if not path.is_file():
                report.errors.append(f"{variable_id}/{frame.frameId}: missing file {path}")
                continue
            report.checkedFiles += 1
            actual_size = path.stat().st_size
            if actual_size != frame.expectedBytes:
                report.errors.append(
                    f"{variable_id}/{frame.frameId}: expected {frame.expectedBytes} bytes, "
                    f"found {actual_size}"
                )
            if verify_hashes:
                if not frame.sha256:
                    report.warnings.append(
                        f"{variable_id}/{frame.frameId}: sha256 is not indexed"
                    )
                elif sha256_file(path) != frame.sha256:
                    report.errors.append(f"{variable_id}/{frame.frameId}: sha256 mismatch")
    report.valid = not report.errors
    return report


class RawVolumeReader:
    def __init__(self, manifest_path: str | Path):
        self.manifest_path = Path(manifest_path).resolve()
        self.manifest = VolumeManifest.load(self.manifest_path)

    def read_encoded(self, variable: VariableSeries, time_index: int) -> np.memmap:
        frame = next(
            (candidate for candidate in variable.frames if candidate.timeIndex == time_index),
            None,
        )
        if frame is None:
            raise KeyError(f"unknown time index: {time_index}")
        path = resolve_frame_path(self.manifest_path, frame.path)
        actual_size = path.stat().st_size
        if actual_size != frame.expectedBytes:
            raise ValueError(
                f"{frame.frameId}: expected {frame.expectedBytes} bytes, found {actual_size}"
            )
        dimensions = self.manifest.dimensions
        return np.memmap(
            path,
            dtype=np.uint8,
            mode="r",
            shape=(dimensions.z, dimensions.y, dimensions.x),
            order="C",
        )

    @staticmethod
    def decode(variable: VariableSeries, encoded: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        valid = np.ones(encoded.shape, dtype=bool)
        for missing in variable.missingRawValues:
            valid &= encoded != missing
        physical = encoded.astype(np.float32) * variable.scale + variable.offset
        return physical, valid

    def materialize_grid(self, request: FacetGridRequest) -> GridNumericResult:
        if request.datasetId != self.manifest.datasetId:
            raise ValueError(
                f"request datasetId {request.datasetId!r} does not match "
                f"{self.manifest.datasetId!r}"
            )
        excluded = set(self.manifest.coordinates.depth.excludedIndices)
        max_depth = self.manifest.dimensions.z - 1
        cells: list[CellNumericResult] = []
        units: set[str] = set()
        requested = set(request.requestedCellIds)
        known_cell_ids = {
            f"{time_bucket.id}__{depth_bucket.id}"
            for depth_bucket in request.depthBuckets
            for time_bucket in request.timeBuckets
        }
        unknown = requested - known_cell_ids
        if unknown:
            raise ValueError(
                f"requestedCellIds contains unknown cells: {sorted(unknown)}"
            )

        for depth_bucket in request.depthBuckets:
            variable_id = depth_bucket.variableId or request.variableId
            try:
                variable = self.manifest.variables[variable_id]
            except KeyError as exc:
                raise ValueError(f"unknown variableId: {variable_id}") from exc
            units.add(variable.unit)
            available_times = {frame.timeIndex for frame in variable.frames}
            depth_indices = tuple(dict.fromkeys(depth_bucket.indices))
            invalid_depths = [
                index
                for index in depth_indices
                if index < 0 or index > max_depth or index in excluded
            ]
            if invalid_depths:
                raise ValueError(
                    f"depth bucket {depth_bucket.id!r} contains invalid indices: "
                    f"{invalid_depths}"
                )
            for time_bucket in request.timeBuckets:
                cell_id = f"{time_bucket.id}__{depth_bucket.id}"
                if requested and cell_id not in requested:
                    continue
                time_indices = tuple(dict.fromkeys(time_bucket.indices))
                invalid_times = [
                    index for index in time_indices if index not in available_times
                ]
                if invalid_times:
                    raise ValueError(
                        f"time bucket {time_bucket.id!r} contains invalid indices: "
                        f"{invalid_times}"
                    )

                total = np.zeros(
                    (self.manifest.dimensions.y, self.manifest.dimensions.x),
                    dtype=np.float64,
                )
                count = np.zeros_like(total, dtype=np.uint32)
                for time_index in time_indices:
                    encoded = self.read_encoded(variable, time_index)
                    selected_encoded = np.asarray(
                        encoded[list(depth_indices), :, :]
                    )
                    selected_values, selected_valid = self.decode(
                        variable, selected_encoded
                    )
                    total += np.where(selected_valid, selected_values, 0.0).sum(axis=0)
                    count += selected_valid.sum(axis=0, dtype=np.uint32)

                aggregate = np.full(total.shape, np.nan, dtype=np.float32)
                np.divide(total, count, out=aggregate, where=count > 0)
                valid_fraction = float(count.sum()) / float(
                    count.size * len(time_indices) * len(depth_indices)
                )
                cells.append(
                    CellNumericResult(
                        cell_id=cell_id,
                        values=aggregate,
                        valid_fraction=valid_fraction,
                        frames_used=time_indices,
                        depth_indices=depth_indices,
                        variable_id=variable_id,
                    )
                )

        finite = np.concatenate(
            [cell.values[np.isfinite(cell.values)] for cell in cells]
        )
        if finite.size == 0:
            raise ValueError("the requested grid contains no valid values")
        return GridNumericResult(
            cells=tuple(cells),
            shared_minimum=(
                request.sharedScaleMinimum
                if request.hasSharedScaleOverride
                else float(finite.min())
            ),
            shared_maximum=(
                request.sharedScaleMaximum
                if request.hasSharedScaleOverride
                else float(finite.max())
            ),
            unit=next(iter(units)) if len(units) == 1 else "mixed units",
            coordinate_reference=self.manifest.coordinates.coordinateReference,
            render_projection=self.manifest.coordinates.renderProjection,
            x_axis=(
                self.manifest.coordinates.x.model_dump()
                if isinstance(self.manifest.coordinates.x, GeographicCoordinate)
                else None
            ),
            y_axis=(
                self.manifest.coordinates.y.model_dump()
                if isinstance(self.manifest.coordinates.y, GeographicCoordinate)
                else None
            ),
        )

    def materialize_aggregate_volume(
        self,
        request: FacetGridRequest,
        cell_id: str,
    ) -> AggregateVolumeResult:
        """Return the exact T-aggregate while preserving every selected Z layer.

        The final chart reduces T and Z to an XY map. Ground Aggregate must keep
        Z so Unity can show the continuous evidence behind that chart rather
        than pasting the chart PNG onto a plane.
        """
        if request.datasetId != self.manifest.datasetId:
            raise ValueError(
                f"request datasetId {request.datasetId!r} does not match "
                f"{self.manifest.datasetId!r}"
            )
        selected_time = None
        selected_depth = None
        for time_bucket in request.timeBuckets:
            for depth_bucket in request.depthBuckets:
                if f"{time_bucket.id}__{depth_bucket.id}" == cell_id:
                    selected_time = time_bucket
                    selected_depth = depth_bucket
                    break
            if selected_time is not None:
                break
        if selected_time is None or selected_depth is None:
            raise ValueError(f"unknown cellId for snapshot request: {cell_id}")

        variable_id = selected_depth.variableId or request.variableId
        try:
            variable = self.manifest.variables[variable_id]
        except KeyError as exc:
            raise ValueError(f"unknown variableId: {variable_id}") from exc

        time_indices = tuple(dict.fromkeys(selected_time.indices))
        depth_indices = tuple(dict.fromkeys(selected_depth.indices))
        available_times = {frame.timeIndex for frame in variable.frames}
        invalid_times = [index for index in time_indices if index not in available_times]
        if invalid_times:
            raise ValueError(f"cell {cell_id!r} contains invalid times: {invalid_times}")
        excluded = set(self.manifest.coordinates.depth.excludedIndices)
        invalid_depths = [
            index
            for index in depth_indices
            if index < 0 or index >= self.manifest.dimensions.z or index in excluded
        ]
        if invalid_depths:
            raise ValueError(f"cell {cell_id!r} contains invalid depths: {invalid_depths}")

        shape = (
            len(depth_indices),
            self.manifest.dimensions.y,
            self.manifest.dimensions.x,
        )
        total = np.zeros(shape, dtype=np.float64)
        count = np.zeros(shape, dtype=np.uint32)
        for time_index in time_indices:
            encoded = self.read_encoded(variable, time_index)
            selected_encoded = np.asarray(encoded[list(depth_indices), :, :])
            selected_values, selected_valid = self.decode(variable, selected_encoded)
            total += np.where(selected_valid, selected_values, 0.0)
            count += selected_valid.astype(np.uint32)

        aggregate = np.full(shape, np.nan, dtype=np.float32)
        np.divide(total, count, out=aggregate, where=count > 0)
        valid_fraction = float(count.sum()) / float(
            count.size * max(1, len(time_indices))
        )
        return AggregateVolumeResult(
            values=aggregate,
            valid_fraction=valid_fraction,
            frames_used=time_indices,
            depth_indices=depth_indices,
            unit=variable.unit,
        )
