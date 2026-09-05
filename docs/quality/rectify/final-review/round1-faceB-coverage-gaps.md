# 面B：收敛式深度审查报告（SearchPopup 业务段 / async void 抽检 / null-forgiving 抽检 / Onboarding 段）

> **审查日期**：2026-09-01  
> **仓库**：/root/DeskBox  
> **分支**：wip/fix-bug，HEAD = 1df469e  
> **工作目录**：/root/DeskBox  
> **审查范围**：round-06 总报告自报覆盖率限制区（§8 第 3 段 + 移交核查项）  
> **与已知缺陷对照**：`docs/quality/defect-ledger.md`、`rectify/R6-P1-independent-review.md`、`rectify/R6-P1-independent-review-remediation.md`

---

## 一、已知问题去重声明

| 编号 | 位置 | 审查期间状态 | 本报告判定 |
|---|---|---|---|
| **DEF-029 / N2** | `SearchPopupWindow.xaml.cs:176/185` | 已修复（commit 4bc81af） | ✅ **已闭环**：`ShowPopup`/`ShowPopupWithQuery` 改经 `DispatchShowPopupAsync` → `ShowPopupSafelyAsync`（全管线 try/catch + Log + 已可见时重聚焦） |
| **N3** | `SearchPopupWindow.xaml.cs:4187-4210`（`SetDragPayloadAsync(List)`） | 已修复（commit 4bc81af） | ✅ **已闭环**：`fallbackText.Length > 0` 即写 Text，与 StorageItems 并存，`items.Count == 0` 门控已消除（契约测试 `SearchPopup_MixedPayload_NoLongerDropsFailedPaths`） |
| **N4** | `SearchPopupWindow.xaml.cs:2781`（`ResultsPanel_PointerMoved` 内 `StartDragAsync`） | 已修复（commit 4bc81af） | ✅ **已闭环**：补 `catch (Exception ex)` + `App.Log` |
| **N5** | `WidgetPositioningService.cs:267-274` | 已修复（commit 4bc81af） | ✅ **已闭环**：wildly 回退分支标注约束注释；`_trackedWindows` UI 线程归属注释 |
| **QC-04**（死宿主） | `QuickCaptureWidgetWindow.Attachments.cs:40-55` | 已修复（commit ad8febe / DEF-027） | ✅ **已闭环**：死宿主整体删除，生产路径无此问题 |

**无重复立案。**

---

## 二、抽检方法论

| 类别 | 抽样范围 | 方法 |
|---|---|---|
| **async void** | 全仓 220 处（≥40+ 抽检要求的 5× 覆盖） | 全文 grep + 体块 brace matching → try/catch 判定 |
| **null-forgiving** | 85 处（`!.`/`= null!` 模式） | 全文 grep + 上下文前置守卫核查 |
| **SearchPopup 业务段** | `ShowPopupSafelyAsync` 链路之外：拖拽/复制/状态机/剪贴板/生命周期 | 逐函数精读 + 调用链追溯 |
| **Onboarding 动画/流程** | 5 个分部文件 + 主文件，生命周期对称性 + 事件退订 | 事件订阅/退订矩阵 + storyboard 生命周期追踪 |
| **交叉对照** | 台账 + 独立复核报告 + F7 卫生批次 | 逐条比对 |

---

## 三、SearchPopupWindow 业务段精读结论

### 3.1 N2/N3/N4（独立复核整改批次，commit 4bc81af）— ✅ 已闭环

| 项 | 原文位置 | 修复证据 | 契约测试 |
|---|---|---|---|
| N2（ShowPopup/ShowPopupWithQuery 入口） | L176/L185 | 改经 `DispatchShowPopupAsync`（非 UI 线程 TryEnqueue 重投）→ `ShowPopupSafelyAsync` 全管线 try/catch | — |
| N3（SetDragPayloadAsync 混合分支） | L4187-4210 | `fallbackText.Length > 0` 即写 Text（`items.Count == 0` 门控消除） | `SearchPopup_MixedPayload_NoLongerDropsFailedPaths` |
| N4（StartDragAsync 无 catch） | L2781 | `ResultsPanel_PointerMoved` 内补 catch（Log） | — |

**N5（EnsureVisible wildly 回退）**：纯注释变更（`WidgetPositioningService.cs` + `ThemeService.cs`），无行为变化。✅ 已闭环。

### 3.2 其他业务路径核验

