# 合并说明：upstream/main (v1.5.8) → fork `wip/fix-bug`

> 合并日期：2026-10-20
> 源：`Tianyu199509/DeskBox` `main` @ `bbb9e697`（v1.5.8，191 个上游提交）
> 目标：`beici/DeskBox` `wip/fix-bug` @ `1aa78145`（fork 1.5.6，163 个本地提交）
> 共同祖先（merge-base）：`d4b0a7a2`（v1.5.5 时期）
> 合并分支：`merge/upstream-1.5.8`（合并提交 `479339d4`，已并入 `wip/fix-bug`）；回退点 `main` @ `1aa78145`

本文档记录**全部**冲突处理决策、代码修改、review 发现的问题与修复，以及验证方式。
未在本文件中特别说明的冲突，均按「默认保留本地」原则处理。

---

## 一、结果总览

| 指标 | 数值 |
|---|---|
| 冲突文件 | **114** |
| 冲突 hunk | **222**（其中 head 侧为空 10、upstream 侧为空 21、两侧均有内容 190、两侧皆空 3） |
| 合并后暂存文件 | 988（新增 428 / 修改 524 / 删除 35 / 重命名 1） |
| 代码变动 | +120,755 / −29,519 行 |
| 最终冲突标记残留 | **0** |
| 语法错误（Roslyn 解析 1,416 个 .cs） | **0** |
| 本地独有功能丢失 | **0**（含 1 个被上游机制替代后确认退役的文件，见 §3.8） |

最终验证门禁：`/tmp/tools/verify_merge.py` 六项全部 PASS（详见 §六）。

---

## 二、总体合并策略

本次合并跨越 v1.5.6 → v1.5.8，两侧改动规模都很大（本地 505 文件、上游 1,021 文件，
227 个文件两侧都改过），无法机械取舍。采用如下分层策略：

1. **默认保留本地（fork）**——fork 的 163 个提交是缺陷修复与质量工程（DEF-xxx 系列 8~16 轮、
   AOT 审计重校准、帧率上限、胶囊动画/层级/边距修复、Show Desktop 自愈、设置锁加固），
   任何回归都不可接受。
2. **上游明显更优时采用上游**——真实 bug 修复、更健壮的实现、或上游架构演进方向
   （设置门面退役、UiDispatch 封送门户、WidgetScreenBindingMode 等）。
3. **两侧互补时取并集**——大量冲突并非互斥，而是同一方法的两段不同加固。
4. **两侧都改但语义冲突时，先判断谁是「活的」**——用调用点、测试断言、编译错误三方证据
   判定哪一侧在当前树中可达，再决定保留谁。

关键的架构判定（影响后续大量文件）：

- **设置架构：采用上游的门面退役**（batches 29–51）。上游把每个设置节迁到
  `src/DeskBox/Features/<Area>/<Area>SettingsViewModel.cs` + `*SettingsCoordinator`，
  `SettingsViewModel.cs` 从 845 行降到 428 行。本地 `ApplySettings` 巨型方法调用的 4 个
  helper（`InitializeFileStackSettings` / `InitializeContentEditorSettings` /
  `InitializeWidgetForegroundSettings` / `InitializePerformanceSettings`）在合并树中
  **已不存在**，保留本地必然 CS0103。
- **引导流程（Onboarding）：保留本地**。本地 `OnboardingWindow.xaml.cs` 957 行 / xaml 911 行，
  上游重写为 `Scene.cs` + `Steps.cs`（508 / 591 行）。详见 §3.6。

---

## 三、逐文件冲突处理决策

### 3.1 项目与配置文件

| 文件 | 决策 | 理由 |
|---|---|---|
| `src/DeskBox/DeskBox.csproj` | **上游** | 仅版本号差异（1.5.6 → 1.5.8），本次就是并入 v1.5.8 线。 |
| `global.json` | **上游** | SDK 10.0.303 → 10.0.402。上游需要新 SDK 特性；本地 `rollForward: latestPatch` 不兼容跨 feature band。 |
| `src/DeskBox.Abstractions/packages.lock.json` | **重新生成** | SDK 升级使隐式 `Microsoft.NET.ILLink.Tasks` 从 10.0.11 升到 10.0.13，锁定模式（`RestoreLockedMode`）下 NU1004。用 `--force-evaluate` 重新生成。 |
| `installer/DeskBox.iss`、`DeskBox.arm64.iss` | **上游** | 同为版本号（`MyAppVersion` 1.5.6 → 1.5.8），与 csproj 保持一致。 |
| `AGENTS.md` | **并集** | 保留本地 14 行仓库概览 + 22 行 AOT/架构/约定段；吸收上游新增的「构建成功判据」（`DeskBox.dll` 写入时间）与「根因 vs 浅修」工程纪律两条。 |

### 3.2 本地化（12 个语言文件）

**程序化重建，而非逐 hunk 手工合并。** 每个 `Strings/*.json` 是单一 5,607 行 hunk（整份文件被两侧重排）。

三路分析结果（base `d4b0a7a2` / HEAD / upstream）：

- 键数：base 2,848 / HEAD 2,667 / upstream 2,937，并集 **3,095**（Windows 端清掉 5 个被两侧并集复活的退役键后为 **3,090**，见 §8.4）。
- **真实冲突只有 1 处**（es-ES `FileInfo.FileModified`）；11 个语言 0 真实冲突。
- 本地独有键 158 个（12 语言一致），上游独有键 428 个（12 语言一致）。

处理规则：以上游键序为骨架，本地独有键插入到其本地前驱之后；值取「谁改过就用谁」，
两侧都改过时按语义判断。唯一真实冲突 `es-ES FileInfo.FileModified` 采用**本地**
`{0} · {1:yyyy/M/d HH:mm}`——本地版本是 DEF-039 的修复（把非法 .NET 格式字母
`aaaa` 改成合法格式），上游版本 `{1:d/M/yyyy H:mm}` 虽也合法但与其余 11 语言的
`HH:mm` 不一致。

产物：12 个文件均为 **3,095 键**（验收后 3,090，见 §8.4）、4 空格缩进（沿用本地 `scripts/add-*-strings.py` 约定）、
`ensure_ascii=False`、LF、结尾换行。占位符索引逐键校验一致，0 个非法格式字母。

### 3.3 数据契约与模型

| 文件 | 决策 | 理由 |
|---|---|---|
| `Models/AppSettings.cs` | **上游** | 恢复 `MusicHideTimelineWhenUnavailable` 与 `LastQuickCaptureFileWidgetId` 两个门面属性。本地 FQC-05 曾判定后者为死代码并删除，但上游让它重新变成活代码（见 3.4）。 |
| `Models/QuickCaptureSettingsSlice.cs` | **修复（补回成员）** | 合并后 slice 缺 `LastQuickCaptureFileWidgetId`，而 `AppSettings.cs:299` 与 `QuickCaptureSettingsCoordinator.cs:421,431` 都在用它 → CS1061。补回属性与类注释。 |
| `Models/GlanceWidgetData.cs` | **本地** | 本地新增字段。 |
| `src/DeskBox.Abstractions/Services/WidgetChromeMode.cs` | **修复（回退 4 行）** | 见 §4.1：git 的 rename 检测把上游对已删文件的修改自动合并进了本地独有的 Abstractions 文件，产生 CS0234。 |

### 3.4 设置层（采用上游门面退役）

| 文件 | 决策 | 理由 |
|---|---|---|
| `ViewModels/SettingsViewModel.cs` | **上游** | 428 行，与上游 md5 一致。本地 143 行 `ApplySettings` 体调用的 4 个 helper 在合并树中不存在。 |
| `ViewModels/SettingsViewModel.FeatureOptions.cs` | **上游** | 293 行。本地侧 725 行差异，引用的 `Available*` 数组已被合并删除。 |
| `ViewModels/SettingsViewModel.SelectionOptions.cs` | **上游** | 42 行。本地 212 行定义 54 个选项，其中 43 个的 `Available*` 后备数组不存在 → CS0103 ×43。 |
| `ViewModels/SettingsViewModel.SettingsSync.cs` | **上游** | 220 行。本地侧调用的 3 个 `Apply*SettingsSnapshot` 已随门面退役删除。 |
| `ViewModels/SettingsViewModel.AotBindableProperties.cs` | **上游** | 42 行 / **33 个 `nameof(`**，与上游 blob 一致。 |
| `ViewModels/SettingsViewModel.FeatureCallbacks.cs` | **删除** | 见 §4.2。 |
| `Views/SettingsWindow.xaml` | **分 hunk** | 见 §4.3。 |
| `Views/SettingsWindow.HotkeyAndAppearance.cs` | **上游** | 本地侧引用的 `_hotkeyRecordingHook` / `WmReservedHotkeyCapture` / `_isRecordingHotkey` 已被合并删除 → CS0103 ×8。 |

### 3.5 应用启动（`App.xaml` / `App.xaml.cs`）

- `App.xaml`：**本地**（纯 fork 画刷新增）。
- `App.xaml.cs`：**真合并**，7 个 hunk 逐一处理：
  - h1：上游 topology-gate 块 + 本地 `InitializeShowDesktopSelfHealWatcher();`（自愈服务）。
  - h2：**两侧都要** —— `if (IsShuttingDown) return;` + `startupPhase = "widget restoration";`。
  - h3：本地自愈方法补全 + 上游 `EndDisplayRemovalGraceByUserAction` / `OnLifecycleGateEvent`。
  - h4：**两侧都要** —— 本地 DEF-087 的 `reason => SafeFireAndForget(() => OnLifecycleRecoveryRequestedAsync(reason))` + 上游 `OnLifecycleGateEvent` 参数。
  - h5/h6：**上游** —— 本地 `_todoReminderService = null` / `+=` 无法编译（合并树中该成员是只读计算属性，见 `TodoReminderRuntime`）。
  - h7：上游 35 步关停序列 + 本地一步 `ShutdownStep.Sync("showdesktop-self-heal", …)`。
