# R16-B 界面交互与工程面专项审查报告（round-16）

> 审查基线 commit `2d64b5c0`（分支 `wip/fix-bug`，工作树干净）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 与 round-09~15 的差异化声明

round-09~15 已精读并连续复核的面（SettingsWindow 十个主分部与 Closed 链、SettingsSections 8 分部、WidgetToolDialogWindow、Glance/Weather/Todo/Music/Search/QuickCapture/FileSurface 内容族已点名分部、SearchPopupWindow 六区、WidgetShellContentHost、ContentWidgetWindow 分部、WidgetWindowBase 分部、FolderWatcherService 全文、OnboardingWindow 活家族、installer 全部 .iss、publish-aot-retail.ps1、build-rust-native.ps1、cleanup 脚本、static_gate.py、update-settings-search-catalog.ps1、native-pe-contract.ps1 等）本轮仅做定点/差异复核。**本轮预算按任务书投向 round-15 diff（`76ef0b28`）复核与剩余盲区**：

- **round-15 修复批复核（本轮主目标）**：
  - **DEF-131（MergeWidgetsAsync 双侧 Cancel）**：`WidgetManager.Groups.cs` 合并路径（:722-941）与切换路径（:943-1298）全量重读 + `WidgetGroupSwitchRequestCoordinator.cs`（166 行全文）语义核验 + 全仓 7 处取消点（:768/:773/:1329/:1520/:1741、WidgetManager.cs:2214、Surfaces.cs:219/:229）逐一对照——详见 §3；
  - **DEF-130（契约 manifest 退役）**：两张 manifest 当前态逐行读 + **tests 全目录「`src/...` 字面量 → 磁盘存在性」穷举重扫**（0 陈旧，修复主体干净）+ `OnboardingWindow.Completion.cs` 存活性核实——**发现 B-01**（修复越界删掉了被方案显式钉死保留的 `Completion.cs = 5` 条目，四处文档与代码矛盾）；
  - **manifest 预算真实性抽验（测试有效性方向）**：以 python 按测试同款正则对当前树复算 `ModuleBoundaryContractTests` 两张 manifest 的逐文件计数——Interop 0 GREW、Destructive 0 GREW、3 条合法收缩（FileService.TransferProgress 4→3、QuickCaptureService 8→6、WidgetManager.FeatureWidgets 1→0）；全部「不在 manifest 的命中文件」逐一甄别为豁免命名空间成员（`src/DeskBox/Platform/*` 12 文件属 P/Invoke 目标域、`SyncOutboxStore.cs` 声明 `namespace DeskBox.Core.Persistence` 属破坏性操作所属域）——**棘轮记忆与代码事实一致，测试断言方向正确**。
- **盲区全读（本轮首次，B 报告从未点名且历轮未被分部名覆盖的文件）**：
  - `Controls/WidgetGroupTitleSwitcher.xaml.cs`（1299 行）+ `.Tabs.cs`（672 行）——组标题切换器的主体与标签页分部（R9 只读过 `.Interaction.cs` 的计时器/反馈区）：标签同步/重排协议、拖拽分离长按协议、悬停切换代际、picker 生命周期、Unloaded 清理链；
  - `Controls/NativeShellFileDragProvider.cs`（368 行）——Shell 原生数据对象附加器（ModuleBoundary manifest 在管文件）：SHParseDisplayName/ILFindLastID/SHCreateDataObject 的 PIDL 生命周期、IUnknown 裸 vtable 槽位、IDataObjectProvider.SetDataObject 互操作；
  - `Controls/FileItemSurface.xaml.cs`（548 行）——文件格子项表面（Mode 双布局懒加载、虚拟化复用重置、LayoutContext 订阅 Loaded/Unloaded 配对）；
  - `Views/SettingsWindow` 六个短分部（R15 §5.9 持续留档项，本轮闭合）：`.AotDeepSmoke.cs`（336）、`.SectionElements.cs`（208）、`.DeferredSections.cs`（157）、`.AotSmoke.cs`（43）、`.Startup.cs`（47）、`.DesktopOrganization.cs`（10）；
  - `Controls/WidgetContents/QuickCaptureClipboardColorEditor.cs`（121）、`Controls/WidgetInlineEditor.xaml.cs`（156）、`ViewModels/WidgetStackItem.cs`（191）、`ViewModels/QuickCaptureWidgetViewModel.ViewScheduling.cs`（109）、`ViewModels/MusicWidgetViewModel.Lifecycle.cs`（128）；
  - **tests 全读**：`ModuleBoundaryContractTests.cs`（418 行，棘轮语义：manifest 缺条目 → NEW 违规；计数超预算 → GREW 违规）。
