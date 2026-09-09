# R6 P1 修复独立复核报告 + 未发现缺陷深挖

> 复核范围：commit `81d4147`（基线 `359aeeb`，分支 `wip/fix-bug`）对 DEF-008 / DEF-009 / DEF-010 的修复，以及修复相邻代码面的新缺陷挖掘。
> 复核方式：独立于 `R6-P1-triage.md` 与 `R6-P1-remediation.md` 自行取证（git diff + 当前源码精读 + 3 个后台深挖 subagent 交叉验证）；构建与测试作为验证手段，全程**只读**（未修改任何源码，工作树除本文档外干净）。
> 门禁实测：Debug x64 构建 **0 错误 / 12 条既有警告**（CS8602×4、CS0169×2、CS0414、CS8601 等，均为审查前就存在的历史警告）；x64 全量回归 **3006/3006 通过**（37s，含本批新增 8 用例）。注：带 `-p:RuntimeIdentifier=win-x64` 的测试变体被本机杀软拦下（rust-thumbnail-proxy 的 cargo build-script 执行被拒，os error 5），改用仓库规范的无 RID 命令后全绿——属环境问题，与代码无关。

---

## 1. 三个 P1 的逐项裁决与证据

### 1.1 DEF-008 批量边距应用不刷新位置锚点且无屏幕钳制 —— **已修复**

原缺陷三要素（锚点不刷新 / 组与拓扑不同步 / 落点无钳制）逐项验证：

| 检查点 | 结论 | 当前树证据 |
|---|---|---|
| C1 锚点捕获同源 | **成立** | `src/DeskBox/Services/WidgetManager.BulkAppearance.cs:180` `WidgetPositioningService.CaptureAnchor(window.Config, target, workArea)`，与 :171 写 X/Y 用**同一 `target`、同一 `workArea`**（都是 :167-170 以 target 中心点 `GetFromPoint(Nearest)` 选屏）。锚点字段集（PositionAnchor/PositionMarginX/Y/PositionMonitorKey/DeviceName/WasPrimary）与 X/Y/W/H 字段集无交叠写：`CaptureAnchorCore`（`WidgetPositioningService.cs:194-229`）只写锚点族，`UpdateConfigFromPhysicalBoundsCore`（:245-257）只写 X/Y/W/H+版本，两两不冲突 |
| C2 组宿主同步 | **成立** | :182 `SynchronizeGroupLayoutFromMember(window.Config)`（`WidgetManager.Groups.cs:1604-1618`）：非组窗口 `FindByMember` 为 null 早退**零副作用**；组宿主（ActiveMemberId 匹配）写 `WidgetGroupConfig.X/Y` 并 `ApplyGroupLayoutToMembers` 回写全体成员 |
| C3 拓扑 profile | **成立** | :183 `CaptureCurrentTopologyLayout(window.Config)`（`WidgetManager.cs:1415-1421` → `WidgetTopologyLayoutService.CaptureCurrentSurface` :26-56），与窗口自身 persist 路径 `SynchronizeWidgetGroupLayout()`（`WidgetWindowBase.Grouping.cs:283-288` = `SynchronizeGroupLayoutFromMember` + `CaptureCurrentTopologyLayout`）**两件套逐一对齐** |
| C4 落点钳制 | **成立** | `ShiftSideToMargin`（`TitleAppearance.cs:704-706`）与 4 参 `ShiftBoundsToNearestEdge`（:758-760）返回值均包 `WidgetPositioningService.EnsureVisible(rect, workArea)`。grep 全仓这**两个助手的所有 6 个调用点**（:484/:503 批量 lambda、:528-532 单格分支）全部属于边距对话框链路；拖拽/组排列/胶囊排列/取消恢复路径不经这两个助手（取消恢复 :379-407 用 `dialogInitialRect` 直排 SetWindowPos，见 Q3，无误伤） |
| C5 单格对称性 | **成立** | 批量序列 = SetWindowPos → UpdateConfigFromPhysicalBounds → CaptureAnchor → UpdateWidget → 组同步 → 拓扑同步；单格序列（`ApplyOwnMarginToSide` :539-548）= SetWindowPos → CapturePositionAnchor → UpdateConfigBoundsFromPhysical(persist)。批量侧管理器 `CaptureAnchor(window.Config, target, workArea)` 与窗口侧 `CapturePositionAnchor`（`WidgetWindowBase.Bounds.cs:499-533`，展开态走 `WidgetPositioningService.CaptureAnchor`，`CollapseHostBoundsToContent` 全仓零 override 为恒等）语义等价（修复文档的等价性论证复核成立） |
| C6 守卫保留 | **成立** | `BulkAppearance.cs:131-136` `IsPositionLocked / IsCompactArrangementActive / IsCompactCollapsedState` 三守卫完整在位，continue 在捕获之前（顺序正确）；`GetOtherVisibleWidgetRects`/others 最近格子语义未动 |
| C7 回归用例 | **成立** | `WidgetPositioningServiceTests.cs` 新增 3 用例：`BatchMarginPersistSequence_KeepsMovedPositionAcrossRestartResolve`（:483-522，断言重启解析 = 目标物理几何，并穿透 DPI）、`…WithoutAnchorCapture_RevertsToStaleAnchorPosition`（:524-551，锁死旧机理：无捕获 → 重启回退 (1580,80)）、`EnsureVisible_ClampsOversizedMarginTargetsBackIntoTheWorkArea`（:554-587，双向钳制 + 完全出屏回退 (32,32)）；`WidgetBatchMarginPositioningContractTests.cs` 3 条源码契约（接线顺序、守卫、两助手钳制 + 单格锚点）。行为用例真锁机理，契约用例兜底 manager 层无法无头实例化 — 与仓库既有契约测试模式一致 |
| C8 写放大 | **成立** | 三连调均为内存操作；`SynchronizeGroupLayoutFromMember` 内部每宿主一次 `SaveDebounced`（`SettingsService.SaveDebounced` 自带去抖合并，仅计数增加）；循环尾一次 SaveDebounced 收尾。预览逐按键场景与既有 LAY-08（已知 P3）同型，无新增同步 IO |

