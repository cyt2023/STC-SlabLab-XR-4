from __future__ import annotations

from enum import Enum
from pathlib import Path
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class StorageOrder(str, Enum):
    ZYX = "ZYX"


class VoxelType(str, Enum):
    UINT8 = "uint8"


class TemporalMeaning(str, Enum):
    INSTANTANEOUS = "instantaneous"
    INTERVAL_MEAN = "interval_mean"


class Dimensions(BaseModel):
    x: int = Field(gt=0)
    y: int = Field(gt=0)
    z: int = Field(gt=0)


class IndexCoordinate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    kind: Literal["ordinal_index"] = "ordinal_index"
    unit: str
    start: int = 0
    step: int = 1


class GeographicCoordinate(BaseModel):
    """A regularly sampled longitude or latitude axis in the source CRS."""

    model_config = ConfigDict(extra="forbid")

    kind: Literal["regular_geographic_grid"] = "regular_geographic_grid"
    axis: Literal["longitude", "latitude"]
    unit: Literal["degree_east", "degree_north"]
    start: float
    step: float


class DepthCoordinate(IndexCoordinate):
    positive: Literal["down", "up"]
    excludedIndices: list[int] = Field(default_factory=list)


class CoordinateMetadata(BaseModel):
    model_config = ConfigDict(extra="forbid")

    coordinateReference: str | None = None
    renderProjection: str | None = None
    x: IndexCoordinate | GeographicCoordinate
    y: IndexCoordinate | GeographicCoordinate
    depth: DepthCoordinate

    @model_validator(mode="after")
    def validate_geographic_axes(self) -> "CoordinateMetadata":
        geographic = isinstance(self.x, GeographicCoordinate) or isinstance(
            self.y, GeographicCoordinate
        )
        if geographic:
            if not isinstance(self.x, GeographicCoordinate) or not isinstance(
                self.y, GeographicCoordinate
            ):
                raise ValueError("x and y must both use geographic coordinates")
            if self.x.axis != "longitude" or self.y.axis != "latitude":
                raise ValueError("geographic x/y axes must be longitude/latitude")
            if not self.coordinateReference:
                raise ValueError("geographic coordinates require coordinateReference")
        return self


class VolumeFrameRef(BaseModel):
    model_config = ConfigDict(extra="forbid")

    frameId: str
    timeIndex: int = Field(ge=0)
    temporalMeaning: TemporalMeaning
    path: str
    expectedBytes: int = Field(gt=0)
    sha256: str | None = None


class VariableSeries(BaseModel):
    model_config = ConfigDict(extra="forbid")

    displayName: str
    unit: str
    valueSemantics: Literal["physical", "encoded_intensity"]
    voxelType: VoxelType
    scale: float
    offset: float
    missingRawValues: list[int] = Field(default_factory=list)
    frames: list[VolumeFrameRef]

    @model_validator(mode="after")
    def validate_frames(self) -> "VariableSeries":
        indices = [frame.timeIndex for frame in self.frames]
        if len(indices) != len(set(indices)):
            raise ValueError("frame timeIndex values must be unique")
        if self.voxelType == VoxelType.UINT8:
            invalid = [value for value in self.missingRawValues if value < 0 or value > 255]
            if invalid:
                raise ValueError(f"uint8 missingRawValues are out of range: {invalid}")
        return self


