# R14-B 界面交互与工程面专项审查报告（round-14）

> 审查基线 commit `435b1a1a`（分支 `wip/fix-bug`，工作树干净）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 与 round-09~13 的差异化声明

round-09~13 已精读并连续复核的面（WidgetToolDialogWindow、Glance/Weather/Todo 订阅链、SettingsWindow Closed 链、SettingsSections 8 分部、QuickCaptureSurfaceContent 核心区、FileSurfaceContent 14 个非 AOT 分部、MusicWidgetContent、FileSurfaceContent.StackPopover、WidgetShellContentHost、ContentWidgetWindow 三个未覆盖分部、WidgetWindowBase.Backdrop、DesktopOrganizationWindow、installer 全部 .iss、publish-aot-retail.ps1、cleanup 脚本、static_gate.py 等）本轮仅做定点/差异复核。**本轮把深读预算投向历轮从未覆盖的盲区**：

- **全读（本轮首次）**：
  - `Views/OnboardingWindow` 全家族（历轮仅点名过 Hotkey 的 DEF-035/036/038 位点与 R13 的 IntroAnimations）：`OnboardingWindow.xaml.cs`（1136 行）+ `.Storage.cs`（289）+ `.TaskFlow.cs`（489）+ `.Features.cs`（43）+ `.Completion.cs`（105）+ `.Appearance.cs`（206）+ `.Hotkey.cs`（462）全文，并对照 `OnboardingWindow.xaml`（1627 行）逐面板核对了可见性控制与调用图——**本轮最大发现（B-01/B-02）**；
  - `Controls/WidgetContents/WeatherWidgetContent.xaml.cs`（523 行，历轮只读过 Adapter 与 VM，正文从未读）+ `WeatherWidgetContentAdapter.cs`（142 行复核）+ `WeatherWidgetViewModel.RefreshAsync` 全路径（含内部 catch/finally/版本守卫核验）；
  - `Controls/WidgetContents/TodoWidgetContent` 剩余分部：`Menus.cs`（605）、`DetailNotesAndSteps.cs`（407）、`EditingAndUndo.cs`（610）、`ListInteraction.cs`（378）、`MasterDetail.cs`（269）、`Attachments.cs`（97）、`TodoWidgetContentAdapter.cs`（257）；
  - `scripts/build-rust-native.ps1`（364 行全文——交叉架构回退时的冻结契约 token 校验、静态 CRT 导入拒绝、PE 契约探针）。
- **定点复核（本轮重点）**：`Services/FolderWatcherService.cs` 全文 1087 行重读——DEF-125 修复（`7c18ed5f`）逐行核验 + `_watchGeneration` 全部赋值点反查（唯一递增点 `Stop()` :617）+ StartAsync 四个 await 窗口的守卫覆盖全排列推演；`SearchPopupWindow.xaml.cs` 构造区（:60-199）与 OnWindowClosed 全量清理链（:4508-4553）——DEF-126 修复核验。
- **tests 抽查（正确性方向）**：`FolderRefreshPolicyTests.cs`（222 行全文——快照分类/ShellMove 判定/健康快照/桌面公共桌面批量四组断言方向逐一核对）；AOT 契约测试族结构抽样（406 个测试文件的契约覆盖面盘点）；死方法外部引用反查（tests/ 零引用）。
- **round-13 修复批复核（本轮主目标）**：`7c18ed5f`（DEF-125/126）全量 diff + 当前树逐路径核验（详见 §3）；基线 `7387efeb..435b1a1a` 代码 diff 经 git 双向核实**仅此 2 个源文件 +13/+11 行**，其余历轮复核结论按「文件零变更」直接沿用。

### 1.2 机械校验（python 只读脚本，stdin 直读，未写任何仓库文件）