**前轮整改的遗漏/瑕疵（本报告新确认）**：
- **`BulkAppearance.cs:172-179` 的新注释声称 "Compact states … never reach this point, so no compact-placement branch is needed here" 在本批修复后成为事实性错误**：三个守卫只跳过「正在/已经处于压缩态」的格子，**展开态但已持久化 `CompactPlacement` 的格子（折叠过一次即有 placement）完全到达该点**，而批量/单格/取消恢复三条持久化路径都不刷新 `CompactPlacement` → **折叠时胶囊按旧坐标回跳**。这构成一个新的相邻缺陷（见 §2 N1，P2），也是外部审查 Claude 的候选 (a) 的确证。
- 原报告/triage 对 LAY-02（位置锁绕过）的守卫位置从 :72-74 漂移至 :131-136，本复核确认漂移后仍在位、语义未变（防误报项，确认无需扣分）。

### 1.2 DEF-009 随记剪贴板写入未标记"自写" —— **已修复**

先读 `Services/DeskBoxClipboardWriteScope.cs`（2s 窗口快照 :5/106；`MarkWrite` :13-33 归一化 Trim 文本 + `Path.GetFullPath` 后的 paths 集合；`ShouldIgnoreText` :50-83 先 Ordinal 精确匹配、再按行路径匹配）再逐位点核对：