- 另**补回 4 个本地独有成员**（见 §4.4）：`_onboardingRaisedFileWidgetId` 字段、
  `OnboardingFileImportCompleted` 事件、`ShowFirstFileWidgetForOnboardingAsync`、
  `ReleaseOnboardingFileWidgetRaise`、`NotifyOnboardingFileImportCompleted`。

### 3.6 引导流程（保留本地）

| 文件 | 决策 |
|---|---|
| `Views/OnboardingWindow.xaml` | **本地**（HEAD 逐字节，911 行） |
| `Views/OnboardingWindow.xaml.cs` | **本地**（HEAD 逐字节，957 行） |
| `Views/OnboardingWindow.IntroAnimations.cs` | **恢复**（HEAD 逐字节，343 行） |
| `Views/OnboardingWindow.Scene.cs` | **删除**（上游独有） |
| `Views/OnboardingWindow.Steps.cs` | **删除**（上游独有） |

**为什么必须逐字节取 HEAD 而非逐 hunk 组合**：自动合并在**非冲突区**已经把 HEAD 的流程替换成了
上游的 scene 架构，逐 hunk 组合只能得到 828 行的「缝合怪」——混着 HEAD 的流程与上游的 Scene API。
`Scene.cs`/`Steps.cs` 声明的每个成员在本地流程中都无引用，它们触碰的每个 `x:Name`
（`SceneMark`、`SceneHalo`、`SceneTrayHalo`、`SceneMoveBadge`、`SceneKeycapHalo`、
`SceneFileToken`、`SceneFeatureTiles`、`SceneDropBoxHalo`、`SceneKeycap`、`Step3TryButton`、
`Step3StatusText`、`Step5*Toggle`）只存在于上游的 xaml 中。删除是唯一自洽解。
`IntroAnimations.cs` 必须恢复：本地 `xaml.cs` 调用 `PlayIntroSequence` / `DismissIntro` /
`PrepareIntroContent` / `StartBrandLogoShine`，这些只在其中声明。

### 3.7 服务层

| 文件 | 决策 | 理由 |
|---|---|---|
| `Services/SettingsService.cs` | **合并** | 保留本地锁加固（合并后 lock 数 20 == HEAD 20，上游 16）+ 上游新增属性。 |
| `Services/SettingsMigrationService.cs` | **合并** | 上游 copy-on-write 管线 + 本地 `Migration_9_To_10`（MusicSettingsStore，schema v10）。 |
| `Services/WidgetManager.cs` | **合并 + 修复** | 见 §4.5（重复类型）、§4.4（补回 `SetWidgetOnboardingTopMost`）。 |
| `Services/WidgetManager.Storage.cs` | **合并（两侧都要）** | 见 §4.6。 |
| `Services/WidgetManager.FeatureWidgets.cs` | **合并（两侧都要 ×5 + 上游 ×1）** | 同 §4.6 的 `ApplyNewWidgetPlacement` + `AddWidget` 组合；hunk5 全取上游。 |
| `Services/WidgetManager.Groups.cs` | **合并（两侧都要）** | 本地 DEF-131 的 `_widgetGroupSwitchRequests.Cancel(...)` + 上游的 `_widgetSurfaceSwitchGates.AcquireManyAsync(...)`；两者互补（先取消在途切换释放 Surface gate，再获取 gate 保证合并期间稳定）。 |
| `Services/WidgetManager.ZOrder.cs` | **合并（两侧都要）** | 见 §4.7。 |
| `Services/WidgetStyleBackupProjection.cs` | **并集** | 两个不同的 wire key 都要保留：本地 `"widgetAnimationFrameRate"` + 上游 `"widgetAnimationStaggerEnabled"`。前者缺失会让 fork 帧率上限从样式备份文档中静默消失，后者被 `WidgetManager.TrayAnimation.cs:392` 与 `AppearanceSettingsCoordinator.cs:94,626` 消费。 |
| `Services/WidgetTrayAnimationController.cs` | **合并** | 保留本地 DEF-089/FANI-01 的逐次新建标量动画（`_compositionResources.CreateScalar` + `InsertKeyFrame(1, (float)maxTranslate)`）+ 上游 crossing-fade。 |
| `Services/ThemeService.cs` | **合并（上游 IDisposable 加固）** | 采用上游 `IDisposable` + `_disposed` 守卫（`OnColorValuesChanged`、dispatcher 回调、`ApplyToWindow`、`ApplyToAllWindows`）+ 防抖计时器改走 `OnAppearanceTimerTick`。 |
| `Services/QuickCaptureStore.cs` | **修复** | 见 §4.8。 |
| `Services/DesktopAutoOrganizationWatcher.cs` | **修复** | 见 §4.9。 |
| `Services/QuickCaptureService.cs` | **修复（补回方法）** | 见 §4.10。 |
| `Services/FeatureWidgetsSettingsCoordinator.cs` | **修复（迁移写入点）** | 见 §4.2。 |
| `Services/TodoReminderService.cs` / `TodoWidgetStore.cs` / `SearchHotkeyService.cs` / `GlobalHotkeyService.cs` / `FolderWatcherService.cs` / `HookHealthWatchdog.cs` / `DragDropPermissionService.cs` / `DesktopOrganizationTransaction.cs` | **本地** | 本地缺陷修复，上游未改或改动更小。 |
| `Services/WidgetLayerService.cs` / `WidgetManager.Groups.cs` / `WidgetManager.ZOrder.cs` / `WidgetManager.Storage.cs` | **合并** | 见上。 |
| `Services/WidgetManager.BulkAppearance.cs` | **修复** | 见 §4.11。 |

### 3.8 拖放与 Shell 层

- **`Helpers/NativeFileDragOut.cs` — 删除**。上游在 `5deda57c`（拖出重做）用
  `ActiveDeskBoxDragRegistry` 取代了 fork 的拖拽源标签机制
  （`ContentWidgetWindow.NativeDragDrop.cs:875` 与 `:1259`）。该文件在合并树中
  **零调用点**（全仓库 `.cs/.xaml/.ps1/.csproj/.json` 扫描确认），且它调用的 4 个
  `Win32Helper` 方法（`RunOleDragDrop` / `UninitializeOleOnCurrentThread` /
  `FreeGlobalMemory` / `TryReadGlobalMemoryText`）上游已删除 → CS0117 ×4。
  自拖拽拒绝语义由上游 `ActiveDeskBoxDragRegistry.TryMatch` 承担，功能未丢失。
- `Helpers/QuickCaptureClipboardActivationHelper.cs`、`Helpers/ShellDataObjectBuilder.cs`：
  **上游**（本地删除、上游修改的 DU 冲突）。
- `Helpers/ShellContextMenuProxy.cs`：**上游**（context-menu v3 全套）。
- `Platform/Win32Helper.cs`：**合并**（上游 P/Invoke 拆分 + 本地帧率/层级辅助）。

### 3.9 视图与控件

| 文件 | 决策 | 理由 |
|---|---|---|
| `Controls/WidgetContents/FileSurfaceContent.xaml.cs` | **合并** | 取上游拖拽架构（`ItemsView_ContainerContentChanging` 逐容器接线）；删除本地重复的第二个 `Items_DragStarting`（CS0111，且调用已被移除的 `FileItemDragPackage.ResolveSupportedOperations`）；保留本地 DEF-023 `TryPrepareDeferred`。 |
| `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml` | **合并** | 本地 `x:Name="QuickCaptureItemDisplayText"` + 上游 `WidgetTextShadow.Cast`。 |
| `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs` | **本地** | `RestoreTransientState(string? searchText)` 单参（合并后 `QuickCaptureWidgetTransientState` 无 `InputText`）；上游 `AddInputWithFeedbackAsync` / `InputText` 在 fork 中已移除。 |
| `Controls/WidgetContents/QuickCaptureWidgetContentAdapter.cs` | **修复（新增访问器）** | 见 §4.11。 |
| `Controls/WidgetContents/SearchWidgetContentAdapter.cs` | **上游 + 修复** | 见 §4.12。 |
| `Controls/WidgetContents/TodoWidgetContentAdapter.cs` | **上游** | 继承 `WidgetContentAdapterBase`，本地重复声明会冲突。 |
| `Controls/WidgetContents/TodoWidgetContent.EditingAndUndo.cs` | **本地** | FEXC-03 `ClearAllTodos` + 保存失败反馈接线。 |
| `Controls/WidgetContents/TodoWidgetContent.xaml` | **合并** | 本地逾期/次要画刷 + 上游 `WidgetTextShadow.Cast`。 |
| `Controls/WidgetShell.xaml.cs` | **合并** | 保留本地 DEF-018 删除（呼吸/底部辉光/微光，上游自己也无调用点）；采用上游 AOT 安全的 `UiElementCursorHelper.SetCursor`；DEF-089 逐次新建平移动画保持完整。 |
| `Controls/WidgetTitleIcon.xaml.cs` | **上游** | 严格超集：新增 `CustomEmoji` + 专用 `CustomImage` / `CustomEmojiElement` XAML 元素（合并后的 xaml 中均存在）；外部调用方已全部使用上游 API。 |
| `Views/ContentWidgetWindow.Commands.cs` | **合并 + 修复** | 采用上游 #437 的 `finally { pickerHandoff?.Dispose(); … }` 句柄泄漏修复，但把本地随记剪贴板取色分支搬进 try 链（否则该功能静默消失）；保留本地 `CreateTitleAppearanceMenu` / `CreateMarginMenuEntry` / `CreateClipboardItemColorMenu`，新增上游 `WidgetScreenMenuBuilder.TryCreate(...)`。 |
| `Views/ContentWidgetWindow.WindowInteraction.cs` | **合并** | 保留本地标题栏点击折叠 + 拖拽阈值组抬升（上游 `UiDispatch.PostAfterCallback` 重复抬升会重现边缘闪烁）；采用上游游标助手。 |
| `Views/WidgetWindowBase.Collapse.cs` | **合并** | 保留本地 DEF-065 peek 守卫于 `EnsureCompactPlacementFromExpandedBounds`，适配上游新的 `DeriveCompactPlacementFromExpandedBounds()`（瞬态、不伪造放置）；保留上游 `ResolveRequestedCompactExpansion`。 |
| `Views/WidgetWindowBase.cs` | **本地** | DEF-027 文档注释。 |
| `Views/SearchPopupWindow.xaml.cs` | **上游** | 游标助手；删除孤立的 `_loggedMissingProtectedCursorProperty` 与 FARC-03 注释。 |
| `Views/ContentWidgetWindow.*`（其余） | **上游** | 未冲突区域。 |