- **盲区抽读**：`AttachmentTileStrip.xaml.cs`（60-190，Loaded/DataContextChanged 链）、`WidgetFeedbackPresenter.xaml.cs`（95-165）、`DesktopOrganizationTaskView.Sources.cs`（44-132）与 `.Actions.cs`（1-175 + RunPlanAsync catch/finally）、`TodoAttachmentViewModel.EnsureThumbnailAsync`（55-88）、`IconHelper.GetIconAsync`（114-160）、`WidgetGroupSwitchRequestCoordinator.cs` 全文。
- **installer/scripts**：自 round-08 基线（`77f2b4b4`）漂移复核——`scripts/ installer/` 仅 4 个已知文件（DEF-119 两个 Dependencies.iss + static_gate 两文件），run-aot-* 17 个脚本、publish-aot-audit.ps1 等自此零变更（历轮抽样结论继续有效）；本轮新增抽读 `run-aot-shortcut-smoke.ps1` 头部（场景参数/环境变量族/SHA256 指纹工具）；`static-baseline.json` 基线值与 `static_gate.py` 比较语义（`cur > base` 上限棘轮，非等值）核验。
- **Rust ABI 契约**：`git diff 77f2b4b4..HEAD -- native/` 仅 README 2 行；`lib.rs` 10 个 `no_mangle` 导出、`DESKBOX_NATIVE_ABI_VERSION = 2`、workspace 双 profile `panic = "abort"`——零漂移。

### 1.2 覆盖率声明

全读约 5.1k 行（WidgetGroupTitleSwitcher 两分部 1.97k + NativeShellFileDragProvider 0.37k + FileItemSurface 0.55k + SettingsWindow 六短分部 0.8k + 编辑器/VM/协调器/测试 ~1.1k）+ 抽读约 1k 行 + 机械校验若干轮（临时 python，stdin/系统临时目录直读，未写任何仓库文件）。**Controls 目录「历轮 B 报告从未点名」清单（37 文件）本轮处理完毕**：15 个全读、9 个抽读、8 个为 AOT smoke 分部（契约测试三向一致性由 R15 验证）、5 个为纯记录/模板选择器/图标类低风险文件（按模式扫描零命中留档）。仍未见逐行全读：`GlanceWidgetViewModel.cs`（1132）、`QuickCaptureWidgetViewModel.Operations.cs`（682）、`TodoWidgetViewModel.ItemOperations.cs`（630）、`WidgetViewModel.Navigation.cs`（525）、`DesktopOrganizationPreviewCard.xaml.cs`（532）、`WidgetTitleIcon.xaml.cs`（480）及 publish-aot-audit.ps1 / run-aot-managed-ui-smoke.ps1 正文——列为持续观察项。

### 1.3 机械校验（python 只读脚本，未写任何仓库文件）

