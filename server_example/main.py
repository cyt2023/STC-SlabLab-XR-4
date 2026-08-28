from pathlib import Path
import re
from typing import Any, Dict, List, Literal, Optional, Tuple

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field


REPO_ROOT = Path(__file__).resolve().parents[1]
UNITY_RAW_DATA = REPO_ROOT / "DataTransformationModule" / "UnityRawData"

app = FastAPI(title="VolumeSTCube JSON Spec Server")


class CsvColumns(BaseModel):
    x: str = "x"
    y: str = "y"
    t: str = "t"
    variable: str = "variable"


class VolumePoint(BaseModel):
    x: float
    y: float
    t: float
    variable: float


class RenderOptions(BaseModel):
    mode: Literal["Volume", "Surface", "Hybrid", "PointPreview"] = "Volume"
    dataLayout: Literal["Auto", "XYTime", "XYZTimeSeries"] = "Auto"
    showBoundingBox: bool = True
    showTimeAxis: bool = True
    timeAxis: Literal["Z", "Y"] = "Z"
    showTimeline: bool = True
    timelineAutoPlay: bool = False
    timelinePlaybackSeconds: float = 10.0
    timelineWindow: float = 0.05
    autoGroupUnderVolumeController: bool = True
    enableInteraction: bool = True
    opacity: float = 1.0


class TransformOptions(BaseModel):
    position: List[float] = Field(default_factory=lambda: [0.0, 0.0, 0.0])
    rotation: List[float] = Field(default_factory=lambda: [0.0, 0.0, 0.0])
    scale: List[float] = Field(default_factory=lambda: [1.0, 1.0, 1.0])


class FilterOptions(BaseModel):
    timeMin: float = 0.0
    timeMax: float = 1.0
    variableMin: float = 0.0
    variableMax: float = 1.0


class PointGridOptions(BaseModel):
    dimX: int = 64
    dimY: int = 64
    dimT: int = 32
    splatRadius: int = 2


class VolumeSTCubeSpecRequest(BaseModel):
    viewId: str = "server_view"
    datasetName: str = "volume_stcube_dataset"
    dataMode: Literal["rawFiles", "pointData", "csvRaw"] = "rawFiles"
    csvFile: Optional[str] = None
    csvColumns: Optional[CsvColumns] = None
    rawFiles: Optional[List[str]] = None
    iniFiles: Optional[List[str]] = None
    points: Optional[List[VolumePoint]] = None
    render: Optional[RenderOptions] = None
    transform: Optional[TransformOptions] = None
    filters: Optional[FilterOptions] = None
    pointGrid: Optional[PointGridOptions] = None


def find_example_files() -> Tuple[List[str], List[str]]:
    raw_files = sorted(
        UNITY_RAW_DATA.glob("*.raw"),
        key=lambda path: [int(token) if token.isdigit() else token.lower() for token in re.split(r"(\d+)", path.name)],
    )
    if not raw_files:
        return [], []

    ini_files = [Path(str(raw) + ".ini") for raw in raw_files]
    valid_pairs = [(raw, ini) for raw, ini in zip(raw_files, ini_files) if ini.exists()]
    return [str(raw) for raw, _ in valid_pairs], [str(ini) for _, ini in valid_pairs]


def make_spec(
    view_id: str,
    dataset_name: str,
    data_mode: str,
    csv_file: Optional[str],
    csv_columns: Optional[Dict[str, str]],
    raw_files: List[str],
    ini_files: List[str],
    points: Optional[List[Dict[str, float]]] = None,
    render: Optional[Dict[str, Any]] = None,
    transform: Optional[Dict[str, Any]] = None,
    filters: Optional[Dict[str, Any]] = None,
    point_grid: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    render_spec: Dict[str, Any] = {
        "mode": "Volume",
        "showBoundingBox": True,
        "showTimeAxis": True,
        "timeAxis": "Z",
        "dataLayout": "Auto",
        "showTimeline": True,
        "timelineAutoPlay": False,
        "timelinePlaybackSeconds": 10.0,
        "timelineWindow": 0.05,
        "autoGroupUnderVolumeController": data_mode != "pointData",
        "enableInteraction": True,
        "opacity": 1.0,
    }
    if render:
        render_spec.update(render)

    spec = {
        "viewType": "VolumeSTCube",
        "viewId": view_id,
        "datasetName": dataset_name,
        "dataMode": data_mode,
        "csvFile": csv_file,
        "csvColumns": csv_columns,
        "rawFiles": raw_files,
        "iniFiles": ini_files,
        "render": render_spec,
        "transform": transform
        or {
            "position": [0, 0, 0],
            "rotation": [0, 0, 0],
            "scale": [1, 1, 1],
        },
        "filters": filters
        or {
            "timeMin": 0.0,
            "timeMax": 1.0,
            "variableMin": 0.0,
            "variableMax": 1.0,
        },
    }

    if points is not None:
        spec["points"] = points
    if point_grid is not None:
        spec["pointGrid"] = point_grid

    return spec


@app.get("/api/volumestcube/example", summary="Build a spec for the bundled RAW collection")
def example_spec() -> Dict[str, Any]:
    raw_files, ini_files = find_example_files()
    return make_spec(
        view_id="fastapi_example_view",
        dataset_name="fastapi_example_dataset",
        data_mode="rawFiles",
        csv_file=None,
        csv_columns=None,
        raw_files=raw_files,
        ini_files=ini_files,
    )


@app.post("/api/volumestcube/spec", summary="Normalize a typed VolumeSTCube request into a Unity JSON spec")
def create_spec(request: VolumeSTCubeSpecRequest) -> Dict[str, Any]:
    render = request.render.model_dump(exclude_none=True) if request.render else None
    transform = request.transform.model_dump() if request.transform else None
    filters = request.filters.model_dump() if request.filters else None
    csv_columns = request.csvColumns.model_dump() if request.csvColumns else None
    point_grid = request.pointGrid.model_dump() if request.pointGrid else None
    points = [point.model_dump() for point in request.points] if request.points else None

    if request.dataMode == "pointData":
        return make_spec(
            view_id=request.viewId,
            dataset_name=request.datasetName,
            data_mode="pointData",
            csv_file=request.csvFile,
            csv_columns=csv_columns,
            raw_files=[],
            ini_files=[],
            points=points or [],
            render=render,
            transform=transform,
            filters=filters,
            point_grid=point_grid,
        )

    raw_files = request.rawFiles
    ini_files = request.iniFiles
    if not raw_files:
        raw_files, discovered_ini = find_example_files()
        ini_files = ini_files or discovered_ini
    if not raw_files:
        raise HTTPException(status_code=400, detail="No RAW files were supplied or found in the example directory.")
    if not ini_files or len(ini_files) != len(raw_files):
        raise HTTPException(status_code=400, detail="rawFiles and iniFiles must contain the same number of entries.")

    return make_spec(
        view_id=request.viewId,
        dataset_name=request.datasetName,
        data_mode="rawFiles",
        csv_file=None,
        csv_columns=None,
        raw_files=raw_files or [],
        ini_files=ini_files or [],
        render=render,
        transform=transform,
        filters=filters,
    )
