# R15-A 核心服务与数据面专项审查报告（round-15）

> 审查基线 commit `08bdd98e`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R15-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 差异化声明

- **基线漂移**：R14 审查基线（`435b1a1a`）以来仅一个代码提交 `fb96a004`（R14 修复批：DEF-127~129，30 文件 +61/−5,539）+ 台账提交 `08bdd98e`（纯文档）。`git diff 435b1a1a..08bdd98e --stat` 确认代码面变更全部落在该批内，其余全部源文件与 round-09~14 六轮深读时的版本逐字节一致，历轮全文审读结论按「文件零变更」直接继承。
- **本轮主攻方向**（按任务指令）：
  1. **round-14 大删除批的残余引用全面核查**——`fb96a004` 全量 diff 逐文件审读：230 个孤儿键 ×12 是否真零残留、ShellDropDelegator/ShellDataObjectBuilder 截断后相关文件是否自洽、OnboardingWindow 剩余代码与 XAML 对已删符号/面板/键的引用、4 个契约测试裁剪的正确性；
  2. **盲区深读**——`WidgetManager.Groups.cs`（2,771 行，历轮仅模式扫描，**本轮首次全文**）、`QuickCaptureStore.cs`（398 行）/`GlanceWidgetStore.cs`（338 行）全文零视角重读、`App.Aot*Smoke` 27 个测试装置一致性盘点、`WidgetViewModel` 家族 5 个从未深读分部（Stacks/Operations/ItemHydration/Navigation/Windowing，约 5,100 行）模式扫描。
- **不再重复深读**：SettingsService、FileService 家族、DeskBoxDataBackupService、Cloud/WebDav/Updater、MusicVolume、ResilientJsonStore、TodoRecurrence、QuickCaptureService、DirectStartup 族、DesktopOrganization 引擎、LocalizationService、FolderWatcherService、SearchEngineService、WeatherService、EverythingSearchService、TodoWidgetViewModel 五分部、WidgetManager 主文件/Storage/ZOrder/CapsuleArrangement 等——round-06~14 已全文主审且基线无漂移，仅做台账锚点定位复核。

### 1.2 覆盖率声明

**全文逐行读（本轮新覆盖或修复批复核）**：

- `fb96a004` 全量 diff（30 文件）+ 删除面交叉验证：12 语言 JSON 删除键提取后全仓反查（src + tests，剔除 obj/bin）、OnboardingWindow 剩余 5 文件（xaml / xaml.cs / TaskFlow / Completion / IntroAnimations）与 XAML 事件处理器逐个反查、NativeDropTarget/Shell32NativeMethods 修改段、4 个测试裁剪 diff
- `Services/WidgetManager.Groups.cs`（2,771 行，**首次全文**——含 MergeWidgets/Switch/Remove/Dissolve/Reorder 全部拓扑事务、WidgetGroupMutationSnapshot 回滚、capsule 身份迁移、拖拽 dwell 定时器）
- `Services/QuickCaptureStore.cs`（398 行全文）、`Services/GlanceWidgetStore.cs`（338 行全文）
- `WidgetManager.FeatureWidgets.cs` 维护路径段（RepairLegacyEmptyContentFeatureFileShells / DeduplicateFeatureWidgets / ResetFeatureWidgetAsync，:690-930）+ `WidgetManager.ZOrder.cs` 锚点定点 + `WidgetManager.SurfaceContent.cs` 提升事务段（:62-130）

**模式扫描 + 命中处精读**：