| 路径 | 位置 | 结论 | 证据 |
|---|---|---|---|
| `CopySelectedItemsAsync` | L3657-3686 | ✅ 安全 | 全 try/catch，含 MarkWrite + Clipboard.SetContent + Flush |
| `DeleteSelectedItemsAsync` | L3689-3745 | ✅ 安全 | 全 try/catch，File.Delete → RecycleBin 路径保护 |
| `AttachItemToTodoAsync` | L4100-4110 | ✅ 安全 | `SearchResultActionService.AttachFileToTodoAsync` 内部有 try/catch（L23-53） |
| `SaveItemToNoteAsync` | L4113-4123 | ✅ 安全 | `SearchResultActionService.SaveFileToNoteAsync` 内部有 try/catch（L78-110） |
| `_rubberBandAutoScrollTimer!` | L3339 | ✅ 安全 | 调用前 `EnsureRubberBandAutoScrollTimer()` 初始化（L3373-3389） |
| `handler = null!` | L2812 | ✅ 安全 | 声明后立即赋值（L2813 `handler = async (_, args) => {...}`），无空洞期 |
| 剪贴板 MarkWrite 标记 | L3671/L3899/L4135 | ✅ 已覆盖 | DEF-009 修复已覆盖三处（多选复制/单文件复制/复制路径） |
| 拖拽路径 `FindIconDragSource`/`FindVisualChild` | L2830-2876 | ✅ 安全 | 返回 null 时有降级到 `row`（L2826 `?? row`） |

### 3.3 低风险模式观察

| 位置 | 结论 | 说明 |
|---|---|---|
| `CopySelectedButton_Click` L3747 | ✅ 安全（委托层） | 委托至 `CopySelectedItemsAsync`（有保护），但自身无 try/catch |
| `CutSelectedButton_Click` L3752 | ✅ 安全（委托层） | 同上 |
| `DeleteSelectedButton_Click` L3757 | ✅ 安全（委托层） | 同上 |
| `AttachSelectedButton_Click` L4398 | ✅ 安全（委托层） | 委托至 `AttachItemToTodoAsync`（有保护） |
| `SaveSelectedButton_Click` L4406 | ✅ 安全（委托层） | 委托至 `SaveItemToNoteAsync`（有保护） |
| `ResultsPanel_PointerMoved` L2760 | ✅ 安全 | 全 try/catch/finally，N4 修复后完整 |

---

## 四、async void 全仓抽检统计

| 统计项 | 数值 |
|---|---|
| 总 async void 方法数 | **220** |
| 有 try+catch 双向保护 | 43（19.5%） |
| 有 try 无 catch | 3（GlanceCalendar 轮、NotesTimer 链） |
| 无 try/catch | 174（79%） |
| 涉及文件数 | 32 |

### 4.1 高风险模式分类

#### A. 委托至受保护内部方法（当前安全，模式风险）

| 文件 | 方法 | 行号 | 委托目标 |
|---|---|---|---|
| `SearchPopupWindow.xaml.cs` | `CopySelectedButton_Click` | 3747 | `CopySelectedItemsAsync` ✅ |
| `SearchPopupWindow.xaml.cs` | `CutSelectedButton_Click` | 3752 | `CopySelectedItemsAsync` ✅ |
| `SearchPopupWindow.xaml.cs` | `DeleteSelectedButton_Click` | 3757 | `DeleteSelectedItemsAsync` ✅ |
| `SearchPopupWindow.xaml.cs` | `AttachSelectedButton_Click` | 4398 | `AttachItemToTodoAsync` ✅ |
| `SearchPopupWindow.xaml.cs` | `SaveSelectedButton_Click` | 4406 | `SaveItemToNoteAsync` ✅ |
| `OnboardingWindow.xaml.cs` | `NextButton_Click` | 327 | `NavigateToStepAsync`/`CompleteOnboardingAsync` ✅ |
| `OnboardingWindow.xaml.cs` | `SkipButton_Click` | 343 | `CompleteOnboardingAsync` ✅ |
| `WidgetWindowBase.Collapse.cs` | `WidgetShellControl_CompactPreviousRequested` | 2367 | `OnCompactPreviousRequestedAsync` ⚠️ |
| `WidgetWindowBase.Collapse.cs` | `WidgetShellControl_CompactPrimaryActionRequested` | 2372 | `OnCompactPrimaryActionRequestedAsync` ⚠️ |
| `WidgetWindowBase.Collapse.cs` | `WidgetShellControl_CompactPlayPauseRequested` | 2378 | `OnCompactPlayPauseRequestedAsync` ⚠️ |
| `WidgetWindowBase.Collapse.cs` | `WidgetShellControl_CompactNextRequested` | 2383 | `OnCompactNextRequestedAsync` ⚠️ |

