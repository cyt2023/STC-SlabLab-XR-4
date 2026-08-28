# STC SlabLab 平面版

平面版复用 XR 版本的数据、体渲染、香港底图、时间轴和 S4D/MatPlot 分析工作流，输入层改为电脑与平板可直接使用的方式。无需头显或手柄。

## 支持平台

- Windows 64 位、macOS
- Android 平板（横屏）
- iPad（横屏，通过 Xcode 工程导出）
- Unity Editor Play Mode

Unity 版本为 `2022.3.62f3`，使用 Unity Hub 打开 `RenderingModule/`，主场景是 `Assets/Scenes/mainScene.unity`。

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
VolumeSTCube > Flat Screen > Configure Current Platform
VolumeSTCube > Flat Screen > Build macOS
VolumeSTCube > Flat Screen > Build Windows 64-bit
VolumeSTCube > Flat Screen > Build Android Tablet APK
VolumeSTCube > Flat Screen > Export iPad Xcode Project
```

构建结果写入 `RenderingModule/Builds/`（已被 Git 忽略）。构建脚本会添加 `SLABLAB_FLAT` 编译标记、关闭该平台的 XR 自动启动、设置横屏和平面版应用信息。

## 后端

Windows 使用根目录的 `Start-Backend.cmd`；macOS/Linux 使用：

```bash
./Start-Backend.sh
./Stop-Backend.sh
```

首次运行仍需按 `docs/BACKEND_SETUP.zh-CN.md` 创建 Python 虚拟环境和安装依赖。平板访问另一台电脑上的后端时，请把服务地址改成局域网 IP，并确保防火墙允许端口 `8010`、`8020`。

平板系统不允许 Unity 直接弹出任意目录选择器。请先把 RAW/INI 数据目录复制到应用的 `Documents/Datasets`（iPad）或应用外部文件目录的 `Datasets`（Android），再在导入页点击 `RESCAN`；导入页会显示该设备的准确路径。

## 与 Quest 版本并存

Quest 构建入口仍然保留。平面版通过 `SLABLAB_FLAT` 选择鼠标/触控输入；Quest 构建脚本会继续使用 OpenXR 和 Touch 控制器。切换目标平台后，应先执行对应的 Configure 菜单项再构建。