| 位点 | 标记实参 | 与读取侧匹配语义 | 时序（await 间隔） |
|---|---|---|---|
| `QuickCaptureSurfaceContent.xaml.cs:2617`（核心位点） | `MarkWrite(text: text)`，text 由 :2600-2606 统一计算（FormatSingle 与 FormatBatch 共用同一插入点，**单/批两分支一并覆盖**；位图单条提前 :2593-2598 分流到已标记的 `CopyItemWithFeedbackAsync`） | 剪贴板文本 = 同一 `text`，Ordinal 精确匹配命中 | `SetText`(5616) → `MarkWrite`(2617) → `SetContent`(2618) 同一同步段，无 await 间隔 |
| `SearchPopupWindow.xaml.cs:3604-3606`（SP-1 多选复制/剪切） | `text: string.Join(Environment.NewLine, paths)` + `paths: paths` | `SetDragPayloadAsync` 全失败回退文本（:4176-4179 的 `fallbackText.ToString().TrimEnd()`）逐字符等于 `Join(Environment.NewLine, paths)`（`AppendLine` 尾缀 \r\n 被 TrimEnd 剥掉）→ Ordinal 命中；即使空白出入，`ShouldIgnoreText` 按行 NormalizePath + OrdinalIgnoreCase 集合匹配第二道兜底，**无尾随换行错配** | `await SetDragPayloadAsync` 在 :3603，MarkWrite 在 await **之后**、SetContent(:3607) 之前——无隔窗失效面 |
| `SearchPopupWindow.xaml.cs:3832-3834`（SP-2 单文件复制/剪切） | `text: path` + `paths: [path]`（先取非空局部 :3826） | 单路径回退 `SetText(path)`(:4142) 逐字符一致 | 同 SP-1，await 在前 |
| `SearchPopupWindow.xaml.cs:4069`（SP-3 复制路径） | `MarkWrite(text: item.DetailPath)`（:4070 SetContent） | 纯文本精确匹配 | 同步 |

其余验证：
- **全仓 `Clipboard.SetContent` 位点清点**（rg 结果 21 处命中）：生产宿主 8 处已标记（Operations.cs:412、FileSurfaceContent.SelectionAndMenus.cs:1282、FileSurfaceContent.xaml.cs:3745、TodoWidgetContent.ClipboardSelection.cs:107、本次新增 4 处）；死宿主 3 处（`QuickCaptureWidgetWindow.SelectionAndDrop.cs:350`、`Items.cs:478`、`Attachments.cs:51`）随 DEF-027 整宿主删除处置，**不扩散、不新增引用** ✓。
- **修正 triage 的一项观察**：triage §5.5 称 `FileSurfaceContent.xaml.cs:3745` 回退分支无标记——经 `git show 359aeeb` 取证，**基线版本该方法的 `MarkWrite` 就在 :3721（Shell 剪贴板尝试之前），同一方法体内先标记再走 TrySetFileDropList/DataPackage 两条写路径，回退分支实际已被覆盖**，无需补标。triage 该条为误报，本复核予以澄清。
- 新增 2 用例（`QuickCaptureClipboardServiceTests.cs` :161-196）：异文不误伤（标记"copied inside DeskBox"后用户复制"user copied something else"仍入库）、2.1s 过窗后同文恢复回录——真覆盖"标记不误伤用户复制"与"窗口过期恢复"两语义 ✓。
- 既有标记路径零改动 ✓。`CopyPathToClipboard` 仍缺 `Clipboard.Flush()`（既有卫生点，已记录于 remediation 观察项 4，不重复立案）。

### 1.3 DEF-010 启动路径线程池线程刷新主题 —— **已修复**