class DatasetAssumption(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: str
    statement: str
    evidence: str
    status: Literal["measured", "inferred", "unverified"]


class VolumeManifest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schemaVersion: Literal["1.0"]
    datasetId: str
    datasetVersion: str
    dimensions: Dimensions
    storageOrder: StorageOrder
    defaultVoxelType: VoxelType
    coordinates: CoordinateMetadata
    variables: dict[str, VariableSeries]
    assumptions: list[DatasetAssumption] = Field(default_factory=list)

    @model_validator(mode="after")
    def validate_manifest(self) -> "VolumeManifest":
        expected = self.dimensions.x * self.dimensions.y * self.dimensions.z
        for variable_id, variable in self.variables.items():
            for frame in variable.frames:
                if frame.expectedBytes != expected:
                    raise ValueError(
                        f"{variable_id}/{frame.frameId}: expectedBytes must equal "
                        f"{expected} for uint8 {self.storageOrder.value}"
                    )
        for index in self.coordinates.depth.excludedIndices:
            if index < 0 or index >= self.dimensions.z:
                raise ValueError(f"excluded depth index is out of range: {index}")
        return self

    @classmethod
    def load(cls, path: str | Path) -> "VolumeManifest":
        return cls.model_validate_json(Path(path).read_text(encoding="utf-8"))


class IndexBucket(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: str
    label: str
    indices: list[int] = Field(min_length=1)
    variableId: str | None = None


class DimensionRoleAssignment(BaseModel):
    model_config = ConfigDict(extra="forbid")

    dimension: Literal["time", "depth", "horizontal", "variable"]
    role: Literal["fixed", "faceted", "mapped"]


class FacetGridRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    datasetId: str
    variableId: str
    timeBuckets: Annotated[list[IndexBucket], Field(min_length=1, max_length=9)]
    depthBuckets: Annotated[list[IndexBucket], Field(min_length=1, max_length=9)]
    dimensionRoles: list[DimensionRoleAssignment] = Field(default_factory=list)
    rawIntent: str = Field(default="", max_length=2000)
    analysisQuestion: str = Field(default="", max_length=1000)
    analyticTask: Literal[
        "characterize_distribution",
        "find_anomalies",
        "determine_range",
        "characterize_trend",
        "correlate",
        "cluster",
    ] = "characterize_distribution"
    requestedCellIds: list[str] = Field(default_factory=list, max_length=81)
    hasSharedScaleOverride: bool = False
    sharedScaleMinimum: float = 0.0
    sharedScaleMaximum: float = 0.0
    chartType: Literal[
        "horizontal_heatmap",
        "bar_chart",
        "histogram",
        "scatter_plot",
        "line_chart",
        "pie_chart",
        "box_plot",
        "violin_plot",
    ] = "horizontal_heatmap"
    colorMap: str = "viridis"
    missingPolicy: Literal["exclude"] = "exclude"
    scalePolicy: Literal["shared_across_grid"] = "shared_across_grid"

    @model_validator(mode="after")
    def validate_dimension_roles(self) -> "FacetGridRequest":
        names = [assignment.dimension for assignment in self.dimensionRoles]
        if len(names) != len(set(names)):
            raise ValueError("dimensionRoles contains duplicate dimensions")
        if self.dimensionRoles:
            roles = {
                assignment.dimension: assignment.role
                for assignment in self.dimensionRoles
            }
            if roles.get("horizontal") != "mapped":
                raise ValueError("horizontal must be the mapped panel dimension")
            if sum(role == "mapped" for role in roles.values()) != 1:
                raise ValueError("exactly one dimension must be mapped")
            if sum(role == "faceted" for role in roles.values()) > 2:
                raise ValueError("at most two dimensions may be faceted")
            if roles.get("variable") == "faceted" and any(
                not bucket.variableId for bucket in self.depthBuckets
            ):
                raise ValueError(
                    "variable faceting requires a variableId on every row bucket"
                )
        if (
            self.hasSharedScaleOverride
            and self.sharedScaleMaximum <= self.sharedScaleMinimum
        ):
            raise ValueError("shared scale maximum must be greater than minimum")
        return self


class IntentResolutionRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    # Empty input is a valid Full Matrix action.  The v4 interaction contract
    # resolves it to the default distribution task instead of blocking the
    # user's workflow in Unity.
    text: str = Field(default="", max_length=2000)
    variableId: str | None = None
    variableDisplayName: str | None = None
    unit: str | None = None


class IntentResolution(BaseModel):
    model_config = ConfigDict(extra="forbid")

    rawText: str
    analyticTask: Literal[
        "characterize_distribution",
        "find_anomalies",
        "determine_range",
        "characterize_trend",
        "correlate",
        "cluster",
    ]
    displayLabel: str
    focus: str
    confidence: float = Field(ge=0.0, le=1.0)
    usedFallback: bool
    normalizedInstruction: str


class ManifestValidationReport(BaseModel):
    valid: bool
    datasetId: str | None = None
    checkedFiles: int = 0
    errors: list[str] = Field(default_factory=list)
    warnings: list[str] = Field(default_factory=list)
