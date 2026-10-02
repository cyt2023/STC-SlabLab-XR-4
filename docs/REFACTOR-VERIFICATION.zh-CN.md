# 重构验收记录

日期：2026-10-02。范围：保持现有流程、可视化交互、功能和数据语义，完成已有职责拆分。

## 完成内容

工作台由 15 个文件整理为 23 个 partial 文件。245 个方法及 Android 原生语音适配类按职责归位；新增 Workflow、Field、Buckets、Speech、History、Findings、Draft、Matrix。主文件保留生命周期、字段、常量、公开状态和状态提示入口。仍采用同一个 MonoBehaviour，避免改变场景组件、序列化和协程归属。

本轮没有重写状态机、范围计算、数据选取、绘图请求、按钮路径、布局、相机参数、默认值或持久化键。字段与初始化声明继续留在原文件、保持原有顺序；主脚本 GUID 保持不变。条件编译区块整体搬移。

源码等价检查比较搬移前后的完整成员片段，而非只比较行数。1,071 个成员片段（含条件编译组）的内容完全一致；源码摘要为 `3ce0d57080e9e3347cde90565316c25cb15da730aeb13eb8d3e5f3e7fdb6c771`。

Python 路由拆分补回三个遗漏的依赖：manifest 的 HTTPException、Ground 接口的 NumPy、摘要路由的 build_llm_digest。路由只保留实际使用的导入；函数和类体保持原样。代码更新时间检测改为递归包含 routers，防止新模块更新后健康检查仍报告旧时间。接口集合及 JSON 契约未改。

## 验证

| 检查 | 结果与范围 |
|---|---|
| 后端测试 | 55 项通过；包括新增跨路由贯通、失败/未完成任务限制、未知数据集和源码时间检测 |
| 仓库契约 | 通过；布局、设置键、快捷键、状态文案、路由和拆分模块全局依赖 |
| Unity Validate All | 编辑器检查及交互基线通过 |
| 六步流程 | 真实运行 Import → Field → Slab/Intent → Matrix → Ground → Findings；返回并重开同一历史快照通过 |
| 可视化基线 | 八组视图测量完全一致；首屏文字像素探针、快捷键、Reset View 和时间输入结果一致 |
| 导出 | Snapshot 按钮生成有效 PNG |
| VR 预览 | 启动世界空间面板，进入 Step 2，无运行错误 |
| 无响应服务 | 保留 45 秒超时窗口及操作提示，连接地址恢复 |
| macOS 构建 | SlabLab-Review.app 构建成功；独立应用启动并载入 24 小时缓存和两个变量 |

六步流程通过独立的本地 S4D 服务运行：真实读取已有 Wave 离线缓存，执行数据注册、RAW 数值运算、绘图契约、MatPlotAgent 确定性绘图、快照、Ground 重建和摘要路由。仅外部模型生成与解释替换为固定测试结果，没有调用收费模型。控制操作调用界面使用的运行时方法，不直接把流程标志设置为“完成”。

逐页截图在停止推进后的帧末采集，并保存在每次运行独立的目录，避免捕获下一页或误用旧截图。这些检查证明已覆盖路径的行为保持一致；不是对任意数据、网络、设备及模型输出的无限保证。既有后期页面的世界空间面板布局也保留，不在本轮重新设计。

最终 Unity 全套检查完成于 13:22:38 UTC，六步流程完成于 13:26:44 UTC，macOS 构建完成于 13:27:28 UTC。构建时 Unity 自动添加的 XR 预加载项已恢复，ProjectSettings 和 Assets/XR 与构建前一致。旧安装包保留；本轮构建位于 RenderingModule/Builds/SlabLab-Review.app。

## 复现

```bash
# 仓库契约及后端回归
./tools/release-check.sh --quick

# 原有全套 Unity 检查：编辑器菜单 VolumeSTCube > Desktop > Validate All
# 关闭编辑器后也可运行：
./tools/run_unity_tests.sh

# 六步流程：先启动隔离服务，再点编辑器菜单 Validate Full Workflow
.venv/bin/python tools/run_workflow_test_server.py
# 完成后 Ctrl+C 关闭隔离服务。

# 与本轮保存的原始源码逐成员比较
.venv/bin/python tools/check_workbench_refactor.py \
  --baseline .runtime/refactor-20261002/before/RenderingModule/Assets/VolumeSTCubeAPI
```

本地证据：

- `.runtime/refactor-20261002/before/`：开始本轮工作时的源码与旧验证记录。
- `.runtime/refactor-20261002/baseline/`：修改前重新运行的有效交互基线。
- `.runtime/refactor-20261002/source-equivalence.json`：完整成员、字段顺序的等价结果。
- `.runtime/test-results/guards-summary.txt` 和 `interaction-baseline.json`：最新 Unity 检查。
- `.runtime/full-workflow/workflow.txt`：最新六步流程结果及截图目录。
- `.runtime/vr-flow-validation.txt` 和 `stall-timeout-validation.txt`：VR/超时结果。

## 验证边界

Quest 真机控制器、麦克风和系统键盘没有在本机验收；头显的 S4D 地址配置缺口仍见 QUEST-HEADSET-CHECKLIST.md。真实远程模型、私人 Wave 网络服务和干净机器安装不由离线测试替代。它们不属于本轮行为保持式重构，不能标为已验证。

仓库在本轮开始前已有大量未提交改动。本轮保留原有工作，没有自动提交、推送或发布旧安装包。
