# S4D Canvas Analysis Service

This service is the PC-side analysis boundary described by the S4D Canvas v5
guidance. It validates versioned manifests, reads RAW data deterministically,
computes grid-level numeric inputs, and prepares one mandatory MatPlotAgent job
for the complete Facet Grid.

MatPlotAgent remains part of the main rendering path. The service prepares and
locks the data semantics, missing-value policy, cell order, and shared scale
before MatPlotAgent runs. A Grid is never sent as unrelated per-cell prompts.

## Current encoded dataset policy

The bundled Hong Kong RAW files do not contain physical units, timestamps,
geographic coordinates, scale/offset metadata, or a depth array. The generated
manifest therefore uses explicit, non-physical semantics:

- `encoded_intensity` values;
- ordinal time indices `0..29`;
- X/Y/depth indices rather than invented coordinates;
- raw `0` as a measured/inferred invalid mask;
- depth index `91` excluded because it breaks the otherwise monotonic
  surface-to-deep coverage pattern.

These fields can later be replaced by physical metadata without changing the
Facet Grid request contract.

## Generate and validate the local manifest

From the repository root:

```powershell
python -m Services.S4DAnalysisService.generate_encoded_manifest
python -m unittest discover -s Services/S4DAnalysisService/tests -v
```

Run the service:

```powershell
python -m uvicorn Services.S4DAnalysisService.app:app --host 127.0.0.1 --port 8020
```

The checked-in `example_grid_request.json` defines the current 3×3
`Time × Depth → Horizontal` MVP for chlorophyll.

Important endpoints:

- `GET /health`
- `GET /datasets`
- `GET /datasets/resolve?variable=...&x=...&y=...&z=...&timeCount=...`
- `GET /datasets/{datasetId}/manifest`
- `POST /datasets/{datasetId}/validate`
- `POST /analysis/resolve-intent`
- `POST /analysis/preview-atlas`
- `POST /analysis/prepare-matplot-job`
- `POST /analysis/materialize`
- `GET /jobs/{jobId}`
- `GET /jobs/{jobId}/panel`
- `GET /jobs/{jobId}/chart-result`
- `GET /snapshots/{snapshotId}`
- `GET /snapshots/{snapshotId}/cells/{cellId}/aggregate-volume`

`resolve-intent` converts free text into one validated analytic task, focus,
confidence score, and normalized MatPlotAgent instruction. Unity keeps Full
Matrix locked until this structured result is returned and confirmed.

`prepare-matplot-job` produces `grid_data.csv`, `grid_contract.json`, and
`grid_prompt.txt`. `materialize` submits that package directly to the existing
MatPlotAgent service. S4D jobs require both `final.png` and
`chart_result.json`; the MatPlotAgent API checks the declared shared minimum,
maximum, and unit before reporting completion.

Every accepted materialization now receives a durable `snapshotId`. The
snapshot stores the validated request, dataset version, manifest digest,
source-file fingerprints, cell footprints, transform description, shared
scale, and MatPlotAgent job IDs. A snapshot becomes `completed` only after the
mandatory MatPlotAgent job succeeds. Ground Aggregate refuses pending/failed
snapshots and refuses to reinterpret an old snapshot if its manifest or RAW
files have changed.

The Ground volume endpoint returns little-endian float32 `Z,Y,X` data. It
averages the exact source frames in the selected Time bucket while preserving
all Z layers in the selected Depth bucket, so Unity displays continuous volume
evidence rather than the chart PNG.