- `WidgetViewModel.Stacks.cs`（2,280）/ `Operations.cs`（1,064）/ `ItemHydration.cs`（871）/ `Navigation.cs`（525）/ `Windowing.cs`（396）——按 `async void` / 同步等待 / lock / Interlocked / Task.Run / File|Directory 变更 / 文化敏感 Parse/ToString / Marshal / new Thread|Timer 全模式扫描；命中 5 处全部精读（4 处 Task.Run 正常异步、1 处 O-9 家族展示排序、1 处 Enum.TryParse 无害）。**声明：该 5 文件为模式扫描 + 命中精读覆盖，非逐行全文**
- `App.Aot*Smoke` 装置一致性：27 个分部 ↔ 测试引用反查、27 个 `Start*IfRequested` 入口 ↔ `App.xaml.cs` 分发点双向比对、含破坏性文件操作的 15 个文件 ↔ `ModuleBoundaryContractTests.DestructiveFileOpExpectedViolations` 清单一一比对
- 契约测试 manifest 死条目排查：对 `ModuleBoundaryContractTests` / `SettingsSliceOwnershipContractTests` / `FolderPickerModernizationContractTests` 三个 manifest 逐一与其断言的消费逻辑（`AssertViolationManifest` / `FacadePassthroughAccess_OnlyShrinks`）核对条目-文件存在性语义

**未覆盖及原因**：

- round-06~14 已全文主审的大文件：基线无漂移，仅锚点复核
- 12 语言 JSON 全量重扫（归 B 代理；本轮仅做删除键反查与 parity 抽验）
- `publish-aot-audit.ps1`（10,498 行，R12 起持续留档观察项，本轮未触碰——其 742 WMC1510 钉值已随 `fb96a004` 更新，grep 验证 4 处一致）

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| O-9 家族（CurrentCulture 比较器与仓库 Ordinal 惯例不一致，纯展示排序） | `Services/WidgetManager.Groups.cs:679`（`GetWidgetGroupJoinTargets` 的合并目标列表按 `StringComparer.CurrentCultureIgnoreCase` 排 DisplayName） | 仅影响拖拽合并候选菜单展示次序，无机器解析消费方；与 R12 已记录的 SearchResultRanker:63 / WidgetStackGroupingService:280 同水位 |
| O-9 家族（同上） | `ViewModels/WidgetViewModel.ItemHydration.cs:115`（桌面快照条目 `ThenBy(Name, NaturalStringComparer.CurrentCultureIgnoreCase)`） | 桌面公共桌面快照的条目名自然排序，纯展示序；首次深读该分部时发现 |
| DEF-092 家族（死代码残留，恒定条件） | `Services/WidgetManager.Groups.cs:2769` `public bool IsEmpty => false;` + `:2440` 消费点 `if (state is null \|\| state.IsEmpty)` | `WidgetGroupTransientState` 私有嵌套 record 的 `IsEmpty` 恒为 false，全仓仅 :2440 一处消费——`\|\| state.IsEmpty` 恒不触发，为等价于 `state is null` 的死条件分支；疑似空态语义被Capture 侧提前短路（opaqueState 为 null 即不建 state）后遗留 |
| DEF-070 家族（活设置列表锁外整表替换/重排 × 后台防抖保存锁内序列化竞态，写侧） | `Services/WidgetManager.Groups.cs:854`（MergeWidgetsAsync 失败回滚 `_settingsService.Settings.WidgetGroups = groupSnapshot` 整表引用替换）与 `:2659-2688`（`WidgetGroupMutationSnapshot.Restore` 在活 `WidgetGroups`/`WidgetCapsuleBarOrder`/`WidgetCapsuleFreePlacements` 上 RemoveAll/Clear/AddRange） | 持 `_widgetGroupGate` 但不持 `SettingsService._lock`，与台账既有家族位点（DesktopOrganizationTransaction.Restore 的 `settings.Widgets = originalWidgets` 赋值，R9 已并入）完全同型；全部调用在 UI 线程、竞态窗口毫秒级，同水位维持，不单独立案 |

---

## 3. 存量复核（范围内挂账条目现状）

### 3.1 round-14 删除批（DEF-127~129，`fb96a004`）复核——本轮重点