| 检查点 | 结论 | 证据 |
|---|---|---|
| C1 启动路径不再后台触碰 WinUI | **成立（方案 a+b 复合）** | `App.xaml.cs:940-944` 删除 `Task.Run(...)` 与 `await themeTask`，改 UI 线程同步调用 `themeService.RefreshAppearance()`；`ThemeService.cs:212-216` 入口加 `if (App.UiDispatcherQueue is { HasThreadAccess: false } dispatcherQueue) { _ = dispatcherQueue.TryEnqueue(RefreshAppearance); return; }` 自愈重投（方法组转换合法：void 无参；重投后重入 re-check；null DQ 时 pattern 不命中走正常路径——DQ 在 :904 已赋值，null 不可达） |
| C2 window.Content 读取受线程保护 | **成立** | 旧顺序问题（:159 裸读 Content 在 :164 防护之前）已被根治：整个 `RefreshAppearance` 现在 UI 线程约束，`ApplyToAllWindows`(:200) 与 `ApplyToWindow` 的 Content 读取 (:159) 不再可能被后台线程触达；:164 的每窗防护保留为纵深防御 |
| C3 _trackedWindows 竞态 | **成立（排序使然 + 归属收敛）** | 修复后 `RefreshAppearance` (:944) 在 `CreateTrayIcon` (:948) **之前**同步执行——启动首刷时 `_trackedWindows` 为空，`TrackWindow` 的 Add 全部发生在首刷之后；全仓 7 个 RefreshAppearance 调用点逐一确认均 UI 线程（App.xaml.cs:944/4138、OnboardingWindow.Appearance.cs:162、SettingsViewModel.PreferenceCommands.cs:76、ThemeService.cs:38 计时器经 TryEnqueue 在 UI 线程创建、:93/106/124）。`_trackedWindows` 仍无锁——**安全性完全依赖"全部访问收敛 UI 线程"的隐式约定**，建议后续补一行注释声明归属（P3 卫生，见 §2 N5 关联） |
| C4 托盘订阅者防护 | **成立（双保险）** | `App.Tray.cs:859-863` 补同款 `HasThreadAccess` → TryEnqueue 重投（在 :851 null 早退之后，重入后重新判空）；同时广播源 `RefreshAppearance` 已 UI 线程约束，双路径均安全 |
| C5 其余无防护订阅者 | **成立（方案 a 下自动满足）** | `WidgetManager.ApplyAppearancePreview`（WidgetManager.cs:438/668-687）与 `SettingsViewModel.RefreshAccentPreview`（:430/464-475）均无投递防护，但订阅者只在界面线程构造：WidgetManager :962（首刷之后）、SettingsViewModel 随 SettingsWindow 打开构造；广播现在只在 UI 线程触发 → 两者端到端安全。订阅关系在 ThemeService 生命周期内不移交其他线程 |
| C6 不回归 | **成立** | 系统色变更链（`OnColorValuesChanged` :29-44 → TryEnqueue → 200ms debounce 计时器 → Tick → RefreshAppearance）不变，仍即时生效；启动首刷工作量≈空（无 TrackWindow、零订阅者），原"并行化"无实际收益，改同步无串行退化 |
| C7 门禁 | **成立** | 3006/3006 全绿；无新增警告（12 条均为历史警告） |

---

## 2. 新发现缺陷清单（按严重度排序）

> 去重声明：以下 N1–N5 均对照 `defect-ledger.md` 与 `R6-P1-remediation.md` 末尾 5 条观察项核对——N1/N2 为新增 P2；N3/N4/N5 为新增 P3；观察项 2（2 参 `ShiftBoundsToNearestEdge` 死代码，经 `git grep` 复核确认零调用）与观察项 4（`CopyPathToClipboard` 缺 Flush）**不入本清单**（已知）。paste-list、死宿主内位点不重复立案。

### N1 【P2，置信度 高】边距路径移动展开态格子后不刷新 CompactPlacement，折叠时胶囊回跳旧坐标（跨会话持续）

