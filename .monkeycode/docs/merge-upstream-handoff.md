# 合并交接文档：upstream/main (v1.5.5) → fork main

> 日期：2026-09-22　分支：`merge-upstream-main`（合并进行中，MERGE_HEAD=d4b0a7a）
> 任务：将 https://github.com/Tianyu199509/DeskBox `main`（@d4b0a7a，v1.5.5）合入 https://github.com/beici/DeskBox `main`（@bc60fbc）。
> 约束：不破坏 fork 现有功能；冲突择优；Linux 环境只能静态核对，无法编译/跑测试。

## 一、本次会话已完成（均已在 index 中）

### 已解决并暂存（git add 过）
- 删除 10 个死宿主文件 `src/DeskBox/Views/QuickCaptureWidgetWindow*`（git rm -f；fork ad8febe DEF-027 已移除该宿主，QuickCapture 走 ContentWidgetWindow + QuickCaptureSurfaceContent）。
- `AppSettings.cs`：上游 slice 架构（门面属性委托 Core/WidgetShell/… slices）。
- `App.xaml.cs`：10 处冲突全解（自愈+watchdog、StartupServiceFactory.Create+legacy 迁移+prefetch、LogBuildIdentity、上游并行启动、widget-manager 带 AuxiliaryWindowProvider、保留 fork ShowStartupFailureNotification、上游 ApplyDefaultAutoStartOnce、Shutdown 双 Dispose）。
- `SettingsService.cs`：删 WidgetTextEdgeMode/TitleAlignment/FrameRate 默认值；用 RunMigrationsOnCopy。
- `SettingsMigrationService.cs`：上游 copy-on-write 管线 + fork Migration_9_To_10（MusicSettingsStore）+ CurrentSchemaVersion=10。**有已知编译破损，见三-1**。
- `SettingsMigrationPipelineTests.cs`：整体取上游。
- `scripts/publish-aot-audit.ps1` + 7 个 `run-aot-*`/`start-aot-preview` 脚本：auditProfileVersion=59。
- `WidgetManager.ZOrder.cs`：融合上游 IsBoundsTransitionActive 延迟 + fork IsCompactAnimationRendering morph 延迟。
- `GlobalHotkeyService.cs`、`App.AotHotkeySmoke.cs`、`SettingsWindow.HotkeyAndAppearance.cs`：整体上游版（同步 API）。
- `WidgetForegroundContractTests.cs`：quickCapture 重定向到 QuickCaptureSurfaceContent.xaml.cs。
- **AGENTS.md**：本会话刚解完（上游 commit 规范段 + fork 零售包/AOT/Rust/架构/本地化段并集），无残留标记。

### WidgetWindowBase.Collapse.cs（仍 5 处冲突，取舍结论已定，直接照做）
上游重构了胶囊放置（Ensure→Derive 委托 + ResolveRequestedCompactExpansion 自适应展开）并删除了 fork 的 clamped-delta 时间线；fork 有 peek-guard（DEF-065）与 N1/DEF-065 margin 刷新。**结论：并集，fork 特性全部保留**：