- **12 语言键位 parity**：en-US **2897** 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2897 × 12 = 35764 键全对齐）；
- **占位符 arity**（各语言与 en-US 同键 `{n}` 索引集合）→ **0 失配**；
- **本地化值孤立花括号** → **0 命中**；
- **.NET 非法日期格式字母**（宽松口径扫描）→ 9 处命中全部为 `Weather.Wind.S`（南/南/南——en-US 值为单字母 "S" 的风向标签，非日期格式）误报，**0 真实违规**（DEF-039 修复保持）；
- **Format 调用点 arity**（两形态 `Format("Key",…)` / `Format(T("Key"),…)`，正确口径：定位 `Format(` 开括号配对后按顶层逗号计值实参）→ 扫描 **230 处 → 0 失配**（DEF-116/122 修复保持）。**留档**：本轮扫描器先后踩了 R13 报告已记录的同一 off-by-one 坑两次（「括号配对起点取错 token」与「key 后分隔逗号误计入实参」分别产生 101/230 条假阳性），修正口径后归零——与 R13「留档防复发」预言一致，进一步佐证该扫描器需要固化版本而非每轮重写；
- **XAML StaticResource/ThemeResource**：144 个 .xaml（含 AOT smoke 专用），195 个唯一引用键，52 个仓外定义——逐一核对全部为 WinUI/XamlControlsResources 平台资源（`AccentButtonStyle`、`TextFillColorPrimaryBrush`、`SystemColor*` 等），**无失效引用**；
- **XAML 事件处理器绑定**：40+ 事件名抽取 → **0 处缺失处理器**；
- **模式族计数**：`TryEnqueue(async` **25 处**（与 R12/R13 持平）；`async void` **249 处**（与 static-baseline.json 基线 249 持平）；`SetIsTranslationEnabled(true)` **14 处**（与 R13 全量盘点逐一吻合）；`CompositionTarget.Rendering` 7 对全部配对（`WidgetCompactAnimationCoordinator` 二选一订阅 + 单退订语义沿用 R12 核验结论）；
- **native 契约**：`git diff 77f2b4b4..HEAD -- native/` 仅 `README.md` 2 行 → **ABI 零漂移**（十导出/ABI 2/掩码 511/panic=abort 结论沿用）；
- **scripts/installer 漂移**：`7387efeb..435b1a1a` 零变更；`77f2b4b4..HEAD` 仅 static_gate 与 DEF-119 已知变更（沿用 R13 结论）；脚本群 Remove-Item 面抽查无新增危险删除点。

### 1.3 覆盖率声明

全读约 6.4k 行（OnboardingWindow 家族 ~2.9k + Weather 正文/VM ~0.9k + Todo 剩余分部 ~2.2k + FileSurfaceContent 构造区 0.24k + build-rust-native.ps1 0.36k）+ 定点精读约 1.6k 行（FolderWatcherService 全文重读、SearchPopupWindow 两区、tests 抽样）。仍未见逐行全读：`FileSurfaceContent.xaml.cs` 正文其余约 2.6k 行（历轮分区覆盖 + 本轮构造区补读）、`WidgetShell.xaml.cs` 正文、`App.xaml.cs`、`publish-aot-audit.ps1`（10498 行，R12 起持续留档观察项）、`run-aot-managed-ui-smoke.ps1`（6967 行）。基线 `7387efeb..435b1a1a` 代码 diff 仅 `7c18ed5f` 的 2 源文件（git 双向核实），其余历轮复核结论按「文件零变更」直接沿用。

---

## 2. 已知模式新位点（并入既有编号，不新立案）

