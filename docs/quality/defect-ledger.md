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
| DEF-008 | 批量边距应用不刷新位置锚点且无屏幕钳制：重启/拓扑切换后批量移动整体回退，大边距可推出屏幕 | **待修复（R6 立案，当前树复核仍成立）**：`MoveVisibleWidgets` 仅 `UpdateConfigFromPhysicalBounds`（无 `CaptureAnchor`），`ResolveBoundsCore` 锚点优先于 X/Y；审查期间整改已补位置锁与压缩态守卫（BulkAppearance.cs:72-74），唯锚点与钳制仍缺 | P1 | 布局持久化/批量路径与单格路径不对称 | R6 | — | rounds/round-06/S04（LAY-01/03）+ S07（CFG-01） |
| DEF-009 | 随记剪贴板写入未标记"自写"：开启记录后每次复制必自我回录垃圾记录 | **待修复（R6 立案，当前树复核仍成立）**：生产宿主 `QuickCaptureSurfaceContent.xaml.cs:2603-2608` 无 `MarkWrite`；对照正确实现 `Operations.cs:407-413`；死宿主内另有 2 处随 DEF-027 清理；`SearchPopupWindow` 三处写入疑似同型待核验 | P1 | 随记/自写忽略配对标记遗漏（平行实现漂移） | R6 | — | rounds/round-06/S05（QC-01） |
| DEF-010 | 启动路径 `Task.Run` 在线程池线程刷新主题：跨线程 WinUI 访问 + 无锁 `_trackedWindows` 并发 + 托盘订阅者无投递保护 | **待修复（R6 立案，当前树复核仍成立）**：`App.xaml.cs:941` 每次启动必现；`ThemeService.cs:206-211` 遍历无锁 List 并裸广播，`App.Tray.cs:849` 直接改托盘句柄 | P1 | 线程安全/UI 线程亲和违规（确定性） | R6 | — | rounds/round-06/S08（THR-01） |
| DEF-011 | 随记详情编辑即以"当前默认格式"覆写条目 ContentFormat 并持久化（Markdown↔PlainText 静默互转，不可逆无回切 UI） | **待修复（R6 立案）**：生产宿主 `QuickCaptureSurfaceContent.xaml.cs:1204-1206` 编辑态取 `ViewModel.EditorContentFormat`；死窗口同型（`ResponsiveDetail.cs:379`） | P2 | 随记/编辑态格式语义覆写 | R6 | — | rounds/round-06/S05（QC-02） |
| DEF-012 | 删除图像随记的 4.2s 撤销窗口与图像缓存 GC 竞态：撤销后条目恢复但图片文件已被物理删除 | **待修复（R6 立案）**：`QuickCaptureService.cs:288` 入库即 `CleanupUnusedImageCacheCore`，撤销快照不在引用集 | P2 | 随记/删除-撤销引用保护只覆盖数据层不覆盖文件层 | R6 | — | rounds/round-06/S05（QC-03） |
| DEF-013 | 随记配色对比度校验仅作用于保存时刻，主题切换后"自定义×跟随主题"组合可跌破阈值（功能本要杜绝的不可读场景） | **待修复（R6 立案，当前树复核仍成立）**：`IsPairReadable` 仅在 `QuickCaptureClipboardColorEditor.cs:58,70`（保存路径），应用路径无复检 | P2 | 随记/B2 配色校验时机缺口 | R6 | — | rounds/round-06/S05（QC-05） |
| DEF-014 | 交互深度泄漏看门狗缺失：[重要勿删] 文档记载的 `RunInteractionLeakWatchdog`/`ForceResetInteractions` 在源码不存在，深度泄漏即死锁无安全网 | **待修复（R6 立案，当前树复核仍成立）**：`WidgetManager.ZOrder.cs`/`WidgetSessionManager.cs` 零命中（提交 8ea60ee 的"interaction-depth leak"指 RECT-1 租约修复，非本条） | P2 | 窗口交互/防死锁安全网回归 + 文档约定违背 | R6 | — | rounds/round-06/S03（WIN-01） |
| DEF-015 | Content 宿主 `ActivateRaisedFromTrayBatch` 未检查 `SetForegroundWindow` 返回值，激活失败不可观测（QuickCapture 宿主有完整检查，双宿主不对齐） | **待修复（R6 立案）**：`ContentWidgetWindow.xaml.cs:919-922` 对照 `QuickCaptureWidgetWindow.xaml.cs:520-524` | P2 | 窗口交互/前台权限失败无诊断锚点 | R6 | — | rounds/round-06/S03（WIN-02） |
| DEF-016 | QuickCapture 失活回落缺 `IsRaisedFromManager`/`ShouldDeferDesktopLayerRestore` 门控：拖拽中途被 force 压回桌面层、manager 批量唤起被提前拆台 | **待修复（R6 立案）**：`QuickCaptureWidgetWindow.WindowInteraction.cs:191-219` 对照 Content 正确实现 `:359-367` | P2 | 窗口交互/双宿主契约不同步 | R6 | — | rounds/round-06/S03（WIN-03） |
| DEF-017 | 显示桌面自愈 hook 注册失败无检测无重试（DEF-001 唯一回归面） | **待修复（R6 立案）**：`WidgetShowDesktopSelfHealService.cs:76-96` 返回 0 时仅信息级日志，幂等守卫以单 hook 成功为准 | P2 | 窗口交互/DEF-001 回归面 | R6 | — | rounds/round-06/S03（WIN-04） |
| DEF-018 | 胶囊常驻氛围效果（呼吸/辉光/边框）用 20Hz CPU 计时器逐拍写 Opacity，持续阻止渲染空闲 | **待修复（R6 立案）**：`WidgetShell.xaml.cs:2905/3416/3473`；应改 ExpressionAnimation/Forever 关键帧下沉合成器（与 DEF-003 方向对齐） | P2 | 渲染性能/常驻装饰动画未随 DEF-003 迁移 | R6 | — | rounds/round-06/S02（ANI-01） |
| DEF-019 | `ThemeService.AppearanceChanged` 广播无逐处理器异常隔离：单个订阅者异常静默截断后续全部订阅者的主题通知 | **待修复（R6 立案）**：`ThemeService.cs:206-211` 裸调用，对照 `LocalizationService`/`SettingsService` 已有快照+逐处理器隔离规范 | P2 | 事件消息/广播源异常隔离缺失 | R6 | — | rounds/round-06/S06（EVT-01） |
| DEF-020 | OnLaunched 单一巨型 try 吞启动失败：任一环节抛出后提醒/更新/诊断/Onboarding 等全部静默跳过，应用以半启动态常驻 | **待修复（R6 立案）**：`App.xaml.cs:880-1100`（catch :1096-1099），无用户反馈无重试无阶段标记 | P2 | 异常处理/启动失败粒度过粗且不可见 | R6 | — | rounds/round-06/S09（EXC-01） |
| DEF-021 | 未注册 `TaskScheduler.UnobservedTaskException` 与 `AppDomain.UnhandledException`：fire-and-forget 任务异常完全不可见 | **待修复（R6 立案，当前树复核零命中）**：纯诊断增强（记录后 `SetObserved`/崩溃前留痕） | P2 | 异常处理/兜底矩阵缺口 | R6 | — | rounds/round-06/S09（EXC-02） |
| DEF-022 | 热键服务 `TryStart`/`Stop` 与同构实现在 UI 线程同步 `Task.Wait`+`Thread.Join`，生命周期恢复路径最卡 ~2.15s | **待修复（R6 立案）**：`ReservedHotkeyHookService.cs:138/197/235`、`DesktopDoubleClickActivationService.cs:161/213-229`；改异步握手 | P2 | 线程安全/UI 线程同步等待 | R6 | — | rounds/round-06/S08（THR-02） |
| DEF-023 | 拖拽启动路径 UI 线程 `GetAwaiter().GetResult()` 同步等待 WinRT 异步 API，网络盘/慢盘拖动即卡 UI | **待修复（R6 立案）**：`FileService.cs:996/1013-1014` ← `FileSurfaceContent.xaml.cs:965`；同文件已有 async 版本未用 | P2 | 线程安全/UI 线程阻塞（高频路径） | R6 | — | rounds/round-06/S08（THR-03） |
| DEF-024 | 自动整理 Watcher 的 CTS 释放竞态：`_featureCts.Dispose()` 与 FSW 回调线程读 `Token` 交错，`ObjectDisposedException` 未被现有 catch 覆盖 | **待修复（R6 立案）**：`DesktopAutoOrganizationWatcher.cs:156-161` vs `:498`、`:1008-1030`；建议只 Cancel 不 Dispose | P2 | 线程安全/资源释放竞态 | R6 | — | rounds/round-06/S08（THR-04） |
| DEF-025 | WeatherService 缓存字段无同步并直接改共享 `WeatherData` 实例，并发刷新时撕裂读/数据污染 | **待修复（R6 立案）**：`WeatherService.cs:146-158/172-180/222-228`；建议不可变缓存记录整体交换 | P2 | 线程安全/共享可变状态无同步 | R6 | — | rounds/round-06/S08（THR-05） |
| DEF-026 | 胶囊排列在极小工作区无法收敛时静默溢出，组级 clamp 单向补偿 | **待修复（R6 立案）**：`WidgetCapsuleArrangementCalculator.cs:155-193/196-229`；虚拟机/极小分辨率边界 | P2 | 布局计算/边界场景未收敛 | R6 | — | rounds/round-06/S04（LAY-04） |
| DEF-027 | QuickCapture 专用宿主已整体退役但两份架构文档仍以"双宿主"为现行契约；13 文件/7,796 行死宿主与引用它的契约测试仍在编译 | **待修复（R6 立案）**：`Views/QuickCaptureWidgetWindow*`（零实例化）；`current_architecture.md` 与 [重要勿删] 手册失同步。整改=修订文档+删死宿主+清理关联测试（删除前 GUI 回归随记全功能）；R6 多条随记发现位于死宿主内随此消除 | P2 | 架构/文档-代码结构性漂移 + 死代码 | R6 | — | rounds/round-06/S10（ARC-01） |

