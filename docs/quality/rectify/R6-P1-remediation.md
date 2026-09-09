# R6 P1 专项整改报告（DEF-008 / DEF-009 / DEF-010）

> 整改日期：2026-08-30 ｜ 基线：`359aeeb` ｜ 方式：报告拆解核对 → 三个修复专项 subagent 并行（各自「核对 → 修复 → 自验证」单项闭环，文件集互不相交）→ 主流程统一门禁 → 交叉验证（逐项一票驳回权）→ 总复核（跨模块 GO）。
> 独立核对参照系：[R6-P1-triage.md](R6-P1-triage.md)（三项全部 CONFIRMED，含交叉验证核对单与 SearchPopup 三处定性表）。

## 门禁结果

- Debug x64 构建：**0 错误**（24 条既有警告，无新增）。
- x64 全量回归：**3006/3006 通过**（含本批新增 8 个用例；整改前 2998）。
- 交叉验证：三项全部 **PASS**；总复核：**GO**（无逻辑冲突、无重入、z-order 与 [重要勿删] 约束面零触碰、Rust 冻结契约零漂移）。

---

## DEF-008 批量边距应用不刷新位置锚点且无屏幕钳制

**根因**：`MoveVisibleWidgets` 批量路径仅 `UpdateConfigFromPhysicalBounds`（只写 X/Y/W/H），恢复路径 `ResolveBoundsCore` 锚点优先于 X/Y → 重启回退旧锚点位置；边距落点计算全仓唯一无工作区钳制。

**修复**（最小侵入，全部复用既有公共 API，`WidgetPositioningService.cs` 零修改）：

| 文件 | 修改点 |
|---|---|
| `src/DeskBox/Services/WidgetManager.BulkAppearance.cs` | `MoveVisibleWidgets` 成功分支在持久化后新增三连调：`WidgetPositioningService.CaptureAnchor(window.Config, target, workArea)`（实参与写 X/Y 完全同一）+ `SynchronizeGroupLayoutFromMember(window.Config)`（组宿主 X/Y 写入 WidgetGroupConfig；非组窗口早退零副作用）+ `CaptureCurrentTopologyLayout(window.Config)`（拓扑 profile 同步）——与单格路径 `SynchronizeWidgetGroupLayout` 两件套逐一对齐 |
| `src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs` | 共用位移助手 `ShiftSideToMargin` 与 4 参 `ShiftBoundsToNearestEdge` 返回值套 `WidgetPositioningService.EnsureVisible(rect, workArea)`——单格与批量两条路径一处钳制同时覆盖；取消恢复走 `dialogInitialRect` 直排不经助手，无误伤 |
| `tests/DeskBox.Tests/WidgetPositioningServiceTests.cs` | +3 行为用例：批量持久化序列→`ResolveBoundsForTest` 重启链位置/尺寸精确保持；**无锚点捕获则回退旧锚点 (1580,80)**（机理复现锁死）；EnsureVisible 双向钳制+完全出屏回退 |
| `tests/DeskBox.Tests/WidgetBatchMarginPositioningContractTests.cs`（新） | 3 条源码契约用例：成功分支接线顺序（persist→anchor→组→拓扑）、锁/压缩守卫在位、两助手钳制+单格锚点捕获在位（行为级用例无法实例化 WidgetManager 的兜底，属仓库既有契约测试模式） |

**等价性论证**（manager 层 CaptureAnchor ≡ 窗口层 CapturePositionAnchor）：全仓 `CollapseHostBoundsToContent`/`ExpandContentBoundsToHost` 零 override（恒等），`IsCompactCollapsedState ≡ IsCompactBoundsStateActive` 且压缩格子本就被批量守卫跳过。

## DEF-009 随记剪贴板写入未标记"自写"

**根因**：生产宿主复制路径与 SearchPopup 复制路径构造 DataPackage → SetContent 无 `DeskBoxClipboardWriteScope.MarkWrite` 配对标记，开启"随记剪贴板记录"后自我回录。

**修复**（只加标记，剪贴板内容/格式/时机零变化）：