| 条目 | 结论 | 当前树证据 |
|---|---|---|
| **DEF-127（ShellDropDelegator 死子系统删除）** | ✅ 删除自洽，**唯 manifest 残留一条死条目**（见 A-01） | ① `ShellDropDelegator.cs`/`ShellDataObjectBuilder.cs`/`ShellDataObjectBuilderTests.cs` 整文件已删，全仓（src+tests+scripts+installer，剔除 obj/bin）对 `TryDelegateDrop`/`TryCreateHdropDataObject`/`BuildHdropBytes`/`ShellDropDelegator`/`ShellDataObjectBuilder` **零残余引用**（仅历史文档 docs/architecture、docs/quality 与 tests/ untracked TestResults 中出现）；② `Shell32NativeMethods.cs` 的 `SHCreateItemFromParsingName` LibraryImport 已随批删除，`FileService.ShellTransfer.cs:406,809` 的同名调用走该文件**自带私有声明**（:809 `private static partial int SHCreateItemFromParsingName`），不受影响；③ `NativeDropTarget.cs:510-521` 分派注释已改写为与 ShortcutFileLauncher 实测结论一致（含 drop_on_shortcut_open.md §10.2 指引）；④ 活发射路径（`LaunchDropHandler` → `HandleNativeLaunchDrop` → `ShortcutFileLauncher.TryLaunchWithFiles`）完好未动 |
| **DEF-128（OnboardingWindow 旧版五步流删除 + 孤儿键 230 ×12）** | ✅ 删除自洽、孤儿键真零残留，**唯 manifest 残留五条死条目**（见 A-01） | ① 230 个删除键（en-US diff 逐键提取）在 src+tests 全仓反查（`.cs`+`.xaml`，剔除 obj/bin）**零命中**；12 语言 parity 机械抽验 **2,667 键 ×12 全对齐**；② 六个分部（Appearance/Features/Completion 截段/Storage/Hotkey/DesktopOrganization）删除后，全仓对 `SetupStep2Features/SetupStep3/SetupStep4/SetupStep5/StartStep1CardAnimation/StartSearchDemoAnimation/SetupTaskStep1/_isRecordingHotkey/_hotkeyRecordingHook(Onboarding 侧)/_keycapPulseStoryboard/_searchDemoCts/_startupToggleRefreshGeneration/PresetAccentColors/WmReservedHotkeyCapture(Onboarding 侧)` 零残余引用（Settings 侧同名热键录制链为活代码，`GlobalHotkeySafetyContractTests:34-36` 已正确改断言 SettingsWindow 源）；③ 七个死面板（Step1~5Panel/TaskStep1Panel/StepOrganizationPanel）在 xaml 与 .cs 双侧零残余；④ XAML 现存 8 个事件处理器（Back/Next/Skip/TaskStep3×2/TaskStep4×2/TaskStep5）逐一在代码后置解析成功；⑤ `GetStepPanel`（xaml.cs:274-278）与 `SetupStep` 活映射自洽；⑥ `OnboardingExperienceTests` 三处切片终点 `Step1Panel→FooterNav` 更新后语义正确（FooterNav :809 位于 TaskStep5Panel :670 之后，切片仍覆盖活动流程区）；⑦ `FolderPickerModernizationContractTests` 正确删除 `OnboardingWindow.Storage.cs` 条目并把总数 8→7——**该测试家族内 manifest 同步惯例存在且本批已正确执行**（与 A-01 的两处遗漏形成对照）；⑧ AutoStart 激活期自纠退役已在提交信息显式声明，`StartupRegistrationContractTests` 断言收敛到 Settings 侧（`RefreshAutoStartState()` 在位） |
| **DEF-129（BrandLogoHost 迁移）** | ✅ 迁移正确 | `OnboardingWindow.xaml:161-199`：`BrandLogoHost` 现位于 RootGrid Row 0（Auto 行，与 TitleBarHost :159 同行高 36px）、`HorizontalAlignment="Center"`、`IsHitTestVisible="False"`、`Opacity="1"`；intro 协议三处引用（`IntroAnimations.cs:39` PlayIntroSequence 置 0 → `:127-128` brandFadeIn 动画回 1、`:218` DismissIntro 复位、`:253` StartBrandLogoShine）全部落在可见元素上；shine 停止点（Closed → `_brandLogoShineStoryboard?.Stop()`，xaml.cs:125 区域）在位；`Assets/deskbox.svg` 恢复为引导期可见的真 Logo 唯一展示位 |
| **台账 R14 补注（DEF-036/038 位点死代码化、DEF-087 onboarding 位点）** | ✅ 在位且与删除后现状一致 | 三条 R14 补注已写入台账行内；删除批后 `OnboardingWindow.Hotkey.cs` 已不存在，历史行号不再可解析但补注文本已阻断后续误靶向 |