1. **DEF-078 家族 +1：Todo 随记 Markdown 任务勾选链（async void 裸逃逸）**。`Controls/WidgetContents/TodoWidgetContent.DetailNotesAndSteps.cs:372-383` `DetailNotesView_TaskToggleRequested`（async void，无 try/catch）裸 `await ViewModel.UpdateNotesAsync(item.Id, updated)`；而 `TodoWidgetViewModel.DetailAndAttachments.cs:233-268` 的 `UpdateNotesAsync` 在 `SaveAsync` 失败时**先回滚 Notes/UpdatedAt 再 `throw;`**（:254-267，事务性回滚语义）——同文件 `SaveActiveNotesAsync` :270-282 对同一方法包了 try/catch（`App.Log` + `DetailNotesSaveFailure` 失败横幅），说明作者明知该调用可抛，Markdown 任务勾选入口是唯一漏防点。失败时异常直穿 async void → 全局兜底仅记日志 → 勾选静默无效且无失败提示（对照 notes 编辑路径有失败横幅）。修法同族：`UpdateNotesAsync` 返回 false/抛异常时复用 `DetailNotesSaveFailure` 横幅或 `ShowUndoToast` 反馈。
2. **格式卫生（不占用缺陷编号，随批清理候选）**：`TodoWidgetContent.ListInteraction.cs:248-253` `ApplyTodoItemTooltips` 为**空体存根**（全方法仅 `_ = localization;`），仍被 :232（`TodoItem_PropertyChanged`）与 `DragDrop.cs:586` 两处调用——tooltip 功能移除后的残留，每次条目 IsCompleted/ColorMarker 变更与拖拽经过都空转一趟，建议删除方法与两处调用。
3. **缩进异常（与 `Localized.cs:88`、`StackPopover.cs:2280` 同类）+4**：`TodoWidgetContent.EditingAndUndo.cs:71`（`CustomDueDateSaveButton_Click` 多 4 空格）、`:196`（`BeginItemEdit`）、`ListInteraction.cs:255`（`TodoItemContent_DoubleTapped`）、`:261`（`TodoItemContent_Tapped`）——同文件内独一处、合并残留型格式瑕疵。

---

## 3. 存量复核（round-13 修复批 + 范围内挂账条目现状）

本基线相对 round-13 收口（`7387efeb`）代码变更仅 `7c18ed5f` 的 2 源文件（+13/+11 行，git 双向核实），下列逐条为当前树证据；其余挂账条目所在文件零变更，round-13 复核结论直接沿用。

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-125（FolderWatcher 成功分支代际复核）** | **✅ 修复复核正确（含守卫不变量全排列推演）** | `FolderWatcherService.cs:285-297`：`TryStartQueryWatcherAsync` await 之后、健康结算（SetHealth/清 `_reconnectPath`）之前补 `lock { if (_isDisposed \|\| startGeneration != _watchGeneration) return; }`，与失败分支 :302-308 对称。**守卫不变量核验**：`_watchGeneration` 全仓唯一递增点为 `Stop()` :617（Dispose 亦经 Stop），故「代际移动」必伴随一次 Stop 的字段清理——被取代调用在 :283-285 已启动的 desktopIni/legacy watcher 与可能已入 `_queryWatcher` 的 query 均由取代方的 Stop()（`ConfigureFolderWatchersAsync` 调用点 :223-224 或 tick 路径 StartAsync :239）确定性释放，无泄漏窗口。**StartAsync 四 await 窗口守卫覆盖全排列**：解析 await（:226）→ 守卫 :231-237；probe await（:254）→ :255-261；**query await（:285）→ :291-297（本修复）**；失败 probe await（:301）→ :302-308——其后至 `App.LogVerbose`（:326-328）全程同步无让出点，成功分支结算窗口闭合。无回归。 |
| **DEF-126（搜索弹窗子类对称卸载）** | **✅ 修复复核正确** | `SearchPopupWindow.xaml.cs:4513-4522`：`OnWindowClosed` 顶部（12 组退订之前）`if (_isPopupCloseWatcherInstalled) { _ = Win32Helper.RemoveWindowSubclass(_hwnd, _popupCloseWatcherProc, PopupCloseWatcherSubclassId); _isPopupCloseWatcherInstalled = false; }`——守卫读取使 `:80` 字段恢复可读；`Closed` 事件单次触发（:167 接线）无重入面；`Win32Helper.RemoveWindowSubclass`（`Platform/Win32Helper.cs:1634-1636`）与安装调用同源同签名；hwnd 在 Closed 时机仍有效。与仓内其余 9 处子类化卸载范式一致，NC_DESTROY 兜底省略理由已在 R13 方案留档。无回归。 |
| **DEF-036 / DEF-038（Onboarding 启动开关时序族，台账位点 `OnboardingWindow.Hotkey.cs:317/:325`）** | **⚠️ 位点已死代码化（现状改判建议，机制结论不变）** | 台账所引 `Step4StartupToggle_Toggled` 现位于 `OnboardingWindow.Hotkey.cs:352-420`，但它仅由 `RefreshStartupToggleFromSystem`（:339/:341/:373/:375，带 Toggled 摘挂/回挂保护）与 XAML `Step4StartupToggle`（**位于永久 `Visibility="Collapsed"` 的旧版 Step4Panel，`OnboardingWindow.xaml:1253`**）触发——详见 §4 B-01：旧版五步流程的 Setup 链（`SetupStep4` :23）全仓零调用，触发面已消失。修复代码本身无回退（代际守卫 :322/:327/:362 原样在位），建议台账为该两条补注「触发面位于 onboarding 旧版死面板」以纠正后续轮次的复核靶向。 |
| **测试时序纪律（DEF-067/114 类）** | **修复保持** | `FolderRefreshPolicyTests` 八组用例全部为文件系统因果断言（创建/删除后即时分类，无时间窗）；`UnavailableParentSnapshot_NeverClaimsThatChildWasDeleted` 负向断言方向正确。无新窗口型弱断言。 |
| **R13 观察项 1-6（CapsuleArrangement 重入丢弃、publish-aot-audit 未全读、DEF-120 进程级取舍、孤儿键、static_gate 基准取向等）** | **维持** | 相关文件本基线零变更。 |

