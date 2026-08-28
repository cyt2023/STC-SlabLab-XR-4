# VolumeSTCube Project Structure Summary

## Important folders

- `DataTransformationModule/`
  - Python preprocessing pipeline for spatial-temporal data.
  - `exampleData/` contains source JSON files and the merge script for example AQI data.
  - Generated interpolation JSON is written to `DataTransformationModule/InterpolateResult/`.
  - Generated Unity `.raw` and `.ini` files are written to `DataTransformationModule/UnityRawData/`.

- `RenderingModule/`
  - Unity project.
  - `Assets/Scripts/Importing/RawImporter/` contains raw volume import code.
  - `Assets/Scripts/Importing/Ini/` contains `.ini` parsing for raw import settings.
  - `Assets/Scripts/VolumeObject/` contains the rendering object, controller, slicing, clipping, and factory classes.
  - `Assets/Scripts/TransferFunction/` contains transfer function models and persistence.
  - `Assets/Editor/` contains editor menu items and importer windows.
  - `Assets/Resources/` contains runtime prefabs used by the renderer, including `VolumeContainer`, slicing, cutout, and axis prefabs.

## DataTransformationModule pipeline

- `exampleData/0_exampleDataMerge.py`
  - Input: `locations.json` and `timeseriesdata.json`.
  - Output: per-timestep CSV files under `exampleData/data_merged/`, named `LOC_AQI_<index>.csv`.
  - Each CSV includes longitude, latitude, station metadata, and `val`.

- `1_KrigingInterpolation.py`
  - Input: merged CSV files from `exampleData/data_merged/` and China GeoJSON masks.
  - Converts WGS84 coordinates to EPSG:3857, builds a grid, runs Ordinary Kriging per timestep, clips outside the China mask, and writes 3D time slabs.
  - Output: JSON files under `InterpolateResult/`.
  - JSON format includes `xLength`, `yLength`, `zLength`, and flattened `data`.

- `2_Smooth.py`
  - Input: interpolation JSON files from `InterpolateResult/`.
  - Applies spatial and temporal smoothing, clips the China frame again, maps values to `uint8`, and writes Unity-ready files.
  - Output: `.raw` files and matching `.ini` files under `UnityRawData/`.
  - `.ini` format uses keys such as `dimx`, `dimy`, `dimz`, `skip`, and `format`.

## RenderingModule loader and rendering path

- Menu item: `Assets/Editor/VolumeRendererEditorFunctions.cs`
  - `Volume Rendering > Load dataset > Load raw dataset`
  - Opens a file panel and creates `RAWDatasetImporterEditorWindow`.

- Editor raw importer window: `Assets/Editor/RAWDatasetImporterEditorWIndow.cs`
  - Reads matching `.ini` settings through `DatasetIniReader`.
  - Uses `RawDatasetImporter`.
  - Spawns the rendered object through `VolumeObjectFactory.CreateObjectAsync`.

- Runtime raw importer: `Assets/Scripts/Importing/RawImporter/RawDatasetImporter.cs`
  - Reads raw bytes according to dimensions, format, endianness, and skip bytes.
  - Creates a `VolumeDataset`.

- `.ini` parser: `Assets/Scripts/Importing/Ini/DatasetIniReader.cs`
  - Parses raw import metadata.

- Volume object factory: `Assets/Scripts/VolumeObject/VolumeObjectFactory.cs`
  - Creates `VolumeRenderedObject`.
  - Instantiates the `VolumeContainer` prefab from `Resources`.
  - Sets materials, textures, noise texture, transfer functions, and shader keywords.

- Rendered volume: `Assets/Scripts/VolumeObject/VolumeRenderedObject.cs`
  - Owns the dataset, mesh renderer, render mode, transfer functions, visibility window, slicing plane creation, and material updates.

- Group controller: `Assets/Scripts/VolumeObject/VolumeControllerObject.cs`
  - Controls multiple volume objects, render mode, transfer function, clipping height window, opacity, lighting, highlight, and visibility thresholds.

- Slicing / clipping / time filtering:
  - `Assets/Scripts/VolumeObject/SlicingPlane.cs`
  - `Assets/Scripts/VolumeObject/CrossSectionPlane.cs`
  - `Assets/Scripts/VolumeObject/CutoutBox.cs`
  - `Assets/Scripts/VolumeObject/CutoutSphere.cs`
  - `Assets/Scripts/VolumeObject/CrossSectionManager.cs`
  - `Assets/Scripts/MapController/TClipper.cs`

## Scripts to wrap with the new API

- `DatasetIniReader`
- `RawDatasetImporter`
- `VolumeObjectFactory`
- `VolumeRenderedObject`
- `VolumeControllerObject` where compatible
- `TransferFunction` and `TransferFunctionDatabase` for future transfer-function presets
- `TClipper` for scene-specific time clipping when present

## Scripts that should not be modified directly in the first API pass

- Existing editor menu and importer windows.
- Existing rendering shaders and materials.
- Existing `VolumeObjectFactory`, `VolumeRenderedObject`, and `VolumeControllerObject` internals.
- Existing scenes, prefabs, resources, and demo UI scripts.

The API wrapper under `RenderingModule/Assets/VolumeSTCubeAPI/` leaves the original manual workflow intact.

The API has two rendering input paths:

- Raw-file path: JSON points to `.raw` and `.ini`, then the API reuses the original raw importer and renderer.
- Point-data path: JSON contains `x`, `y`, `t`, and `variable`, then the API creates a runtime `VolumeDataset` in Unity and reuses the original `VolumeObjectFactory` and renderer.