- **位置**：`src/DeskBox/Views/WidgetWindowBase.Collapse.cs`（折叠目标计算 :2586-2591 与 :2810-2816）、`src/DeskBox/Services/WidgetCompactBoundsCalculator.cs:66-107`（`Resolve` 优先非空 placement）；修复面：`WidgetManager.BulkAppearance.cs:155-185`（批量）、`WidgetWindowBase.TitleAppearance.cs:512-549`（单格）、:379-404（取消恢复）。
- **触发条件**：任意非 Expanded 折叠模式（System/Smart/Click）的格子**折叠过一次**（`EnsureCompactPlacement` 已持久化 `CompactPlacement`），在**展开态**下对其/全体执行边距移动（单格或"应用到所有"或对话框预览），随后折叠。
- **影响**：胶囊（或组胶囊栏）落到**移动前旧坐标**，偏移量 = margin 移动量；`EnsureCompactPlacement`（`Collapse.cs:3676-3684`）在 placement 非空时早退，**不覆盖不修复**，回跳反复出现；重启后从持久化 stale placement 解析（`WidgetCompactBoundsCalculator.cs:66-107` 不读新 X/Y/anchor），**跨会话持续**；组宿主场景 `CaptureGroupLayout`（`Groups.cs:1865`）把 stale placement 固化进组配置并回写全体成员（:1896），数据污染确定发生（胶囊栏重排有自愈但被不含位置的几何签名门控 :484-504）。
- **根因**：折叠目标恒经 `GetCompactBounds → WidgetCompactBoundsCalculator.Resolve`，placement 非空即优先用 `placement.X/Y + anchor/margin/monitor` 解析，与展开框无关；而边距三条持久化路径只写 X/Y/W/H+锚点，从不同步 placement —— 与交互拖拽（`CompleteExpandedWidgetDrag` :3465-3504 平移并 `CaptureCompactPlacement`）、缩放（`RecaptureCompactPlacementAfterExpandedResize` :3402-3430）形成**路径不对称**。本批修复（81d4147）把批量路径补全了锚点/组/拓扑持久化，唯独留下 placement 成员；`BulkAppearance.cs:172-179` 新注释的"compact states never reach this point"不成立。
- **证据**：
  ```csharp
  // WidgetCompactBoundsCalculator.cs:66-107（摘）
  if (config.CompactPlacement is not { } placement)
  {
      return Calculate(expandedBounds, config.PositionAnchor, ...);  // 仅无 placement 才从展开框重算
  }
  ...
  RectInt32 resolved = WidgetPositioningService.ResolveBoundsForCurrentTopology(placementConfig); // 用旧 placement 坐标
  ```
- **修复方向（未实施，供返工）**：在三条边距持久化路径上，当 `UsesCompactExpansionGeometry() && !_targetCollapsed && Config.CompactPlacement != null` 时按移动 delta 平移 placement（镜像 `Collapse.cs:3484-3503`）或调用 `RefreshCompactPlacementFromExpandedBounds(persist: true)`，再走组/拓扑同步；`IDesktopWidgetWindow` 目前无 compact 刷新接口，需先补一个方法。与后续 DEF-008 返工批次合并处理。

### N2 【P2，置信度 中】SearchPopup 公开 `async void` 入口全方法无异常保护，加载链抛错即未处理崩溃 + 弹窗半显示

- **位置**：`src/DeskBox/Views/SearchPopupWindow.xaml.cs:173`（`public async void ShowPopup()`）、:181（`ShowPopupWithQuery(string)`）、:186-320（`ShowPopupCoreAsync` 全域无 catch，仅 :287-294 try/finally 保护 `_suppressPanelEntranceAnimation`）；调用方 `App.xaml.cs:3919`（热键）/ :3986、:3990（`OpenSearchPopupCore`）/ `SearchWidgetContent.xaml.cs:293,300` / `ContentWidgetWindow.xaml.cs:521` 全部 fire-and-forget 零守卫。
- **触发条件**：`_viewModel.OnPopupOpenedAsync()`（:289）或 `_viewModel.Query` 赋值 → `RebuildTabs`(:814-892，不在 try 内) 链抛错（集合/属性链上任何异常）。
- **影响**：异常沿 async void 逃逸为 UI 线程未处理异常；`AppDomain.UnhandledException`/`TaskScheduler.UnobservedTaskException` 未注册（DEF-021，两缺陷互为放大：修 DEF-021 后本缺陷降级为"半显示 + 状态残缺"，但仍无清理）；崩溃瞬间 `IsPopupVisible=true` 已置（:222），弹窗处于"可见但加载中断"态。
- **根因**：公开 API 入口（非事件处理器）使用 async void 且未自吞；无全局兜底时即进程级崩溃路径。
- **证据**：`IsPopupVisible = true;`（:222）→ `PopupShown?.Invoke`（:223）→ `await Task.Yield()`（:249）→ `await _viewModel.OnPopupOpenedAsync()`（:289）——首帧后所有异步加载无保护。
- **修复方向**：`ShowPopup`/`ShowPopupWithQuery` 改 `async Task` 并全文 try/catch（记录 + 清理半显示态），或至少包一层 try/catch + `App.Log`；调用方保持 fire-and-forget 可经 `SafeFireAndForget` 收敛。

