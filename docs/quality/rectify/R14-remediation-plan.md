# R14 整改方案（round-14 双路审查闭环批次）—— 修订 v2

> 输入：`docs/quality/rounds/round-14/全量代码缺陷审查总报告.md`（DEF-127~129，死代码/清理类，互相关联）。
> 原则：最小侵入（本批为「删除已证死代码」方向）、执行顺序 W1→W2→W3（W3 依赖 W2 的前置迁移）、不触碰 z-order 红线。
> 基线：`wip/fix-bug` @ `435b1a1a`，工作树仅含 round-14 文档产出。
> **修订记录**：v2 按独立完善性审查首轮 NO-GO 的 M1/M2 阻断项与 S1~S5 建议全面修订（W3 增补 DesktopOrganization.cs 与 3 个活方法内死分支、显式声明 AutoStart 激活期自纠行为退役；契约测试爆炸半径 4 文件 7 方法逐条写明「保 Settings 半段」裁剪法；XAML/行号区间全面校正；W1 删除清单补全；孤儿键已知活键例外与安全删除流程固化）。

## 1. 逐项处置

### W1 ｜ DEF-127 ｜ ShellDropDelegator 死子系统删除 + 注释校正（P3）

- **现状（主流程 + 审查双重复核）**：死集封闭且**ShellDataObjectBuilder.cs 无活成员**（3 方法 + 4 个 P/Invoke + 7 常量/Guid 全部只被死链使用）——实质整文件删除。活路径确认为 `NativeDropTarget.cs:211,523`（LaunchDropHandler）→ `ContentWidgetWindow.NativeDragDrop.cs:1052 HandleNativeLaunchDrop` → `ShortcutFileLauncher.TryLaunchWithFiles`。
- **修法（删除清单经审查补全）**：
  1. `ShellDropDelegator.cs`：删除 `TryDelegateDrop`/`BindShortcutDropTarget`/`CallDragEnter`/`CallDrop`/`CallDragLeave`/`InvokeEffectCall`/`ReleaseObject`/`GetVtableEntry`（:395-408，审查补遗）及 5 个 vtable 槽位常量、3 个 Guid、`ShellPointL` 私有结构（随链孤儿）——**保留活成员** `ShellDropLaunchOutcome`/`ShellDropLaunchResult`/`ShortcutDropOutcomePolicy`（被 NativeDropTarget/ContentWidgetWindow.NativeDragDrop/tests ItemDropBehaviorPolicyTests 引用）；若删后仅剩类注释则整文件删除并同步处理 using；
  2. `ShellDataObjectBuilder.cs`：**整文件删除**（无活成员）；
  3. `Shell32NativeMethods.cs:11-13` `SHCreateItemFromParsingName` 删除（唯一调用方是死链 BindShortcutDropTarget:272；FileService.ShellTransfer.cs 用自己的私有声明，不受影响）；
  4. `tests/DeskBox.Tests/ShellDataObjectBuilderTests.cs` 整文件删除（6 用例全部钉死死路径）；
  5. `NativeDropTarget.cs` **:513-518**（审查更正行号；:506-509 现为 App.Log）陈旧分派注释改为与 `HandleNativeLaunchDrop`/`ShortcutFileLauncher` 一致。
- **验证**：构建 0 错误 + 全量回归（删 6 用例后基线 4297→4291）+ grep 复验零残留。

### W2 ｜ DEF-129 ｜ BrandLogoHost 迁出死面板（P3，DEF-128 前置）

- **现状（审查核正行号）**：`BrandLogoHost` Grid 实际位于 xaml **:855-892**，在 `Step1Panel`（:846-967，Collapsed）内部；活协议全部经 `Storyboard.SetTarget(DependencyObject)`（:181/:271）与属性操作引用 `BrandLogoHost`/`BrandLogoShineTransform` 两字段——**无 TargetName 字符串解析，同文件内迁移后零代码改动成立**；约束：必须留在 OnboardingWindow.xaml 同一文件内（不得抽 UserControl，否则字段生成与零代码改动同时失效）。
- **修法（落点按审查给出的两案择一）**：迁至 `RootGrid` Row 0（36px 标题条，:159 TitleBarHost 所在行）**水平居中**，并加 `IsHitTestVisible="False"`（不挡 TitleBarHost 拖拽区）；备选案为 `StepContainer`（:170，TaskStep 面板共同父容器）首子元素顶部居中——备选案在窄窗下与 TaskStepBadge 有挤压/遮挡风险，故取 Row 0 案。实施时目检 Row 0 是否与标题文字重叠（若重叠则改用备选案并记录）。
- **验证**：构建 + 全量回归 + 静态走查（`Step1Panel` 内无被活协议引用的元素）；GUI 目检列入人工复验清单（沿用「GUI 实测待运行窗口」惯例）。