- **12 语言键位 parity**：en-US **2667** 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2667 × 12 = 32004 键全对齐；round-14 删键后的现键集）；
- **占位符 arity**（各语言与 en-US 同键 `{n}` 索引集合）→ **0 失配**；en-US 索引空洞 → **0**；
- **代码引用键 → 键集存在性**（`T()/Format()/GetString("…")`、`Localized.Key=`、`*Key=` 属性五形态定向扫描）→ **0 真实缺失**（85 命中逐一甄别为 `{Binding …}`/`{x:Bind …}` 路径、十六进制色值、`x:Class` 与属性路径误报）；
- **Format 调用点 arity**（修正口径：括号配对起点锚定 `Format(` 的 `(` 本体——本轮实现先后踩了 R13/R14 留档的两类 off-by-one 坑：「`\s*` 把锚点吃出括号外」与「键段分隔逗号计入实参」，修正后）→ 字面量/T 包裹形态 **227 处 → 0 失配**；37 处数据驱动形态（键非字面量）为 R15 留档的扫描器盲区，其中已知失配位点 `SettingsWindow.Maintenance.cs:493-504`（DEF-116 家族）人工维持原判；
- **XAML**：**189** 个唯一 StaticResource/ThemeResource 引用键，50 个仓外定义逐一核对全部为 WinUI/XamlControlsResources 平台资源（与 R13~R15 名单一致）→ **0 失效**；**727** 处事件绑定 → **0 处缺失处理器**；
- **tests 陈旧路径引用穷举**：tests 全部 .cs 中 `"src/..."` 字面量 → 磁盘存在性 → **0 陈旧**（DEF-130 修复后；R15 报 6 处，本轮清零）；
- **模式族计数**：`async void` **247** 处（R14 删除批后，≤ static-baseline 上限 249）；`SetContent(` 剪贴板写点 8 处（与基线一致）；`static_gate.py` 基线为「只增即罚」上限语义核验成立。

---

## 2. 已知模式新位点（并入既有编号，不新立案）

1. **DEF-078 家族 +2 位点（低水位，方向为外观级静默失败）**：`src/DeskBox/Controls/AttachmentTileStrip.xaml.cs:83-94` `AttachmentTile_Loaded` 与 `AttachmentTile_DataContextChanged`（均 async void、无 try/catch）→ `TodoAttachmentViewModel.EnsureThumbnailAsync`（`ViewModels/TodoAttachmentViewModel.cs:68-87`，**只有 try/finally 没有 catch**）→ `IconHelper.GetIconAsync`。缩略图解码/Shell 取流抛异常时直穿 async void → 全局兜底仅记日志 → 格子停留占位图标。缓解因素：`GetIconAsync` 内部多层回退取 null 兜底（`IconHelper.cs:114-160`），`_thumbnailLoadAttempted` 的 finally 复位使下次呈现仍可重试，失败方向纯外观（无数据、无交互阻断），故列家族低水位不升级。修法同族：`EnsureThumbnailAsync` 补 catch（或两个 tile 处理器包 try/catch）+ `App.Log`。
2. **R12 观察项 3 同族 +1**：`src/DeskBox/Controls/DesktopOrganizationTaskView.Actions.cs:130` `DoneButton.Style = … (Style)Application.Current.Resources["AccentButtonStyle"]`——平台键直接索引器访问（键当前恒存在），与 R12 记录的 `DesktopOrganizationSettingsSection.xaml.cs:117/141/562/817` 同型，并入该观察项清单。

---

## 3. 存量复核（round-15 修复批 + 范围内挂账条目现状）

本基线相对 round-15 收口（`08bdd98e`）代码变更仅 `76ef0b28` 一个提交（3 文件：WidgetManager.Groups.cs +14、两个契约测试 −7+5，git 双向核实），下列逐条为当前树证据；其余挂账条目所在文件零变更，round-15 复核结论直接沿用。

