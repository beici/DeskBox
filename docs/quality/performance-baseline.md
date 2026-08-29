# DeskBox 性能基线（滚动更新）

> 规则：每轮迭代在目标机重测并追加一行；任何指标显著退化（>5%）必须在该轮内归因。
> 测量脚本：`scripts/measure-deskbox-memory.ps1`、`scripts/measure-scenario-memory.ps1`；动画帧指标来自 `PerformanceLogger` 的 `CompactAnimation` / `CompactBoundsBatch` 标记。

## 指标定义（生产红线对齐）

| 指标 | 定义 | 目标 |
|---|---|---|
| WS / PrivateBytes | 主进程工作集 / 私有提交（任务管理器同口径） | 编辑场景峰值收敛，无持续增长 |
| Handles / GDI | 进程句柄数 / GDI 对象数 | 恒定（编辑前后差值≈0） |
| DWM 增量 | 布局编辑场景前后 DWM 进程内存增量 | 逐轮下降 |
| 动画 dropped | `CompactAnimation` dropped 帧 / 总帧 | 0（Win11 单胶囊）；并发 ≤3 连续 drop |
| FPS | 展开动画实测帧率 | ≥60（条件允许跟随刷新率） |

## 测量场景

S1 冷启动静置 5 分钟；S2 布局编辑 50 次（拖动 + 缩放交替）；S3 悬停展开/收起 30 次；S4 托盘批量唤起 ×10；S5 显示桌面 toggle ×20。

## 基线记录

| 轮次 | 日期 | 机器 | 场景 | WS | PrivateBytes | Handles | GDI | DWM 增量 | dropped | 备注 |
|---|---|---|---|---|---|---|---|---|---|---|
| R1 | 2026-08-30 | 开发机（本轮） | — | 未采集 | 未采集 | 未采集 | 未采集 | 未采集 | 未采集 | 环境为开发态，无法产生生产代表性数据；R2 在目标机按 S1–S5 采集后填入首行真实基线 |

> 诚实性说明：R1 未产出任何数字基线，不在此伪造占位值。R2 第一项工作即目标机采集，之后本表逐轮追加。