---

## 4. 新发现问题清单

### B-01｜OnboardingWindow 旧版五步流程整体死代码（~1,500 行跨 8 文件），台账两条 P2 位点落入其中
- **优先级**：P3（代码卫生 + 质量台账靶向失准；无生产行为错误）
- **位置**（核心证据链）：
  - `Views/OnboardingWindow.xaml.cs:313-322` —— 活代码的步骤面板映射 `GetStepPanel` 只返回 `TaskStep3Panel/TaskStep4Panel/TaskStep2Panel/TaskStep5Panel`（`StepCount = 4`）；`SetupStep`（:511-538）只调 `SetupTaskStep3/4/2/5`；
  - 旧版面板全部 `Visibility="Collapsed"` 且全仓零代码引用：`OnboardingWindow.xaml:847`（Step1Panel）、`:971`（Step2Panel）、`:1028`（Step3Panel）、`:1143`（Step4Panel）、`:1397`（Step5Panel）、以及活家族内的 `:174`（TaskStep1Panel，同样零引用）；
  - 旧版 Setup 链全仓零调用：`OnboardingWindow.Features.cs:21`（SetupStep2Features）、`OnboardingWindow.Appearance.cs:21`（SetupStep3，连带 BuildThemeSelector/BuildAccentSelector/BuildMaterialSelector/UpdateAppearancePreview/Step3CapsuleToggle_Toggled 整文件失效）、`OnboardingWindow.Hotkey.cs:23`（SetupStep4，连带 Step4HotkeyToggle_Toggled/Step4SearchHotkeyToggle_Toggled/RefreshSearchHotkeyText/Step4HotkeyChange_Click/StartKeycapPulse 等）、`OnboardingWindow.Storage.cs:23`（SetupStep4Storage，连带 ChangeStoragePathToAsync 迁移链/Step4PinToggle_Toggled）、`OnboardingWindow.Completion.cs:21`（SetupStep5）、`OnboardingWindow.xaml.cs:560`（StartStep1CardAnimation）、`:608`（StartSearchDemoAnimation + RunSearchDemoAsync）、`OnboardingWindow.TaskFlow.cs:29`（空方法 SetupTaskStep1）。