| 文件 | 修改点 |
|---|---|
| `src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs` | `CopySelectedQuickCaptureItemsAsync` 在 SetText/SetContent 之间的公共同步插入点补 `DeskBoxClipboardWriteScope.MarkWrite(text: text)`——FormatSingle 与 FormatBatch 两分支一并覆盖 |
| `src/DeskBox/Views/SearchPopupWindow.xaml.cs` | 移交核查三处全部证实为生产可达同型遗漏并修复：`CopySelectedItemsAsync`（`await SetDragPayloadAsync` **之后**、SetContent 前补 text+paths 双标记，无隔窗失效面）；`CopyFileSystemItemAsync`（非空局部 `path` 后补 `MarkWrite(text: path, paths: [path])`）；`CopyPathToClipboard`（补 `MarkWrite(text: item.DetailPath)`，对齐 `FileSurfaceContent` 先例） |
| `tests/DeskBox.Tests/QuickCaptureClipboardServiceTests.cs` | +2 用例：标记后 2s 窗口内不同文本不被误伤、正常回录；2.1s 过窗后同文本恢复回录（窗口语义锁死） |

**明确不修**：死宿主 `QuickCaptureWidgetWindow` 内 2 处同型位点（`SelectionAndDrop.cs`/`Items.cs`）随 DEF-027 整宿主删除处置。

## DEF-010 启动路径在线程池线程刷新主题

**根因**：`OnLaunched` 将整个必须 UI 线程执行的 `RefreshAppearance` 丢进线程池（每次启动确定性跨线程 WinUI 访问 + 无锁 `_trackedWindows` 并发 + 后台广播）。

**修复**（选择"启动回 UI 线程 + 入口自愈重投"复合方案）：

| 文件 | 修改点 |
|---|---|
| `src/DeskBox/App.xaml.cs` | 删除 `Task.Run(() => themeService.RefreshAppearance())` 与 `await themeTask`，改 UI 线程同步调用——经核实该刷新点工作量≈空（`_trackedWindows` 为空、广播零订阅者），原"并行化"只制造竞态无收益；首刷确定性先于托盘创建/WidgetManager 构造，时序不弱反强 |
| `src/DeskBox/Services/ThemeService.cs` | `RefreshAppearance` 入口新增 `App.UiDispatcherQueue is { HasThreadAccess: false } → TryEnqueue(RefreshAppearance)` 自愈重投（对齐 `WidgetShowDesktopSelfHealService` 既有模式），使未来任何线程池续体自动落 UI 线程 |
| `src/DeskBox/App.Tray.cs` | `UpdateTrayIconAppearance` 补同款投递防护（防御纵深；null 早退保留在防护之前，重入后重新判空） |

**线程语境核实**：全仓 7 个 RefreshAppearance 调用点逐一确认均 UI 线程；广播侧仅 ThemeService 一处且已被守卫覆盖，无防护订阅者（WidgetManager/SettingsViewModel）端到端安全。

---

## 非阻塞观察项（记录待后续批次）

1. `SearchPopupWindow.xaml.cs:3826` `string path = item.DetailPath;` 可能产生 CS8600——修复前同一行的 `SetDragPayloadAsync(data, item.DetailPath)` 本就产生 CS8604，净警告数不变；惯用修法为先取局部再守卫。
2. `TitleAppearance.cs` 两参 `ShiftBoundsToNearestEdge(bounds, margin)` 重载全仓零调用（死代码，无钳制）——建议随 P3 卫生批次删除。
3. 批量钳制 workArea 取移动前格子中心（`ResolveWorkAreaStatic`）——跨界推挪拉回源显示器工作区，保守且自洽，非回归。
4. `CopyPathToClipboard` 缺 `Clipboard.Flush()`——既有卫生点。
5. `ThemeService` 重投路径 TryEnqueue 失败（仅队列关闭才可能）静默跳过——可后续补一条日志。

## 回滚方式

三项修复互相独立、均为小 diff：按文件 `git revert`/`git checkout <baseline> -- <file>` 即可单项回滚（DEF-008 涉及 BulkAppearance.cs + TitleAppearance.cs + 2 测试文件；DEF-009 涉及 QuickCaptureSurfaceContent.xaml.cs + SearchPopupWindow.xaml.cs + 1 测试文件；DEF-010 涉及 App.xaml.cs + ThemeService.cs + App.Tray.cs）。
