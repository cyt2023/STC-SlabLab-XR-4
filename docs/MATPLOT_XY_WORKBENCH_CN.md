# VolumeSTCube → MatPlotAgent 二维切片工作台

运行 Unity 场景后，系统会自动显示 `VolumeSTCube XY Slice Workbench`。按 `F8` 可以显示或隐藏工作台。

## 数据流程

```text
XYZ+T RAW 文件夹
→ 选择数据集
→ 选择 Z 层
→ 查看同一 Z 层的全部时间节点并选择时间
→ 输入自然语言绘图要求
→ VolumeSTCube 提取 x,y,value CSV
→ MatPlotAgent 生成并全屏展示二维图表
```

默认数据根目录为仓库下的 `OneDrive_1_4-30-2026`。也可以设置环境变量：

```powershell
$env:VOLUMESTC_DATA_ROOT = 'D:\data\my_xyz_time_data'
```

数据根目录的每个一级子目录会成为一个可选数据集。每个数据集必须包含按时间编号的 `.raw` 文件以及一一对应的 `.raw.ini` 文件。

## 启动 MatPlotAgent

在运行 Unity 前启动本地 MatPlotAgent 服务：

```powershell
cd C:\Users\cyt\Desktop\matplotagent\MatPlotAgent
python -m uvicorn api_server:app --host 127.0.0.1 --port 8010
```

工作台默认访问 `http://127.0.0.1:8010`，也可以直接在界面中修改。

## 分步界面

工作台采用单页向导，每次只显示当前步骤：

1. 首先单独显示所有相互分开的 Z 层缩略图，点击一层后自动进入下一步。
2. 单独按时间顺序显示该 Z 层的全部 XY 预览。样例数据有 30 个时间节点，因此显示 30 张卡片；点击一张后加载对应的完整 XYZ 体数据。
3. 单独显示自然语言输入界面，例如输入“生成 value 的直方图”或“生成 XY 热力图”，然后点击 `Extract XY and generate chart`。
4. MatPlotAgent 完成后，生成的二维图会占据工作台的主要显示区域。

顶部步骤条和每页的返回按钮可以重新选择 Z、时间节点或修改自然语言要求。

临时 CSV 保存在 `Application.temporaryCachePath/VolumeSTCubeMatPlot`。MatPlotAgent 不会收到原始 XYZ+T RAW 文件。

当前 OneDrive 样例中的 `value` 是用于渲染的 `uint8` 值。工作台会明确告诉 MatPlotAgent 不要虚构物理单位。如果以后提供每个变量的归一化参数，可在切片导出层增加反归一化字段。
