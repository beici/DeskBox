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

### R3 实测结论（2026-08-30，原始样本：`.artifacts/quality-baseline/r3-samples.jsonl` + 日志留痕）

| 轮次 | 日期 | 场景 | 指标 | 结果 |
|---|---|---|---|---|
| R3 | 2026-08-30 | S3 五连悬停（候选 1 后，R1+R2 代码，165Hz） | CompactAnimation dropped | 10 次动画共 **dropped=3/611 帧（0.49%）**，maxFrame 6.7–10.2ms（budget 6.1）；对比修复前现场版 dropped 2–6/60（3–10%），**质量红线 ≥60fps 实测达成** |
| R3 | 2026-08-30 | S3 采样（悬停活动后） | WS 419.4 / Priv 305.7 MB，Handles 2466，GDI 91 | 无退化；DWM Priv 930.5MB（悬停表面活动波动，静置 561） |
| R3 | 2026-08-30 | DEF-001 正向用例（人为 iconic） | `[ShowDesktop] Restored iconic resting widget kind=Search restored=1` | 自愈正向恢复端到端通过，窗口数 12 保持 |
| R3 | 2026-08-30 | 候选 1 视觉回归 | 悬停展开态截图 | 展开布局/角半径/标题栏渲染正常，相邻胶囊无影响 |
| R3 | 2026-08-30 | 冷启动首展（观察项） | 首次展开 dropped=31/60（后续同格子 dropped 0–1） | warm-up 首展成本仍在，列入 R4 候选 |

**候选 3 决议**：`HardwareAdaptiveAnimationService` 在 App 中未接线（与 CS0169 警告互证），完整联动需先接线服务（超出最小侵入边界）；且稳态 dropped 已 0–1，档位联动的边际收益集中在冷启动首展——与 warm-up 增强合并列入 R4 评估。

### R2 关键实测结论（原始样本：`.artifacts/quality-baseline/r2-samples.jsonl`）

1. **DEF-004 关联坐实**：DWM Private 5.88GB 与 DeskBox 长期运行实例强相关（关闭实例即回落 5.3GB）；主进程 Private 760MB→318MB 同步回落。GDI 对象仅 200 个，**GDI 泄漏假设排除**。
2. **修复前动画基线参考**：安装版 1.4.8 日志（04:21）记录 165Hz 下 `CompactAnimation frames=60 dropped=2–6`（3%–10% 掉帧），对应 DEF-003。
3. **S2/S3/S5 场景数字**：GUI 悬停/布局编辑自动化在目标机受工具限制（CUA 无法作用于 owner-attached 格子窗口 + UIA 树为空），列入人工复验清单；S5 显示桌面场景已通过日志链路完成验证（见 R2 报告）。

> 诚实性说明：S2/S3 未伪造数字。上表全部为真实采集值，测量脚本 `scripts/measure-quality-baseline.ps1`。