### 3.2 其他存量条目（基线无漂移声明下的锚点/定点复核）

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-016（QuickCapture 失活回落缺门控，台账统计仍记「待修 P2」）** | **建议改判「已消除」** | 触发宿主 `QuickCaptureWidgetWindow` 自 F6（DEF-027）整文件删除，当前树仅剩 `WidgetWindowBase.cs:33` 一句「host was removed」的历史注释；无修复对象，P2 待修计数应清零（台账正文早已注明「随 DEF-027 跳过」，唯统计节未同步） |
| DEF-070 家族（FeatureWidgets 锁外写点） | 维持 | `WidgetManager.FeatureWidgets.cs:707,710,759-760` 与 `:890` 区域逐点确认仍在原位（本轮定点 grep）；新增同族写侧位点见 §2 |
| O-9 家族（QuickCaptureStore Tags 去重 CurrentCultureIgnoreCase） | 维持 | `QuickCaptureStore.cs:266` 原位点在位（本轮全文重读确认） |
| DEF-102（QuickCaptureStore per-path 门控） | 已修复（再证在位） | `QuickCaptureStore.cs:35-36,59` s_pathGates + TrimPathGates（FMEM-01 上限 64）与 TodoWidgetStore 对称，注释完整记录设计契约 |
| O-27（TodoWidgetStore.RebaseManagedAttachmentPathsAsync 分离 Load/Save） | 维持 | 未触碰（基线无漂移）；本轮 QuickCaptureStore 全文确认其同类「gate 只串行化文件操作」契约有显式注释（:27-34），属文档化设计而非漂移 |
| DEF-048/049/050/051/052/053/056/076/078/079/080/082/084/087/096/101、EXC-06、MEM-01/02、THR-06、O-1~O-29 | 维持 | 所在文件相对 R14 基线零变更（diff 反向核实），历轮证据直接继承；本轮唯一触碰的 WidgetManager.Groups.cs 新增家族位点已在 §2 单列 |
| ARC-04（DI 注册未接线，WeatherService 单例）/ O-22（WeatherService 所有权陷阱） | 维持 | `ServiceRegistry.cs` 未触碰 |

### 3.3 证伪留档（避免后续轮次重复怀疑）

