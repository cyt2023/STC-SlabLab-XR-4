# R2IFNO HKT event predictions

- The six requested date ranges are interpreted as inclusive full Hong Kong-time (UTC+08:00) hourly days.
- Each HDF5 `prediction` dataset has axis order `[time, node, channel]`.
- Channel order is `[HS, Water_Level]`; values are denormalized model-space physical predictions in metres.
- Every output at time `t` uses the 12 forcing hours `t-11 ... t` (`lead_hours=0`).
- `time_utc` and `time_hkt` are both saved; `time_unix_hours` is the authoritative numeric key.
- Water level uses the same vertical datum as the training target; no vertical-datum conversion was applied.
- The mesh contains 7 predicted nodes not referenced by any triangular face; `node_in_face_mask` identifies all 7,364 nodes that can/cannot be rendered by the face mesh.
- These files contain predictions only, not ground truth.

## Events

| # | Event | HKT target range | UTC target range | Hours |
|---:|---|---|---|---:|
| 1 | 正常/平稳天气 | 2021-04-21T00:00:00+08:00 – 2021-04-23T23:00:00+08:00 | 2021-04-20T16:00:00Z – 2021-04-23T15:00:00Z | 72 |
| 2 | 持续强降雨 | 2021-06-22T00:00:00+08:00 – 2021-06-29T23:00:00+08:00 | 2021-06-21T16:00:00Z – 2021-06-29T15:00:00Z | 192 |
| 3 | Kompasu，强台风/风暴增水 | 2021-10-11T00:00:00+08:00 – 2021-10-14T23:00:00+08:00 | 2021-10-10T16:00:00Z – 2021-10-14T15:00:00Z | 96 |
| 4 | 正常/不稳定天气 | 2022-03-08T00:00:00+08:00 – 2022-03-12T23:00:00+08:00 | 2022-03-07T16:00:00Z – 2022-03-12T15:00:00Z | 120 |
| 5 | 非台风季风大风 | 2022-09-27T00:00:00+08:00 – 2022-09-29T23:00:00+08:00 | 2022-09-26T16:00:00Z – 2022-09-29T15:00:00Z | 72 |
| 6 | Nalgae + 季风，复合台风事件 | 2022-10-30T00:00:00+08:00 – 2022-11-03T23:00:00+08:00 | 2022-10-29T16:00:00Z – 2022-11-03T15:00:00Z | 120 |
