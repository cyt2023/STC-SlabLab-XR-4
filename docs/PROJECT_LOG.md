# Project Log

## 2026-05-08 Original preprocessing and scene-layout parity

The Unity integration now has a closer path to the original VolumeSTCube paper/demo scene instead of relying on the simplified CSV preview.

Added original-scene raw stack support:

- `VolumeSTCubeAPI.CreateViewFromRawDirectory(...)` imports a folder of `.raw/.raw.ini` files through the original raw importer.
- `VolumeSTCubeOriginalSceneAdapter` parents imported `VolumeRenderedObject` layers under `VolumeController`, applies the original layered scale/height layout, refreshes axis/clipper/controller references, and clears stale API test objects.
- Editor menu entries now support importing a processed raw stack or a single raw file with the VolumeSTCube scene preset.
- Raw folders with a non-eight file count can be repacked into eight compatible layer chunks when dimensions, format, endianness, and skip metadata match.

Added original Python preprocessing from Unity:

```text
Volume Rendering > Load dataset > Run original Python preprocessing and import raw stack
Volume Rendering > Load dataset > Run QUICK original Python preprocessing test and import raw stack
```

The full entry keeps the original 8-layer, 552-timestamp, 175x175 grid, 2x expansion workflow. The quick entry uses the same merge, kriging, China clipping, smoothing, raw export, and Unity import path, but reduces the data size to 8 layers, 16 timestamps per layer, and a 64x64 grid expanded to 128x128. This makes layout and visual testing practical before running the expensive full preprocessing.

Improved Python preprocessing usability:

- `0_exampleDataMerge.py` now skips already merged timestamp CSV files and avoids repeated per-row column lookup.
- `1_KrigingInterpolation.py` and `2_Smooth.py` accept environment variables for layer count, time width, grid size, expansion ratio, and smoothing radii.
- Unity's preprocessing runner now shows elapsed time while waiting for Python so long Kriging/Smooth runs are visible instead of appearing frozen.
- Python dependencies required by the original scripts are documented through `DataTransformationModule/requirements.txt`; the tested missing packages were `geopandas`, `PyKrige`, `pyproj`, `scipy`, and `shapely`.

Added scene tuning controls:

- `VolumeControllerObject` exposes Inspector fields for layer spacing scale, layer Y offset, and map X/Z offset/scale.
- `VolumeRenderedControllerCustomInspector` adds a `VolumeSTCube Scene Layout` section and an `Apply layout to imported volumes` button.
- `ControlPanel.resetAll()` no longer accidentally flips direct volume rendering into surface rendering.

Known performance note:

The full original preprocessing writes very large intermediate JSON files under `DataTransformationModule/InterpolateResult/`. On the test machine, the quick profile generated 8 raw layers successfully (`128 x 128 x 16` each), while the full profile was confirmed to be much longer-running.

## 2026-05-04 Unity API integration status

The project now provides a Unity-side wrapper around the original VolumeSTCube rendering workflow. The original editor menu, importer, renderer, shaders, scenes, and resources are kept intact. The new code is added under `RenderingModule/Assets/VolumeSTCubeAPI/` and exposes a stable facade named `VolumeSTCubeAPI`.

## Unified interface

External Unity code can create visualizations through these calls:

```csharp
VolumeSTCubeAPI.CreateView(data, config);
VolumeSTCubeAPI.CreateViewFromJson(json);
VolumeSTCubeAPI.CreateViewFromCsv(csvPath, config);
VolumeSTCubeAPI.CreateViewFromCsvRaw(csvPath, config);
VolumeSTCubeAPI.CreateViewFromGeoCsv(csvPath, config);
VolumeSTCubeAPI.CreateViewFromPoints(xValues, yValues, tValues, variableValues, config);
```

Created views can be managed through:

```csharp
VolumeSTCubeAPI.ApplyTimeFilter(viewId, tMin, tMax);
VolumeSTCubeAPI.SetVisible(viewId, visible);
VolumeSTCubeAPI.DestroyView(viewId);
VolumeSTCubeAPI.ClearAll();
```

For `.raw` data, the wrapper calls the original VolumeSTCube path:

```text
DatasetIniReader -> RawDatasetImporter -> VolumeObjectFactory -> VolumeRenderedObject
```