- **`FileService.ShellTransfer.cs` 的 `SHCreateItemFromParsingName`**：R14 删除 `Shell32NativeMethods` 导出后仍存在调用点（:406）——经核实该文件自带 `private static partial` LibraryImport 声明（:809），与被删的共享声明无耦合，**非断链残留**，不立案。
- **`QuickCaptureStore.SaveAsync` 在门外执行 Normalize + JSON 序列化（:157-161）**：初判似「绕过门控的读改写」；实为**有意契约**——类头注释（:27-34）明确 gate 只串行化文件操作，Load→mutate→Save 的跨门窗口由调用方（QuickCaptureService 缓存门 / DEF-108 修复路径）负责，与 O-27 对 TodoWidgetStore 的定性一致。不立案，留档防重复怀疑。
- **`GlanceWidgetStore` 的 per-instance 门控足够性**：`WidgetStores` 静态字典按 widgetId 缓存实例（:27-28,65-83），同 id 全部调用共享同一 `_gate`，无 per-path 多实例串行缺口；`RaiseChanged`（:324-337）用 `GetInvocationList` 逐订阅者隔离，为 DEF-101 的正面范式。不立案。
- **`App.Aot*Smoke` 装置一致性**：27 个分部全部被 tests 引用（零孤儿装置）；15 个含破坏性文件操作的文件与 `DestructiveFileOpExpectedViolations` 清单**一一对应（0 多 0 少 0 计数漂移风险）**；全部 `Start*IfRequested` 入口与 `App.xaml.cs:1293` 一带的分发点双向闭合。装置面无一致性缺陷。
- **`OnboardingExperienceTests` 切片终点替换**：`Step1Panel→FooterNav` 后切片区间（TaskStep2Panel :215 → FooterNav :809）仍完整覆盖四个活动 TaskStep 面板，断言方向不变。非弱化，不立案。

---

## 4. 新发现问题清单

### A-01 ｜ round-14 删除批遗漏收缩两张契约测试 manifest：6 条死条目指向已删除文件，ratchet 记忆失真并预留「复活免费违规预算」 ｜ P3

- **位置**：`tests/DeskBox.Tests/ModuleBoundaryContractTests.cs:32`（`["src/DeskBox/Helpers/ShellDataObjectBuilder.cs"] = 5`，文件已随 DEF-127 整删）；`tests/DeskBox.Tests/SettingsSliceOwnershipContractTests.cs:344,346-349`（`OnboardingWindow.Appearance.cs=10`、`.DesktopOrganization.cs=1`、`.Features.cs=1`、`.Hotkey.cs=15`、`.Storage.cs=5`，五个分部已随 DEF-128 整删；`:345` 的 `Completion.cs=5` 文件尚存但当前树实测 facade 访问为 0，属合法 slack）
- **触发条件**：静态存在（测试基线数据缺陷），无运行时触发。
- **影响**：
  1. **ratchet 记忆失真**：两个 manifest 的自我声明均为「Entries may only shrink or disappear… the manifest is the ratchet's memory」「exact violation manifests pin today's offenders file-by-file」——死条目使 manifest 不再反映现实，后续按 manifest 做边界清点的人会拿到幽灵预算。
  2. **复活免费预算**：断言逻辑（`AssertViolationManifest` :339-358、`FacadePassthroughAccess_OnlyShrinks`）只对**实际存在的违规文件**反查 manifest，从不校验「每条 manifest 条目 ↔ 文件存在」。若未来任何人重建同名文件（如恢复 ShellDataObjectBuilder 或新建 OnboardingWindow.Hotkey.cs），将分别获得 5 条 P/Invoke 越界或 15/10/5 条 facade 旁路的**静默豁免**——这正是 manifest 注释宣称要堵死的「一个文件的清理替另一个文件的回归买单」substitution gap。
  3. **惯例不一致实锤**：同批删除对 `FolderPickerModernizationContractTests` **正确执行了**收缩（删 `OnboardingWindow.Storage.cs` 条目 + 总数 8→7），且 `SettingsSliceOwnershipContractTests` 自身保留着 DEF-027 时代「QuickCaptureWidgetWindow partials went away… so their facade budgets are retired too」的先例注释——证明正确惯例在仓内已确立两次，本批在另两张表上漏执行。