### N3 【P3，置信度 中】SearchPopup 多选复制混合解析分支静默丢弃解析失败的路径

- **位置**：`SearchPopupWindow.xaml.cs:4145-4180`（`SetDragPayloadAsync(List<string>)`），具体 :4172-4179。
- **触发条件**：多选复制/剪切中部分路径解析失败（已删除/被锁/网络盘断开）：`items.Count > 0` 分支只 `SetStorageItems(items)`，:4176 的 `fallbackText.Length > 0 && items.Count == 0` 条件使失败路径文本被**整体丢弃**。
- **影响**：用户选 N 个复制，剪贴板只有成功解析的子集，粘贴时数量少于预期，失败项静默消失（无任何提示）。
- **根因**：混合分支只写 StorageItems 不合并 fallbackText。
- **证据**：
  ```csharp
  if (items.Count > 0) { data.SetStorageItems(items); }
  if (fallbackText.Length > 0 && items.Count == 0) { data.SetText(...); }  // 混合时失败路径被丢
  ```
- **备注**：与 DEF-009 修复正交（剪贴板记录器在无 Text 时不产生自回录），属既有新发现。

### N4 【P3，置信度 低】SearchPopup 拖拽启动 `StartDragAsync` 仅 try/finally 无 catch

- **位置**：`SearchPopupWindow.xaml.cs:2781`（`ResultsPanel_PointerMoved` async void 内 `await dragSource.StartDragAsync(...)`，:2783-2788 finally 退订）。
- **触发条件**：罕见的指针状态异常/平台拖拽失败。
- **影响**：异常从 async void 逃逸为未处理异常（DEF-021 未注册全局兜底 → 进程崩溃面）。拖拽状态机其余部分（`_dragCandidate/_dragSourceRow/_dragOccurred` 清理点 :2644-2645/2675-2677/2694-2695/2786-2787/2840-2842/3189-3191，重入守卫 :2711）**经核验健全**，无 stale 重复触发、无 null 路径，仅此单点缺 catch。

### N5 【P3，置信度 中低】`EnsureVisible` 完全出屏回退 (32,32) 对超宽格子非"捕获→解析"不动点（当前边距入口不可达，列为防御性注意项）

- **位置**：`WidgetPositioningService.cs:267-274`（wildly 分支）+ :216-224（`CaptureAnchorCore` 锚侧选择）。
- **触发条件/影响**：当 `W > workArea.Width - 32`（如 1568+ 宽于 1600 工作区）时，(32,32) 处 rightMargin<0 → 锚翻转为 RightTop、MarginX 记 0 → 重启 `ResolveAnchoredX = workArea.Width - W < 32`，产生 ≤31px 位移。**经数值推演，边距入口（margin≤200、boundary 受 workArea 约束）该条件不可达**（1500/1600 场景只会走 clamp 分支到 X=0/100，均为不动点，两个方向数值演算见锚点深挖报告）；仅当 `EnsureVisible` 未来被其他输入域（更宽的目标）复用时需警惕。
- 关联的取消恢复出界例外（`TitleAppearance.cs:379-404` 在 `dialogInitialRect` 部分出屏时 clamp 负边距为 0 → 重启拉回界内）为**既有行为**（`ResolveBoundsCore` 一贯如此），非本批引入，一并记录不单独立案。

**已核验为安全/有效的面**（防误报，供后续轮次参考）：
- `SearchResultItem.DetailPath` 全部 4 处 `!`（:2745/:3593/:3652/:3787）均有前置非空/存在守卫，其余消费点（ExecuteItem、OpenSelectedLocation、Attach/Save、Rename/Delete、Preview、Properties）全部自守卫——无空引用违例。
- 三处 `MarkWrite` 在纯文本回退分支逐字符命中 `ShouldIgnoreText`（精确 Ordinal + 按行 NormalizePath 双道），无尾随换行错配——DEF-009 修复在全部实际产出面上有效。
- clamp→捕获→重启三连（单格/批量/取消恢复）构成自洽不动点：`ResolveBoundsCore` 末尾再过一次 `EnsureVisible`(`PositioningService.cs:89`)，clamped 目标恰为捕获→解析的不动点（1500/1600 双向数值演算一致）。