### W3 ｜ DEF-128 ｜ OnboardingWindow 旧版五步流程整体删除（P3）

- **现状（审查全量复核校正）**：死集封闭属实（22 个死符号零活引用、死链内部互调封闭），但删除面比首轮方案清单更大，且含一处**未声明的活行为变化**。
- **修法（按审查 M1/M2/S1~S5 全量修订）**：
  1. **XAML 删除（按 x:Name 定位结构边界，不做行号字面切割）**：`TaskStep1Panel`（:173-259）、旧五面板 `Step1Panel`（:846-967）/`Step2Panel`（:970-1026）/`Step3Panel`（:1028-1141）/`Step4Panel`（:1143-1396）/`Step5Panel`（:1397-1518）——**:1520-1523 为活内容（StepContainer 闭合/ScrollViewer/Footer），不得包含**；`RootGrid.KeyDown` 接线不在 XAML（在 xaml.cs:99 lambda），随 C# 删除；
  2. **C# 死成员删除（含审查补遗的传递闭包）**：`OnboardingWindow.Appearance.cs` 整文件；`Features/Completion/Storage/Hotkey/TaskFlow.cs` 中死成员（SetupStep2Features/SetupStep4/SetupStep5/SetupStep4Storage/SetupTaskStep1/StartKeycapPulse/RefreshSearchHotkeyText/Step4* 事件处理器/ChangeStoragePathToAsync 迁移链）；`xaml.cs` 的 StartStep1CardAnimation/StartSearchDemoAnimation/RunSearchDemoAsync/OnHotkeyKeyDown 与孤儿字段（`_hotkeyRecordingHook`、`_keycapPulseStoryboard`、`_searchDemoCts`、`_hotkeyDemoCts`【本就恒 null】、`_startupToggleRefreshGeneration`、`PresetAccentColors`、`WmReservedHotkeyCapture` 常量）；
  3. **`OnboardingWindow.DesktopOrganization.cs`（审查 M1 补遗，首轮遗漏）**：Storage.cs 死成员删除 + Step4Panel 删除后，该文件 3 处编译失败（`OrganizationChangePath_Click:32`→`ChangeStoragePathAsync`、`RefreshOrganizationPath:26`→`OrganizationPathText`、传递波及 `SetupOrganizationStep:14`/`InvalidateDesktopOrganizationPlan:19`【本就零调用】）——全文件随之删除（其唯一接线在死面板 :1374）；
  4. **3 个活方法内的死分支随删**：`NavigateToStepAsync` :384-387（读 `_isRecordingHotkey`/调 `EndHotkeyRecording`）、`WindowSubclassProc` :982-991（`WmReservedHotkeyCapture` 分支调 `ApplyRecordedHotkeyAsync`）、`ApplyResponsiveLayout` :212-235（Step3 布局段）——**`UpdateFooterState` 无 Step3 段，不动**（审查更正）；
  5. **显式声明的活行为变化（审查 M1）**：`OnboardingWindow_Activated`（xaml.cs:98 订阅，活）→ `RefreshStartupToggleFromSystem`（Hotkey.cs:314）在每次窗口激活时把 OS 启动状态回写 `_settingsService.Settings.AutoStart` 并 SaveDebounced——`Step4StartupToggle`（死面板 :1253）删除迫使该链退役，**等于移除一项激活期自纠行为**；决议：随死面板一并退役（TaskStep 流无启动项开关，该自纠仅服务于旧流），Settings.AutoStart 活语义由 SettingsWindow 承担不受影响；此行为增量写入整改报告与台账；
  6. **契约测试裁剪（审查 M2——4 文件 7 方法，非计数类；一律「保 Settings 半段、只裁 Onboarding 半段」）**：
     - `GlobalHotkeySafetyContractTests.SettingsAndOnboardingRecorders_IgnoreReservedHookMaskKey`（:124-131）：slice 以 `"private void OnHotkeyKeyDown"`/`"private void Step4HotkeyToggle_Toggled"` 为标记——只删 Onboarding 半段断言，**保留 Settings 侧 IsInternalMaskKey 排序守卫**；
     - `StartupRegistrationContractTests.SettingsAndOnboardingExposeWindowsStartupAppsRecovery`（:44-55）：同理只裁 Onboarding 半段（`result.RequiresSystemSettings`/`RefreshStartupToggleFromSystem` 断言随死链退役）；
     - `OnboardingExperienceTests` ×3（:71-75/:105-109/:278-282）：activeFlow 切片终止标记 `x:Name="Step1Panel"` 改为 `x:Name="FooterNav"`；
     - `FolderPickerModernizationContractTests.AllEightProductEntrances_AwaitTheOwnerAwarePicker`（:46/:59/:74）：Onboarding 条目按既有先例（:352-353 QuickCapture 注释）退役并 `Assert.Equal(8, totalCalls)` → 7。
  7. **孤儿键删除（审查安全流程固化）**：①先删死 XAML/C# 并构建 0 错误；②候选键（en-US 计约 200：Onboarding.Step1~5 108 + Scene 36 + Step6 21 + Organization 27 + Reconfigure 1 + Storage.ChangePath 1 + Task.Step1 8）逐一在 src（排除 obj/bin）+ tests 全键名精确 grep；③**已知活键例外不删**：`Onboarding.Step2.TrayActionTitle`（活 TaskStep2Panel）、`Onboarding.Step4.PinTitle`（活 TaskStep3Panel，OnboardingExperienceTests:110 断言）、`Onboarding.Task.Step2.Warning.CloudSync/Network/Removable`（SettingsWindow.StorageAndUpdates.cs:200-208 活引用）、`Settings.AutoStart.*`（SettingsWindow 活用 + 全语言断言）；④零引用键 12 语言同一提交同步删除；⑤ `static_gate`（动态基准 parity）+ `OnboardingExperienceTests.TaskFlow_IsLocalizedInEveryLanguage`（反向兜底）双验；⑥ Scene.*/Step6.*/Reconfigure.* 为既有零引用孤儿，可同删但在整改报告单独记数；
  8. `scripts/publish-aot-audit.ps1` 运行前提（审查核正）：仅支持 `-Platform x64`（ARM64 走独立脚本）、依赖 `rust-arm64-msvc-environment.ps1` MSVC 环境助手、Rust 工具链、全量 AOT 发布（10 分钟级）、auditProfileVersion 63——尝试运行并留档；环境不可用则如实记录并以 AOT 契约测试 + 全量回归兜底。
