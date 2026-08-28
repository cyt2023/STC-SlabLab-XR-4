# STC SlabLab Flat (Desktop / Tablet)

English | [中文](README.zh-CN.md)

STC SlabLab Flat is the monitor and tablet edition of the interactive Unity workbench for exploring continuous `XYZ + time` scientific fields, grounding an XY depth slab, and sending selected data to MatPlotAgent for natural-language 2D analysis. It supports Windows, macOS, Android tablets, and iPad with mouse, keyboard, and touch gestures; see the [flat-screen guide](docs/FLAT_SCREEN.zh-CN.md). The Quest/OpenXR build path remains available.

It builds on the original [VolumeSTCube](https://github.com/Kapo-Huang/VolumeSTCube) Unity project while preserving its importer, volume renderer, shaders, materials, and controls.

It supports two data layouts through one loading workflow:

- `XY + time`: X/Y are geographic space and RAW Z stores time slices.
- `XYZ + time`: each RAW file is a complete 3D volume and file order is time.

The default `Auto` mode distinguishes them from filenames and matching INI metadata.

## Highlights

- Reuses the original STC renderer and interaction controls.
- Automatically detects `XY+T` and `XYZ+T` collections.
- Uses one timeline: Z-slice selection for XY+T and file switching for XYZ+T.
- Keeps XYZ+T responsive with background loading, scrub debounce, and bounded prefetch.
- Exposes C#, JSON, and FastAPI JSON Spec interfaces.
- Returns actionable loading errors and manages view lifetime by `viewId`.
- Leaves original STC rendering internals unchanged behind the integration layer.

## Requirements

- Unity `2022.3 LTS` (`2022.3.62f3` is the current project version).
- Windows is the primary tested platform.
- Python 3.9 or newer is recommended for preprocessing and the server example.

Open this directory from Unity Hub:

```text
RenderingModule/
```

Main scene:

```text
RenderingModule/Assets/Scenes/mainScene.unity
```

## Repository layout

```text
STC-SlabLab-XR/
├── DataTransformationModule/       # Original XY+T Python preprocessing
├── OneDrive_1_4-30-2026/           # Local Hong Kong XYZ+T test data, when present
├── RenderingModule/                # Unity project
│   └── Assets/VolumeSTCubeAPI/      # API and integration layer
├── server_example/                 # FastAPI JSON Spec example
├── docs/                           # API, test, and structure documentation
├── README.md
└── README.zh-CN.md
```

## Data layouts

### XY + time

```text
X/Y = geographic space
RAW texture Z = time samples
voxel = observed variable
```

`DataTransformationModule/UnityRawData` uses this layout. The current sample consists of eight `128 x 128 x 16` chunks, for 128 time samples in total.

The timeline selects Z time slices while the spatial footprint remains aligned with the map.

### XYZ + time

```text
X/Y/Z = 3D space
ordered RAW files = time
voxel = observed variable
```

The Hong Kong `chlorophyll`, `NO3`, and `salt` folders use this layout. The current datasets contain 30 time files of approximately `400 x 441 x 92` each.

Only the current volume and one adjacent prefetched dataset are retained. The loader does not place all 30 volumes in memory.

### Automatic detection

```csharp
config.dataLayout = VolumeSTCubeDataLayout.Auto;
```

Multiple equally shaped 3D RAW files with an explicit numbered time token such as `_time_0_255` are detected as `XYZTimeSeries`. Original `timeWidth` chunk files remain `XYTime`.

Override ambiguous third-party naming explicitly when necessary:

```csharp
config.dataLayout = VolumeSTCubeDataLayout.XYTime;
// or
config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
```

## RAW/INI contract

Each RAW file requires a matching `.raw.ini` file:

```text
volume_salt_data_time_0_255.raw
volume_salt_data_time_0_255.raw.ini
```

Example metadata:

```text
dimx:400
dimy:441
dimz:92
skip:0
format:uint8
endianness:littleendian
```

Directory loading reads top-level files only. RAW/INI entries must pair one-to-one and are naturally sorted.

## Unity workflow

Open `mainScene.unity`, then use:

```text
Volume Rendering > Load dataset > Load RAW folder (auto XY+T or XYZ+T)
```

- Select `DataTransformationModule/UnityRawData` for the original XY+T data.
- Select a Hong Kong variable folder such as `OneDrive_1_4-30-2026/chlorophyll` for XYZ+T.

The other supported entries are:

```text
Volume Rendering > Load dataset > Preprocess CSV and load as XY+T
Volume Rendering > Load dataset > Clear current dataset
```

After loading, enter Play Mode and use the bottom timeline. XYZ+T prepares the first texture, then prefetches an adjacent frame. Playback waits for an unavailable frame instead of blocking the Unity main thread. Scrubbing applies the final selection after roughly 0.12 seconds of inactivity.

## C# API

External scripts should depend on `UnityVolumeRendering.VolumeSTCubeAPI` only. Importers, runtime loaders, frame loaders, caches, and scene adapters are implementation details.

### Recommended loading call

```csharp
using UnityVolumeRendering;

VolumeSTCubeConfig config =
    VolumeSTCubeConfig.Default("hong_kong_chlorophyll");

config.dataLayout = VolumeSTCubeDataLayout.Auto;
config.showTimeline = true;
config.timelineAutoPlay = false;
config.opacity = 0.8f;

VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromRawDirectory(
    @"D:\data\chlorophyll",
    config);
```

For a user-facing error message:

```csharp
bool loaded = VolumeSTCubeAPI.TryCreateViewFromRawDirectory(
    @"D:\data\chlorophyll",
    config,
    out VolumeSTCubeView view,
    out string error);

if (!loaded)
    statusText.text = error;
```

The API copies `VolumeSTCubeData` collections and `VolumeSTCubeConfig` at the call boundary. Internal ID normalization, layout detection, and timeline updates do not mutate caller-owned request objects.

### View control and lifetime

```csharp
VolumeSTCubeAPI.ApplyTimeFilter("hong_kong_chlorophyll", 0.3f, 0.4f);
VolumeSTCubeAPI.SetVisible("hong_kong_chlorophyll", false);

VolumeSTCubeView view = VolumeSTCubeAPI.GetView("hong_kong_chlorophyll");
view?.ApplyOpacity(0.65f);

VolumeSTCubeAPI.DestroyView("hong_kong_chlorophyll");
VolumeSTCubeAPI.ClearAll();
```

`viewId` is the registry key. Creating a view with the same ID replaces the registered view.

## JSON API

```csharp
VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromJson(json);
```

Example RAW series:

```json
{
  "viewType": "VolumeSTCube",
  "viewId": "hong_kong_salt",
  "datasetName": "salt",
  "dataMode": "rawFiles",
  "rawFiles": ["D:/data/salt/time_0.raw", "D:/data/salt/time_1.raw"],
  "iniFiles": ["D:/data/salt/time_0.raw.ini", "D:/data/salt/time_1.raw.ini"],
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

## FastAPI example

```bash
cd server_example
pip install -r requirements.txt
uvicorn main:app --reload --host 0.0.0.0 --port 8000
```

Endpoints:

- `GET /api/volumestcube/example`
- `POST /api/volumestcube/spec`
- `GET /docs` for generated OpenAPI documentation

Unity client:

```csharp
VolumeSTCubeServerClient client = GetComponent<VolumeSTCubeServerClient>();
client.ViewLoaded += view => Debug.Log($"Loaded {view.viewId}");
client.RequestFailed += error => statusText.text = error;
client.LoadExampleFromServer();
```

The server prepares JSON specs; rendering remains inside Unity.

## Architecture

```text
External C# / JSON / FastAPI
             |
      VolumeSTCubeAPI
        /           \
directory source   view registry
        |
    runtime loader
      /         \
RAW factory     XYZ time-series state/cache
                       |
                time-frame loader
                       |
          scene adapter + original STC renderer
```

- Layout detection decides dimension semantics only.
- The RAW factory owns RAW/INI import and original STC object creation.
- The time-series component owns indices, async work, and bounded cache policy.
- The frame loader replaces one visible volume and restores render state.
- The scene adapter owns map, camera, controller, and UI layout integration.
- The registry owns view lookup and lifetime bookkeeping.

## Original XY+T preprocessing

`DataTransformationModule` retains the original path:

```text
point CSV
-> coordinate projection
-> kriging interpolation
-> China boundary clipping
-> spatial/temporal smoothing
-> uint8 normalization
-> RAW/INI export
-> Unity XY+T rendering
```

Main scripts:

- `exampleData/0_exampleDataMerge.py`
- `1_KrigingInterpolation.py`
- `2_Smooth.py`

Common Python dependencies include `geopandas`, `pykrige`, `pyproj`, `pandas`, `numpy`, and `tqdm`.

## Validation

The integration is checked with:

- Unity runtime C# compilation.
- Unity Editor C# compilation.
- FastAPI/Pydantic schema and spec generation.
- Original eight-file collection detection as XY+T.
- Hong Kong 30-file collection detection as XYZ+T.

Recommended manual check:

1. Exit Play Mode.
2. Open `mainScene.unity`.
3. Load a directory through the unified menu.
4. Confirm the Console has no red exceptions.
5. Enter Play Mode and inspect the map, controls, and timeline.
6. Repeat for one XY+T and one XYZ+T directory.

## Known limitations

- RAW/INI commonly omit geographic bounds and CRS metadata. Without those values, a third-party dataset cannot be guaranteed to align pixel-perfectly with the basemap.
- The first XYZ+T texture still requires preparation time. Bounded caching targets responsive interaction without keeping every volume resident.
- FastAPI does not render images or video.
- `CreateViewFromCsvRaw` is a quick Unity grid preview, not the original Python kriging, clipping, and smoothing pipeline.

## Documentation

- [External API](docs/API_USAGE.md)
- [API limitations](docs/API_LIMITATIONS.md)
- [Test plan](docs/TEST_PLAN.md)
- [Project structure](docs/PROJECT_STRUCTURE_SUMMARY.md)
- [FastAPI example](server_example/README.md)

Original data link: [Google Drive](https://drive.google.com/drive/folders/1YM0BodLTbHRy8Y4qby6m92QR1LFT0hOO?usp=sharing)