This means the project supports a unified API without replacing the proven renderer.

## Test entry points

One-click API smoke test:

```text
Volume Rendering > Test > One-click API Smoke Test
```

This test creates a `VolumeSTCubeJsonRunner`, injects an inline point-data JSON spec, and calls `VolumeSTCubeAPI.CreateViewFromJson(...)`. It should create `VolumeSTCubeView_one_click_point_test` and at least one `VolumeRenderedObject`.

Cleanup:

```text
Volume Rendering > Test > Clear One-click Smoke Test
```

Manual raw data test:

```text
Volume Rendering > Load dataset > Load raw dataset
```

Select the `.raw` file, for example:

```text
volume_salt_data_time_0_255.raw
```

Keep the matching metadata file in the same folder:

```text
volume_salt_data_time_0_255.raw.ini
```

The `.raw.ini` file is not the primary file selected in the file picker. It provides dimensions, format, skip bytes, and related import settings for the `.raw` file.

CSV-to-raw import test:

```text
Volume Rendering > Load dataset > Import CSV dataset
```

This entry accepts CSV files with `x,y,z` or geographic aliases such as `lng,lat,val` and `longitude,latitude,value`. Unity generates `.raw/.raw.ini` in `Application.persistentDataPath/VolumeSTCubeGeneratedRaw/`, then immediately renders the generated raw dataset through the same original raw importer path.

Original scene raw stack import:

```text
Volume Rendering > Load dataset > Import raw stack with VolumeSTCube scene preset
Volume Rendering > Load dataset > Import single raw with VolumeSTCube scene preset
```

These entries automate the original VolumeSTCube README rendering setup. The stack entry imports all `.raw` files from a selected folder in sorted order; the single-file entry imports one selected `.raw` file. Both require matching `.raw.ini` metadata beside each raw file, parent the generated `VolumeRenderedObject` layers directly under `VolumeController`, refresh the original layer scale/height layout, and apply the scene-style opacity preset.

External API calls can use:

```csharp
VolumeSTCubeAPI.CreateViewFromCsvRaw(csvPath, config);
VolumeSTCubeAPI.CreateViewFromGeoCsv(csvPath, config);
```

JSON specs can use:

```json
{
  "dataMode": "csvRaw",
  "csvFile": "C:/path/to/data.csv"
}
```

## Test data used

The one-click API smoke test uses inline point samples in:

```text
RenderingModule/Assets/Editor/VolumeSTCubeOneClickTest.cs
```

The raw-data workflow was prepared for generated VolumeSTCube files named like:

```text
volume_salt_data_time_0_255.raw
volume_salt_data_time_0_255.raw.ini
volume_salt_data_time_1_255.raw
volume_salt_data_time_1_255.raw.ini
```

These files are data artifacts and are not required for the one-click API smoke test.

## Acceptance result

The codebase now satisfies the core encapsulation requirement: other Unity scripts can create VolumeSTCube visualizations through a single public API facade instead of manually wiring importer and renderer internals.

Expected acceptance checks:

- `RenderingModule` opens as the Unity project.
- `Volume Rendering > Test > One-click API Smoke Test` creates a visible test volume.
- `Volume Rendering > Load dataset > Import CSV dataset` accepts a CSV and creates a rendered raw-backed volume.
- Calling `VolumeSTCubeAPI.CreateViewFromJson(...)` with either point data or raw-file paths creates a `VolumeSTCubeView`.
- Calling `VolumeSTCubeAPI.CreateViewFromCsvRaw(...)` generates `.raw/.ini` and renders the generated raw dataset.
- The generated hierarchy contains `VolumeRenderedObject`, proving that the wrapper reaches the original renderer.
- Raw files can still be loaded through the original manual menu, preserving backward compatibility.

## Current boundaries

- Rendering remains inside Unity.
- `server_example` returns JSON specs; it does not render images or videos.
- Direct point-data mode uses simple Unity-side grid splatting.
- CSV-to-raw mode uses Unity-side grid splatting and normalization for quick import and API integration.
- High-quality interpolation and smoothing should still use the Python preprocessing pipeline to generate `.raw/.ini` files.
- Some optional config fields, such as transfer-function preset names, are stored for API compatibility but are not fully connected to every original UI/controller path.
