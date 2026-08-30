# DeskBox 缺陷总表（缺陷库）

> 维护规则：每轮迭代开始时从本表选取输入；每个问题闭环（修复 + 回归验证）后更新状态。
> 优先级定义：P0 = 生产环境核心功能失效；P1 = 高频可复现、明显影响体验；P2 = 低频或边界场景；P3 = 代码质量/卫生。
> 详细单问题文档位于 `docs/quality/issues/`。

| 编号 | 标题 | 状态 | 优先级 | 根因分类 | 首次记录 | 修复轮次 | 单问题文档 |
|---|---|---|---|---|---|---|---|
| DEF-001 | 「显示桌面」后部分格子不显示，需打开新窗口才恢复 | **已闭环（实测）**：链路三要素 + 正向恢复用例（人为 iconic → `Restored iconic resting widget restored=1`，R3） | P0 | 窗口交互/Shell 事件恢复缺失 | R1 | R1+R2+R3 | issues/DEF-001-show-desktop-widgets-not-restored.md |
| DEF-002 | 胶囊悬停自动展开偶现无响应（需点击桌面空白后恢复） | 已修复；R3 悬停实测展开正常响应（SetCursorPos 真实悬停），偶发无响应复现未再现 | P1 | 窗口交互/状态机阻塞面误判 | R1 | R1+R3 | issues/DEF-002-hover-expand-blocked-by-tooltip-popup.md |
| DEF-003 | 胶囊展开/收起动画逐帧开销，达不到稳定 60fps | **已闭环（实测）**：R4 真 A/B——候选 1 后 dropped=0（0%），R1 版 0.49%，修复前 3–10%；视觉回归通过；冷启动首展 warm-up 观察项列 R4 后评估 | P1 | 渲染性能/逐帧 DP 写 + HWND 缩放 | R1 | R1+R2+R4 | issues/DEF-003-hover-expand-animation-frame-cost.md |
| DEF-004 | 主进程内存达 600MB、DWM 内存 1–2GB 的关联定位 | **定位完成 + 长周期观测已启动（R5）**：单会话增量有界平台化（R4）；每 6 小时定时采样任务已建立（`record-longperiod-sample.ps1` → `r5-longperiod-samples.jsonl`，含实例缺席标记），数据积累 3–7 天后按增长形态定论：线性增长→表面构成剖析修复；平台化→归因历史版本/多因素并关闭观测 | P1 | 资源管理（DWM 表面高水位，非 GDI/句柄/编辑会话泄漏） | R1 | 观测中 | issues/DEF-004-memory-600mb-dwm-correlation.md |
| DEF-005 | Internet 快捷方式枚举测试依赖宿主机 Steam 状态 | 已修复（测试密闭化，回归通过） | P3 | 测试隔离性 | R1 | R1 | issues/DEF-005-internet-shortcut-test-environment-coupling.md |
| DEF-006 | 点击格子标题区域时其他格子闪烁 | **已闭环（像素级实测）**：修复在 HEAD 生效（穷举核验无残留批量 TOPMOST 路径，托盘路径为有意保留）；R4 像素 diff changed=0；复测「未生效」判定为二进制陈旧（1.4.8 安装版无此修复）——`[Build]` 启动身份日志已根治识别问题；复测整改追加 peer idle 序与兜底锚定两处小改进 | P2 | 窗口交互/DWM 带迁移抖动 | 补充批次 | 复测整改 | batch-2/BUG-C1 + rectify/RECT-5 |
| DEF-007 | StackPopover 关闭按钮交互深度泄漏（点击一次即封锁全部空闲内存回收，直至托盘隐藏强制自愈） | **已闭环**：`HideStackPopoverForReuse` 以布尔租约恰好一次归还深度单位（复测整改审计发现，静态高嫌疑定位） | P1 | 资源管理/交互深度计数不成对 | 复测整改 | 复测整改 | rectify/RECT-1 |

## 功能台账（补充批次，代码级验证完成）

| 编号 | 功能 | 状态 | 文档 |
|---|---|---|---|
| BATCH2-F1 | 格子边距手动精确输入（统一/分边、实时预览+取消恢复、0–200 拦截、双向同步、批量应用、随布局持久化） | **已闭环（实测+复测整改）**：R4 全链实测；复测整改修复审计缺口——位置锁定绕过、胶囊瞬态几何持久化、程序化回写虚假校验、远侧编辑被吞、Save 静默失败、uniform 陈旧值 | batch-2/FEATURE-B1 + rectify/RECT-4 |
| BATCH2-F2 | 随记剪贴板记录自定义配色（取色器+HEX、跟随主题默认、自定义优先、对比度校验、一键恢复、持久化） | **复测整改完成移植**：原实现挂在零实例化的 `QuickCaptureWidgetWindow`（不可达，复测结论在源码层面成立）；已移植生产宿主 `QuickCaptureSurfaceContent`（画刷/应用/取色器/持久化）+ `ContentWidgetWindow` More 菜单入口（QuickCapture kind）+ NormalizeOverrides 入归一化管线；旧死窗口实现待独立清理 | batch-2/FEATURE-B2 + rectify/RECT-3 |
| BATCH2-F3 | 标题对齐（左/中/右+批量）、自定义图标（PNG/ICO/JPG+居中适配+恢复）、组内标题排序（既有能力确认，菜单上移/下移+持久化） | **对齐已闭环（实测）**：居中对齐实测成功并已恢复；图标 picker 路径列人工清单；组排序既有能力 | batch-2/FEATURE-B3-title-icon-group-order.md |

## R1 轮扫描发现、列入观察清单（未立案）

| 现象 | 位置 | 说明 |
|---|---|---|
| `WidgetSurfaceSnapshotCache<T>` 无生产调用方 | `src/DeskBox/Services/WidgetSurfaceSnapshotCache.cs` | 仅测试引用，疑似遗留死代码；待确认后于后续轮次清理（P3） |
| `HardwareAdaptiveAnimationService._measuredRenderDuration/_isMeasuring` 未使用字段 | 编译警告 CS0169 | 与 DEF-003 的硬件档位联动候选相关，R2 一并处理 |
| `OnboardingWindow._desktopOrganizationCompleted` 未使用 | 编译警告 CS0414 | P3 卫生问题 |
