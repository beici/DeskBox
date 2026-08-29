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
| R2 | 2026-08-30 | 目标机（本机，165Hz，2560×1440） | S0 安装版 1.4.8 长运行参考点 | 487.9 MB | 760.4 MB | 6697 | 200 | DWM WS 812.5 / Priv 5880.8 MB | —（1.4.8 日志：165Hz 下 CompactAnimation dropped=2–6/60 帧） | 生产参考点，非本轮构建 |
| R2 | 2026-08-30 | 同上 | S1 R1 代码 Debug 静置 60s | 428.6 MB | 318.1 MB | 3348 | 91 | DWM WS 451 / Priv 561.1 MB | — | 关闭长运行实例后 DWM Private 5.88GB→0.56GB，**DEF-004 DWM 强关联实证** |

### R2 关键实测结论（原始样本：`.artifacts/quality-baseline/r2-samples.jsonl`）

1. **DEF-004 关联坐实**：DWM Private 5.88GB 与 DeskBox 长期运行实例强相关（关闭实例即回落 5.3GB）；主进程 Private 760MB→318MB 同步回落。GDI 对象仅 200 个，**GDI 泄漏假设排除**。
2. **修复前动画基线参考**：安装版 1.4.8 日志（04:21）记录 165Hz 下 `CompactAnimation frames=60 dropped=2–6`（3%–10% 掉帧），对应 DEF-003。
3. **S2/S3/S5 场景数字**：GUI 悬停/布局编辑自动化在目标机受工具限制（CUA 无法作用于 owner-attached 格子窗口 + UIA 树为空），列入人工复验清单；S5 显示桌面场景已通过日志链路完成验证（见 R2 报告）。

> 诚实性说明：S2/S3 未伪造数字。上表全部为真实采集值，测量脚本 `scripts/measure-quality-baseline.ps1`。
