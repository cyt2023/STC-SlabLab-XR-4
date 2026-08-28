# VolumeSTCube FastAPI Example

This server does not render. It only returns JSON visualization specs that Unity can consume through `VolumeSTCubeServerClient`.

## Install

```bash
pip install -r requirements.txt
```

## Run

```bash
uvicorn main:app --reload --host 0.0.0.0 --port 8000
```

## Endpoints

- `GET /api/volumestcube/example`
  - Returns a JSON spec containing every naturally sorted `.raw/.raw.ini` pair found in `DataTransformationModule/UnityRawData/`.

- `POST /api/volumestcube/spec`
  - Accepts raw-file or point-data view/config fields and returns a VolumeSTCube JSON spec.

Example POST body:

```json
{
  "viewId": "custom_server_view",
  "datasetName": "air_pollution_example",
  "rawFiles": ["/absolute/path/to/time_0.raw", "/absolute/path/to/time_1.raw"],
  "iniFiles": ["/absolute/path/to/time_0.raw.ini", "/absolute/path/to/time_1.raw.ini"],
  "render": {
    "mode": "Volume",
    "dataLayout": "Auto",
    "showTimeline": true,
    "opacity": 0.8
  }
}
```

`rawFiles` and `iniFiles` are ordered, positional lists and must have equal lengths. `render.dataLayout` accepts `Auto`, `XYTime`, or `XYZTimeSeries`; use `Auto` unless the file naming convention is ambiguous.

Interactive request/response documentation is available at `http://localhost:8000/docs` while the server is running. The request model documents render, transform, filter, point-grid, CSV-column, and point fields.

Point-data POST body:

```json
{
  "viewId": "point_server_view",
  "datasetName": "point_data_example",
  "dataMode": "pointData",
  "points": [
    {"x": 1.0, "y": 2.0, "t": 0.1, "variable": 20.5},
    {"x": 1.5, "y": 2.2, "t": 0.2, "variable": 25.0}
  ],
  "render": {
    "mode": "Volume",
    "opacity": 1.0
  },
  "pointGrid": {
    "dimX": 64,
    "dimY": 64,
    "dimT": 32,
    "splatRadius": 2
  }
}
```

CSV point-data POST body:

```json
{
  "viewId": "csv_server_view",
  "datasetName": "csv_point_data",
  "dataMode": "pointData",
  "csvFile": "C:/path/to/points.csv",
  "csvColumns": {
    "x": "longitude",
    "y": "latitude",
    "t": "time_index",
    "variable": "temperature"
  },
  "render": {
    "mode": "Volume",
    "opacity": 1.0
  }
}
```

## Unity connection

1. Open `RenderingModule` in Unity.
2. Add `VolumeSTCubeServerClient` to a GameObject.
3. Keep `serverBaseUrl` as `http://localhost:8000`.
4. Call `LoadExampleFromServer()` from UI, script, or the inspector context you prefer.

```csharp
VolumeSTCubeServerClient client = GetComponent<VolumeSTCubeServerClient>();
client.ViewLoaded += view => Debug.Log($"Loaded {view.viewId}");
client.RequestFailed += error => statusText.text = error;
client.LoadExampleFromServer();
```

`LoadExampleFromServer()` performs the example GET. `LoadSpecFromUrl(url)` performs a GET against a custom spec endpoint. `SendJsonAndRender(jsonBody)` posts a typed request to `/api/volumestcube/spec`.
