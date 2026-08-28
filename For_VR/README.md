# VR 事件 Prediction 与 Ground Truth 数据说明

## 1. 数据目录

本数据集包含两类逐小时海洋场数据：模型预测值（Prediction）和对应真值（Ground Truth）。



两个目录各包含 6 个事件 HDF5 文件。同一个事件在两个目录中使用完全相同的文件名，因此可以直接按文件名一一配对。

## 2. Prediction 与 Ground Truth

### Prediction

Prediction 是模型生成的逐小时空间预测结果。每个时刻同时包含：

- `HS`：有效波高
- `Water_Level`：水位

### Ground Truth

Ground Truth 是与 Prediction 完全相同时刻、相同节点顺序和相同空间网格上的参考真值。真值直接来自原始物理场数据，没有进行插值、时间平均或垂直基准转换。

## 3. 数据维度、通道和单位

Prediction 和 Ground Truth 的主数据维度均为：

```text
[time, node, channel]
```

即：

```text
[事件小时数, 7364, 2]
```

通道定义如下：

| channel 索引 | 名称 | 含义 | 单位 |
|---:|---|---|---|
| 0 | `HS` | Significant Wave Height，有效波高 | m |
| 1 | `Water_Level` | 水位 | m |

所有主场值均为 `float32`。6 个事件合计包含 672 个逐小时时刻，每一类数据共有 `672 × 7364 × 2 = 9,897,216` 个场值。

在这 672 个事件时刻内，整体数值范围为：

| 数据 | HS 范围（m） | Water_Level 范围（m） |
|---|---:|---:|
| Prediction | 0.009045 至 5.432013 | -2.483155 至 2.967173 |
| Ground Truth | 0 至 6.009716 | -5.045749 至 2.676027 |

以上范围只描述本目录中的 6 个事件，不代表完整历史数据的范围。

## 4. 经纬度与空间网格

Prediction 和 Ground Truth 使用完全相同的香港区域非结构三角网格。

### 坐标系统

- 坐标参考系统：`EPSG:4326`
- 经度、纬度单位：度（degree）
- 经度数据键：`lon`
- 纬度数据键：`lat`
- 每个节点的坐标顺序：`[longitude, latitude]`

空间覆盖范围为：

| 坐标 | 最小值 | 最大值 |
|---|---:|---:|
| 经度 | 113.650220°E | 114.659994°E |
| 纬度 | 22.0300455°N | 22.6998139°N |

### 节点与三角形

| 网格内容 | 数量 |
|---|---:|
| 空间节点 | 7,364 |
| 三角形面 | 13,445 |
| 被至少一个三角形引用的节点 | 7,357 |
| 未被任何三角形引用的节点 | 7 |

每个时刻的 Prediction 和 Ground Truth 都保存全部 7,364 个节点的 `HS` 和 `Water_Level`，节点顺序完全一致。

三角形连接数据包括：

- `faces_0based`：shape `[13445, 3]`，局部节点编号从 0 开始
- `faces_1based`：shape `[13445, 3]`，局部节点编号从 1 开始
- `node_in_face_mask`：shape `[7364]`，表示节点是否被至少一个三角形引用

未被三角形引用的 0-based 节点编号为：

```text
2575, 2576, 2577, 7318, 7319, 7332, 7363
```

这 7 个节点仍然具有 Prediction 和 Ground Truth 值。为保持节点编号和数据维度一致，不应从场值数组中删除它们；使用三角面显示时，它们自然不会出现在三角网格表面上。

## 5. 事件与时间范围

原始事件日期均按香港时间 HKT（UTC+08:00）解释。每个日期范围均包含首日 `00:00 HKT` 至末日 `23:00 HKT` 的所有整点。

| # | 事件类型 | HKT 时间范围（含首尾小时） | 对应 UTC 时间范围（含首尾小时） | 小时数 | 单文件主数据 shape |
|---:|---|---|---|---:|---|
| 1 | 正常/平稳天气 | 2021-04-21 00:00 至 2021-04-23 23:00 | 2021-04-20 16:00 至 2021-04-23 15:00 | 72 | `[72, 7364, 2]` |
| 2 | 持续强降雨 | 2021-06-22 00:00 至 2021-06-29 23:00 | 2021-06-21 16:00 至 2021-06-29 15:00 | 192 | `[192, 7364, 2]` |
| 3 | Kompasu，强台风/风暴增水 | 2021-10-11 00:00 至 2021-10-14 23:00 | 2021-10-10 16:00 至 2021-10-14 15:00 | 96 | `[96, 7364, 2]` |
| 4 | 正常/不稳定天气 | 2022-03-08 00:00 至 2022-03-12 23:00 | 2022-03-07 16:00 至 2022-03-12 15:00 | 120 | `[120, 7364, 2]` |
| 5 | 非台风季风大风 | 2022-09-27 00:00 至 2022-09-29 23:00 | 2022-09-26 16:00 至 2022-09-29 15:00 | 72 | `[72, 7364, 2]` |
| 6 | Nalgae + 季风，复合台风事件 | 2022-10-30 00:00 至 2022-11-03 23:00 | 2022-10-29 16:00 至 2022-11-03 15:00 | 120 | `[120, 7364, 2]` |

