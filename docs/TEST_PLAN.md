# VolumeSTCube Test Plan

## One-click API smoke test

This is the fastest acceptance test for the unified API wrapper.

1. Open `RenderingModule` as the Unity project.
2. Open any scene, for example `Assets/Scenes/mainScene.unity`.
3. Use `Volume Rendering > Test > One-click API Smoke Test`.
4. Pass criteria:
   - Console logs `VolumeSTCubeJsonRunner.RenderJson succeeded`.
   - Console logs that a runtime `VolumeDataset` was generated from point data.
   - The scene hierarchy contains `VolumeSTCubeView_one_click_point_test`.
   - The generated view contains at least one `VolumeRenderedObject`.
   - The Scene or Game view displays the volume.
5. Clean up with `Volume Rendering > Test > Clear One-click Smoke Test`.

Test data: this path uses built-in inline point samples inside `RenderingModule/Assets/Editor/VolumeSTCubeOneClickTest.cs`, so it does not require external raw data.

## Original manual loading test

1. Open `RenderingModule` as the Unity project.
2. Use `Volume Rendering > Load dataset > Load raw dataset`.
3. Select an existing `.raw` file from the generated Unity raw data folder, for example `volume_salt_data_time_0_255.raw`.
4. Keep the matching `.raw.ini` file next to it, for example `volume_salt_data_time_0_255.raw.ini`.
5. Confirm a `VolumeRenderedObject` appears and renders as before.

## API raw loading test

1. Create a small MonoBehaviour that constructs `VolumeSTCubeData` with `.raw` and `.ini` paths.
2. Call `VolumeSTCubeAPI.CreateView(data, VolumeSTCubeConfig.Default("api_raw_test"))`.
3. Confirm the created hierarchy contains `VolumeSTCubeView_api_raw_test` and one or more `VolumeRenderedObject` children.
4. Confirm the original renderer material and volume texture are used.

## JSON loading test

1. In a scene, create an empty GameObject named `VolumeSTCubeJsonRunner`.
2. Add the `VolumeSTCubeJsonRunner` component.
3. Either paste a JSON spec into `jsonSpec` and use the component context menu `Render JSON Spec`, or fill `rawFilePath` and `iniFilePath` and use `Build JSON From Paths And Render`.
4. Confirm the console logs `VolumeSTCubeJsonRunner.RenderJson succeeded`.
5. Confirm the scene hierarchy contains `VolumeSTCubeView_<viewId>` and at least one `VolumeRenderedObject`.
6. Confirm the rendered image appears in the Scene/Game view.
7. Call `VolumeSTCubeAPI.SetVisible`, `ApplyTimeFilter`, and `DestroyView` and check logs for successful actions or documented warnings.

## Direct point-data loading test

1. Create a JSON spec with `dataMode: "pointData"` and a `points` array containing `x`, `y`, `t`, and `variable`.
2. Set `render.mode` to `Volume`.
3. Optionally set `pointGrid.dimX`, `pointGrid.dimY`, `pointGrid.dimT`, and `pointGrid.splatRadius`.
4. Call `VolumeSTCubeAPI.CreateViewFromJson(json)` or use `VolumeSTCubeJsonRunner`.
5. Pass criteria:
   - `CreateViewFromJson` returns a non-null `VolumeSTCubeView`.
   - Unity logs that a `VolumeDataset` was generated from point data.
   - The scene hierarchy contains a `VolumeRenderedObject`.
   - The Scene/Game view displays a volume visualization.
6. Repeat with `render.mode` set to `PointPreview` and confirm the API shows point spheres instead of a volume.

## Direct point-data C# API test

1. Prepare four lists for one point-data collection: `x`, `y`, `t`, and `variable`.
2. Make sure all four lists have the same length.
3. Call `VolumeSTCubeAPI.CreateViewFromPoints(x, y, t, variable, config)`.
4. Confirm the scene displays a volume visualization generated from the full point collection.

## CSV point-data loading test

1. Put a CSV file in the workspace or another Unity-accessible path.
2. Either use default headers `x`, `y`, `t`, and `variable`, or specify custom column names through `CreateViewFromCsv(csvPath, xColumn, yColumn, tColumn, variableColumn, config)` / JSON `csvColumns`.
3. Call `VolumeSTCubeAPI.CreateViewFromCsv(...)` or pass JSON with `dataMode: "pointData"` and `csvFile`.
4. Confirm Unity logs that point rows were loaded from CSV.
5. Confirm Unity logs that a `VolumeDataset` was generated from point data.
6. Confirm the scene displays a volume visualization.

## Unity internal API acceptance test

This is the required acceptance path for proving other Unity code can call the wrapper safely.

1. Use known-good `.raw` and `.ini` files that already work through `Volume Rendering > Load dataset > Load raw dataset`, or prepare a point-data JSON spec.
2. Put those same paths into a JSON spec, or use `dataMode: "pointData"` with `x`, `y`, `t`, and `variable`.
3. Call `VolumeSTCubeAPI.CreateViewFromJson(json)` from a MonoBehaviour, UI button, or `VolumeSTCubeJsonRunner`.
4. Pass criteria:
   - `CreateViewFromJson` returns a non-null `VolumeSTCubeView`.
   - `VolumeSTCubeView.rootObject` is non-null.
   - `VolumeSTCubeView.volumeObjects.Count > 0`.
   - Every created volume object has a `VolumeRenderedObject` component.
   - The child `VolumeContainer` has a `MeshRenderer`.
   - The scene displays the volume.
5. If this test fails, treat it as an API blocker before adding more server features.

## Server-to-Unity loading test

1. Start the FastAPI server from `server_example/`.
2. Add `VolumeSTCubeServerClient` to a Unity GameObject.
3. Set `serverBaseUrl` to `http://localhost:8000`.
4. Call `LoadExampleFromServer()`.
5. Confirm Unity logs request start, response received, render started, and render succeeded or a clear path/config error.