### 3.10 测试

- **AOT 审计计数簇**（`scripts/publish-aot-audit.ps1` + 24 个 `AotStage*ContractTests.cs`）：
  **WMC1510 = 742 全统一**（本地值；上游 875、base 866）。**Windows 端实测合并树为 796，已按实测重校准，见 §8.5。** 该值只有真实 AOT 发布才能验证，
  本环境不可能执行，因此以「保留 fork 的审计重校准 + 全站一致」为准。
  `$auditProfileVersion = 63` / `schemaVersion = 55` 保留（验收后 profile 升到 **64**，见 §8.5）。
- `$stage5B4B1ExpectedBindableViewModelPropertyCount`：**351 → 33**。计数目标是
  `SettingsViewModel.AotBindableProperties.cs`（不是 `SettingsViewModel.cs`）；合并后该文件
  与上游一致、恰好 33 个 `nameof(`。三处耦合点同时更新（脚本字面量 + 测试值断言 + 测试的
  脚本文本断言）。
- `AotStage4D1BContractTests.cs` 本地化 XAML 计数：**10 键/379 → 7 键/487**。
  用测试自身的两个正则重放合并树得出（`toolkit:SettingsCard|HeaderKey` 223、
  `DescriptionKey` 190、`SettingsExpander` 37/23、`InfoBar` 6/6、`TextBox|HeaderKey` 2）。
  `Grid`/`StackPanel`/`Expander` 三组键随上游删除的收藏面板一起消失。
- `SettingsSliceOwnershipContractTests.cs`：门面属性数 **221 → 240**
  （上游 238 + fork 的 `WidgetAnimationFrameRate` + `WidgetTitleAlignment`）。
  用测试自身谓词在三个版本上验证方法忠实性：base 220 / HEAD 221 / 上游 238 / 合并 240。
- `JsonSerializationBaselineContractTests.cs`：**36 文件/86 调用/33 context owner → 41/96/37**。
  独立重算确认：41 个文件有序列化调用、96 次调用、37 个 context owner。
- **上游既有缺陷修复**（合并继承，非本次引入；已证明：上游自己的脚本对自己的树失败 4 处，
  而 HEAD 与 base 各自对自己 0 失败）：
  - `{x:Bind CancelText, Mode=OneWay}` 期望 1 → 实际 2；
  - `{x:Bind Title, Mode=OneTime}` 期望 2 → 实际 0；
  - `x:DataType="models:SearchRecommendationItem"` 期望 2 → 实际 0；
  - `FileStack.StacksEnabled` 家族（4E4 + 5B4B1 共 4 处）→ 重新指向存活等价物
    `Interaction.FileItemContextMenuEnabled`（XAML 侧）与
    `AutoStacking` / `SetProperty(ref _autoStacking, value)` / `_settings.SetFileStackAutoStacking(value)`
    （编辑器侧）。**选择重指向而非删除**，避免丢掉真实审计钉。
  - 另发现第 7 处被大小写敏感 grep 漏掉的 `_stacksEnabled` 字面量，同样重指向。
- 其余测试：多数为「保留本地断言 + 从合并树重新推导计数」，逐文件已在代码内注释说明来源。

---

## 四、review 发现并修复的问题

以下问题均由多轮 review（编译门禁 + 死代码分析 + 交叉引用核对）发现，**不是冲突本身**。

### 4.1 `src/DeskBox.Abstractions/Services/WidgetChromeMode.cs` — CS0234（自动合并误伤）

**现象**：`error CS0234: 命名空间 'DeskBox.Contracts' 中不存在类型或命名空间名 'WidgetChromeKinds'`（4 处）。

**根因**：该文件是**本地独有**（上游删除了旧的 `src/DeskBox/Services/WidgetChromeMode.cs`，
fork 把它迁进了 Abstractions 项目）。git 的 rename 检测把上游对已删文件的修改自动合并进了
这个本地文件，把 4 个常量从 `nameof(WidgetChromeMode.X)` 改成了 `Contracts.WidgetChromeKinds.X`。
但 `WidgetChromeKinds` 定义在 `src/DeskBox/Contracts/IAppearanceSettings.cs`（主程序集），
Abstractions 项目无法引用。

**修复**：回退为 `nameof(WidgetChromeMode.Standard/Compact/Overlay/Hidden)`。字符串值完全相同，
行为零变化。

### 4.2 `SettingsViewModel.FeatureCallbacks.cs` — 删除并把 2 个写入点迁移到协调器

**现象**：该文件（486 行，本地 HEAD 内容）引用 26 个已被合并删除的 `[ObservableProperty]`
与 13 个不存在的 helper，且上游在 `21aa2dce`（batch 47）已整文件删除。

**判定依据**：
- 26 个 `partial void On*Changed` 实现全部指向已删除属性；
- 6 个 helper 方法**只在该文件内部被引用**；
- 上游用 `QuickCaptureSettingsCoordinator` / `TodoSettingsCoordinator` /
  `FeatureWidgetsSettingsCoordinator` 完整替代了它的全部职责（且更健壮：带 normalize/compare/skip）。

**修复**：删除文件。但 fork 在其中加了 2 行真实功能（`90b9e246`）：
`_musicSettingsStore.Update(store => store.UseArtworkBackdrop = value)` 与
`… EnableCoverHoverMotion = value`。这两行必须迁移，否则音乐格子的按类型设置存储会变陈旧
——`MusicWidgetViewModel.cs:946` **只**从该 store 读取，而上游协调器只写 `AppSettings` 镜像，
这正是 `90b9e246` 修掉的「split-brain」。

迁移实现：
1. `src/DeskBox/Services/SettingsService.cs` 新增 `internal string DataDirectory => Path.GetDirectoryName(_settingsPath)!;`
   （与既有 `ManagedStorageMigrationDirectory` 同款模式），让协调器能定位到**服务解析出的**
   数据根（包括测试传入的临时根，绝不写真实用户目录）。
2. `src/DeskBox/Services/FeatureWidgetsSettingsCoordinator.cs` 新增
   `private readonly MusicSettingsStore _musicSettingsStore;`，构造时
   `new MusicSettingsStore(Path.Combine(settings.DataDirectory, "music"))`，并在
   `SetMusicUseArtworkBackdrop` / `SetMusicEnableCoverHoverMotion` 写 slice 之后同步写 store。
3. 删除 `src/DeskBox/ViewModels/SettingsViewModel.cs` 中已成死字段的 `_musicSettingsStore`。

**测试兼容性**：`FeatureWidgetsSettingsCoordinatorTests` 与 `MusicSettingsEditorPilotTests`
都构造 `new SettingsService(临时根)` + `new FeatureWidgetsSettingsCoordinator(settings)`，
协调器签名未变，且 store 现在指向临时根下的 `music/`，不污染真实 profile。

### 4.3 `SettingsWindow.xaml` — 分 hunk + 帧率上限 UI 丢失与恢复

三个 hunk 各自独立判断：

- **hunk1（动画卡片）→ 上游**。该 `DataTemplate` 的 DataContext 现在是
  `_appearanceSettingsViewModel`（`SettingsWindow.DeferredSections.cs:195-207` 的节级
  DataContext 切换），本地卡片绑定的 `AvailableWidgetFrameRateOptions` /
  `SelectedWidgetFrameRate` 已随 `SelectionOptions.cs` 取上游而消失。
- **hunk2（随记记录配色）→ 本地**。fork 独有的 `SettingsWindow.QuickCaptureColors.cs`
  引用 `QuickCaptureRecordTextColorButton` / `BackgroundColorButton` / `HoverTextButton`
  三个 `x:Name`，它们只存在于本地这个 hunk；取上游会 CS0103。
- **hunk3（音乐节）→ 上游**。上游内联节绑定 `Features/Music/MusicSettingsViewModel` 的真实成员，
  并提供 `StackPanel x:Name="MusicSettingsSection"`，满足
  `SettingsWindow.SectionElements.cs:135-136` 的 x:Name 查找。

**但取上游 hunk1 导致 fork 的「动画帧率上限」设置 UI 被静默丢失**（运行时逻辑完好：
`WidgetShellSettingsSlice.WidgetAnimationFrameRate`、`SettingsService` 归一化、
`WidgetCompactFrameSkipPolicy`、`WidgetWindowBase.Collapse` 消费点全部健在，
12 语言的 `Settings.Animation.FrameRate.*` 键也全部保留，`SettingsSearchCatalog.cs:59`
甚至还在索引这个已不存在的卡片）。这是真实功能回归，必须修复。