- **根因机制**：删除批的测试裁剪按「编译失败→改断言」驱动（4 个直接引用删除文件的测试被裁剪），而这两张 manifest 是**数据驱动**（文件删除不产生编译错误、断言也不断言条目存在性），因此静默漏网；回归 4291/4291 全绿进一步掩盖了漂移。
- **证据**：
```csharp
// ModuleBoundaryContractTests.cs:32 —— 文件已于 fb96a004 删除，条目仍在
["src/DeskBox/Helpers/ShellDataObjectBuilder.cs"] = 5,
// SettingsSliceOwnershipContractTests.cs:344-349 —— 5 个分部已整删，条目仍在
["src/DeskBox/Views/OnboardingWindow.Appearance.cs"] = 10,
["src/DeskBox/Views/OnboardingWindow.DesktopOrganization.cs"] = 1,
["src/DeskBox/Views/OnboardingWindow.Features.cs"] = 1,
["src/DeskBox/Views/OnboardingWindow.Hotkey.cs"] = 15,
["src/DeskBox/Views/OnboardingWindow.Storage.cs"] = 5,
// 断言侧（:339-358）只查 actual→expected 方向，无条目存在性校验：
// foreach (actual) { if (!expected.TryGetValue(path, ...)) NEW; else if (count>budget) GREW; }
// 同仓先例（同文件内注释）："QuickCaptureWidgetWindow partials went away with
//  the dead host (ad8febe, DEF-027/016), so their facade budgets are retired too."
```
- **建议修法（最小侵入）**：随下一批卫生提交删除上述 6 条死条目（`ModuleBoundaryContractTests.cs:32` 一条 + `SettingsSliceOwnershipContractTests.cs:344,346-349` 五条；`:345` Completion 条目可顺带按当前树实测值收紧为 0 或删除）；可加一个守卫用例断言「manifest 每个 key 在 src/DeskBox 下存在对应文件」一次性堵住该盲区类别。
- **置信度**：高（文件删除为 git 实证；断言语义为逐行读码；惯例先例为同文件注释原文）。

### A-02 ｜ WidgetManager.MergeWidgetsAsync 是唯一不取消在途 Surface 切换请求的群组拓扑变更入口：合并与成员切换分持两把不相交的锁，UI 线程 await 交错面真实存在 ｜ P3

