# STC SlabLab 桌面 / VR 双模式

项目保留两个独立前端模式，共用 Dataset、体渲染、香港底图、时间轴和 S4D/MatPlot 分析核心：

- `Desktop`：电脑和平板使用鼠标、键盘或触控，无需头显。
- `VR`：Quest 使用 OpenXR、头显跟踪和 Touch 控制器。

模式在应用启动前确定，不在运行过程中热切换，因为切换 XR Loader 需要重新启动 Player。

## 支持平台

- Windows 64 位、macOS
- Android 平板（横屏）
- iPad（横屏，通过 Xcode 工程导出）
- Unity Editor Play Mode

Unity 版本为 `2022.3.62f3`，使用 Unity Hub 打开 `RenderingModule/`，主场景是 `Assets/Scenes/mainScene.unity`。

## 在 Editor 中切换模式

在进入 Play Mode 前选择：

```text
VolumeSTCube > Mode > Desktop
VolumeSTCube > Mode > VR
```

也可以用 `Start Desktop` 或 `Start VR` 选择模式并直接进入 Play Mode。模式切换后需要重新开始 Play，已经运行的场景不会在中途更换输入系统。

## 操作

| 功能 | 电脑 | 平板 |
| --- | --- | --- |
| 选择按钮/对象 | 鼠标左键 | 单指点按 |
| 拖动令牌/边界 | 按住鼠标左键拖动 | 单指按住拖动 |
| 转动视角 | 鼠标右键拖动 | 双指拖动 |
| 前后移动 | 鼠标滚轮 | 双指缩放 |
| 平移 | `W/A/S/D`，`Shift` 加速 | — |
| 显示/隐藏菜单 | `Tab` 或左上角 Menu | 左上角 Menu |
| 重置布局 | 左上角 Reset | 左上角 Reset |
| 操作说明 | `H`、`F1` 或 Help | 左上角 Help |

顶层 HUD 会自动避开 iPad/Android 的安全区域，并按屏幕尺寸缩放。

## 构建

在 Unity 菜单中选择：

```text
VolumeSTCube > Desktop > Configure Current Platform
VolumeSTCube > Desktop > Build macOS
VolumeSTCube > Desktop > Build Windows 64-bit
VolumeSTCube > Desktop > Build Android Tablet APK
VolumeSTCube > Desktop > Export iPad Xcode Project
```

构建结果写入 `RenderingModule/Builds/`（已被 Git 忽略）。桌面构建脚本会启用 `SLABLAB_DESKTOP`（并暂时保留兼容标记 `SLABLAB_FLAT`）、移除 `SLABLAB_VR`，同时关闭该平台的 XR 自动启动。

## 后端

Windows 使用根目录的 `Start-Backend.cmd`；macOS/Linux 使用：

```bash
./Start-Backend.sh
./Stop-Backend.sh
```

首次运行仍需按 `docs/BACKEND_SETUP.zh-CN.md` 创建 Python 虚拟环境和安装依赖。平板访问另一台电脑上的后端时，请把服务地址改成局域网 IP，并确保防火墙允许端口 `8010`、`8020`。

平板系统不允许 Unity 直接弹出任意目录选择器。请先把 RAW/INI 数据目录复制到应用的 `Documents/Datasets`（iPad）或应用外部文件目录的 `Datasets`（Android），再在导入页点击 `RESCAN`；导入页会显示该设备的准确路径。

## 与 Quest 版本并存

Quest 使用以下入口：

```text
VolumeSTCube > Quest > Configure Project
VolumeSTCube > Quest > Build APK
```

Quest 构建脚本会移除所有桌面标记、启用 `SLABLAB_VR`，并配置 OpenXR 和 Touch 控制器。两个构建脚本会主动清除对方的标记，因此可以在同一个 Unity 工程中安全切换桌面、Android 平板和 Quest 构建。