- **触发条件**：无运行时触发——纯静态死代码面；触发的是「审查与维护成本」。
- **影响**：①约 **770 行 XAML**（六个永久 Collapsed 面板）+ 约 **750 行 C#**（七个分部内全部或大部分方法）永不可达，是 R9 起孤儿键扫描反复报告的 `Onboarding.Step1~5.*`/`Onboarding.Scene.*`/`Onboarding.Task.*` 簇（443/573/172 候选）的实体来源——死 UI 一次清理可同时消解最大孤儿键簇；②**台账靶向失准**：DEF-036/038（P2，机制已修）的位点 `Step4StartupToggle_Toggled` 及 DEF-087 记录的 `OnboardingWindow.Hotkey.cs:94` 同步握手调用点都在这条死链上，后续轮次若按台账行号复核会持续复核死代码；③`RootGrid.KeyDown → OnHotkeyKeyDown`（活接线，`xaml.cs:99`）因 `_isRecordingHotkey` 唯一置位点在死链 `BeginHotkeyRecordingAsync` 内而恒为空转入口。
- **根因机制**：引导流程从「旧版 Step1-5」重构为「TaskStep 任务流」（`SetupTaskStep2/3/4/5` + `GetStepPanel` 新映射）时，旧面板/旧 Setup/旧动画整体退役但未删除；`Step1Panel` 等仅保留 `Visibility="Collapsed"` 的就地废弃态。
- **证据**：
  ```csharp
  // OnboardingWindow.xaml.cs:315-322（活映射不含任何旧版面板）
  private FrameworkElement GetStepPanel(int index) => index switch {
      0 => TaskStep3Panel, 1 => TaskStep4Panel, 2 => TaskStep2Panel, 3 => TaskStep5Panel, _ => TaskStep3Panel };
  // grep 实证：SetupStep3/SetupStep4/SetupStep5/SetupStep2Features/StartStep1CardAnimation/
  // StartSearchDemoAnimation/SetupTaskStep1 全仓（src+tests）调用点 = 0；
  // Step1Panel~Step5Panel、TaskStep1Panel 在 .cs 中引用 = 0（仅 obj/ 生成物）。
  ```
- **建议修法（最小侵入）**：随卫生批一次性删除六个 Collapsed 面板（xaml :174-260、:845-1523）与上述死方法/死分部（Appearance.cs 整文件；Features/Completion/Storage/Hotkey 中的死成员；`UpdateFooterState`/`ApplyResponsiveLayout` 中对 Step3Panel/Step3PreviewHost 的死布局段）；同步删除 `Onboarding.Step1~5.*`/`Onboarding.Scene.*` 孤儿键簇（12 语言）；台账为 DEF-036/038/087 的 onboarding 位点补注「死代码」标记。删除后需跑 `scripts/publish-aot-audit.ps1`（XAML 变更触发 WMC1510 审计漂移）。
- **置信度**：高（调用图与可见性控制均可机械复核；grep 三方交叉——src 排除 obj、tests、XAML 事件签名不匹配排除）。