**修复（完整移植到上游外观编辑器）**：

1. `src/DeskBox/Contracts/IAppearanceSettings.cs`
   - `AppearanceAnimationSettings` 增加**尾部可选**参数 `int FrameRate = 0`（保持既有 2 个位置参数调用点有效）；
   - 接口新增 `void SetAnimationFrameRate(int frameRate, bool scheduleSave = true);`；
   - **`WidgetAnimationKinds` 新增规范常量** `FrameRateTiers = [30, 60, 90, 120]`、
     `DefaultFrameRate = 60`、`NormalizeFrameRate(int)`。
     放在 Contracts 的原因：`src/DeskBox/Features/**` 不得引用 `DeskBox.Services`
     （由 `FeatureSettingsBoundaryContractTests.FeatureBusinessCode_DoesNotResolveGlobalApplicationServices`
     通过 IL 引用检查强制）。
2. `src/DeskBox/Services/WidgetCompactFrameSkipPolicy.cs`：三个成员改为**委托**规范常量
   （`SelectableFrameRates => WidgetAnimationKinds.FrameRateTiers` 等），避免两处清单漂移。
3. `src/DeskBox/Services/AppearanceSettingsCoordinator.cs`：`ReadAnimation()` 追加
   `NormalizeFrameRate(shell.WidgetAnimationFrameRate)`；新增 `SetAnimationFrameRate`（照抄
   `SetAnimationSpeed` 的形状）。
4. `src/DeskBox/Features/Appearance/AppearanceSettingsViewModel.cs`：新增
   `_animationFrameRate`、`_cachedAnimationFrameRateNames`、`AnimationFrameRate`
   （**不**调用 `SyncAnimationPresetSelection()`——帧率与预设正交）、
   `AvailableAnimationFrameRateOptions`（数组-后-索引的 AOT 封送模式）；
   `SyncPresentation()` 增加投影；`RefreshLocalization()` 增加缓存清理与属性通知。
5. `AppearanceSettingsViewModel.AotBindableProperties.cs`：按字母序新增
   `nameof(AnimationFrameRate)` 与 `nameof(AvailableAnimationFrameRateOptions)`。
6. `SettingsWindow.xaml`：在 Speed 卡片之后、Direction 卡片之前插入帧率 `SettingsCard`。

**连带更新**：新增一张本地化 `SettingsCard` 使 4D1B 计数 +1 → 测试改为
`SettingsCard|HeaderKey` 223 / `DescriptionKey` 190 / 总和 487（用测试自身正则重放确认）。
AOT 脚本不钉这些数字（已 grep 确认），无需改脚本。

### 4.4 `App.xaml.cs` / `WidgetManager.cs` — 补回引导流程的 5 个成员

本地引导流程（保留侧）调用 4 个 `App` 成员与 1 个 `WidgetManager` 方法，它们都是
**base 与 HEAD 都有、上游删除**：

- `App`：`_onboardingRaisedFileWidgetId` 字段、`OnboardingFileImportCompleted` 事件、
  `ShowFirstFileWidgetForOnboardingAsync()`、`ReleaseOnboardingFileWidgetRaise()`、
  `NotifyOnboardingFileImportCompleted(int)`。
- `WidgetManager`：`internal bool SetWidgetOnboardingTopMost(string widgetId, bool isTopMost)`
  （HEAD `WidgetManager.cs:1417`）。全部依赖已确认在合并树中存在：
  `_fileWidgets`（`Dictionary<string, FileWidgetSession>`，`FileWidgetSession.Host` 为
  `ContentWidgetWindow`）、`_contentWidgets`、`WidgetLayerService.UsesDesktopPinnedMode()`、
  `RaiseTemporarilyFromManager()`、`ForceRestoreDesktopLayerFromManager()`、
  `Win32Helper.SetWindowTopMost(IntPtr, bool)`，且两个窗口方法都在合并后的
  `IDesktopWidgetWindow` 接口上。

同时恢复 `FileSurfaceContent.xaml.cs` 中的调用点
`App.Current.NotifyOnboardingFileImportCompleted(importedItemCount)`（`OnboardingExperienceTests`
断言该字面量；不恢复则该成员成为死代码）。

### 4.5 `WidgetManager.cs` — 重复类型定义（CS0101）

**现象**：`DeskBox.Services` 命名空间中 `ManagedStorageMigrationOptions` / `Progress` / `Result`
被定义两次（`WidgetManager.cs` 内联 + 上游新增的 `ManagedStorageMigrationModels.cs`）。

**根因**：上游把托管存储迁移从「就地交互式（逐项重试/跳过/中止）」重写为「事务式复制」
（`ManagedStorageMigrationService` + `WidgetManager.StorageMigration.cs` +
`ManagedStorageMigrationModels.cs`），因此**删除了** `WidgetManager.cs` 里的旧记录类型。
冲突处理时保留了本地侧，于是新旧定义并存。

**关键判断**：那 9 个交互式迁移提交（`bce9f1b6` 等）**全部是 merge-base 的祖先**——
它们是上游自己的旧代码，fork 只是继承；`git diff d4b0a7a2 HEAD` 对这些文件为空。
所以这不是「丢失 fork 功能」，而是上游自己已废弃的实现。

**修复**：删除 `WidgetManager.cs` 中 8 个已死类型（3 个记录 + 3 个附属记录 + 2 个异常类）。
删除前逐一确认：8 个类型在 `WidgetManager.cs` 之外**零引用**，且所有存活消费方
（`WidgetManager.StorageMigration.cs`、`ManagedStorageMigrationService.cs`、
`ManagedStorageMigrationDialog.cs`、`SettingsWindow.StorageAndUpdates.cs`、
`ManagedStorageCopyMigrationTests.cs`）都使用上游形状。
保留 `QuickCaptureFileWidgetTarget`（上游所有，被 `WidgetManager.FeatureWidgets.cs` 6 处引用，
无其他定义）。

### 4.6 `ApplyNewWidgetPlacement` + `AddWidget` 组合（6 处）

**现象**：6 个「新建格子」路径上，本地是 `_settingsService.AddWidget(config);`，
上游是 `ApplyNewWidgetPlacement(config); _settingsService.Settings.Widgets.Add(config);`。

**判断**：两者不可互相替代。
- `ApplyNewWidgetPlacement`（`WidgetManager.NewWidgetPlacement.cs:26`）是上游独有的
  「新格子放置漏斗」（spec 5.8）：显式 bounds → 源面板所在显示器 → 「新格子出现在」设置，
  必要时置 `config.NeedsInitialPlacement = true`。
- `SettingsService.AddWidget`（`SettingsService.cs:1587`）是 fork 独有的 DEF-070
  锁保护写入口（`lock (_lock) { _settings.Widgets.Add(config); }`）。
  直接用裸 `Settings.Widgets.Add` 会重新引入 DEF-070 的并发写丢数据竞态，
  该竞态由 `SettingsSharedStateConcurrencyTests.AddWidget_ConcurrentWithSaves_ConservesEveryEntry`
  （96 个并发 widget + 并发保存）钉死。

**修复**：全部 6 处改为**两行并存**：
```csharp
ApplyNewWidgetPlacement(config);
_settingsService.AddWidget(config);
```
位置：`WidgetManager.Storage.cs:173-174`、`WidgetManager.FeatureWidgets.cs:97-98`、`:158-159`、
`:440-441`、`:509-510`、`:1174-1175`。

### 4.7 `WidgetManager.ZOrder.cs` — 两个 hunk 都是互补而非互斥

- **hunk1**（`NormalizeIdleWidgetZOrder`）：本地是「morph 渲染中则延后 120ms 再试」的守卫
  （DEF-056 家族，防止 EndDeferWindowPos 批打断 morph 的下一帧）；上游是「cohesion-exit 在
  动画中仍归一化」的**日志**块（无 return）。两者语义不冲突 → **都保留**，顺序为
  本地守卫（return false）→ 上游日志 → 落到统一批处理。
- **hunk2**（`ActivateAllVisibleWidgetsFromTitle`）：本地「只抬升被点击的格子」（避免整组
  亚克力背板重采样导致全员闪烁，DEF-059/061）；上游 #375「抑制窗口内复用既有抬升」
  （避免连点重放整组层级事务）。两者正交 → **合并**：保留上游的
  `TitleActivationRaisePolicy.ShouldSkipRepeatRaise` 守卫与
  `_lastTitleActivationRaiseWindowHandle` / `_lastTitleActivationRaiseUtc` 记账，
  但 `handles` 仍为单元素 `new List<IntPtr> { activeHwnd }`（不展开为全组）。

**连带更新**：上游独有的 `tests/DeskBox.Tests/WidgetRaiseBandContractTests.cs` 中
`TitleActivation_ReusesLiveRaiseWithinSuppressWindow` 断言
`BringGroupTemporarilyToFront(handles, activeHwnd)`，与 fork 的单格语义冲突。
改为断言 `BringTitleActivatedGroupToFront(handles, activeHwnd)`，并加注释说明
「fork 只抬升被点击格子，#375 的重复抬升抑制保留不变」。第一个断言
（`TitleActivationRaisePolicy.ShouldSkipRepeatRaise`）保持，现在通过。

### 4.8 `QuickCaptureStore.cs` — `async` 签名丢失（CS0161/CS4032）

**现象**：`public Task SaveAsync(...)` 方法体内 `await _gate.WaitAsync()`，
产生 CS4032 ×2 + CS0161。

