# R2IFNO HKT event ground truth

- These files contain source ground truth, not neural-network predictions.
- Every timestamp is copied from the matching prediction HDF5 and aligned by exact `time_unix_hours` lookup.
- Each HDF5 field has axis order `[time, node, channel]`, with channels `[HS, Water_Level]` and units `[m, m]`.
- Ground truth is read directly from `HS_WatLev.npy` in physical source space; no normalization, denormalization, interpolation, or datum conversion is applied.
- For VR compatibility, `/prediction` contains the ground-truth values and `/ground_truth` is a hard link to the same HDF5 object (no duplicate data). Use the root attribute `data_role=ground_truth` to distinguish these files.
- Time, 12-hour forcing-window, channel, longitude/latitude, face, and node-index datasets are copied exactly from the corresponding prediction file.
- Files use the same basenames as the prediction files, so they pair one-to-one across the two directories.

## Events

| # | Event | HKT target range | UTC target range | Hours | Source |
|---:|---|---|---|---:|---|
| 1 | 正常/平稳天气 | 2021-04-21T00:00:00+08:00 – 2021-04-23T23:00:00+08:00 | 2021-04-20T16:00:00Z – 2021-04-23T15:00:00Z | 72 | 1970-2021 target source |
| 2 | 持续强降雨 | 2021-06-22T00:00:00+08:00 – 2021-06-29T23:00:00+08:00 | 2021-06-21T16:00:00Z – 2021-06-29T15:00:00Z | 192 | 1970-2021 target source |
| 3 | Kompasu，强台风/风暴增水 | 2021-10-11T00:00:00+08:00 – 2021-10-14T23:00:00+08:00 | 2021-10-10T16:00:00Z – 2021-10-14T15:00:00Z | 96 | 1970-2021 target source |
| 4 | 正常/不稳定天气 | 2022-03-08T00:00:00+08:00 – 2022-03-12T23:00:00+08:00 | 2022-03-07T16:00:00Z – 2022-03-12T15:00:00Z | 120 | 2022 target source |
| 5 | 非台风季风大风 | 2022-09-27T00:00:00+08:00 – 2022-09-29T23:00:00+08:00 | 2022-09-26T16:00:00Z – 2022-09-29T15:00:00Z | 72 | 2022 target source |
| 6 | Nalgae + 季风，复合台风事件 | 2022-10-30T00:00:00+08:00 – 2022-11-03T23:00:00+08:00 | 2022-10-29T16:00:00Z – 2022-11-03T15:00:00Z | 120 | 2022 target source |