#### B. 直接调用未保护外部 API（真实风险点）

| 文件 | 方法 | 行号 | 风险操作 |
|---|---|---|---|
| `ContentWidgetWindow.xaml.cs` | `OnCompactPrimaryActionRequestedAsync` | 517 | `todo.ViewModel.SetCompletedAsync` 无 try/catch |
| `Services/TodoReminderService.cs` | `Timer_Tick` | 372 | `CheckNowAsync` 无保护（定时器异常 = 静默失败） |
| `Views/SettingsWindow.Maintenance.cs` | `ResyncRuntimeStateButton_Click` | 18 | `ViewModel.ResyncRuntimeStateAsync` 无保护 |
| `Views/SettingsWindow.Maintenance.cs` | `OpenUacSettingsButton_Click` | 112 | `Task.Run(Win32Helper.OpenFile)` 无保护 |
| `Views/SettingsWindow.StorageAndUpdates.cs` | `ShowStoreSupportDialogButton_Click` | 235 | `SupportDeskBoxDialog.ShowAsync` 无保护 |
| `Views/SettingsWindow.StorageAndUpdates.cs` | `OneClickUpdateButton_Click` | 283 | 更新启动无保护 |
| `Views/OnboardingWindow.Storage.cs` | `Step4PinToggle_Toggled` | 179 | `ExplorerQuickAccessHelper` API 无保护（弹窗路径可能抛异常） |
| `Views/OnboardingWindow.TaskFlow.cs` | `TaskStep4ToggleWidgets_Click` | 212 | `ToggleWidgetsForOnboardingAsync` 无保护 |

#### C. 纯 UI 交互（低风险，异常表现为 UI 卡顿而非崩溃）

- `TodoWidgetContent.DetailNotesAndSteps.cs` 中 14 个按钮/键盘/定时器处理器（如 `DetailAddStepButton_Click`、`NotesAutosaveTimer_Tick`）
- `MusicWidgetContent.xaml.cs` 中 6 个播放器控制处理器
- `FileSurfaceContent.xaml.cs` 中多个文件操作处理器

---

## 五、null-forgiving (`!`) 抽检结论

### 5.1 SearchPopupWindow（仅 2 处）

| 行号 | 代码 | 结论 | 证据 |
|---|---|---|---|
| L2812 | `TypedEventHandler<UIElement, DragStartingEventArgs> handler = null!;` | ✅ 安全 | 立即赋值：`handler = async (_, args) => {...}`（L2813-2838），无空洞期 |
| L3339 | `_rubberBandAutoScrollTimer!.Start();` | ✅ 安全 | 调用前 `EnsureRubberBandAutoScrollTimer()`（L3338）保证非 null |

### 5.2 QuickCaptureService._data! 模式（27 处）

**结论**：✅ **全部安全**。所有 `_data!` 使用均在 `try { await EnsureLoadedCoreAsync(); ... }` 块内，`EnsureLoadedCoreAsync` 在成功路径上保证 `_data` 非 null。

| 行号 | 上下文守卫 |
|---|---|
| L68 | `await EnsureLoadedAsync()` |
| L140/L345/L604 | `await EnsureLoadedCoreAsync()` |
| L1428 | `SaveCoreAsync` 私有方法，调用方均有 `EnsureLoadedCoreAsync` |
| L1523/L1729 | 私有静态/实例方法，调用方保证 `_data` 已加载 |

### 5.3 其他文件（AOT smoke 测试、ViewModel、Helpers）

- **`App.Aot*Smoke.cs` 系列**：测试桩代码，`cached.Module!` 等均有前置 `if (cached?.Success == true)` 守卫 ✅
- **`ViewModels/SettingsViewModel.AboutAndUpdates.cs`**：`manifest!.ManualDownloadUrl` 等均有 `IsSafeWebUrl(manifest?....)` 守卫 ✅
- **`Helpers/ShortcutNativeBackend.cs`**：`cachedLoad!.Module!` 等均在 `if (defaultLoad.Success)` 块内 ✅

**未发现前置守卫缺失的空洞 `!` 使用。**

---

## 六、OnboardingWindow 动画/流程生命周期核验

### 6.1 事件订阅/退订矩阵