| 条目 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-131（MergeWidgetsAsync 双侧 Cancel）** | **✅ 修复复核正确（含协调器语义与全拓扑入口对称性核验）** | ①`WidgetManager.Groups.cs:762-774`：同组早退校验（:755-760）之后、`preserveRaisedLayer` 解析与 chrome 校验之前，对 `sourceGroup?.SurfaceId` 与 `targetGroup?.SurfaceId` 各补 `_widgetGroupSwitchRequests.Cancel`——standalone 一侧（组为 null）正确跳过；②取消点至拓扑变更（:846-853）之间**全程同步无让出点**（chrome 解析/成员数校验均为同步段），无「取消后新切换插队」窗口；③协调器语义核验（`WidgetGroupSwitchRequestCoordinator.cs:85-100`）：`Cancel` 从字典摘除该 surface 的当前请求并 Cancel/Dispose 其 CTS，回调在锁外触发（:33-35 注释的约定成立）；切换侧三个取消感知点全部在位——`switchGate.WaitAsync(request.CancellationToken)`（:986）、gate 取得后 `ThrowIfCancellationRequested`（:994）、`SwitchContentWidgetGroupMemberInPlaceAsync` 的 OCE 过滤 catch（:1076-1083）与准备段取消检查（:1161）——合并取消后，在途切换于最近一个感知点退出，结算段（:1198 起）不设防为 R15 方案明示的既有设计；④与同族入口对称性：`RemoveWidgetFromGroupAsync:1324-1330`、`DissolveWidgetGroupContainingAsync:1515-1521` 均为「先取消后进 `_widgetGroupGate`」，合并为「进 gate 后、动拓扑前」取消——两位置等价（切换不持 `_widgetGroupGate`，协调器自有锁），且合并版把取消放在校验之后反而少取消「注定被拒的合并」……实际上两族入口都在自身校验前/中取消，语义一致；全仓 7 处取消点清单核对无第五类拓扑入口遗漏。**无回归。** |
| **DEF-130（契约 manifest 死条目退役）——主体** | **✅ 六条死条目退役干净** | 两张 manifest 当前态 0 陈旧条目（§1.3 穷举重扫）；`ModuleBoundaryContractTests.cs:32` 注释「ShellDataObjectBuilder went away with the dead drop-delegation path (DEF-127)」与同文件注释风格对齐；`SettingsSliceOwnershipContractTests.cs:344-348` 注释援引 :349-350 下方的 QuickCaptureWidgetWindow 退役先例格式。 |
| **DEF-130——`Completion.cs = 5`「保留」裁定** | **⚠️ 未按裁定执行（立案 B-01）** | R15 整改方案 W1 明文「**不得触碰 ：345（Completion.cs=5，合法 slack）**」（`rectify/R15-remediation-plan.md:12`）、整改报告称「`:345` 的 `Completion.cs = 5` 按审查指令保留」（`rectify/R15-remediation-report.md:12`）、提交信息称「the live Completion.cs=5 slack stays, pinned by review」、台账 DEF-130 行称「`Completion.cs=5` 合法 slack 按审查钉死保留」——**但 `76ef0b28` 的 diff 实际删除了该行**（hunk 中 `-        ["src/DeskBox/Views/OnboardingWindow.Completion.cs"] = 5,`），当前 manifest :344-348 仅存注释，注释还声称该预算「survives as legal slack」。详见 §4 B-01。 |
| **DEF-078 / DEF-097 / ANI-06 / DEF-080 / DEF-116 / EXC-06 / 孤儿键 / EVT-02 / MEM-01 / MEM-02 / DEF-048~053 / THR-06 等** | **维持** | `76ef0b28` 未触碰任何挂账位点文件；`WidgetManager.Groups.cs` 为 R15 首次全文审读文件，本轮合并/切换区重读亦无新增发现。 |
| **R15 观察项 1-10** | **维持** | 相关文件本基线零变更；观察项 9（六个 SettingsWindow 短分部未全读）随本轮闭合（§1.1，零立案）。 |

---

## 4. 新发现问题清单