1. **块1（~L130-160 字段区，`<<<<<<< HEAD` 到 `>>>>>>> upstream/main`）**：保留 fork 侧全部字段（`_collapseAnimationProgressTimestamp/_progressMs/_stalledMs/_maximumStepMs/_maximumStallMs/_hasCommittedCollapseAnimationFrame/_pendingBoundsMoveFallbackBounds/_beginApplyingBoundsCallback/_endApplyingBoundsCallback/_pendingBoundsMoveFallbackCallback/_pendingBoundsCommitCallbacks/_compactTransitionPresentationMs/_compactTransitionLayoutResolveMs`——3620-3653 的 tick 与 4413-4434 的 bounds fallback 在引用），**删** `_lastCompactExpansionBlockedFeedbackTimestamp`（块4取上游后变死字段）。fork 侧上游无对应。
2. **块2（~L530-637，原 RefreshCompactPlacementFromExpandedBounds vs SetCompactExpansionDirectionOverride/ApplyCompactExpansionDirectionChange）**：**并集**。上游两方法保留（当前树 1952 行 ApplyCollapseSettingsChanged 在调用；暂存的 WidgetCompactTrayVisibilityContractTests 断言 `ApplyCompactExpansionDirectionChange` 方法体含 CancelPendingCompactExpansion+InvalidateCompactExpansionReadiness，且位于 Ensure 方法之前）。fork 的 `RefreshCompactPlacementAfterBoundsMove`（public，含 Dispatcher 重入+peek 保护）与 `RefreshCompactPlacementFromExpandedBounds(bool persist)`（含 DEF-065 peek guard：`RestsCollapsed && CurrentCompactViewState == Peek` 时直接 return，防止 hover peek 污染 placement）也保留——调用方：`WidgetWindowBase.TitleAppearance.cs:617,806`、`WidgetManager.BulkAppearance.cs:184`、`WidgetManager.cs:220`（接口）。两方法之间用注释分隔即可。
3. **块3（~L660-677 Ensure 内部）**：**并集**。结构取上游：`EnsureCompactPlacementFromExpandedBounds` 只做 early-return+守卫后调 `DeriveCompactPlacementFromExpandedBounds(persist)`（新私有方法，含 2003 行调用方；测试 2003 行处断言 Derive 存在）；但 Ensure 的守卫要**加上 fork 的 peek guard**（DEF-065）：`if (RestsCollapsed && CurrentCompactViewState == Peek) { App.LogVerbose("[Compact] Peek expansion does not seed a placement …"); return; }`（在 `Config.CompactPlacement is not null` 早退之后、调 Derive 之前）。Derive 方法体=上游版（无 peek guard——上游测试 FixedDirectionCollapse_ 断言 SetCollapsedState 段不含 Refresh 调用，但 Derive 本身的 peek 语义由 Ensure 守卫承载）。
4. **块4（~L3195-3237，SetCollapsedState 展开分支 `preparedExpansionLayout ??`）**：**取上游** `ResolveRequestedCompactExpansion(compactBounds)`（单行）。fork 的"requireFullSize 失败→恢复 capsule+反馈气泡"整段删除。理由：暂存的测试 `ExpansionReadiness_NeverBlocksResolvedExpansionRequests` 明确断言该方法段 `Assert.DoesNotContain("!readinessLayout.CanExpand")`、`DoesNotContain("LogCompactExpansionBlocked(compact, readinessLayout")`；`RequestedCompactExpansion_AlwaysExpandsAndNeverShowsBlockedFeedback` 断言 resolver 段含 `requireFullSize: true`。注意当前树 3193 行已有上游式 `LogCompactExpansionBlocked(to, layout)`（collapse 分支，保留）。`LogCompactExpansionBlocked` 定义已取上游两参签名（2754 行）。
5. **块5（~L3432-3436 `long …Started = Stopwatch.GetTimestamp();`）**：取 fork `long responsiveLayoutDone = …`（fork 性能日志的区间锚点，块6需要）。
6. **块6（~L3492-3536 动画时钟+性能日志）**：**取 fork 整块**。fork 的 clamped-delta 初始化是必需品：watchdog（3564 行）引用 `_collapseAnimationMaximumStallMs`、tick（3644-3647）引用 `_collapseAnimationMaximumStepMs`，取上游会把两者置 0 导致动画 watchdog 立即超时/步进异常。fork 的 PerformanceLogger.Mark("CompactTransitionSetup") 是上游 "CompactBoundsTransitionPrep" 的严格超集（多了 presentation/layoutResolve 分段），取 fork 不丢上游信息。fork 块里 `setupStarted`（3414 行声明）、`responsiveLayoutDone`、`refreshRateDone`、`borderVisualsDone` 都有定义。
- 另：该文件字段区当前还有一个**已在共同区**的 `WidgetAnimationFrameRate` 相关代码（3451-3452 行 `SettingsService.Settings.WidgetAnimationFrameRate`）——依赖三-5 的属性补回，否则编译失败。

### 尚未解决的文件（18 - 1 AGENTS = 17 个 src/Strings + ~53 测试）
见 §二。

## 二、剩余工作清单（按优先级）