### B-02｜品牌标志（BrandLogoHost）嵌在永久 Collapsed 的死面板内：intro→主内容交接协议操作不可见元素，Logo 全程不显示且 shine 动画空转
- **优先级**：P3（外观级缺失 + 常驻空转动画；引导流程功能不受损）
- **位置**：`Views/OnboardingWindow.xaml:851-885`（`BrandLogoHost`/`BrandLogo`/`BrandLogoShine`/`BrandLogoShineTransform` 位于 `Step1Panel`（:847，`Visibility="Collapsed"`，B-01 死面板）内部）；活协议侧 `OnboardingWindow.IntroAnimations.cs:39-46`（`PlayIntroSequence` 置 `BrandLogoHost.Opacity = 0` 并在交接序列 :121-128 以 `brandFadeIn` 任务把它的透明度动画回 1）、`:212-224`（`DismissIntro` 复位 `BrandLogoHost.Opacity = 1`）、`:253-277`（`StartBrandLogoShine`，`RepeatBehavior.Forever`，由 `xaml.cs:108` 在 RootGrid.Loaded 启动、仅 `Closed` :140 停止）。
- **触发条件**：每次打开 OnboardingWindow（首启引导或 `RestartIntro`）。
- **影响**：①品牌标志（`Assets/deskbox.svg` 全仓唯一展示位）在引导窗口全程**不可见**——intro 覆盖层用的是程序化绘制的三层色块 mark（`CreateDeskBoxMark`，非真实 Logo），覆盖层淡出后本应接棒的真实 Logo 位于 Collapsed 父面板内，`brandFadeIn`/`DismissIntro` 的透明度操作全部落在不可见元素上；②`StartBrandLogoShine` 的 1450ms 循环 shine storyboard 自 Loaded 起常驻运行至窗口关闭（唯一停止点），目标元素不可见，纯空转；③若未来有人把 Step1Panel 删除（B-01 的修法），这三个字段引用将直接编译失败——死面板与活协议事实上互相咬合，**必须先把 BrandLogoHost 迁出才能安全执行 B-01 的清理**。
- **根因机制**：重构为 TaskStep 任务流时，品牌标志随旧版 Step1Panel 一起被折叠，但 intro 动画协议（PlayIntroSequence/DismissIntro/StartBrandLogoShine）的引用仍指向旧面板内的元素——「活协议 ↔ 死容器」的错位。
- **证据**：
  ```xml
  <!-- OnboardingWindow.xaml:847-851：活代码引用的 Logo 在死面板内 -->
  <StackPanel x:Name="Step1Panel" Visibility="Collapsed" ...>
      <!-- Brand logo -->
      <Grid x:Name="BrandLogoHost" Width="36" Height="36" ...>
  <!-- grep 实证：Step1Panel 在全部 .cs 中引用 = 0；BrandLogoHost 仅被
       IntroAnimations.cs（活）与 Closed 清理（:140）引用 -->
  ```
- **建议修法（最小侵入）**：把 `BrandLogoHost` 的 Grid（:851-885）整体迁出 Step1Panel、置于 StepContainer 顶层或 TitleBarHost 旁的常驻容器（或在 TaskStep 面板外侧固定位置），其余 B-01 清理即可安全进行；若产品上确认引导页不需要真实 Logo，则反向最小修法是同步删除 `StartBrandLogoShine` 启动点与 intro 交接序列中对 BrandLogoHost/BrandLogoShine 的三处引用，避免常驻空转。
- **置信度**：高（结构关系直接可验；「不可见」由父面板 Collapsed + 全仓无 `.Visibility` 恢复调用双重证实）。

---

## 5. 观察项（不够立案标准）

