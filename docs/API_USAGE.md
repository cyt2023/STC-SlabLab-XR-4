# VolumeSTCube External API

## Public entry point

External Unity scripts should call `UnityVolumeRendering.VolumeSTCubeAPI` only. Do not call `RawDatasetImporter`, `VolumeObjectFactory`, `VolumeSTCubeRuntimeLoader`, the time-frame loader, or the scene adapter directly.

The recommended RAW-directory call is:

```csharp
using UnityVolumeRendering;

VolumeSTCubeConfig config = VolumeSTCubeConfig.Default("hong_kong_chlorophyll");
config.dataLayout = VolumeSTCubeDataLayout.Auto;
config.showTimeline = true;
config.timelineAutoPlay = false;

VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromRawDirectory(
    @"D:\data\chlorophyll",
    config);

if (view == null)
    Debug.LogError("Dataset loading failed; see the preceding Console error.");
```

Every top-level `.raw` file must have a matching `.raw.ini` file. Files are naturally sorted before loading.

## Error-aware loading

UI and service integrations should use the non-throwing overload so they can show a useful error to the caller:

```csharp
bool loaded = VolumeSTCubeAPI.TryCreateViewFromRawDirectory(
    @"D:\data\chlorophyll",
    config,
    out VolumeSTCubeView view,
    out string error);

if (!loaded)
    statusText.text = error;
```

`view` is non-null only when the method returns `true`. The API reports validation and directory errors through `error`; lower-level renderer details are also written to the Unity Console.

## XY+T and XYZ+T

`config.dataLayout` controls dimension semantics:

| Value | Meaning | Timeline behavior |
| --- | --- | --- |
| `Auto` | Detect from file names and matching INI dimensions | Recommended default |
| `XYTime` | X/Y are spatial and texture Z contains time slices | Timeline clips/steps through Z |
| `XYZTimeSeries` | Each RAW file is one complete XYZ volume; file order is time | Timeline switches RAW files |

Auto detection treats a collection as `XYZTimeSeries` when it contains multiple equally shaped 3D RAW files whose names contain an explicit numbered time token such as `_time_0_255`. The original STC `timeWidth` chunk files remain `XYTime`.

If a third-party naming convention is ambiguous, override it explicitly:

```csharp
config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
```

XYZ+T uses bounded caching: the current frame and one adjacent preloaded frame. Loading and data-texture preparation run asynchronously; the timeline waits for a requested frame instead of blocking the Unity main thread.

## Explicit data model

Use `CreateView` when paths are already supplied by another service:

```csharp
VolumeSTCubeData data = new VolumeSTCubeData
{
    datasetName = "salinity"
};
data.rawFilePaths.Add(@"D:\data\salt\volume_salt_data_time_0_255.raw");
data.iniFilePaths.Add(@"D:\data\salt\volume_salt_data_time_0_255.raw.ini");

VolumeSTCubeView view = VolumeSTCubeAPI.CreateView(data, config);
```

The RAW and INI lists are positional: `iniFilePaths[i]` describes `rawFilePaths[i]`. Populate one source kind per request: RAW paths, a CSV file, or direct point arrays.

## View control and lifetime

```csharp
VolumeSTCubeAPI.ApplyTimeFilter("hong_kong_chlorophyll", 0.3f, 0.4f);
VolumeSTCubeAPI.SetVisible("hong_kong_chlorophyll", false);

VolumeSTCubeView view = VolumeSTCubeAPI.GetView("hong_kong_chlorophyll");
view?.ApplyOpacity(0.65f);

VolumeSTCubeAPI.DestroyView("hong_kong_chlorophyll");
// Or remove every API-owned view:
VolumeSTCubeAPI.ClearAll();
```

`viewId` is the identity key. Creating another view with the same ID replaces the registered view. Call `DestroyView` when a view is no longer required so runtime datasets and timeline objects can be released.

The API copies `VolumeSTCubeData` collections and `VolumeSTCubeConfig` at the call boundary. Loading may normalize the internal view ID, detected layout, and timeline window, but it does not mutate the caller's request objects.

## JSON input

```csharp
VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromJson(json);
```

Recommended RAW JSON:

```json
{
  "viewType": "VolumeSTCube",
  "viewId": "hong_kong_salt",
  "datasetName": "salt",
  "dataMode": "rawFiles",
  "rawFiles": [
    "D:/data/salt/volume_salt_data_time_0_255.raw",
    "D:/data/salt/volume_salt_data_time_1_255.raw"
  ],
  "iniFiles": [
    "D:/data/salt/volume_salt_data_time_0_255.raw.ini",
    "D:/data/salt/volume_salt_data_time_1_255.raw.ini"
  ],
  "render": {
    "mode": "Volume",
    "dataLayout": "Auto",
    "showTimeline": true,
    "timelineAutoPlay": false,
    "timelinePlaybackSeconds": 10.0,
    "opacity": 0.8
  }
}
```

Valid `dataLayout` strings are `Auto`, `XYTime`, and `XYZTimeSeries`.

## Point and CSV compatibility calls

Direct points:

```csharp
VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromPoints(
    xValues,
    yValues,
    timeValues,
    valueValues,
    VolumeSTCubeConfig.Default("points"));
```

CSV point data:

```csharp
VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromCsv(
    @"D:\data\points.csv",
    "longitude",
    "latitude",
    "time_index",
    "temperature",
    VolumeSTCubeConfig.Default("csv_points"));
```

`CreateViewFromCsvRaw` is a quick Unity-side grid preview. It is not equivalent to the original Python kriging, clipping, and smoothing pipeline.

## Unity menu

The supported editor workflow contains three entries:

```text
Volume Rendering > Load dataset > Load RAW folder (auto XY+T or XYZ+T)
Volume Rendering > Load dataset > Preprocess CSV and load as XY+T
Volume Rendering > Load dataset > Clear current dataset
```

## Internal dependency direction

```text
External script / JSON / server response
                  |
          VolumeSTCubeAPI
            /           \
 directory source     view registry
          |
      runtime loader
       /          \
raw volume factory  XYZ time-series state
                         |
                  time-frame loader
                         |
             original STC renderer + scene adapter
```

The original STC importer, renderer, shaders, and controller remain behind the integration layer.