### A. src 冲突（7 个文件）
1. `src/DeskBox/Views/WidgetWindowBase.Collapse.cs`（5 处）——按 §一结论执行。
2. `src/DeskBox/Views/WidgetWindowBase.Foreground.cs`（2 处）——fork 版含 morph 渲染守卫（IsCompactAnimationRendering 延迟 z-order 提升），上游是 IsBoundsTransitionActive 延迟。参考已暂存的 WidgetManager.ZOrder.cs 融合方式：两个延迟条件并存（`if (_isCollapseAnimationRendering || IsBoundsTransitionActive) …`）。
3. `src/DeskBox/Services/ThemeService.cs`（3 处）——上游取并行 themeTask 预热（`await themeTask` 模式）；fork 侧有 WM10 兼容回退。逐块看，保留双方语义。
4. `src/DeskBox/Controls/WidgetShell.xaml.cs`（1 处）。
5. `src/DeskBox/ViewModels/SettingsViewModel.cs`（1 处）。
6. `src/DeskBox/Views/SettingsWindow.SectionElements.cs`（1 处）——fork 有 WidgetToolDialog/帧率设置 UI 相关；上游有新 SettingsSections 结构。
7. 11 个 `src/DeskBox/Strings/*.json`（ar-SA、bn-BD、de-DE、en-US、es-ES、fr-FR、hi-IN、ja-JP、pt-BR、ru-RU、zh-CN、zh-TW 中 11 个有标记）——键并集：fork 侧加的键有 `Settings.Animation.FrameRate.*`（en-US 2668-2679 共 6 键）、`Startup.Failure.*`；上游侧有 CopilotKey 等。**十二个文件键集合必须完全一致**（AGENTS.md 约束）。做完后跑一致性核对（对比键集合）。

### B. 编译破损修复（Strings 之外的最高优先级）
1. `src/DeskBox/Services/SettingsMigrationService.cs`：接口是 `ValueTask MigrateAsync(AppSettings)`，但管线 109 行调 `migration.Migrate(stepCopy)`（同步）不匹配。方案：接口改同步 `void Migrate(AppSettings settings)`，各迁移步实现改同步；`Migration_9_To_10` 需要同步路径（保留其 `static MigrateAsync` 供 `MusicSettingsStoreTests` 与 MusicSettingsStore 运行时调用——先查清两者用法再改）。保持 CurrentSchemaVersion=10。
2. `src/DeskBox/App.xaml.cs:1632`：`await GlobalHotkeyService.RefreshRegistrationAsync()` 已随上游同步版消失（现在只有 `RefreshRegistration()`，GlobalHotkeyService.cs:111）。改为上游 OnLifecycleRecoveryRequested 三连同步调用：`GlobalHotkeyService.RefreshRegistration(); _searchHotkeyService?.RefreshRegistration(); DesktopDoubleClickActivationService.RefreshRegistration();`（对照 upstream/main:1494-1510 逐行抄）。1637 行 `await DesktopDoubleClickActivationService.RefreshRegistrationAsync()` 同理（该服务两版都有：163 同步/177 异步，按上游写法统一）。
3. `Settings.WidgetAnimationFrameRate` 属性丢失：上游 AppSettings 已删（上游无此设置），但合并树 `WidgetWindowBase.Collapse.cs:3451` 与 `SettingsViewModel.SelectionOptions.cs:110-120`（`WidgetCompactFrameSkipPolicy.SelectableFrameRates/NormalizeFrameRate`）仍在用。**决策：保留 fork 的帧率上限功能**——在 `WidgetShellSettingsSlice.cs`（或 Core slice，看 fork HEAD 原属性在 AppSettings.cs:303 的分区）补回 `public int WidgetAnimationFrameRate { get; set; } = 60;` + JsonPropertyName（如果原来有的话；对照 `git show HEAD:src/DeskBox/Models/AppSettings.cs | sed -n '295,310p'`），并确认 AppSettings 门面把它暴露出来。同时保留 `WidgetCompactFrameSkipPolicy.cs`（fork 文件，未冲突）。
4. `src/DeskBox/Controls/FileItemDragPackage.cs`（状态 M=已暂存，但要核实）：工作树已是 fork 版带 7 参 `TryPrepare(..., out result, bool isManagedShortcutDrag = false)`，`FileSurfaceContent.xaml.cs:1162-1177` 按 7 参调用——已对齐。`TryPrepareDeferred`（186 行）调用方查 `QuickCaptureDragPackage`/`QuickCaptureSurfaceContent` 是否还需要；若上游侧已删调用方则保留也无害（fork 编译需要它时必须在）。
5. `ThemeService`：确认解完后 `await themeTask` 存在（上游并行预热语义）。