**根因**：上游把它重写为 `return ResilientJsonStore.SaveAsync(...)` 的表达式式方法，
而 fork 的 DEF-043 家族门（`_gate`）被并入方法体，合并后签名仍是 `Task` 而非 `async Task`。

**修复**：改回 `public async Task SaveAsync(...)`，保留 fork 的 `_gate` 语义
（注释说明门跨整个写入持有，两个写者不会交错丢改动），方法体保持上游的流式 UTF-8 写入
（避免整份文档先物化成字符串导致每次保存瞬时内存翻倍）。

### 4.9 `DesktopAutoOrganizationWatcher.cs` — 局部变量重复声明（CS0136）

**现象**：`A local or parameter named 'target' cannot be declared in this scope`。

**根因**：fork 在 `try` 外声明了 `WidgetConfig? target = null;`（供 catch 块报告
「正在整理到哪个目标」），上游在 `try` 内又声明了一次同名变量。

**修复**：内层改为**赋值**外层变量（`target = widgetsSnapshot.FirstOrDefault(...)`），
并加注释说明不得遮蔽——否则 catch 块的可恢复不可用报告会拿不到目标。

### 4.10 `QuickCaptureService.cs` — 丢失 `BuildImageExportFileName`（CS0117）

**现象**：`'QuickCaptureService' does not contain a definition for 'BuildImageExportFileName'`
（`WidgetManager.FeatureWidgets.cs:785` 调用）。

**根因**：该方法上游所有（`QuickCaptureService.cs:1903`），但合并把它丢掉了
（上游新增、本地未改的文件被自动合并时漏项）。

**修复**：按上游原文补回
`internal static string BuildImageExportFileName(string? fileNamePrefix, DateTimeOffset timestamp, string sourceImagePath)`
（依赖的 `FileService.SanitizeFileSystemName` 与 `NormalizeImageExtension` 均已在合并树中存在）。

### 4.11 QuickCapture 内容契约迁移后遗留的 3 处失效模式匹配（CS8121）

**现象**：`An expression of type 'IWidgetContent' cannot be handled by a pattern of type 'QuickCaptureSurfaceContent'`
（`ContentWidgetWindow.Commands.cs:202,423`、`WidgetManager.BulkAppearance.cs:25`）。

**根因**：上游把 QuickCapture 统一到 `QuickCaptureWidgetContentAdapter`（residency adapter），
`QuickCaptureSurfaceContent` 降级为普通 `UserControl`（不再实现 `IWidgetContent`）。
合并后 `CurrentContent`（类型 `IWidgetContent?`）已不可能匹配到 leaf 类型。
本地 3 处「随记剪贴板取色 / 条目定位」功能因此静默失效。

**修复**：
1. `QuickCaptureWidgetContentAdapter` 新增
   `internal QuickCaptureSurfaceContent? MaterializedSurface => MaterializedView as QuickCaptureSurfaceContent;`
   （`MaterializedView` 是基类的 `protected` 成员，需要公开访问器）。
2. 3 处模式匹配改为
   `CurrentContent is QuickCaptureWidgetContentAdapter { MaterializedSurface: { } surface }`。

这样保留了 fork 的剪贴板记录配色与条目定位功能，同时遵循上游的 adapter 契约。

### 4.12 `SearchWidgetContentAdapter.cs` — 上游订阅了不存在的事件

**现象**：上游该文件体订阅 `content.SearchRequested +=/-=`，但合并后的
`SearchWidgetContent.xaml.cs`（自动合并为 fork 的 DEF-099/100 版本）没有该事件 → CS1061。

**修复**：删除 adapter 的 `SearchRequested` 事件与 `OnViewMaterialized` /
`Content_SearchRequested` / Dispose 反订阅，保留 DEF-099/100 注释。
无其他文件订阅；搜索弹窗由 `SearchWidgetContent` 自身调用 `App.Current.OpenSearchPopup()` 打开，
行为不变。

### 4.13 `TodoStoreChangedByReminder` 无生产者 — 恢复订阅

**现象**（由 review 交叉引用核对发现）：`App.TodoStoreChangedByReminder` 事件与
`RelayTodoStoreChangedByReminder` 方法都在，但**没有任何订阅点**；
`TodoWidgetContent.xaml.cs` 仍在订阅该事件，因此 fork 的「提醒服务后台写入 → 待办格子实时刷新」
链路静默失效。

**根因**：上游把提醒服务重构为 `TodoReminderRuntime`（会话式），
HEAD 中「创建服务时挂事件」的那一行在合并时被上游的 runtime 创建路径取代。
但 `TodoReminderService.TodoStoreChanged` 事件本身**仍然存在**（`:40`，且仍在 `:578` 触发）。

**修复**：在 `CreateTodoReminderService()` 中，创建实例后挂上
`service.TodoStoreChanged += RelayTodoStoreChangedByReminder;` 再返回，
并加注释说明「runtime 每次重新激活都会创建新会话实例，因此订阅按实例挂载」。
（会话销毁时事件随实例一起回收；handler 是 `App` 上的实例方法，不会累积。）

---

## 五、被上游机制取代而确认退役的本地实现

| 本地实现 | 上游替代 | 判定依据 |
|---|---|---|
| `Helpers/NativeFileDragOut.cs`（拖拽源标签） | `ActiveDeskBoxDragRegistry`（`ContentWidgetWindow.NativeDragDrop.cs:875,1259`） | 全仓库零调用点；它调用的 4 个 `Win32Helper` 方法上游已删 |
| 托管存储迁移的交互式逐项流程（`ManagedStorageMigrationResidue` 等 8 个类型） | `ManagedStorageMigrationService` + `WidgetManager.StorageMigration.cs` 事务式复制 | 9 个相关提交全部是 merge-base 祖先（上游自己的旧代码）；8 个类型在 `WidgetManager.cs` 外零引用 |
| `Views/OnboardingWindow.Scene.cs` / `.Steps.cs`（上游侧） | 本地引导流程 | 本地流程对它们零引用；它们触碰的 x:Name 只存在于上游 xaml |

---

## 六、验证方式

本环境是 **Linux 容器**，无法执行 WinUI3 / XAML 代码生成 / Rust / Native AOT 的真实构建
（已实测：XamlCompiler 需要 Windows 元数据分发器 COM shim，Mono 无法提供）。
因此采用**多层静态 + 半编译验证**，并明确标注其边界。

### 6.1 可用工具链

- .NET SDK **10.0.402**（与 `global.json` 完全一致），安装于 `/opt/dotnet10`。
- NuGet 源修正：用户配置中的 `nuget-cn`（`nuget.cdn.azure.cn`）在本容器 DNS 不可达，
  改用 `/tmp/NuGet.local.config`（仅 `nuget.org`）通过 `--configfile` 传入，
  **未修改用户配置文件**。
- 恢复命令（**成功**）：
  ```
  dotnet restore DeskBox.sln --configfile /tmp/NuGet.local.config \
    -p:EnableWindowsTargeting=true -p:Platform=x64 -p:RuntimeIdentifier=win-x64
  ```

### 6.2 验证门禁（`/tmp/tools/verify_merge.py`，6 项全 PASS）

| # | 检查 | 结果 |
|---|---|---|
| 1 | 冲突标记残留（`src/`、`tests/`、`scripts/`、`installer/`、`docs/`） | **0 个文件** |
| 2 | 本地独有功能指纹（20 项：帧率上限、胶囊策略、自愈服务、音乐存储、schema v10 迁移、桌面图标几何、边距计算、层级规划、标题外观、元数据键、剪贴板配色、工具对话框、Abstractions 项目、逐次平移动画、peek 守卫等） | **全部存在** |
| 3 | 上游功能落地指纹（12 项：UiDispatch、context-menu v3、ZIP 服务、FeatureRuntimeRegistry、屏幕目录、拓扑门、托管迁移、Features 编辑器、WidgetScreenBindingMode、提权提示窗、热键录制、图标新鲜度账本） | **全部存在** |
| 4 | 12 语言键集与占位符一致性 | **3,095 键 × 12 语言，0 差异**（验收后 3,090，见 §8.4） |
| 5 | AOT 审计计数一致性（脚本 vs 24 个测试） | **WMC1510 = 742，35 个 stage 变量，全部一致**（验收后 **796**，见 §8.5） |
| 6 | `DeskBox.Abstractions` Roslyn 编译 | **0 错误** |

### 6.3 Roslyn 语法门禁

`/tmp/syntool`（基于 `Microsoft.CodeAnalysis.CSharp` 的独立解析器）：

```
parsed 1416 file(s), 0 syntax error(s)
```
覆盖 `src/DeskBox`、`src/DeskBox.Abstractions`、`src/DeskBox.Updater`、`tests/DeskBox.Tests`。

### 6.4 差分编译门禁（关键）

由于 Linux 上无法运行 XAML 代码生成，主程序的 `-t:Compile` 必然产生大量
`CS0103: The name '<x:Name>' does not exist` 噪声。因此改用**差分**方式：
先对**干净的 1.5.6 基线**（worktree `/tmp/deskbox-base`）跑同样的编译，得到
**945 个唯一错误指纹**的基线画像；再对合并树跑同样的编译，比对**新增指纹**。

命令（两侧一致）：
```
dotnet build src/DeskBox/DeskBox.csproj -p:EnableWindowsTargeting=true -p:Platform=x64 \
  -p:RuntimeIdentifier=win-x64 --no-restore -t:Compile \
  -p:BuildProjectReferences=false -p:DeskBoxShellThumbnailProxy=false
```
（`-t:Compile` 跳过仅 Windows 可用的 PRI/MRT 工具；`DeskBoxShellThumbnailProxy=false`
跳过需要 `powershell.exe` 的 Rust 代理构建步骤。）