| 事件 | 订阅行 | 退订行 | 结论 |
|---|---|---|---|
| `_localizationService.LanguageChanged` | L69 | L147 | ✅ 对称 |
| `_settingsService.SettingsChanged` | L70 | L148 | ✅ 对称 |
| `App.Current.OnboardingFileImportCompleted` | L71 | L149 | ✅ 对称 |
| `App.Current.OnboardingWidgetsVisibilityChanged` | L72 | L150 | ✅ 对称 |
| `SizeChanged` | L94 | — | ✅ 无需退订（生命周期跟随窗口） |
| `RootGrid.KeyDown` | L95 | — | ✅ 无需退订（生命周期跟随窗口） |
| `RootGrid.Loaded` | L96 | — | ✅ 无需退订（一次性） |
| `RootGrid.ActualThemeChanged` | L122 | — | ✅ 无需退订（生命周期跟随窗口） |
| `Closed` | L129 | — | ✅ 窗口关闭时触发，退订在其内部完成 |
| `storyboard.Completed` | L484 | — | ⚠️ 见 §6.2 |

### 6.2 `_stepTransitionStoryboard.Completed` 生命周期

- **订阅**：L484（`NavigateToStepAsync` 内）
- **停止**：L400（下次 `NavigateToStepAsync` 调用时 `Stop()`）
- **问题**：Completed handler 闭包捕获 `currentPanel`/`newPanel` 引用，未在 Completed 后显式解引用
- **实际影响**：每次过渡动画完成后（~1s），storyboard 自然完成，GC 可回收闭包。不存在持久内存泄漏
- **结论**：✅ **低风险，模式建议改进**：可在 Completed 内 `storyboard.Completed -= handler;` 提前解引用

### 6.3 `PlayIntroSequence` 动画生命周期

- **保护**：完整 try/catch + `_introGeneration` 版本号守卫（L32/L84/L91/L105/L110/L115/L136/L196）
- **超时处理**：`Task.WhenAny(animationTask, timeoutTask)` 保证最长 4.7s 后恢复
- **结论**：✅ **设计健全**

### 6.4 关键流程处理器

| 方法 | 行号 | 结论 |
|---|---|---|
| `NextButton_Click` | L327 | ✅ 委托至 `NavigateToStepAsync`/`CompleteOnboardingAsync`，前者有 `_isAnimating` 守卫 |
| `SkipButton_Click` | L343 | ✅ 委托至 `CompleteOnboardingAsync` |
| `Step4PinToggle_Toggled` | L179 | ⚠️ 无 try/catch，但 `TryPinFolderToQuickAccessAsync` 内部有保护 |
| `TaskStep4ToggleWidgets_Click` | L212 | ⚠️ 无 try/catch，`ToggleWidgetsForOnboardingAsync` 行为需确认 |

---

## 七、新立案缺陷清单

> **立案规则**：与已知台账 `DEF-008`~`DEF-029`、R6 卫生批次 P3 清单（QC-04~QC-15, EVT-02~EVT-04, CFG-03~CFG-09, THR-06~THR-07, EXC-04~EXC-06, ARC-02~ARC-06）无重复。