---

## 3. 总体结论

**P1 批次可视为闭环。** 三项修复（D008 锚点/组/拓扑三连 + 落点钳制，D009 四处标记，D010 启动回 UI 线程 + 入口自愈）均已独立取证确认为修复到位，机理被新增用例锁死（锚点缺失回退用例、2.1s 窗口用例、EnsureVisible 钳制用例），构建 0 错误、x64 回归 3006/3006 全绿。外部审查候选三项的落地结论：候选 (a)（CompactPlacement 间隙）**确证成立 → 本报告立案 N1（P2）**；候选 (b)（批量预览 N×3 持久化开销）经核实为既有 LAY-08 同型、SaveDebounced 合并，不新立案；候选 (c)（SearchPopup async void / `!`）核验 `!` 全部安全、事件型 async void 大多已受保护，但**两个公开 async void 入口是真实风险 → 立案 N2（P2）**。

**返工指令（精确、最小、可验证）**：
1. **N1（建议随 DEF-008 同批处理，P2）**：在 `ApplyOwnMarginToSide`、`MoveVisibleWidgets`、取消恢复块三条边距持久化路径上，当 `UsesCompactExpansionGeometry() && !_targetCollapsed && Config.CompactPlacement != null` 时，按移动 delta 平移 placement（镜像 `CompleteExpandedWidgetDrag` :3484-3503）或 `RefreshCompactPlacementFromExpandedBounds(persist:true)`；补接口方法供 manager 侧调用；同步删/改 `BulkAppearance.cs:172-179` 的错误注释；补行为用例（批量边距→折叠→胶囊位置 = 新展开框派生位置）。
2. **N2（P2）**：`ShowPopup`/`ShowPopupWithQuery` 改 `async Task` + 全域 try/catch（Log + 半显示态清理），或经 `SafeFireAndForget` 收敛；与 DEF-021（全局兜底注册）解耦处理，勿等。
3. **N3/N4/N5（P3）**：随 P3 卫生批次：N3 混合分支补写 fallbackText；N4 `StartDragAsync` 补 catch；N5 在 `EnsureVisible` 或文档标注"宽于工作区-32 时 wildly 回退非不动点"的约束；另建议为 `_trackedWindows` 的 UI 线程归属加注释声明（关联 C3）。

> 复核过程为只读：未修改任何源码；唯一落盘文件为本报告。门禁数据与全部 file:line 均为本次实测/直读所得。
>
> **整改追踪（第 10 次循环核验）**：本报告 N1~N5 已按上方返工指令全量整改并闭环——
> N1（ICL-028/DEF-028）：新增 `WidgetWindowBase.RefreshCompactPlacementAfterBoundsMove`（折叠态/关闭/无展开几何/无已存 placement 均早退，UI 线程自愈重投），经 `IDesktopWidgetWindow` 暴露；批量 `MoveVisibleWidgets`（锚点捕获后、组/拓扑同步前）、单格 `ApplyOwnMarginToSide`、取消恢复块三条路径均接入；`BulkAppearance.cs` 误导注释改正。N2（DEF-029）：`ShowPopup`/`ShowPopupWithQuery` 改经 `DispatchShowPopupAsync`（非 UI 线程 TryEnqueue 重投 + 队列关闭静默记录）→ `ShowPopupSafelyAsync` 全管线 try/catch（Log + 已可见时重聚焦）。N3：`SetDragPayloadAsync(List)` 失败路径文本不再被 `items.Count==0` 门控丢弃。N4：`StartDragAsync` 补 catch。N5：`EnsureVisible` wildly 回退非不动点约束注释 + `_trackedWindows` UI 线程归属注释。门禁：Debug x64 构建 0 错误（24 条既有警告）；x64 回归 **3011/3011**（+5 新用例 `tests/DeskBox.Tests/MarginMoveCompactPlacementContractTests.cs`；既有备份测试一次偶发失败单独重跑通过、整轮重跑全绿，与本批无关）。台账新立 DEF-028/DEF-029 标记已修复；TODO 清单补第 10 次核验行。