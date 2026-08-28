# 本地后端

双击仓库根目录的 `Start-Backend.cmd` 即可启动 PC 后端。脚本会：

- 使用当前工作区的 `.venv`；
- 在 `127.0.0.1:8010` 启动 MatPlotAgent；
- 在 `127.0.0.1:8020` 启动 S4D Analysis Service；
- 确认端口属于当前工作区，避免误连 D 盘或其他副本；
- 确认 `for_vr_hong_kong_events_v1` 已注册；
- 将日志写入 `.runtime/backend/logs`。

Unity Windows Game 视图直接访问上述地址。Quest 使用 `Start-QuestDemo.cmd`，
脚本会先启动同一套后端，再配置 ADB reverse。停止服务可双击
`Stop-Backend.cmd`。

健康检查：

```text
http://127.0.0.1:8010/health
http://127.0.0.1:8020/health
http://127.0.0.1:8020/datasets
```

`/health` 会返回绝对工作区路径。若该路径不是当前仓库，说明端口被另一份
项目占用；`Start-Backend.cmd` 会在确认它确实是旧 MatPlot/S4D 进程后进行切换。