1. **`CompleteOnboardingAsync` 未禁用 SkipButton**（`OnboardingWindow.xaml.cs:355-372`）：末步点击 Next 后 `NextButton`/`BackButton`/六个 toggle 均同步禁用，但 `SkipButton_Click`（:350-353）仍可点击并并发进入第二次 `CompleteOnboardingAsync`（`await _featureWidgetSelectionUpdateTask` 链 + 幂等设置写入 + 第二次 `Close()`）。设置写入幂等、二次 Close 走同一次窗口销毁（Closed 清理幂等：CTS 置 null、hook Dispose 幂等），仅理论路径；对照 ：363-364 的既有禁用清单补一行 `SkipButton.IsEnabled = false` 即可闭合。
2. **`TaskStep4ToggleWidgets_Click` 缺 try/finally**（`OnboardingWindow.TaskFlow.cs:393-398`）：`IsEnabled=false → await ToggleWidgetsForOnboardingAsync() → IsEnabled=true` 无 finally——异常时按钮滞留禁用。对照同文件 :114-149/:151-184 两个 toggle 处理器均有 try/finally；`ToggleTrayWidgetsAsync`（`App.Tray.cs:502-506`）当前无抛出路径，理论性较强。
3. **`Step4PinToggle_Toggled` / `RefreshStep4StorageAssessment` 的迁移对话框 XamlRoot 重叠面**：属于 B-01 死代码内的理论缺陷（同 XamlRoot 已有对话框时 `ShowAsync` 抛异常），删除死代码即消解，不单独立案（R12 对 `SearchSettingsSection` 同型的观察口径一致）。
4. **`TodoWidgetContent.Menus.cs` Recurrence 与 Snooze 子菜单共用同一图标字形 `\uE823`**（:161 与 :175）——疑似复制粘贴未换字形，纯视觉一致性。
5. **round-09~13 各轮观察项全部维持**（`Exclusion.None` 防御缺口、Updater `RestartApp` 不确认存活、cleanup 脚本不清理空目录、`Localized.cs:88` 缩进、`WidgetToolDialogWindow` Enter 假设、`StackPopover.cs:2280` 同行双语句、依赖包下载无哈希校验、音量滑条同值回写、`WaitForDeskBoxDependencies` 固定轮询、SearchPopup 入场守卫理论超窗、DEF-120 记忆化进程级取舍、`WidgetManager.CapsuleArrangement` 重入丢弃、`publish-aot-audit.ps1` 未全读）——相关文件本基线零变更（唯一触碰的 ShortcutDrop/FolderWatcher/SearchPopup 均与上述观察无涉）。
6. **孤儿键**：本轮未重扫（方法依赖候选集，R9~R12 分别报 443/573/172/未扫）；本轮 B-01 已定位其中最大簇（`Onboarding.Step1~5.*`/`Onboarding.Scene.*`/部分 `Onboarding.Task.*`）的实体来源为六个死面板——清理死 XAML 与孤儿键应作为同一批次执行，避免键集 parity 校验窗口期出现假缺键。
7. **`WeatherWidgetContent.RefreshButton_Click` 的 `_ = RefreshAsync(...)`**（`WeatherWidgetContent.xaml.cs:293`）不并入 DEF-078：`WeatherWidgetViewModel.RefreshAsync` 全路径内部 try/catch/finally（`RefreshAndLayout.cs:76-144`，含 `_refreshRequestVersion` 代际守卫与 pending 合并），异常不可能逃逸——正面结论留档。
8. **扫描器口径留档**：Format arity 扫描的正确口径为「定位 `Format(` 开括号 → 配对闭括号 → 内部顶层逗号数即实参数（key 本身不计）」；本轮验证了 R13 留档的 off-by-one 坑可被两种不同实现路径复现，建议将本轮修正版口径固化进 `scripts/quality/static_gate.py` 或契约测试。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：2（B-01 Onboarding 旧版流程死代码 ~1,500 行；B-02 BrandLogoHost 活协议操作不可见元素）
- **总立案数**：2（连续第九轮 P0/P1 = 0；立案数 1 → 2，全部为 onboarding 盲区首次深读所得的卫生类问题）
- **已知模式新位点**：DEF-078 家族 +1（Todo Markdown 任务勾选链，含 `UpdateNotesAsync` 回滚后 rethrow 与调用点防护不对称的新论证）；格式卫生 2 组（空体存根 ApplyTodoItemTooltips、缩进异常 ×4）不占编号
- **存量复核**：round-13 修复批 **DEF-125/126 逐行复核全部正确落地、无回归**（含 `_watchGeneration` 唯一递增点反查、StartAsync 四 await 守卫覆盖全排列、被取代调用启动产物的确定性释放推演、RemoveWindowSubclass 对称性核验）；**重要现状改判建议**：DEF-036/038/087 的 onboarding 位点已随旧版流程死代码化（触发面消失，修复代码本身无回退）；无其他改判、无已修复项回退。
- **正面结论**：12 语言 2897 键 parity / 占位符 arity / 孤立花括号 / Format 调用点 arity（230 处 0 失配）机械校验全绿（日期字母 9 命中均为风向标签误报）；XAML 195 资源键 0 失效、事件绑定 0 缺失处理器；Rust ABI 零漂移（native/ 自 round-08 仅 README 2 行）；scripts/installer 自 round-13 基线零漂移；`build-rust-native.ps1` 全文审读——交叉架构回退的冻结 token 校验、静态 CRT 导入拒绝、PE 契约双路验证设计完整；`FolderRefreshPolicyTests` 断言方向全部正确；WeatherWidgetContent/Adapter/RefreshAsync 订阅-退订-异常防护链完整；TodoWidgetContent 七个剩余分部订阅/生命周期/撤销协议纪律良好；`FolderWatcherService` 全文重读与 `SearchPopupWindow` 关键区核验零新增问题。