**最终结果：合并树与基线在每一个非 CS0103 错误类别上完全一致。**

| 错误码 | 基线 | 合并树 | 说明 |
|---|---:|---:|---|
| CS0103 | 6,698 | 7,116 | 全部为 XAML `x:Name` 未生成（上游新增了 36 个 xaml 文件，噪声随之增加） |
| CS0029 | 20 | 20 | 相同 |
| CS1061 | 18 | 18 | 相同 |
| CS0030 | 6 | 6 | 相同 |
| CS0165 | 4 | 6 | 2 处基线已有；新增 1 处在 `SettingsWindow.Navigation.cs`，是 `SettingsRoot`（x:Name）未解析的级联，非真实缺陷 |
| CS5001 | 2 | 2 | 相同（无入口点，因 `-t:Compile` 只编译不产出可执行） |

**过程中通过该门禁发现并修复的真实缺陷**：§4.1（CS0234）、§4.5（CS0101）、
§4.8（CS0161/CS4032）、§4.9（CS0136）、§4.10（CS0117）、§4.11（CS8121）、§4.12（CS1061）。
这些是纯静态审查看不到的——正是编译门禁的价值。

### 6.5 契约测试重算

- 门面棘轮模拟：**240 个门面名 / 131 个清单条目 / 877 个文件扫描 / 0 违规**。
- JSON 序列化基线：期望 41 文件 / 96 调用 == 实际 41 / 96，0 差异。
- 4D1B 本地化 XAML：用测试自身正则重放 → 7 键 / 487，与测试断言一致。
- 可绑定计数：脚本 33 == 测试值断言 33 == 测试脚本文本断言 33 == 文件实际 33。

### 6.6 本环境**无法**验证、需在 Windows 完成的项

以下项目无法在此容器验证，已尽量降低风险，但**必须在 Windows 上复核**：

1. **完整构建**：`dotnet build`（含 XAML 代码生成、PRI/MRT 资源、Rust native、WinUI 打包）。
   XAML 代码生成在 Linux 上不可行（已实测证明）。
2. **完整测试套件**：
   `dotnet test tests/DeskBox.Tests --no-restore -p:Platform=x64`（按 AGENTS.md 要求
   **不得**用默认 AnyCPU）。
3. **`scripts/publish-aot-audit.ps1` 的 PowerShell 语法**：本容器无 `pwsh`，
   只做了结构校验（括号/花括号平衡 vs HEAD）。建议先跑一次
   `pwsh -NoProfile -Command "& { [void][System.Management.Automation.Language.Parser]::ParseFile('scripts/publish-aot-audit.ps1',[ref]$null,[ref]$null) }"`
   确认无语法错误，再跑完整审计。
4. **AOT 审计的实际 WMC1510 计数**：`742` 是 fork 的重校准值，只有真实 AOT 发布能验证。**已在 Windows 实测为 796 并完成重校准，见 §8.5。**
   实际校准涉及 **35 处脚本字面量 + 20 处测试断言**。
5. **真机行为冒烟**（建议重点）：
   - 文件格子拖出（Win10 无操作选择框、托管快捷方式）——本次删除了 fork 的第二个
     `Items_DragStarting`，行为应完全由上游处理器覆盖；
   - 托盘显隐、Show Desktop 自愈；
   - 胶囊展开/收起动画流畅度（帧率上限卡片现在可调）；
   - 设置页：外观/动画节（含新增帧率卡片）、随记记录配色、音乐节；
   - 引导流程（本地五步流程 + 文件练习步骤）；
   - 托管存储迁移（现在走上游事务式复制）。

---

## 七、提交与后续

- 合并提交 `479339d4` 直接在 `wip/fix-bug` 分支上（没有另建合并分支），父提交为
  `1aa78145`（本地 1.5.6）与 `bbb9e697`（上游 v1.5.8）。
- 提交身份遵循仓库约定：`Simon <1047078635@qq.com>`，无 `Co-Authored-By` /
  `Generated with` 等任何工具署名（`.githooks/commit-msg` 强制）。
- 回退点：`main` 仍停在合并前的 `1aa78145`（1.5.6 全绿状态）。没有另建 `backup/pre-upstream-1.5.8` 之类的备份分支——`main` 本身就是备份。

**建议的 Windows 验收顺序**：

```
git config core.hooksPath .githooks
dotnet restore .\DeskBox.sln -p:Platform=x64
dotnet build .\src\DeskBox\DeskBox.csproj --configuration Debug --no-restore -p:Platform=x64
dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-aot-audit.ps1    # 校验 WMC1510 与审计链
```

> 脚本本身兼容 Windows PowerShell 5.1（`Parser::ParseFile` 解析 0 错误）与 PowerShell 7；
> 有 `pwsh` 的机器直接用 `pwsh -NoProfile -File .\scripts\publish-aot-audit.ps1` 亦可。

---

## 八、Windows 端验收结果

Linux 侧只能做到「静态 + 半编译」验证（§六），本节记录合并树在 Windows 上的
真实构建、测试与 Native AOT 审计结果，以及由此暴露并修复的问题。

验收环境：Windows 10 21H2+ / x64，.NET SDK **10.0.402**（与 `global.json` 一致），
WinAppSDK **2.5.1**，Rust **1.96.0**，MSVC 14.44.35207 + Windows SDK 10.0.26100.0。

### 8.1 最终结果

| 门禁 | 结果 |
|---|---|
| `dotnet build`（Debug / x64，全解决方案） | **0 错误** |
| `dotnet test`（x64，全量） | **5,808 / 5,808 通过，0 失败，0 跳过** |
| `scripts/publish-aot-audit.ps1 -Platform x64` | **exit 0**，`auditFindings` 为空 |
| AOT 审计 warning 白名单 | 仅 `CS0108` 2 / `CS8601` 2 / `CS8602` 16 / `WMC1510` 796，全部在允许集内 |
| AOT 审计 always-throw | **0** |
| Rust ABI | ABI 2 / capability mask 511 / 10 个导出，未变 |
| 12 语言键集与占位符 | **3,090 键 × 12 语言，0 差异**（含 341 个含占位符的键逐键比对） |
| 提交身份 | `Simon <1047078635@qq.com>`，无任何工具署名 |

验收前基线：合并提交 `479339d4` 之后的 `8afb5a55` 上，测试套件**一次都没跑过**
（§六 明确标注为未验证项），AOT 审计也从未执行。首次全量运行得到 **39 个失败**；
定位并修复后降到 **7 个**，再降到 **0**。AOT 审计首次完整运行为 **45 条 findings**，
全部修复后通过。

### 8.2 必须先说明的机器环境陷阱

这台验收机器上，**任何可执行文件只要解析后的路径落在 `E:\DeskBox` 目录树内，
就会被强制放进 App Silo 并降为 Low 完整性级别**（实测 `TokenIsAppSilo = 1`、
完整性 RID `0x1000`）。同一份二进制放到 `E:\DeskBox-baseline`、`E:\DeskBox2`、
`C:\Users\...\Temp` 下则是 `TokenIsAppSilo = 0`、High（`0x3000`）。
通过 junction 解析回该树同样会被捕获。来源是机器上的第三方安全软件过滤器，
不是本仓库的代码或 DSH 沙箱（`CreateAppContainerProfile` / `CreateJobObject` /
`TokenIsAppSilo` 在全仓零命中）。

被降权后会出现这些**与代码无关**的假失败：

- `HKCU` 新建子键被拒（`UnauthorizedAccessException`）；
- WinRT `StorageFile.GetFileFromPathAsync` 被拒（中文错误「该项目没有位于应用程序
  可以访问的位置」）；