- **位置**：`src/DeskBox/Services/WidgetManager.Groups.cs:722-927`（`MergeWidgetsAsync` 全方法无 `_widgetGroupSwitchRequests.Cancel`、不取目标 surface 的 switchGate）；对照同文件取消点 `:1315`（RemoveWidgetFromGroupAsync）、`:1506`（DissolveWidgetGroupContainingAsync）、`:1727`（SetWidgetGroupVisibility 隐去分支）、`WidgetManager.cs:2214`（HideWidget）、`WidgetManager.Surfaces.cs:219,229`（重建/按 id 取消）；切换侧 `:961-980`（`Begin` 后仅持 `switchGate = _widgetSurfaceSwitchGates[...]`，:972 `WaitAsync`）、`:1147`（最后一个取消检查点 `ThrowIfCancellationRequested`）、`:1156-1237`（无取消检查的「原子结算段」：首帧等待 :1166、`CommitSurfaceHost` :1188、`CompleteAsync` :1210、capsule 身份迁移 :1184/:1248）
- **触发条件**：同一 surface 上「成员切换在途 × 拓扑合并提交」毫秒~秒级重叠。切换在首帧等待处最长挂 900ms（`WidgetGroupFirstFrameTimeout` :21-22），期间 UI 线程可完整跑完一次 Merge（其 `PromoteGroupToUnifiedSurfaceHostAsync` 含 `SaveCheckedAsync` 磁盘 IO 与窗口创建等多个 await，窗口不小）；真实手势面为「悬停/滚轮切换组内成员的同时，把另一格子拖到该组合并」。
- **影响**：合并侧在切换的结算窗口内 ①`RetireLoadedWindowForGroup` 退休成员窗口（:890-898）、②改写 `mergedGroup.ActiveMemberId`（:827）、③经 `NormalizeCapsuleIdentityForGroup`/`TransferCapsuleIdentity` 重排 capsule order（:874,:920）——切换侧恢复后其原子结算段不设防：`CommitSurfaceHost(group, persistentWindow)` 可能对已被退休/替换身份的 HWND 提交、`group.ActiveMemberId = targetConfig.Id`（:1185）与 `TransferCapsuleIdentity`（:1184）双写合并刚落定的身份（后写者胜）、capsule order 与合并侧交错；若首帧等待因窗口被退休而永不完成则走 900ms 超时回滚（:1170-1181），`transition.Rollback()` 落在已退休窗口上。多数终态可被下次 normalize/导航自愈，最坏为该 surface 注册表身份短暂错挂（与 DEF-123 家族同级的瞬时失配，非数据丢失）。
- **根因机制**：切换的并发防线是「请求协调器取消 + surface switchGate」，而合并是**唯一既不触发前者、也不持有后者**的拓扑变更入口——它只持全局 `_widgetGroupGate`（:737），与切换持有的 per-surface `switchGate` 互不排斥；其余全部拓扑变更者（detach/dissolve/hide/visibility-off/widget 移除/surface 重建）都先 `Cancel` 再动状态，唯独 merge 漏配，属防御网的系统性不对称而非单点笔误。
- **证据**：
```csharp
// MergeWidgetsAsync（:737-927）：仅 await _widgetGroupGate.WaitAsync()，全方法
// grep "_widgetGroupSwitchRequests" 零命中；RetireLoadedWindowForGroup 循环 :890-898
// SwitchWidgetGroupMemberAsync（:961-980）：
WidgetGroupSwitchRequest request = _widgetGroupSwitchRequests.Begin(...);
switchGate = GetWidgetSurfaceSwitchGate(requestedGroup);
await switchGate.WaitAsync(request.CancellationToken);      // 只持 surface 门
// :1147 最后一次取消检查 → 其后 :1156-1237 原子结算段无任何取消/重验检查点
// 全仓取消点清单：Groups.cs:1315,1506,1727 + WidgetManager.cs:2214 +
//                Surfaces.cs:219(CancelAll),229 —— Merge 不在其中
```
- **建议修法（最小侵入）**：在 `MergeWidgetsAsync` 取得 `_widgetGroupGate` 后、改拓扑前，对合并涉及的两侧 surface 各补一行 `_widgetGroupSwitchRequests.Cancel(surfaceId)`（与 :1315/:1506 同型，一行一处）；若求完备可再对目标 surface 的 switchGate 做非阻塞 `Wait(0)` 试持或把取消点下沉进 `PromoteGroupToUnifiedSurfaceHostAsync` 的 beforeRetire 段。切换侧既有「取消检查点 + 结算段不设防」语义无需改动。
- **置信度**：机制高（取消点清单为全仓 grep 实证、双锁不相交与 await 交错面为逐行读码推演）；触发面低（需拖拽合并与组内切换手势毫秒级重叠，且多数终态自愈），故 P3。

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-30 | `OnboardingWindow.Completion.cs` 截断后仅剩 `OnLanguageChanged` 一个方法，但保留了删除前整文件的约 14 个 using（CommunityToolkit.WinUI.Animations、SystemBackdrops、Shapes、WinRT.Interop 等现均未使用）与一个空节注释「Intro Sequence (preserved from original)」。编译无碍，属删除批格式卫生残留；若该分部不再增长可整并回 xaml.cs 或清理 using | `Views/OnboardingWindow.Completion.cs:1-19,31` | 高 |
| O-31 | `GlanceWidgetStore.SaveAsync` 不执行 `MigrateLegacyStoreIfNeededLockedAsync`（Load/Update 均执行）：安装后首个针对某 glance 实例的操作若为 Save，旧 `glance/glance.json` 迁移延迟到下一次 Load/Update，期间旧文件滞留（无数据风险、最终必迁）。补一行或留档均可 | `Services/GlanceWidgetStore.cs:108-123 vs :87-106` | 高（机制）/ 无（后果） |
| O-32 | `scripts/quality/static-baseline.json` 未随 R14 刷新：`async_void_count` 仍为删除前的 249（R14 门禁实测 247、空 catch 同步下降），gate 为「≤ 基线」语义故合法通过——属 slack 而非漂移；建议随下次 `--update-baseline` 收紧，防止删除批的改善被后续新增慢慢吃掉 | `scripts/quality/static-baseline.json:2,5` | 高 |
| O-33 | `WidgetManager.Groups.cs` 的拖拽 dwell 预览状态字段（`_groupDragSourceId/_groupDragTargetId/_groupDragDropReady` :28-31）为 UI 线程专属且定时器为一次性 DispatcherQueueTimer，无跨线程写面——仅留档确认勿与 THR-06 家族混淆；另 `DissolveAllWidgetGroupsAsync`（:95-133）逐个成员单次取 `_widgetGroupGate` 的循环在两次获取之间允许其他变更插队，因每次内层调用自校验、循环条件每次重读列表，语义安全，留档防后续轮次误报 | `Services/WidgetManager.Groups.cs:28-31,95-133` | 高 |

