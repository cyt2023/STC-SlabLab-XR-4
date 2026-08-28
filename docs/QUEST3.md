# Meta Quest 3 空间工作台

Quest 版本使用 Android ARM64 + OpenXR，并提供以连续数据立方体为中心的 Slab Lab 工作流：

1. **Continuous Field**：选择 chlorophyll、NO3 或 salt，并通过 30 节点时间轨道查看连续 XYZ+T 场。
2. **Configure Slab**：用右手柄扳机直接拖动立方体中的发光平面，选择真实 Z 层。
3. **Fill-Matrix**：将 before / during / after 与 surface / mid / seafloor 组合成 Time × Depth 3×3 热图。
4. **Ground-to-Verify**：在 slab 上拖出 XY 区域，将数据标记为 `selected` 和 `rest`。
5. **Result**：通过自然语言把区域切片发送给 MatPlotAgent，并大尺寸显示分析图。

## Unity 构建

1. 使用 Unity 2022.3.62f3 打开 `RenderingModule`。
2. 等待 Package Manager 导入 XR Plugin Management 和 OpenXR。
3. 执行 `VolumeSTCube > Quest > Configure Project`。
4. 执行 `VolumeSTCube > Quest > Build APK`。

APK 输出为 `RenderingModule/Builds/VolumeSTCubeQuest.apk`，包名为
`com.volumestcube.quest`。

## 手柄交互

- 右手射线 + 扳机：点击变量、时间、矩阵单元和按钮。
- 在 slab 上按住扳机并上下移动手柄：连续调整 Z 层。
- 开启 Draw mode 后，在 slab 上按住扳机拖动：绘制 XY 验证区域。
- B：隐藏或重新显示空间控制台。
- 左摇杆：按头部朝向移动。
- 右摇杆左右：旋转体数据；上下：缩放体数据。
- X：复位观察位置和三维体。

## MatPlotAgent

PC 服务默认监听 `127.0.0.1:8010`。USB 调试连接后执行：

```powershell
adb reverse tcp:8010 tcp:8010
```

Quest 使用 `http://127.0.0.1:8010`。每次重新插线或 ADB 服务重启后，需要重新执行端口反向命令。

S4D Facet Grid 分析服务监听 `127.0.0.1:8020`。Quest 同样通过 ADB
反向端口访问：

```powershell
adb reverse tcp:8020 tcp:8020
```

仓库根目录的 `Start-QuestDemo.ps1` 会自动启动 MatPlotAgent 和 S4D
分析服务，并同时配置 8010、8020 两个反向端口。

在 Unity Editor 中进入 Play Mode 时使用相同的空间工作台。进入
`FULL MATRIX` 后点击 `GENERATE 3 x 3 WITH MATPLOTAGENT`，Unity 会将完整 Time × Depth Grid 提交到 S4D
服务，再由 MatPlotAgent 一次生成共享色标的完整 Facet Grid。

开发版数据目录为：

`/data/user/0/com.volumestcube.quest/files/OneDrive_1_4-30-2026`

区域分析 CSV 的列为 `x,y,value,region`，其中 `region` 为 `selected` 或 `rest`。