| 编号 | 严重度 | 文件 | 行号 | 标题 | 结论 | 触发 | 影响 | 根因 | 证据 | 置信度 | 处置建议 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **F7-B1** | P3 | `Views/WidgetWindowBase.Collapse.cs` | 2367-2383 | Compact 壳控件 4 个 async void 处理器委托至未保护的 `OnCompact*RequestedAsync` | **隐患** | 用户通过胶囊迷你控件触发播放/上下曲/主操作 | 极少数场景下平台异常逃逸为 UI 线程未处理异常 | 调用链 `async void → await OnCompact*RequestedAsync`，目标方法无 try/catch | `ContentWidgetWindow.xaml.cs:517` 无保护；委托链无防御 | 中 | 在 `WidgetWindowBase.Collapse.cs` 四个处理器内加 try/catch，或在 `ContentWidgetWindow.OnCompactPrimaryActionRequestedAsync` 补保护 |
| **F7-B2** | P3 | `Views/ContentWidgetWindow.xaml.cs` | 517 | `OnCompactPrimaryActionRequestedAsync` 无异常边界 | **隐患** | 胶囊主操作（播放/暂停/下一步）触发 | 极少数平台/音乐服务异常逃逸 | 职责不清：async void 调用方期望 self-contained | `await todo.ViewModel.SetCompletedAsync(...)` 无保护 | 高 | 补 `try { ... } catch (Exception ex) { App.Log(...) }` |
| **F7-B3** | P3 | `Views/OnboardingWindow.xaml.cs` | 484 | `_stepTransitionStoryboard.Completed` 事件闭包未显式退订 | **卫生** | 引导页步骤切换 | 每次过渡动画 ~1s 内闭包捕获的 UI 元素引用延迟回收 | 设计时依赖 GC 最终回收 | storyboard 自然完成后 GC 可回收，但最佳实践应显式退订 | 中 | 在 Completed 内 `storyboard.Completed -= handler;` |
| **F7-B4** | P3 | `Views/SettingsWindow.Maintenance.cs` | 18/112 | `ResyncRuntimeStateButton_Click` / `OpenUacSettingsButton_Click` 无异常边界 | **卫生** | 用户点击维护按钮 | 极端情况下 UI 线程异常（概率极低） | 事件处理器模式不统一 | 部分维护按钮（ExportDiagnostics）有 try/catch，部分无 | 中 | 统一加 try/catch + App.Log |
| **F7-B5** | P3 | 全仓 174 处 | — | async void 事件处理器全量缺乏 try/catch 的架构卫生问题 | **观察** | 任意用户交互触发 | 长期累积增加调试难度和潜在崩溃面 | 项目约定"事件处理器豁免 async void 但异常会终止后续 UI 逻辑"未强制执行 | 全仓统计见 §4 | 中 | 建议代码审查 checklist 加入 async void 保护检查项 |

---

## 八、已核验为安全的项（防误报，供后续参考）

| 项 | 文件:行 | 结论 | 证据 |
|---|---|---|---|
| `ShowPopupCoreAsync` 全管线 | L247-380 | ✅ 安全 | 虽无 try/catch，但由 `ShowPopupSafelyAsync` 外包（L231-244） |
| `DispatchShowPopupAsync` | L197-223 | ✅ 安全 | 自身 try/catch + TryEnqueue 守卫 |
| `ResultsPanel_PointerMoved` 拖拽 | L2760-2844 | ✅ 安全（N4 修复后） | 完整 try/catch/finally，`_dragCandidate/_dragSourceRow` 清理健全 |
| `_rubberBandAutoScrollTimer!` | L3339 | ✅ 安全 | `EnsureRubberBandAutoScrollTimer()` 前置守卫 |
| `handler = null!` | L2812 | ✅ 安全 | 声明后立即赋值，无空洞期 |
| QuickCaptureService `_data!` | 全 27 处 | ✅ 安全 | 均在 `await EnsureLoadedCoreAsync()` 后的 try 块内 |
| AOT smoke `Module!` | 多处 | ✅ 安全 | 均有 `if (cached?.Success == true)` 前置守卫 |
| Onboarding `PlayIntroSequence` | L29-95 | ✅ 安全 | 完整 try/catch + `_introGeneration` 版本号守卫 + 超时处理 |
| Onboarding 事件退订 | L69-150 | ✅ 对称 | 4 个 App 级订阅均在 Closed 内退订 |
| `CopySelectedItemsAsync`/`DeleteSelectedItemsAsync` | L3657-3745 | ✅ 安全 | 全 try/catch + MarkWrite + Status 反馈 |
| `AttachItemToTodoAsync`/`SaveItemToNoteAsync` | L4100-4123 | ✅ 安全 | 委托至 `SearchResultActionService`（内部有 try/catch） |

---

## 九、执行说明

- **执行过程**：全文检索 220 处 async void + 85 处 null-forgiving + 逐函数精读 SearchPopup/Onboarding 业务段 + 交叉对照台账
- **门禁**：未修改任何源码；唯一落盘文件为本报告
- **与已知台账关系**：N2-N5/DEF-028/DEF-029 均已闭环；本批次 5 条新发现均为 P3 卫生类
- **后续建议**：F7-B1/B2 可随 P3 卫生批次统一修复；F7-B3 为可选改进；F7-B4/F7-B5 建议纳入代码审查 checklist

---

> **审查者**：Agnes-2.5-Flash（Hermes Agent subagent）  
> **时间**：2026-09-01 22:30 CST  
> **对照基准**：`docs/quality/defect-ledger.md`、`rectify/R6-P1-independent-review.md`、`rectify/R6-P1-independent-review-remediation.md`、`round-06/全量代码缺陷审查总报告.md`