---

## 6. 统计

- **新立案**：2 条 —— P0×0，P1×0，P2×0，**P3×2**（A-01：round-14 删除批遗漏收缩两张契约 manifest，6 条死条目；A-02：MergeWidgetsAsync 不取消在途 Surface 切换，防御网系统性不对称）
- **观察项**：4 条（O-30 ~ O-33）
- **已知模式新位点**：4 行（O-9 家族 ×2、DEF-092 家族 ×1、DEF-070 家族写侧 ×1）
- **round-14 删除批复核（本轮重点）**：DEF-127/128/129 逐文件核验——删除面**自洽**（孤儿键 230 ×12 真零残留、2,667 ×12 parity 抽验通过、删除符号/面板/字段全仓零残余引用、XAML 8 处事件处理器全解析、测试裁剪与 AutoStart 退役声明正确、BrandLogoHost 迁移后 intro 协议闭合）；**唯一实质残余即 A-01 的 6 条 manifest 死条目**（同批对第三张 manifest 的收缩是正确的，惯例在仓内已确立但未贯彻到全部三张表）
- **存量复核**：DEF-016 建议由「待修 P2」改判「已消除」（宿主已删、无修复对象，台账统计节待同步）；DEF-070 家族新增写侧位点（Groups 回滚路径）；O-27/DEF-102 等 7 组锚点再证在位；其余挂账按「基线零变更」继承，无回退
- **盲区盘点结论**：`WidgetManager.Groups.cs`（2,771 行）首次全文审读——回滚快照/门控/首帧超时/请求协调分层完备，除 A-02 的跨锁不对称与 §2 家族位点外零新增；`QuickCaptureStore`/`GlanceWidgetStore` 全文重读零新立案（gate 契约与 GetInvocationList 广播隔离均为正面范式）；`App.Aot*Smoke` 27 装置一致性全绿（引用/分发/manifest 三向闭合）；`WidgetViewModel` 五个未深读分部（~5,100 行）模式扫描除 1 处 O-9 位点外零命中（声明：扫描级覆盖）
- **证伪留档**：5 项（FileService 自带 SHCreateItemFromParsingName 非断链、QuickCaptureStore 门外序列化为显式契约、GlanceWidgetStore 实例缓存使门控足够、Aot*Smoke 装置一致性、OnboardingExperienceTests 切片终点替换非弱化）
- **总体结论**：连续第十轮 P0/P1 = 0。round-14 大删除批（约 5,500 行 + 230 键 ×12）经全面残余核查质量良好：五张消费面（代码、XAML、12 语言键、测试断言、脚本）中四张完全干净，唯一系统性残余是数据驱动型契约 manifest 的收缩遗漏（A-01，纯测试基线卫生）；本轮盲区深读亦未发现高危位点，核心服务与数据面维持历史最高加固水位，收敛趋势延续（R11→R15 立案 3→5→2→3→2，全部 P3）。