## 功能台账（补充批次，代码级验证完成）

| 编号 | 功能 | 状态 | 文档 |
|---|---|---|---|
| BATCH2-F1 | 格子边距手动精确输入（统一/分边、实时预览+取消恢复、0–200 拦截、双向同步、批量应用、随布局持久化） | **已闭环（实测+复测整改）**：R4 全链实测；复测整改修复审计缺口——位置锁定绕过、胶囊瞬态几何持久化、程序化回写虚假校验、远侧编辑被吞、Save 静默失败、uniform 陈旧值 | batch-2/FEATURE-B1 + rectify/RECT-4 |
| BATCH2-F2 | 随记剪贴板记录自定义配色（取色器+HEX、跟随主题默认、自定义优先、对比度校验、一键恢复、持久化） | **复测整改完成移植**：原实现挂在零实例化的 `QuickCaptureWidgetWindow`（不可达，复测结论在源码层面成立）；已移植生产宿主 `QuickCaptureSurfaceContent`（画刷/应用/取色器/持久化）+ `ContentWidgetWindow` More 菜单入口（QuickCapture kind）+ NormalizeOverrides 入归一化管线；旧死窗口实现待独立清理 | batch-2/FEATURE-B2 + rectify/RECT-3 |
| BATCH2-F3 | 标题对齐（左/中/右+批量）、自定义图标（PNG/ICO/JPG+居中适配+恢复）、组内标题排序（既有能力确认，菜单上移/下移+持久化） | **对齐已闭环（实测）**：居中对齐实测成功并已恢复；图标 picker 路径列人工清单；组排序既有能力 | batch-2/FEATURE-B3-title-icon-group-order.md |