### B-01｜DEF-130 修复越界删除被方案显式钉死保留的 `Completion.cs = 5` 棘轮条目，代码注释/提交信息/整改报告/台账四处与实际 manifest 状态矛盾
- **优先级**：P3（质量治理一致性问题：修复自身引入了它所要消除的那类「棘轮记忆失真」；运行时与测试行为均无害——棘轮只罚增不罚减，删除后测试恒绿）
- **位置**：
  - 实际代码：`tests/DeskBox.Tests/SettingsSliceOwnershipContractTests.cs:344-348`（现仅为注释块；被删条目原为 ：345 `["src/DeskBox/Views/OnboardingWindow.Completion.cs"] = 5`）；引入删除的提交 `76ef0b28`；
  - 矛盾文档：`docs/quality/rectify/R15-remediation-plan.md:12`（「不得触碰 ：345」）、`docs/quality/rectify/R15-remediation-report.md:12`（「按审查指令保留」）、提交 `76ef0b28` message（"the live Completion.cs=5 slack stays, pinned by review"）、`docs/quality/defect-ledger.md:469`（「合法 slack 按审查钉死保留」）。
- **触发条件**：无运行时触发。触发的是三类后果：①注释失真——:347 注释声称「`OnboardingWindow.Completion.cs (5) survives as legal slack`」而 manifest 中并无该条目；②未来行为与文档预期相反——`ModuleBoundaryContractTests.cs`/`SettingsSliceOwnershipContractTests.cs:376-381` 的棘轮语义是「manifest 缺条目 + 计数 > 0 → NEW 违规」：`Completion.cs` 当前 facade 访问为 0 所以恒绿，但**未来任何人给该活文件（OnLanguageChanged，37 行）添加一次合法 facade 访问，CI 将以 NEW 违规失败**，而维护者按注释/台账预期应有 5 的合法预算，造成困惑；③质量链路失真——「方案钉死 → 报告声明执行了钉死 → 提交信息声明执行了钉死 → 台账记录执行了钉死」四环全部与代码相反，DEF-130 立案理由（棘轮记忆失真、文档误导后续轮次复核）在修复提交里原样复发。
- **根因机制**：R15 整改实施时把方案 W1 删除清单按「:344-349 连续六行」整体执行（六行在文件中物理相邻），未按方案 v2 的钉死指令跳过 :345；注释文本又按「Completion 保留」的裁定书写，形成「注释描述的意图」与「diff 实际效果」的错位；后续报告与台账按注释/意图而非 diff 落笔，未做行级反查。
- **证据**：
  ```diff
  # git show 76ef0b28 -- tests/DeskBox.Tests/SettingsSliceOwnershipContractTests.cs（节选）
  -        ["src/DeskBox/Views/OnboardingWindow.Appearance.cs"] = 10,
  -        ["src/DeskBox/Views/OnboardingWindow.Completion.cs"] = 5,   // ← 方案明令不得触碰
  -        ["src/DeskBox/Views/OnboardingWindow.DesktopOrganization.cs"] = 1,
  ...
  +        // OnboardingWindow.Completion.cs (5) survives as legal slack: the file
  +        // is live (OnLanguageChanged) with zero facade access today.
  ```
  当前树 `SettingsSliceOwnershipContractTests.cs:344-350`：注释块后直接是 `TaskFlow.cs = 5`（:349）与 `xaml.cs = 4`（:350），无 Completion 条目；`OnboardingWindow.Completion.cs` 在盘（37 行，`OnLanguageChanged` 活成员，facade 访问 0）——与 R15 方案 ：11 的现状描述一致，证明「保留」裁定本身正确，只是未被执行。
- **建议修法（最小侵入，二选一）**：
  - **推荐**：恢复 `["src/DeskBox/Views/OnboardingWindow.Completion.cs"] = 5,` 一行（置于 :348 注释块与 ：349 之间），使代码回到方案/报告/提交信息/台账共同声明的状态——保留裁定成立（该文件存活且是合法 slack），注释无需改动；
  - 或反向统一：接受删除（棘轮更严、方向安全），但须同步改写 ：347-348 注释（删去「survives as legal slack」表述，改为「budget line retired; the file is live with zero facade access — a future access must extend the manifest consciously」）、在台账 DEF-130 行补记「实际执行与裁定不符」勘误。两案均零行为风险；推荐案改动最小且让四处文档全部恢复为真。
- **置信度**：高（diff 与当前树状态均可机械复核；方案/报告原文逐句引用）。

---

## 5. 观察项（不够立案标准）

