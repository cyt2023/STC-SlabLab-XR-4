# VolumeSTCube API Limitations

- Unity still performs rendering. The server-side example only returns visualization JSON specs.
- The API supports `.raw` plus `.ini` loading because the original renderer is built around `RawDatasetImporter`, `DatasetIniReader`, and `VolumeObjectFactory`.
- The API also supports direct `x, y, t, variable` point data by generating a runtime `VolumeDataset` inside Unity and passing it to the original `VolumeObjectFactory`.
- Point data can be supplied as JSON points, C# lists, or a local CSV file with `x`, `y`, `t`, and `variable` columns.
- Direct point-data rendering uses simple grid binning/splatting in Unity. It is intended for immediate visualization, not as a full replacement for the Python kriging, smoothing, masking, and scientific preprocessing pipeline.
- JSON parsing uses Unity `JsonUtility`, so the supported JSON shape is intentionally simple and strongly typed.
- `VolumeSTCubeView.ApplyTimeFilter` calls `VolumeControllerObject.SetClipedHeight` or scene `TClipper` only when compatible objects are available. Otherwise it logs a warning.
- `VolumeSTCubeView.ApplyOpacity` uses the original `VolumeControllerObject.SetOpacity` when a compatible controller is available. Ungrouped individual volumes currently log a warning instead of silently faking opacity.
- The original `VolumeControllerObject` has assumptions about direct child `VolumeRenderedObject` layout. The API groups generated views conservatively and avoids modifying that controller in this first pass.
- `showBoundingBox`, `showTimeAxis`, `enableInteraction`, `colorMapName`, and `transferFunctionName` are stored in config but are not fully wired to all existing scene/UI systems yet.
