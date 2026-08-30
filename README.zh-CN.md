# STC SlabLab（桌面 / 平板 + VR 双模式）

[English](README.md) | 中文

本项目在原始 [VolumeSTCube](https://github.com/Kapo-Huang/VolumeSTCube) Unity 体渲染项目上增加了低耦合的 API 集成层，同时保留原有 STC 导入器、渲染器、Shader、材质和交互控制。

当前仓库包含两个独立前端模式：Desktop 支持 Windows、macOS、Android 平板和 iPad；VR 支持 Quest/OpenXR。两种模式共用数据、渲染和分析核心，分别使用鼠标/触控与头显/Touch 控制器。模式切换、操作和一键构建方式见 [桌面 / VR 双模式说明](docs/FLAT_SCREEN.zh-CN.md)。

当前版本统一支持两类时空数据：

- `XY + 时间`：X/Y 是地理空间，RAW 内部的 Z 是时间切片。
- `XYZ + 时间`：每个 RAW 是一个完整三维空间体，多份 RAW 按文件顺序组成时间序列。

两种模式共用同一个加载入口，默认根据文件名和 INI 元数据自动识别。

## 功能概览

- 复用原始 STC 体渲染、传输函数、裁剪、高亮和控制面板。
- 自动识别 `XY+T` 与 `XYZ+T` 数据布局。
- 统一 Timeline：旧模式切换 Z 时间片，新模式切换完整 XYZ 文件。
- `XYZ+T` 使用后台读取、拖动防抖和有限预取缓存，避免一次加载全部时间帧。
- 支持 Unity C#、JSON 和 FastAPI JSON Spec 调用。
- 提供明确的错误返回和 View 生命周期管理。
- 保留地图与数据体的统一场景布局，不改写原始 STC 核心渲染代码。

## 环境

- Unity：`2022.3 LTS`，当前工程版本为 `2022.3.62f3`。
- 推荐系统：Windows。
- Python：建议 3.9 及以上。

使用 Unity Hub 打开：

```text
RenderingModule/
```

主场景：

```text
RenderingModule/Assets/Scenes/mainScene.unity
```

## 项目结构

```text
STC-SlabLab-XR/
├── DataTransformationModule/       # 原始 XY+T Python 预处理流程
├── OneDrive_1_4-30-2026/           # 香港 XYZ+T 测试数据（本地存在时）
├── RenderingModule/                # Unity 工程
│   └── Assets/VolumeSTCubeAPI/      # 新增 API 与集成层
├── server_example/                 # FastAPI JSON Spec 示例
├── docs/                           # API、测试和结构文档
├── README.md
└── README.zh-CN.md
```

## 数据模式

### XY + 时间

轴语义：

```text
X = 经度/横向空间
Y = 纬度/纵向空间
Z = 时间切片
voxel = 观测变量值
```

原项目的 `DataTransformationModule/UnityRawData` 属于该模式。当前测试数据由 8 个 RAW 分块组成，每个分块为 `128 × 128 × 16`，共 128 个时间切片。

Timeline 在纹理 Z 中选择时间切片，空间上仍与底图保持配准。

### XYZ + 时间

轴语义：

```text
X/Y/Z = 三维空间
RAW 文件顺序 = 时间
voxel = 观测变量值
```

香港 `chlorophyll`、`NO3`、`salt` 数据属于该模式。每个目录包含多份同尺寸 RAW；当前数据为 30 个时间文件，每份约为 `400 × 441 × 92`。

Timeline 每次选择一份完整三维体。系统只保留当前帧和一个邻近预取帧，不会把 30 份数据同时放入内存。

### 自动识别

默认配置：

```csharp
config.dataLayout = VolumeSTCubeDataLayout.Auto;
```

多份尺寸和格式一致、文件名带显式编号时间标记（例如 `_time_0_255`）的三维 RAW 会识别为 `XYZTimeSeries`。原始 `timeWidth` 分块继续识别为 `XYTime`。

命名不明确时可以强制指定：

```csharp
config.dataLayout = VolumeSTCubeDataLayout.XYTime;
// 或
config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
```

## RAW/INI 文件要求

每个 RAW 必须有一个同目录、同名追加 `.ini` 的元数据文件：

```text
volume_salt_data_time_0_255.raw
volume_salt_data_time_0_255.raw.ini
```

INI 至少需要描述：

```text
dimx:400
dimy:441
dimz:92
skip:0
format:uint8
endianness:littleendian
```

加载目录时只读取顶层文件。RAW 与 INI 必须一一对应，文件顺序会自然排序。

## Unity 操作

打开 `mainScene.unity`，使用：

```text
Volume Rendering > Load dataset > Load RAW folder (auto XY+T or XYZ+T)
```

选择包含 RAW/INI 的文件夹：

- 选择 `DataTransformationModule/UnityRawData`：自动进入 `XY+T`。
- 选择香港变量目录，例如 `OneDrive_1_4-30-2026/chlorophyll`：自动进入 `XYZ+T`。

其他正式菜单：

```text
Volume Rendering > Load dataset > Preprocess CSV and load as XY+T
Volume Rendering > Load dataset > Clear current dataset
```

加载完成后进入 Play Mode，通过底部 Timeline 播放或选择时间。

### For_VR 香港事件数据

工作区存在 `For_VR` 时，启动器会优先打开转换后的
`For_VR/UnityRaw`，并默认选择 `Prediction_HS`。这批 HDF5 数据是
“经度 × 纬度 × 时间”的二维水面场，没有物理深度轴，因此 Unity 会：

- 在香港 OpenStreetMap 底图上绘制彩色高度曲面，而不是显示为厚体块；
- 将原始三角网格配准到 `113.65022–114.65999°E、22.03005–22.69981°N`；
- 提供上一帧、播放/暂停、下一帧和可拖动时间轴，按 HKT 显示 672 个小时帧；
- 在后续 S4D 分析中固定为单一 `Hong Kong surface` 层，仅对时间分桶。

如源 HDF5 有变化，运行以下命令重新生成 Unity RAW 和分析清单：

```powershell
.\.venv\Scripts\python.exe tools\convert_for_vr_to_unity.py
```

`XYZ+T` 首帧需要生成数据纹理；之后会后台预取邻近帧。若下一帧尚未完成，Timeline 会短暂停住而不是阻塞 Unity 主线程。手动拖动会在停止约 0.12 秒后加载最终时间点。

## C# API

外部 Unity 脚本只应依赖：

```csharp
UnityVolumeRendering.VolumeSTCubeAPI
```

不要直接调用内部 RAW 工厂、Runtime Loader、时间帧加载器或场景适配器。

### 推荐加载方式

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

if (view == null)
    Debug.LogError("数据加载失败，请查看 Unity Console。");
```

### 获取明确错误信息

界面或服务集成建议使用 `Try` 接口：

```csharp
bool loaded = VolumeSTCubeAPI.TryCreateViewFromRawDirectory(
    @"D:\data\chlorophyll",
    config,
    out VolumeSTCubeView view,
    out string error);

if (!loaded)
    statusText.text = error;
```

输入的 `VolumeSTCubeData` 和 `VolumeSTCubeConfig` 会在 API 边界复制，内部模式识别、viewId 归一化和 Timeline 状态不会修改调用方对象。

### View 控制

```csharp
VolumeSTCubeAPI.ApplyTimeFilter("hong_kong_chlorophyll", 0.3f, 0.4f);
VolumeSTCubeAPI.SetVisible("hong_kong_chlorophyll", false);

VolumeSTCubeView view = VolumeSTCubeAPI.GetView("hong_kong_chlorophyll");
view?.ApplyOpacity(0.65f);

VolumeSTCubeAPI.DestroyView("hong_kong_chlorophyll");
// 清理全部 API View：
VolumeSTCubeAPI.ClearAll();
```

`viewId` 是 View 的唯一标识。创建同名 View 时会替换已注册 View。

### 显式 RAW 列表

```csharp
VolumeSTCubeData data = new VolumeSTCubeData
{
    datasetName = "salt"
};

data.rawFilePaths.Add(@"D:\data\salt\time_0.raw");
data.iniFilePaths.Add(@"D:\data\salt\time_0.raw.ini");

VolumeSTCubeView view = VolumeSTCubeAPI.CreateView(data, config);
```

`rawFilePaths[i]` 必须与 `iniFilePaths[i]` 对应。

## JSON API

```csharp
VolumeSTCubeView view = VolumeSTCubeAPI.CreateViewFromJson(json);
```

RAW 时间序列示例：

```json
{
  "viewType": "VolumeSTCube",
  "viewId": "hong_kong_salt",
  "datasetName": "salt",
  "dataMode": "rawFiles",
  "rawFiles": [
    "D:/data/salt/time_0.raw",
    "D:/data/salt/time_1.raw"
  ],
  "iniFiles": [
    "D:/data/salt/time_0.raw.ini",
    "D:/data/salt/time_1.raw.ini"
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

`dataLayout` 可用值：`Auto`、`XYTime`、`XYZTimeSeries`。

## FastAPI 示例

安装并启动：

```bash
cd server_example
pip install -r requirements.txt
uvicorn main:app --reload --host 0.0.0.0 --port 8000
```

接口：

- `GET /api/volumestcube/example`：返回原始示例 RAW 集合的 JSON Spec。
- `POST /api/volumestcube/spec`：接收类型明确的请求并返回 Unity JSON Spec。
- `GET /docs`：FastAPI 自动接口文档。

Unity 客户端：

```csharp
VolumeSTCubeServerClient client = GetComponent<VolumeSTCubeServerClient>();
client.ViewLoaded += view => Debug.Log($"Loaded {view.viewId}");
client.RequestFailed += error => statusText.text = error;
client.LoadExampleFromServer();
```

FastAPI 不执行渲染，只负责整理和返回 Spec；实际渲染仍在 Unity 中进行。

## 架构

```text
外部 C# / JSON / FastAPI
          |
   VolumeSTCubeAPI            对外门面
      /          \
目录数据源       View 注册表
      |
 Runtime Loader               加载流程编排
    /          \
RAW 工厂       XYZ 时间序列状态与缓存
                    |
             时间帧替换器
                    |
       场景适配器 + 原始 STC 渲染器
```

职责划分：

- 数据布局识别器只判断 `XY+T / XYZ+T`。
- RAW 工厂只处理 RAW/INI 和原始 STC 对象创建。
- 时间序列组件只维护索引、异步任务和有限缓存。
- 时间帧替换器只负责替换可见体并恢复渲染状态。
- 场景适配器只负责地图、相机、控制器和 UI 布局。
- View 注册表只负责对象查找与生命周期记录。

## 原始 XY+T 预处理

`DataTransformationModule` 保留原项目的数据处理流程：

```text
点位 CSV
-> 坐标投影
-> 克里金插值
-> 中国边界裁剪
-> 空间/时间平滑
-> uint8 归一化
-> RAW/INI 导出
-> Unity XY+T 渲染
```

主要脚本：

- `exampleData/0_exampleDataMerge.py`
- `1_KrigingInterpolation.py`
- `2_Smooth.py`

常用 Python 依赖包括 `geopandas`、`pykrige`、`pyproj`、`pandas`、`numpy` 和 `tqdm`。

## 验证

代码检查包括：

- Unity Runtime C# 编译。
- Unity Editor C# 编译。
- FastAPI/Pydantic schema 与 Spec 生成检查。
- 原始 8 文件数据自动识别为 `XY+T`。
- 香港 30 文件数据自动识别为 `XYZ+T`。

手动验证建议：

1. 退出 Play Mode。
2. 打开 `mainScene.unity`。
3. 使用统一 RAW 文件夹入口加载数据。
4. 确认 Console 无红色异常。
5. 进入 Play Mode，检查地图、控制面板和 Timeline。
6. 分别测试旧 `XY+T` 和新 `XYZ+T` 目录。

## 已知限制

- RAW/INI 本身通常不包含经纬度边界和坐标系。没有地理范围元数据时，系统只能按场景预设缩放，无法保证香港数据与底图像素级配准。
- `XYZ+T` 的首次纹理生成仍需要时间；缓存目标是保持界面响应并平滑相邻切换，不是把全部数据常驻内存。
- FastAPI 只返回数据描述，不负责生成图片或视频。
- `CreateViewFromCsvRaw` 是 Unity 端快速网格预览，不等价于原 Python 克里金、裁剪和平滑流程。

## 进一步文档

- [外部 API 详细说明](docs/API_USAGE.md)
- [API 当前限制](docs/API_LIMITATIONS.md)
- [测试计划](docs/TEST_PLAN.md)
- [项目结构](docs/PROJECT_STRUCTURE_SUMMARY.md)
- [FastAPI 示例](server_example/README.md)

数据来源链接（原仓库提供）：[Google Drive](https://drive.google.com/drive/folders/1YM0BodLTbHRy8Y4qby6m92QR1LFT0hOO?usp=sharing)