## R6 卫生批次（P3，45 条，未逐条单独立案——总报告 §6 为权威清单）

> 来源：R6 十专项审查。全部含位置/触发条件/根因/证据，详见 `rounds/round-06/全量代码缺陷审查总报告.md` §6 与各专项报告。修复方式：按域归并顺手修复；5 项死代码（ARC-06、ANI-02、CFG-07、QC-08、EVT-04）可同批删除。

| 域 | 条目 |
|---|---|
| 死代码宿主内（随 DEF-027 清理消除，不单独修复） | QC-04（详情复制 async void 无保护）、QC-09（菜单交互层释放竞态，B2 菜单时序残留根因收敛至此） |
| 资源生命周期（S1） | MEM-01（托盘旧 Icon 未 Dispose）、MEM-02（3 处 SoftwareBitmap 未确定性释放） |
| 动画卫生（S2） | ANI-02（硬件自适应链路死代码约 1000 行含 CS0169 观察项）、ANI-03（每帧委托分配，当前树仍在）、ANI-04（EnableDependentAnimation 反模式）、ANI-05（转场中断透明度跳变观察项）、ANI-06（SetIsTranslationEnabled 不复位） |
| 窗口交互与文档（S3） | WIN-05/WIN-06（[重要勿删] 文档失同步两处，与 DEF-014 合并为一次文档校正）、WIN-07（SearchPopupWindow 前台失败无降级）、WIN-08（拓扑协调器 async void）、WIN-09（raised 状态双写） |
| 布局（S4） | LAY-05（主屏启发式）、LAY-06（级联偏移不随 DPI）、LAY-07（组 X/Y 无有限性校验）、LAY-08（批量预览逐按键全量应用） |
| 随记（S5） | QC-06（读取链局部无容错）、QC-07（自写忽略窗误伤）、QC-08（缩略图键+死代码）、QC-10（Markdown 无界递归 StackOverflow 面）、QC-11（预览文件无清理）、QC-12（附件导入孤儿化）、QC-13（拖放超长静默丢弃）、QC-14（图像入库无上限持锁整读）、QC-15（Surface/死窗口平行实现漂移——QC-01/02/04/09 已实际发生） |
| 事件（S6） | EVT-02（TodoItem 旧 item 订阅不退订）、EVT-03≡EXC-03（全局兜底无熔断、广播截断不可观测——双专项合并立案）、EVT-04（RegistrationChanged 死事件） |
| 持久化（S7） | CFG-03（SearchHistory 非原子直写+UI 线程 IO）、CFG-04（遗留迁移非原子）、CFG-05（迁移失败仍推进版本号）、CFG-07（NormalizeGlobal 死代码）、CFG-08（Common.Close/More 十二语言缺失，活引用=审查期间新移植的 QuickCaptureClipboardColorEditor.cs:72）、CFG-09（TodoWidgetStore 无写 gate） |
| 线程卫生（S8） | THR-06（剪贴板服务状态字段裸读写+绕过分支）、THR-07（StoreStartupService UI 线程同步 IPC） |
| 异常卫生（S9） | EXC-04（空 catch 全量定性，仅 2 处无声吞改进点）、EXC-05（DateTimeOffset.Parse+catch）、EXC-06（CitySearchService.Predefined 无防护） |
| 架构卫生（S10） | ARC-02（current_architecture.md 漏记 Search/Glance）、ARC-03（search-core 文档领先仓库，README 引用不存在脚本）、ARC-04（3 项 DI 注册未接线）、ARC-05（WidgetContentFactory 7 处重复实例化）、ARC-06（WidgetSurfaceSnapshotCache 死代码——R1 观察项复核成立） |