对应的 HDF5 文件名如下：

```text
event_01_normal_stable_weather_20210421_20210423.h5
event_02_persistent_heavy_rain_20210622_20210629.h5
event_03_kompasu_typhoon_storm_surge_20211011_20211014.h5
event_04_normal_unstable_weather_20220308_20220312.h5
event_05_non_typhoon_monsoon_gale_20220927_20220929.h5
event_06_nalgae_monsoon_compound_typhoon_20221030_20221103.h5
```

## 6. 时间字段

> **时间基准结论：Prediction 和 Ground Truth 的主时间轴均以 UTC 为准。用户给出的事件日期原本是 HKT，数据制作时先减去 8 小时转换为 UTC，再查找输入、Prediction 和 Ground Truth。为了便于按香港当地时间查看，每个文件也同时保存了对应的 HKT 时间文本。**

例如：

```text
2021-04-21 00:00 HKT = 2021-04-20 16:00 UTC
```

因此，主场值第一维的每个时间位置都应使用 `time_unix_hours` 或 `time_utc` 解释和对齐；如果需要在 VR 中显示香港当地时间，则使用同一位置的 `time_hkt`。不要在读取 `time_unix_hours` 后再次人为加 8 小时进行数据对齐。

每个 HDF5 同时保存 UTC 和 HKT 时间：

| 数据键 | shape | dtype | 含义 |
|---|---:|---|---|
| `time_unix_hours` | `[T]` | `int64` | 自 1970-01-01 00:00 UTC 起的整小时编号，推荐作为 Prediction/GT 对齐键 |
| `time_unix_seconds` | `[T]` | `int64` | Unix 秒 |
| `time_utc` | `[T]` | UTF-8 string | UTC 时间 |
| `time_hkt` | `[T]` | UTF-8 string | 香港时间 UTC+08:00 |

同名 Prediction 和 GT 文件的上述时间数组完全相同。`time_unix_hours` 表示绝对时刻，不需要再加 8 小时；`time_hkt` 已经是该时刻对应的香港时间显示。

文件中还保存每个预测时刻对应的 12 小时气象输入时间窗口：

- `forcing_window_time_unix_hours`：shape `[T, 12]`
- `forcing_window_global_input_index`：shape `[T, 12]`

这些窗口信息在 Prediction 和 GT 文件中保持一致，主要用于追踪 Prediction 所对应的输入时段。

## 7. HDF5 字段汇总

| 数据键 | shape | 说明 |
|---|---:|---|
| `prediction` | `[T, 7364, 2]` | Prediction 文件中是预测值；GT 文件中是 Ground Truth 的兼容别名 |
| `ground_truth` | `[T, 7364, 2]` | 仅 GT 文件包含，表示真值 |
| `channel_names` | `[2]` | `[HS, Water_Level]` |
| `channel_units` | `[2]` | `[m, m]` |
| `time_unix_hours` | `[T]` | 整小时绝对时间索引 |
| `time_unix_seconds` | `[T]` | Unix 秒 |
| `time_utc` | `[T]` | UTC 时间文本 |
| `time_hkt` | `[T]` | HKT 时间文本 |
| `forcing_window_time_unix_hours` | `[T, 12]` | 12 小时输入窗口的时间 |
| `forcing_window_global_input_index` | `[T, 12]` | 12 小时输入窗口的全局索引 |
| `lon` | `[7364]` | 节点经度，单位为度 |
| `lat` | `[7364]` | 节点纬度，单位为度 |
| `faces_0based` | `[13445, 3]` | 0-based 三角面连接 |
| `faces_1based` | `[13445, 3]` | 1-based 三角面连接 |
| `node_indices_original` | `[7364]` | 节点在原始大网格中的 0-based 编号 |
| `node_indices_original_1based` | `[7364]` | 节点在原始大网格中的 1-based 编号 |
| `node_in_face_mask` | `[7364]` | 节点是否被三角形引用 |

主场值采用逐时刻分块，chunk 为 `[1, 7364, 2]`，并使用 gzip 压缩。

## 8. 配对与使用注意事项

- Prediction 和 GT 必须按相同文件名配对
- 两个文件必须使用相同的 `time_unix_hours`
- 两类数据的节点顺序和通道顺序完全一致，可以直接逐元素比较
- 节点经纬度、三角面和节点编号在两个目录中保持一致
- GT 中优先读取 `ground_truth`；Prediction 中读取 `prediction`
- 不要删除未被三角形引用的 7 个节点
- `Water_Level` 沿用原始数据的垂直基准，本数据未做基准转换
- 当前数据说明没有明确记录 `Water_Level` 是否以 HKPD、Chart Datum 或其他基准为零点，因此不应自行标注为 HKPD 或 Chart Datum

## 9. 完整性

两类数据已经完成以下一致性检查：

- 6 个事件文件一一对应
- 共 672 个连续逐小时时刻
- 主数据 shape、节点顺序和通道顺序一致
- 时间、经纬度、三角面和节点编号一致
- 所有场值均为有限值
- `HS` 非负
- Ground Truth 与对应原始物理场逐值一致