### C. 测试冲突（~53 个文件）
- 全部是 `tests/DeskBox.Tests/Aot*ContractTests*.cs`（61→59 断言、WMC 计数）+ `JsonSerializationBaselineContractTests.cs` + `MusicVolumeAotContractTests.cs`。**统一策略：冲突块整体取 upstream 侧**（审计脚本已定为上游 59 版）。可用脚本辅助：对每个文件把 `<<<<<<< HEAD … ======= … >>>>>>> upstream/main` 替换为中间段（注意少数文件可能有 fork 独有断言，替换前抽查 2-3 个确认）。
- 完成后全局 grep：`QuickCaptureWidgetWindow` 引用残留（测试若引用已删路径需重定向到 QuickCaptureSurfaceContent/ContentWidgetWindow）。

### D. 收尾校验与提交
1. `grep -rn '^<<<<<<< \|^=======$\|^>>>>>>> ' src tests scripts AGENTS.md` 必须 0 命中。
2. 陈旧引用 grep（应 0 命中）：`TryApplyGestureAsync`、`GlobalHotkeyService.RefreshRegistrationAsync`、`RunMigrationsAsync`、`QuickCaptureWidgetWindow`、`WidgetTextEdgeMode`、`TitleAlignment`（AppSettings 已删属性）。
3. `SettingsMigrationService`：`Migrate(stepCopy)` 调用与新接口一致。
4. 十二个 Strings JSON：JSON 合法（`python3 -m json.tool` 或等价）+ 键集合一致。
5. `git add -A && git commit`（**merge commit，保留 MERGE_HEAD**；不要丢掉第二父）。提交身份：本环境是 monkeycode-ai@chaitin.com，**目标仓库要求 Simon <1047078635@qq.com>**——推送前 `git -c user.name=Simon -c user.email=1047078635@qq.com commit …` 或让用户自己提交（AGENTS.md 钩子规则）。core.hooksPath 未设置，本地钩子不生效，CI 会查 attribution。
6. 推送到 `wip/fix-bug`：`git push origin merge-upstream-main:wip/fix-bug`（远端分支已存在）。

## 三、关键上下文（接手必读）

1. **合并状态**：`git rev-parse -q --verify MERGE_HEAD` 应非空（=d4b0a7a）。若为空说明有人 commit 过，先 `git log --oneline -3` 核对。
2. **上游 schema=9 vs fork=10**：fork Migration_9_To_10 是音乐 store 迁移，必须保留；上游管线是同步 Migrate。
3. **热键 API 已上游化**：fork 的 HotkeyApplyResult/异步 API 被 checkout 覆盖。SettingsWindow.HotkeyAndAppearance.cs 已取上游版配套，冲突焦点只在 App.xaml.cs 调用点。
4. **契约测试数字=上游 59**：若合并后 XAML/WMC 实际计数与 59 不符（fork 加过 XAML），需要在 Windows 上跑 `scripts/publish-aot-audit.ps1` 校准——Linux 环境无法验证，这留给用户/CI。
5. **fork 保留特性清单**（冲突时偏向 fork 的场景）：DEF-020 启动失败通知、WidgetShowDesktopSelfHealService、Migration_9_To_10、morph 渲染守卫（Collapse/Foreground/ZOrder）、peek guard（DEF-065）、N1 margin 刷新（RefreshCompactPlacementAfterBoundsMove）、帧率上限设置（WidgetAnimationFrameRate + FrameSkipPolicy + SelectionOptions + Strings 键 + Settings UI）。
6. **上游采纳特性**：settings slices、StartupPipeline/watchdog、HookHealthWatchdog、同步热键 API、audit 59、DeskBox.Platform 命名空间、ResolveRequestedCompactExpansion 自适应展开（展开永不硬失败）、Derive 放置委托。
7. **环境**：Linux；不可 build WinUI3/AOT；唯一验证手段是静态 grep + git diff 语义核对。上游远端已配好：`upstream=Tianyu199509/DeskBox`，`origin=beici/DeskBox`。
8. **z-order 文档**：`docs/architecture/[重要勿删]widget_zorder_lifecycle.md` 勿删（AGENTS.md 约束），本次合并它无冲突。

## 四、提交规范提醒
- 只用用户身份 Simon <1047078635@qq.com>；无 Co-Authored-By/Generated with；消息风格参照仓库近期（`fix(deskbox): …` / `merge upstream/main …`）。本次建议：`Merge upstream/main (v1.5.5): settings slices, startup pipeline, sync hotkey API; keep fork DEF fixes and frame-rate cap`。