**R6 审查期间已被整改覆盖（3 条，立案时真实存在，不立案）**：LAY-02（批量路径位置锁绕过 → `BulkAppearance.cs:72` 已守卫）、CFG-02（批量路径压缩态守卫 → 同处已守卫）、CFG-06（`QuickCaptureClipboardColorSettings.NormalizeOverrides` 死代码 → `SettingsService.cs:2246` 已接入管线，与 RECT-3 记载一致）。审查执行与 15:02 提交 `8ea60ee`（复测整改批次）并行导致时间窗交叉，总报告 §2 有逐条核验记录。

## R1 轮扫描发现、列入观察清单（未立案）

| 现象 | 位置 | 说明 |
|---|---|---|
| `WidgetSurfaceSnapshotCache<T>` 无生产调用方 | `src/DeskBox/Services/WidgetSurfaceSnapshotCache.cs` | **R6 复核成立（ARC-06）**：全仓仅 tests 引用，实现正确非泄漏源；转"确认清理"立案，随 P3 卫生批次删除（P3） |
| `HardwareAdaptiveAnimationService._measuredRenderDuration/_isMeasuring` 未使用字段 | 编译警告 CS0169 | **R6 复核改判（ANI-02）**：两字段属整条未接线死代码链路（AdaptiveTrayAnimationController/HardwareAdaptiveAnimationService/SmartAnimationAdapter 约 1000 行），与 DEF-003 修复无关联；建议随死代码清理批次整体删除，原"与 DEF-003 联动候选"假设作废 |
| `OnboardingWindow._desktopOrganizationCompleted` 未使用 | 编译警告 CS0414 | R6 复核仍成立（P3 卫生），建议与死代码清理同批处理 |