1. **`static-baseline.json` 上限未随 R14 缩减收紧**（`scripts/quality/static-baseline.json:2,6`：`async_void_count=249`、`empty_catch_count=245`，当前树实测 247/241）：`static_gate.py:263` 为 `cur > base` 上限棘轮，上限偏松意味着静默回潮 +2 async void/+4 空 catch 不触发门禁。属「缩减后未收紧棘轮」的同类卫生项（与 DEF-130 哲学同源但影响为负向余量而非失真条目），随下次基线刷新顺手收紧即可。另 R15 整改报告「五项与基线持平」措辞与实际（247<249 系收缩）不符，纯措辞。
2. **`WidgetGroupTitleSwitcher` 两分部正面结论**：`SynchronizeTabs` 的 Remove+Insert 重排在 `index == count` 边界恒为合法 append（预检 `index >= Count` 保证）；`CommitPendingTabSelection` 的每次调用均由前置的 `_pendingTabSelection` 新赋值驱动，无陈旧值提交路径；标签拖拽完成链（`TabsView_TabDragCompleted`，async void）有 try/catch/finally + 快照同组校验 + Esc/按键取消三重防护；悬停切换 80ms 延迟用代际 + CTS 双守卫；长按分离协议的 CTS/Storyboard/指针捕获在 Cancel/Completed/Unloaded 三路清理；构造器 Unloaded 统一取消四类交互态。全文件唯二 `.Replace("{0}", …)` 手工格式化位点（:385/:1012-1021）所用键 `Widget.Group.Switching`/`Widget.Group.Position` 均含且仅含对应 `{n}` 字面占位符（en-US 实值核验），行为正确。
3. **`NativeShellFileDragProvider` 互操作正面结论 + 一处留档**：IUnknown 裸 vtable 槽位 QueryInterface=0/Release=2 正确；`ILFindLastID` 返回的内部指针只随完整 PIDL 释放、不单独 CoTaskMemFree（与 shell32 契约一致）；SHParseDisplayName 结果双重校验（hr>=0 且 PIDL 非空）；TryAttach finally 双 Release 对称；CreateShellDataObject 失败路径先释放 dataObject 再抛。**留档**：`IDataObjectProvider`（`3D25F6D6-…`）为 WinUI 未公开互操作接口，`SetDataObject` 位于 vtable 槽 4（:300-307）无法从公开文档独立核验——该路径是 .lnk 拖出的活路径且有实机验证历史（1.5.5 上游行为），按「实测先于文档」采信。
4. **`FileItemSurface` / `QuickCaptureClipboardColorEditor` / `WidgetInlineEditor` / `WidgetStackItem` / `QuickCaptureWidgetViewModel.ViewScheduling` / `MusicWidgetViewModel.Lifecycle` 正面结论**：双布局懒加载在 MeasureOverride 前完成（避免列表模式为每项创建闲置图标树）、虚拟化复用重置链完整；颜色编辑器对比度校验用 `InvariantCulture` 格式化比值（DEF-080 纪律范本）且全链 catch-all；内联编辑器为纯转发薄壳；ViewScheduling 计时器均先 Stop 后按需 Start 且 `_isDisposed` 复检；Music 生命周期以三代际计数（媒体属性/时间线/回放）+ `_hiddenMediaStateDirty` 位 + 揭示后新鲜度判定收敛隐藏期刷新。
5. **`DesktopOrganizationTaskView` Actions/Sources 分部正面结论**：`ExecuteButton_Click` 中 `await RunPlanAsync(plan)` 虽在自身 try 之外，但 `RunPlanAsync` 内部 catch-all（OCE/磁盘不足/待恢复/Exception 四段）+ finally 复位 15 个控件态，无逃逸路径；Recover/Retry 两入口 try/catch/finally 完整。
6. **SettingsWindow 六短分部闭合正面结论**：`SectionElements` 的 AOT 反射回退重包裹模式（`MarshalInspectable<T>.FromAbi`，PasswordBox 无反射元数据问题）注释与实现一致；`DeferredSections.EnsureSettingsSectionCreated` 的模板初始化（`ProcessBindings` 显式喂 VM）与一次性 Loaded 自退订正确；`Startup` 的 async void 处理器委托给全 try/catch 的 Task 方法（本仓 async void 纪律的正面样板）；AotSmoke/AotDeepSmoke 的 10s 有界轮询与快照 record 结构无资源面。
7. **未覆盖留档（下轮候选）**：`GlanceWidgetViewModel.cs`（1132，历轮仅读过其 SettingsSection 与 Content 生命周期区）、`QuickCaptureWidgetViewModel.Operations.cs`（682）、`TodoWidgetViewModel.ItemOperations.cs`（630）、`WidgetViewModel.Navigation.cs`（525）、`DesktopOrganizationPreviewCard.xaml.cs`（532）、`WidgetTitleIcon.xaml.cs`（480）、`FileItemDragPackage.cs`（293）、`PreferredDropEffectFilterDataObject.cs`（239，其 CreateInterfacePointer/ReleaseInterfacePointer 被 NativeShellFileDragProvider 消费）——本轮模式扫描零危险命中；`publish-aot-audit.ps1`（10498 行）与 `run-aot-managed-ui-smoke.ps1`（6967 行）正文维持 R12 起留档。
8. **round-09~15 各轮观察项全部维持**（`Exclusion.None` 防御缺口、Updater `RestartApp` 不确认存活、cleanup 脚本不清理空目录、`Localized.cs:88` 缩进、`WidgetToolDialogWindow` Enter 假设、`StackPopover.cs:2280` 同行双语句、依赖包下载无哈希校验、音量滑条同值回写、`WaitForDeskBoxDependencies` 固定轮询、SearchPopup 入场守卫理论超窗、DEF-120 记忆化进程级取舍、`WidgetManager.CapsuleArrangement` 重入丢弃、publish-aot-audit 未全读、Format-arity 扫描器第四形态固化建议）——相关文件本基线零变更（`76ef0b28` 未触及）。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：1（B-01：DEF-130 修复越界删除被钉死保留的 `Completion.cs = 5` 棘轮条目，四处文档与 manifest 实态矛盾）
- **总立案数**：1（连续第十一轮 P0/P1 = 0；立案数 2 → 1）
- **已知模式新位点**：DEF-078 家族 +2 位点（AttachmentTileStrip 缩略图链，外观级低水位）；R12 观察项 3 同族 +1（Application.Resources 索引器）
- **存量复核**：round-15 修复批逐项复核——**DEF-131 正确落地**（协调器语义、三个取消感知点、与 Remove/Dissolve 的对称性、取消点至拓扑变更零让出点均核验通过）；**DEF-130 主体正确**（6 死条目退役、tests 陈旧路径穷举清零）但 **Completion.cs=5 保留裁定未被执行**（立案 B-01）；ModuleBoundary 两张 manifest 预算对当前树复算 0 GREW（3 条合法收缩），棘轮记忆与代码事实一致；无已修复项回退。
- **正面结论**：12 语言 2667 键 ×12 parity / 占位符 arity / 索引空洞三项机械校验全绿；代码引用键 0 真实缺失（85 命中全为误报）；Format 调用点 227 处 0 失配（37 处数据驱动形态留档）；XAML 189 资源键 0 失效、727 事件绑定 0 缺失处理器；Rust ABI 十导出 / ABI 2 / 掩码 511 / 双 profile panic=abort 零漂移；scripts/installer 自 round-08 仅 4 个已知变更文件；SettingsWindow 六个持续留档短分部本轮闭合零立案；WidgetGroupTitleSwitcher、NativeShellFileDragProvider、FileItemSurface、三个编辑器/VM 分部、DesktopOrganizationTaskView Actions/Sources 全读/抽读零立案。**以挑剔视角验证 round-15 修复批的结论：DEF-131 闭合干净；DEF-130 主体干净、唯一残余是修复自身越界引发的文档-代码失真（B-01）。**