- `mklink /J` 建出的 junction 重解析点数据被写成 `\??\Global\E:\...`，多出
  `Global\` 命名空间前缀，导致 `.NET` 的 `ResolveLinkTarget(returnFinalTarget: false)`
  返回畸形路径；
- `%TEMP%` / `%LOCALAPPDATA%` 写入被拒。

因此**本节的测试与审计结果全部在 `E:\DeskBox` 树之外的工作树中取得**
（`git worktree add --detach E:\DeskBox-verify <commit>`，同一棵树、同一批未提交改动，
逐文件 SHA256 校验一致）。在树内直接跑会得到 39 个失败中约 9 个的噪声。
这一点对后续任何人复验都成立：**要么把仓库放到该树外，要么用计划任务等脱离
该路径的进程上下文运行**。

### 8.3 测试套件暴露并修复的缺陷

39 个失败分为六类。其中 9 个是上述环境陷阱造成的假失败（把测试数据根重定向到
`DESKBOX_DEV_DATA_ROOT` 后全部转绿——`DeskBoxDataPathService.ResolveConfiguredRoot()`
在 Debug 下读这个环境变量，本来就是仓库既有的测试隔离机制）。其余是真实缺陷。

#### 8.3.1 合并后未跑过测试导致的契约计数漂移（13 个）

Linux 侧重算过这些计数，但从未执行过断言。逐个用三路对比（fork `1aa78145` /
merge-base `d4b0a7a2` / upstream `bbb9e697`）判定是「上游合法变更」还是「合并错误」，
只对前者更新冻结值：

| 契约 | 原值 | 实测 | 判定 |
|---|---|---|---|
| `AppSettings` 门面 passthrough 计数 | 238 / 239 | **240 / 241** | fork 独有的 `WidgetTitleAlignment`、`WidgetAnimationFrameRate` 在 base 与 upstream 均不存在，只在 fork 与合并树存在 → 冻结值偏低 |
| `Todo` feature 源文件数 | 37 | **38** | 上游新增 `TodoSettingsCoordinator.cs` |
| `Glance` feature 源文件数 | 19 | **22** | 上游新增 3 个日历/图片服务，均有真实调用点 |
| `Search` feature 源文件数 | 21 | **23** | 上游新增 `SearchSettingsCoordinator.cs`、`SettingsSearchChannelPolicy.cs` |
| `QuickCapture` feature 源文件数 | 25 | **27** | 上游新增 2 个；同时删掉 1 个被合并错误复活的死文件（见 8.3.2） |
| `ModuleBoundary` 全局访问 manifest | `ThemeService` 3 / `WidgetManager.ZOrder` 10 | **4 / 12** | 多出的是 fork 独有守卫（跨线程重投、interaction-leak watchdog），上游无对应物 |
| `ToggleSwitch` 无障碍名 | Onboarding 6 | **8** | fork 保留本地五步引导时丢了上游的无障碍标注 |
| 架构守卫：注册子目录 | `Views` 只有 `SettingsSections` | 需加 `Dialogs` | 上游新增 `HotkeyRecorderDialog.xaml.cs` |
| 架构守卫：host 命名空间白名单 | 冻结于 fork 时代 | 需放行 feature 自己的命名空间 | 上游把设置分节搬进 `Features/<Name>/` 后，`SearchSettingsSection.xaml.cs`、`QuickCaptureSettingsCoordinator.cs` 合法引用 `DeskBox.Features.Search` / `.QuickCapture` |

#### 8.3.2 被合并错误复活的死代码（2 个文件）

fork 的清理提交 `5078325e` 删掉过一批死代码，上游没有该提交，合并把它们复活了：

- `src/DeskBox/Helpers/ShellDataObjectBuilder.cs`（213 行）——全仓零调用点，且它调用的
  4 个 `Win32Helper` 方法上游已删除；
- `src/DeskBox/Helpers/QuickCaptureClipboardActivationHelper.cs`（15 行）——全仓零调用点。

两个文件已删除。同批被删的 `src/DeskBox/Contracts/IFeatureLifecycleEvents.cs` 在合并树里
正确保持了删除状态，只有这两个漏网。

#### 8.3.3 上游改了实现/测试但没同步的地方（5 个）

| 失败 | 根因 | 改动 |
|---|---|---|
| `SettingsWiringContractTests` 5 个死绑定 | 合并取了上游门面退役，但 fork-only 的 `MusicSettingsSection.xaml` 与 `SettingsWindow.xaml` 两处 expander 仍绑已退役属性 | 绑定改到编辑器属性（`AvailableDisplayModeOptions` / `DisplayMode` / `UseArtworkBackdrop` / `EnableCoverHoverMotion` / `Enabled`） |
| `QuickCaptureSettingsEditorTests` 仍含 `{Binding QuickCapture` | 同上，7 处只迁移了 5 处 | 剩余 2 处 expander 的 `IsEnabled` 改绑 `Enabled` |
| `WidgetRaiseBandContractTests` ×2 | 合并把 fork 的 `BringTitleActivatedGroupToFront` 插在旧切片 marker 之间，把下一个方法整段吞进切片；另一个是 marker 硬编码 CRLF 而该文件是 LF-only | 测试切片 marker 改到正确方法边界 + 切片内做 EOL 归一（产品代码正确，未改） |
| `CapsuleClickAndMorphContinuityTests` | fork 的两行 `CapturePositionAnchor` + `UpdateConfigBoundsFromPhysical` 被上游的 `CommitUserPlacementFromWindow` 取代，后者是其超集 | 测试断言改到上游方法（产品代码正确，未改） |
| `ThemeDictionaryContractTests` | 合并取上游四字典结构，但 fork 的 5 个画刷只补进 Light/Dark | `Default` / `HighContrast` 各补 5 条，四字典均 23 键 |

#### 8.3.4 合并集成缺陷（6 个真实行为回归）

这一批是**合并本身写坏的**，全部有基线对照：在 `1aa78145` 上同样的测试全绿。

1. **`TodoReminderService` 丢失失活守卫**（`TodoReminderServiceTests` ×2）
   上游在 `LoadStoreSnapshotAsync` 之后有 `if (_disposed || !ShouldBeRunning()) return;`，
   合并改用 fork 的 `store.MutateAsync` 路径后丢了该检查点 → 禁用/释放后仍会写入
   并通知。已在 `src/DeskBox/Services/TodoReminderService.cs` 的 mutate 回调开头补回守卫。

2. **`SettingsService` 丢失运行时状态保留策略**（`ApplyDefaultPreferences_...`）
   上游 `DefaultPreferencePreservationReasons` 里有
   `[nameof(AppSettings.LastQuickCaptureFileWidgetId)] = RuntimeState`，合并丢失 →
   恢复默认偏好时把该字段清成了 `""`。已补回（策略键数 43，与上游一致）。

3. **两套互斥的 schema 守卫被串联**（`SaveCheckedAsync_NewerSchemaProfileIsRefused...`）
   上游的 `if (_settingsSchemaNewerThanBuild) return false;` 提前 return 且**不设**
   `LastPersistenceFailure`；fork 用锁内 `_loadedDiskSchemaVersion` 守卫并设该字段。
   合并把两者都留下了，上游分支先命中 → fork 分支成死代码。已删除上游的提前 return。

4. **`MusicSettingsStoreTests` 钉了旧 schema 版本**（陈旧测试）
   `SettingsMigrationService` 的当前 schema 版本三路为 base 9 / fork 10 / 上游 **14**，
   合并取上游 14 并已注册 9→10→…→13→14 全链。测试仍钉 10。已把测试改为 14
   （产品常量不降）。

5. **`TodoStoreChanged` 订阅不对称**（`F9RemediationContractTests`）
   合并只在 `CreateTodoReminderService` 里 `+=`，全文件无 `-=`。fork 的退订在
   `RefreshTodoReminderService` 的 `if (!shouldRun)` 分支，但该逻辑已被上游的
   `TodoReminderRuntime.Reconcile()` 取代，`Retire()` 只 `DisposeAsync()` 不碰事件 →
   每次重新激活泄漏一个旧订阅。已在 `src/DeskBox/App.xaml.cs` 的 retire 分支补 `-=`。

6. **`TodoReminderService` 残留读缓存路径**（`ReminderService_WritesOnlyThroughMutate...`）
   fork 的 `LoadStoreSnapshotAsync` 是只读快照路径，上游改成 `MutateAsync` 后该方法
   在合并树里已无调用点，但方法体、`_storeSnapshots` 字段与 `CheckCoreAsync` 里的
   修剪块都还在，且其中的 `await store.LoadAsync();` 命中了「只允许走 Mutate」的守卫正则。
   已删除这三处死代码。

#### 8.3.5 junction 解析缺陷（3 个）

`FileService.TryResolveExistingPathForTraversal` 用
`ResolveLinkTarget(returnFinalTarget: false)` 取单跳目标后递归解析。在 8.2 描述的机器上，
被写坏的重解析点会让该调用返回畸形路径（把 `Global\E:\...` 当相对路径拼到当前目录），
递归必然失败，于是 junction 被判定为「不可解析」。

修法：`src/DeskBox/Services/FileService.PathResolution.cs` 增加一条回退——当单跳目标
**非完全限定路径或不存在**时，改用 `ResolveLinkTarget(returnFinalTarget: true)` 取最终
目标再递归。健康路径保持原有的逐跳 visit/cycle 语义（`MaxReparsePointHops = 64`）不变。

这一条严格说是对机器异常的免疫，不是合并回归（`FileService.PathResolution.cs` 在
`git diff 1aa78145..HEAD` 中无改动）。但保留它是合理的：畸形重解析点在真实用户机器上
也会由安全软件产生，而旧代码会让这类 junction 完全不可遍历。

#### 8.3.6 被合并错误复活的死代码之外：两个诊断探针

验收期间在测试项目里临时加过 3 个诊断测试（导出 testhost 令牌、复现 junction 畸形、
复现 WinRT 拒绝访问），**已在提交前全部删除**，不在合并树里。

### 8.4 本地化键集（12 语言）

合并采用「两侧键并集」策略，因此把 fork 早已退役、上游从未有过的 5 个键也并了回来：

`Settings.WindowShadow.Confirm.Title`、`.Confirm.Body`、`.ApplyFailed`、
`.ApplyFailedNoEffect`、`Widget.ViewAndSort`。产品代码对这 5 个键**零引用**，
两条契约测试分别断言它们必须不存在。已从 12 个语言文件全部删除：
**3,095 → 3,090 键**，12 语言键集与占位符逐键比对仍 0 差异。

`MusicSettingsSection.xaml` 是 fork 独有、全仓零实例化的文件（上游在
`SettingsWindow.xaml` 里内联了等价模板）。曾考虑删除，但
`AotStage4D1BContractTests` 把它的 3 个 `HeaderKey` + 3 个 `DescriptionKey` + 4 个
`{Binding` 计进了全局 223 / 190 / 487 的冻结值，删除会制造新失败并牵动审计脚本。
按「保留本地功能行为」的硬约束，**保留该文件**并给它补 2 个 `AutomationNameKey`。
已用测试同款正则重算合并树 41 个生产 XAML：223 / 190 / 37 / 23 / 6 / 6 / 2，
合计 487，与断言完全吻合。

### 8.5 Native AOT 审计

首次完整运行得到 **45 条 findings**。全部修复后 `exit 0`。

#### 8.5.1 从未被编译过的产品缺陷（4 项）

AOT 审计是唯一会编译 `*.Aot*Smoke.cs` / `Services/Aot*Fixture.cs` 的配置
（`DeskBox.csproj` 在 `DeskBoxAotSmokeHarness != true` 时把它们 `Compile Remove`），
而上游 CI 只跑 restore + build + test、不跑审计 → 上游带进来的缺陷从未暴露：

1. **`AotShellMoveFixture.cs` 缺 `using DeskBox.Helpers;`**（CS0246）
   该文件引用 `StaOperationAbandonedException`（定义在 `Helpers/BoundedStaOperationRunner.cs`）
   却没有 using，AOT 配置直接编译失败，并连带产生 7 个 WMC XAML 假错。
   已补 using。该文件与上游 `bbb9e697` **逐字节相同** → 上游自身缺陷，引入提交 `6314ecfa`。

2. **`RunRegistrationHeartbeat` 产生 CS4014**（`SearchHotkeyService.cs` / `GlobalHotkeyService.cs`）
   合并保留了 fork 的 `private async Task RefreshRegistrationCore(...)` 签名，同时引入
   上游新增的 `void RunRegistrationHeartbeat()` 接口方法 → 调用点无法 await。
   两处改为 `RefreshRegistrationCore(quiet: true).GetAwaiter().GetResult();`，与同文件既有的
   `RefreshRegistration()` 先例一致：该路径下 core 内唯一的 await 同步完成，不会死锁，
   且调用后的状态读取必须观察到刷新结果。

3. **`SHAssocEnumHandlers` 的 COM 编组签名产生 IL2050**（`ShellOpenWithInterop.cs`）
   `[MarshalAs(UnmanagedType.Interface)] out ShellOpenWithEnumHandlers` 是 Native AOT
   无法保证的 COM 编组。改为 `out IntPtr` + `Marshal.GetObjectForIUnknown`（本文件其余
   指针已用同一模式），并显式释放枚举器 RCW。`Next` 的
   `[MarshalAs(UnmanagedType.LPArray), Out]` **不能动**——2026-10-16 的
   `MngSafeArrayMarshaler` 崩溃事故就是它的默认 SAFEARRAY 编组造成的。

4. **`ShellLinkForFlagProbe` 的 ComImport coclass 被 ILC 判为 always-throw**
   （`DragDropPermissionService.cs`）ILC 输出
   `will always throw because: Invalid IL or CLR metadata`，导致**零售 AOT 版的
   「以管理员身份运行」快捷方式标志探测永久返回 false**——真实功能回归。
   改为直接读 `.lnk` 二进制头：ShellLinkHeader 里 `LinkFlags` 在偏移 `0x14`
   （HeaderSize 4 + LinkCLSID 16），`SLDF_RUNAS_USER = 0x2000` 是同一位集，
   因此读 24 字节头部与 COM 往返等价，且零 COM、零 P/Invoke。

#### 8.5.2 审计脚本自身的过期 pattern（5 类）

以下 5 类 pattern 都是**上游改了产品实现或测试却没同步脚本**，即审计在上游自身
就是红的（上游不跑审计所以无人发现）：

1. `$stage5B4B1RoutePatterns` 里 3 个 Capsule 深链路由
   （`CapsuleBehaviorSettings` / `CapsuleArrangementSettings` / `CapsuleAnimationSettings`）
   已在上游退役——`SettingsWindow.AotDeepSmoke.cs` 的 `deepSettingsRoutes` 只剩
   `CapsuleMode` / `CapsuleOverridesSettings`。删掉 3 条。
2. `File.Delete(attachment.FilePath)` 已被 `AttachmentStorageService.TryDeleteManagedCopyAsync(`
   取代，而**契约测试正断言该字符串不得出现** → 脚本要求必须出现是自相矛盾。
3. `await actions.DeleteItemsAsync(actions.GetSelectedItems())` 已被拆成两行调用。
4. `MenuFlyoutItemAutomationPeer` + `PatternInterface.Invoke` 已被上游替换为
   `AotContextMenuProbe.InvokeProvider` 模式（3 个 stage 数组各 1 对）。
5. `$stage5B4B1ExpectedBindableViewModelPropertyCount = 33` 经核实与合并树一致，**不改**。

#### 8.5.3 计数重校准

WMC1510 在合并树上实测 **796**，三路为 fork 742 / base 866 / 上游 875 —— 都不是合并树
的值：13 个产生该警告的 XAML 里 10 个取了上游侧（与 `bbb9e697` 逐字节相同），另加
fork 独有的 `MusicSettingsSection.xaml`。已把脚本里 **35 处**钉值 + 契约测试里
**20 处**断言统一改为 796。

按脚本头部约定，任何 pattern / 计数改动都必须 bump `$auditProfileVersion`：
**63 → 64**，并同步 `$RequiredAuditProfileVersion` 与 `$requiredAuditProfileVersion`
链路（启动器 + `run-aot-*-smoke.ps1` + 全部 Stage 契约测试），共 102 处。

#### 8.5.4 审计结论

- `auditFindings`：**空**
- `alwaysThrowMessages`：**0**
- `warningCodes`：`CS0108` 2 / `CS8601` 2 / `CS8602` 16 / `WMC1510` 796，全部在
  `allowedWarningCodes` 白名单内
- 35 处 WMC1510 钉版 Expected/Maximum **全部等于实测 796**（0 处 stale）
- `sourceStableDuringAudit`：`true`（审计前后工作树指纹一致）——**但见 8.5.5：**
  这个 `true` 在该 worktree 上是假阴性，干净检出上的首次审计会因 lock 漂移而失败。
- `analysisContract` 下所有 `*Missing*Patterns` 数组为空
- `rustNative`：ABI 2 / capability mask 511 / 10 个必需导出
- 产出 103 个文件 / 64.3 MiB

#### 8.5.5 干净检出上首次审计必失败的 lock 漂移（已修）

本轮审计在 `E:\DeskBox-verify` worktree 上通过，但那是**假阴性**：该 worktree 早前多次
restore 已把 `src/DeskBox.Abstractions/packages.aot.lock.json` 改脏，审计开始时的
`$sourceSnapshotBefore` 捕获到的已经是改写后的状态，所以前后指纹一致。

**缺陷**：该 lock 文件在合并树里仍钉 `Microsoft.DotNet.ILCompiler` /
`Microsoft.NET.ILLink.Tasks` = **10.0.11**，而 `global.json` 已升到 SDK 10.0.402，
AOT restore 实际解析出 **10.0.13**（与 `src/DeskBox`、`src/DeskBox.Updater` 两个 lock 一致）。
合并提交 `479339d4` 重新生成了另外 4 个 lock 文件，**漏掉**这一个。

**后果**：`scripts/publish-aot-audit.ps1` 在 L248（restore 之前）拍工作树快照，
在 L8529（publish 之后）再拍一次并比对；中间那次 restore 改写 lock 文件，
于是干净检出上的**第一次**审计必然报 `sourceStableDuringAudit = false` 并失败：
`The repository changed while the AOT audit was running. The output is not a trusted
source snapshot`。第二次跑才会通过——这会让任何新克隆的仓库第一次审计就红。

**验证**（干净 HEAD 树上）：`git status` 为空 → 执行审计脚本原样的 restore 命令 →
`git status` 变 1 个 M，该文件 ILCompiler 从 10.0.11 变成 10.0.13；再跑一次 restore
幂等（文件哈希不变）。普通开发流程的 `dotnet restore .\DeskBox.sln -p:Platform=x64`
不受影响（它用 `packages.lock.json` 而非 aot lock，保持工作树干净）。

**修复**：用审计脚本同款 restore 命令重新生成该 lock（仅 ILCompiler / ILLink 版本与
contentHash 变化，3 个 RID 段全部同步到 10.0.13），提交后干净树上的 AOT restore 不再
产生漂移。仓库没有 `RestoreLockedMode`，也没有测试守卫 lock 版本一致性，所以这个漂移
此前只能靠人工发现。

### 8.6 提交

| 提交 | 内容 | 规模 |
|---|---|---|
| `5486cc33` | 测试验收暴露的缺陷（8.3 / 8.4） | 31 文件，+235 / −379 |
| `ec9375e8` | Native AOT 审计暴露的缺陷与重校准（8.5） | 60 文件，+291 / −236 |
| `75342d6c` | 本文档 §八及 §三 / §六 的过时数字 | 2 文件，+289 / −13 |
| 本提交 | 8.5.5 的 lock 漂移修复 + §七 的分支/回退点更正 | 2 文件 |

以上提交都在合并提交 `479339d4` 之后，未 amend 合并提交。身份 `Simon <1047078635@qq.com>`，
无 `Co-Authored-By` / `Generated with` 等署名（`.githooks/commit-msg` 强制）。

### 8.7 仍需在真机手工确认的项

自动化门禁已全绿，但以下行为只能人工观察，**未在本轮验收覆盖**：

1. **拖放**：从文件格子拖出到桌面（Win10 无操作选择框、托管快捷方式）、拖回源格子应被拒绝。
   合并删除了 fork 的第二个 `Items_DragStarting`，行为现在完全由上游处理器覆盖。
2. **胶囊展开/收起动画**流畅度，以及新恢复的帧率上限卡片（30/60/90/120 四档，
   写入 `settings.json` 的 `widgetAnimationFrameRate`）。
3. **设置页各节**：外观/动画、随记记录配色、音乐（含封面背景与悬停动效重启后保持）、
   文件叠放。
4. **引导流程**：本地五步流程 + 文件练习步骤。
5. **托管存储迁移**：现在走上游事务式复制（fork 的逐项重试已退役）。
6. **托盘显隐 / Show Desktop 自愈**。
7. **待办提醒 → 格子实时刷新**（对应 8.3.4 第 5、6 项的订阅修复）。

复验时请先把仓库放到 8.2 描述的目录树之外，否则会得到与代码无关的假失败。