- **验证**：分段增量编译（删一段编译一次）→ 最终构建 0 错误 → 全量回归全绿 → static_gate（键数动态下降 ×12 对齐）→ audit 留档。

## 2. 不进本批（延后维持，与总报告 §5 一致）

新位点 4 组（DEF-070 读侧 ×2、DEF-092 ×1、O-26 ×1、DEF-078 ×1——并入既有编号挂账）、观察项 8 条。

## 3. 门禁

1. 每波次增量构建 0 错误（W3 删除量大，分段编译）；
2. 停止仓库路径 DeskBox 实例 → 最终 Debug x64 构建 0 错误；
3. `python scripts/quality/static_gate.py` PASS（键数动态下降 ×12 对齐；零 async void/同步等待/空 catch 新增）；
4. x64 全量回归全绿（W1 删 6 用例后基线 4291）；
5. `scripts/publish-aot-audit.ps1` 留档（或环境不可用的如实记录）；
6. 启动规范 Debug 实例核验；
7. 台账/TODO 收口（DEF-127~129 → ✅ 已修复；DEF-036/038/087 补注「位点已随旧版引导流死代码删除」；AutoStart 激活期自纠行为退役记入台账；整改批小节遵守「待实施→回填」纪律）。

## 4. 风险与回滚

- W1/W3 为纯删除（合计约 3,500+ 行含键），W2 为同文件 XAML 结构迁移；删除面经全仓 grep 零引用实证 + 审查全量复验（「删除集本身全部属实、无误删」）。
- 最大风险：W3 契约测试裁剪误删 Settings 半段活守卫——按 M2 指令「只裁 Onboarding 半段」并在回归中由 GlobalHotkeySafety/StartupRegistration 剩余断言兜底。
- 回滚：单 commit revert。
