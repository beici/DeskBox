# R15-B 界面交互与工程面专项审查报告（round-15）

> 审查基线 commit `08bdd98e`（分支 `wip/fix-bug`，工作树干净）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 与 round-09~14 的差异化声明

round-09~14 已精读并连续复核的面（WidgetToolDialogWindow、Glance/Weather/Todo 订阅链、SettingsWindow Closed 链与 CloudBackup/Navigation/StorageAndUpdates/HotkeyAndAppearance 区、SettingsSections 8 分部、QuickCaptureSurfaceContent 核心区、FileSurfaceContent 全部 14 非 AOT 分部、MusicWidgetContent、SearchPopupWindow 关键六区、WidgetShellContentHost、ContentWidgetWindow 分部、WidgetWindowBase 分部、FolderWatcherService 全文、OnboardingWindow 旧版家族、installer 全部 .iss、publish-aot-retail.ps1、cleanup 脚本、static_gate.py、build-rust-native.ps1 等）本轮仅做定点/差异复核。**本轮预算按任务书投向 round-14 大删除批（`fb96a004`，38 文件 +639/−5542）的正确性验证与剩余盲区**：

- **round-14 删除面核验（本轮主目标）**：
  - `OnboardingWindow.xaml`（911 行）全文重读 + XML 良构性解析（ElementTree 全树配对）——删除七个死面板后的嵌套平衡、63 个 `x:Name` 唯一性、5 处事件绑定处理器存在性逐项核验；
  - `OnboardingWindow.xaml.cs`（957）+ `.TaskFlow.cs`（485）+ `.IntroAnimations.cs`（343）+ `.Completion.cs`（37）四份活分部全文重读——`GetStepPanel`/`SetupStep`/`StepCount=4` 一致性、intro 交接协议、 Closed 清理链、子类卸载对称性、`SizeChanged`→`ApplyResponsiveLayout` 死段删除后的完整性；
  - `IntroAnimations ↔ BrandLogoHost` 迁移后引用链（DEF-129 修复）：新位置（RootGrid Row 0 居中、`IsHitTestVisible=False`）、`Opacity` 生命周期（PlayIntroSequence 置 0 → brandFadeIn 动画 → DismissIntro 复位 1）、shine 动画目标变换存在性、与 `IntroOverlay`（声明在后、整窗覆盖）的层序关系逐项核验；
  - **孤儿键 230 ×12 删除的反向残留**：从 `git diff 435b1a1a..fb96a004 -- Strings/en-US.json` 提取 230 个被删键，对 src/DeskBox 全部 738 个 .cs/.xaml（排除 obj/bin）做子串级反查 + tests 全目录反查——零残留；en-US 中误留零；
  - DEF-127 删除面（ShellDropDelegator/ShellDataObjectBuilder/SHCreateItemFromParsingName）残留反查——src/tests 零残留（`WmReservedHotkeyCapture=0x8443` 在 SettingsWindow.xaml.cs:57 为活代码的 Settings 侧录制器，非残留）；
  - round-14 裁剪的 4 个契约测试文件（FolderPickerModernization/GlobalHotkeySafety/OnboardingExperience/StartupRegistration）逐行读——切片标记 `Step1Panel→FooterNav` 的语义变化、全部 Contains/DoesNotContain 断言对当前树的成立性逐条核对；
  - **tests 全目录「`src/...` 路径引用 → 磁盘存在性」穷举扫描**（发现 B-01，见 §4）。
- **机械校验**（见 §1.3）。
- **盲区全读（本轮首次）**：
  - `Controls/WidgetContents/SearchWidgetContent.xaml.cs`（349 行全文）+ `.xaml` 事件绑定——搜索格子宿主历轮只点名过 DEF-100 死链与 Adapter，正文从未读；
  - `Views/SettingsWindow.Maintenance.cs`（548）、`SettingsWindow.Feedback.cs`（541）、`SettingsWindow.LocalizationAndWidgets.cs`（445）、`SettingsWindow.DataTools.cs`（425）——SettingsWindow 16 分部中历轮从未覆盖的四个（至此除 AotSmoke/AotDeepSmoke/SectionElements/DeferredSections/Startup/DesktopOrganization 六个短分部外全部覆盖）；
  - `scripts/update-settings-search-catalog.ps1`（101 行全文，生成器逻辑）+ `SettingsSearchCatalog.cs` 生成物与当前 `SettingsWindow.xaml` 的键存在性比对；`scripts/native-pe-contract.ps1`（275 行，PE 解析辅助的边界检查抽样）；`run-aot-managed-ui-smoke.ps1` 头部结构（场景清单与环境变量族与 App.Aot*.cs 对应性）。
- **差异复核**：`WeatherWidgetContent.xaml.cs`（R14 已全文审读，本基线 `435b1a1a..08bdd98e` 无该文件变更——R14 结论直接沿用）；`git diff 435b1a1a..08bdd98e` 全量 38 文件清单逐一核对（38 文件 = 台账 DEF-127/128/129 的声明范围 + 台账/rectify 文档，无计划外代码变更）。

### 1.2 覆盖率声明

全读约 3.4k 行（SearchWidgetContent 0.35k + SettingsWindow 四分部 1.96k + OnboardingWindow 活家族重读 1.82k 中的协议关键区 + 契约测试 5 文件 0.6k + 脚本 0.4k）+ 机械校验若干轮（临时 python，stdin/系统临时目录直读，未写任何仓库文件）。仍未见逐行全读：`App.xaml.cs` 正文其余部分、`publish-aot-audit.ps1`（10498 行）、`run-aot-managed-ui-smoke.ps1`（6967 行，本轮仅头部场景结构）、`run-aot-*` 其余 12 个 smoke 脚本、`SettingsWindow` 六个短分部——沿用历轮扫描结论并列为持续观察。基线 `435b1a1a..08bdd98e` 代码 diff 仅 `fb96a004` 一个提交（git 双向核实），其余历轮复核结论按「文件零变更」直接沿用。

### 1.3 机械校验（python 只读脚本，未写任何仓库文件）

- **12 语言键位 parity**：en-US **2667** 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2667 × 12 = 32004 键全对齐；round-14 删 230 键后键集）；
- **占位符 arity**（各语言与 en-US 同键 `{n}` 索引集合）→ **0 失配**；en-US 索引空洞（{0} 起连续）→ **0**；本地化值孤立花括号 → **0**；.NET 非法日期字母（aaaa/гггг 等模式）→ **0**（DEF-039 修复保持）；
- **孤儿键 230 ×12 反向残留**：src 738 文件 + tests 全目录子串级反查 → **0 残留**（R14 删除批干净闭合，最大孤儿键簇实体已消解）；
- **代码引用键 → 键集存在性**：全仓 `svc:Localized.Key=` → **0 缺失**；`T()/Format()/GetString("字面量")` → **0 缺失**（2 处命中为 round-10 已展开验证的动态前缀拼接 `DesktopOrganization.Exclusion.` / `…RetentionHelp.`，非缺失）；`*Key=` 属性形态 → **0 缺失**；
- **Format 调用点 arity**（`Format("Key",…)` / `Format(T("Key"),…)` 两形态，括号配对后顶层逗号计数）→ 扫描 **261 处 → 0 失配**（扫描器形态对「键先选入局部变量再 Format」的数据驱动位点天然盲，该形态本轮人工核出 1 处失配，见 §2.1）；
- **OnboardingWindow.xaml 结构**：XML 解析良构（删除后嵌套平衡）；63 个 `x:Name` 无重复；5 处事件绑定处理器全部存在；`StaticResource` 键全部定义于本文件 `Grid.Resources`（21-126）；
- **全仓 XAML 事件绑定**（37 类事件名）→ **435 处 → 0 处缺失处理器**；**全仓 StaticResource/ThemeResource** → 189 个唯一引用键，50 个仓外定义逐一核对全部为 WinUI/XamlControlsResources 平台资源（其中 `TextFillColorPrimary`/`SolidBackgroundFillColorBase`/`SystemColor*Color` 等非 Brush 后缀键经使用点抽查均为合法的 Color 语义用法）——**无失效引用，round-14 删除未引入悬空资源**；
- **SettingsSearchCatalog.cs 生成物**：204 条目，HeaderKey/DescriptionKey 对 en-US **0 缺失**；DEF-072 补键（`Settings.QuickCapture.RecordColors.Reset.Title`）在键集与目录中均在位；7 组跨 Section 同 HeaderKey 为合法的多节注册；
- **tests 陈旧路径引用穷举**：tests 全部 .cs 中 `"src/..."` 字面量 → 磁盘存在性 → **6 条真实陈旧**（B-01，两个测试文件的 manifest 条目）；其余命中逐一甄别为路径重映射表（TestPaths.cs 的 Abstractions 迁移映射）、有意的不存在断言（`Assert.False(File.Exists…)`：AotStage4D2 的 FileOperationHelper、WidgetForeground 的 TextEdge 两文件）、模板占位符（`{locale}`/`{fileName}`）与字符串续行假阳性——无漏判。

---

## 2. 已知模式新位点（并入既有编号，不新立案）

1. **DEF-116 家族 +1（Format 调用点实参 ↔ 键占位符失配，数据驱动键选择形态）**：`src/DeskBox/Views/SettingsWindow.Maintenance.cs:493-504` `CheckAttachmentHealthButton_Click`——三元把键名选入局部变量 `key`（`Partial`/`Healthy`/`Issues`），随后单一 `Format(key, 5 实参)` 服务三键：en-US `Settings.AttachmentHealth.Partial` 有 {0}~{4}（匹配）、`…Healthy` 仅 {0}（**吞 4 个实参**）、`…Issues` 有 {0}~{3}（**吞第 5 个实参**）。`string.Format` 静默吞多余实参，当前无用户可见破损，机制与已闭环的 DEF-116 完全同型。**扫描器盲区留档**：R10~R15 五轮的 arity 扫描三形态（字面量/T 包裹/三元 T 内联）都匹配「Format 调用的第一个实参位」，本位点三元在两条语句之前、首个实参是标识符——未来扫描器应增加「局部变量键选择 → 回溯赋值点」的第四形态，或直接把该位点人工核对结论固化。修法同族：按分支分别 Format（零风险）或给 Healthy/Issues 补占位符（需 12 语言同步，不推荐）。置信度：高（键值与实参数均为当前树直接验证）。
2. **DEF-078 家族 +3 位点（async void 静默失效——破坏性维护操作无失败反馈）**：`src/DeskBox/Views/SettingsWindow.DataTools.cs:39-76` `ClearQuickCaptureDataButton_Click`、`:78-110` `ClearQuickCaptureRecentButton_Click`、`:112-139` `CleanupQuickCaptureImageCacheButton_Click`——三者均为 async void 且无 try/catch，而底层 `QuickCaptureService.ClearAsync/ClearRecentAsync/CleanupUnusedImageCacheAsync`（`Services/QuickCaptureService.cs:1076-1100/:1101-1125/:1174-1182`）只有 **try/finally 没有 catch**，`SaveCoreAsync`（磁盘写入）或缓存清理的 IO 异常会直穿 async void → 全局兜底仅记日志 → 用户点击「清除随记数据/清除最近/清理图片缓存」**静默无反应**（对照同文件 Maintenance.cs 的导出/恢复/快照族全部有 catch + 失败对话框；且这是数据破坏性操作，失败方向比 DEF-078 既有条目重）。修法同族：包 try/catch + `ShowInfoDialogAsync` 失败反馈。并入 DEF-078 挂账清单。置信度：高（调用点与服务层均当前树验证）。
3. **DEF-078 家族 +2 位点（理论性）**：`src/DeskBox/Views/OnboardingWindow.TaskFlow.cs:110-145` `TaskStep3QuickAccessToggle_Toggled` 与 `:147-180` `TaskStep3DesktopShortcutToggle_Toggled`——async void、try/finally 无 catch；`ExplorerQuickAccessHelper.RunShellStaAsync` 在 STA 线程动作抛出时 `TrySetException`（`Helpers/ExplorerQuickAccessHelper.cs:342-352`），但 C#/Rust 两条内部路径均 catch-all 返回 false（`:421-459`、`:150-178`），`ManagedStorageDesktopShortcutService.CreateAsync/RemoveAsync` 亦 catch-all（`:163-168/:200-205`）——异常逃逸面接近纯理论；残余语义点：异常路径下 toggle 的 `IsOn` 不像失败结果路径那样回滚。并入 DEF-078 清单（低水位），不单独立案。
4. **DEF-080 家族 +4 位点（culture 敏感时间戳，纯显示级）**：`src/DeskBox/Views/SettingsWindow.Maintenance.cs:248`（`BackupCreatedAtUtc.ToLocalTime().ToString("g")` 恢复确认对话框）、`:407`（快照标题 `:g`）、`:419`（快照摘要 `ToString("g")`）、`SettingsWindow.Feedback.cs:470`（我的反馈卡片 `ToString("d")`）——ar-SA 区域下呈阿拉伯-印度数字/回历。与台账既有显示级位点同水位，并入 DEF-080 观察面。

---

## 3. 存量复核（round-14 修复批 + 范围内挂账条目现状）

本基线相对 round-14 收口（`435b1a1a`）代码变更仅 `fb96a004` 一个提交（38 文件 +639/−5542，git 双向核实），下列逐条为当前树证据；其余挂账条目所在文件零变更，round-14 复核结论直接沿用。

| 条目 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-128（Onboarding 旧版流程删除）——XAML 结构完整性** | **✅ 删除面干净（本轮重点核验对象，正面结论）** | ①`OnboardingWindow.xaml` ElementTree 解析良构，无未闭合/错配标签；63 个 `x:Name` 无重复、无悬空（不再被 .cs 引用但保留 x:Name 的六个——`BrandLogo`/`BrandLogoShine`/`ContentScrollViewer`/`FooterAcrylicSurface`/`TaskStep2MenuPreview`/`TaskStep2OpenTrayMenuButton`——均为视觉子元素或被契约测试按名断言消费（OnboardingExperienceTests.cs:289/294、FrostedActionSurfaceContractTests.cs:12），非死名）；②5 处事件绑定（TaskStep4OpenTrayMenu_Click/TaskStep4ToggleWidgets_Click/Skip/Back/Next）处理器全部在活分部中；③`svc:Localized.Key` 引用键全部在 2667 键集内；④被删 XAML 元素（Step1Panel~Step5Panel、Step3PreviewHost、Step5SearchDemo 等）在 src 无任何残留引用（仅 obj/ 陈旧生成物，非源码事实）；⑤四个活分部（xaml.cs/TaskFlow/IntroAnimations/Completion）无对已删字段（`_keycapPulseStoryboard`/`_hotkeyDemoCts`/`_searchDemoCts`/`_isRecordingHotkey`/`_hotkeyRecordingHook`/`WmReservedHotkeyCapture`）的残留引用。 |
| **DEF-128 删除批——孤儿键 230 ×12** | **✅ 干净闭合** | 反向残留 0（§1.3）；键集 2667×12 parity 全绿；最大孤儿键簇（`Onboarding.Step1~5.*`/`Onboarding.Scene.*`）实体已随面板删除，R9~R14 反复报告的孤儿键观察面实质收窄。 |
| **DEF-129（BrandLogoHost 迁移）——intro 交接协议** | **✅ 修复复核正确（含协议全路径推演）** | ①新位置 `OnboardingWindow.xaml:160-199`：RootGrid Row 0 居中、`IsHitTestVisible=False`（不干扰 `SetTitleBar(TitleBarHost)` 的拖拽区）、`ms-appx:///Assets/deskbox.svg` 资产在盘；②`PlayIntroSequence`（IntroAnimations.cs:39 置 Opacity=0 → :46 复位变换）→ 交接序列 :121-128 `brandFadeIn` 与 step/footer 同批 WhenAll → `DismissIntro` :218 复位 Opacity=1——三条活引用全部指向迁移后的可见元素，「活协议 ↔ 死容器」错位消除；③`StartBrandLogoShine`（:253-277）目标 `BrandLogoShineTransform` 存在（XAML :194），1450ms Forever 循环自 Loaded（xaml.cs:98）至 Closed（:130）停止——元素可见后不再是空转；④层序：`IntroOverlay` 声明于 BrandLogoHost 之后（XAML :872）整窗覆盖，intro 期间 logo 淡入发生在覆盖层之下、覆盖层 :138 淡出时 logo 已就位——视觉次序自洽；⑤`Grid.Row="0"`（Auto 行，TitleBarHost 同行）负 margin 外溢与 Grid 默认不裁剪下无遮挡问题。唯一细微处：logo 淡入的 400ms 全程在不清透的覆盖层之下完成（观察项 5，不立案）。 |
| **DEF-128 删除批——TaskStep 活流完整性** | **✅ 完整** | `StepCount=4`（xaml.cs:270）↔ `GetStepPanel` 四映射（:272-279）↔ `SetupStep` 四 case（:475-489）↔ XAML 四面板（TaskStep2/3/4/5Panel）一一对应；四个 step 专属 ambient 动画（:520-671）目标元素全部存在于 XAML；`UpdateFooterState` 的 `_hasCompletedFilePractice`/`_hasCompletedVisibilityPractice` 分支（:771-778）与 TaskFlow 字段一致；`_storageEntryStateRefreshGeneration` 的递增/守卫/失败早退链（TaskFlow.cs:56-108）自洽且 Closed 递增（xaml.cs:127）；`RefreshTaskStep3StorageEntryStateAsync` 的双 8s `WaitAsync` 超时与 catch 均在位。 |
| **DEF-128 删除批——契约测试裁剪正确性** | **✅ 裁剪后断言全部对当前树成立** | ①`OnboardingExperienceTests`：切片标记改 `TaskStep2Panel..FooterNav` 后恰覆盖四个活 Task 面板（XAML :215→:809），三处 activeFlow 断言（:77/:110-111/:284-296）逐一在切片内命中；`Tag="Todo" Toggled=…` 等精确串在 XAML :713 等处逐字匹配；②`FolderPickerModernizationContractTests`：manifest 去 `OnboardingWindow.Storage.cs` 后总数 7 = 现存五文件实际计数（App.Tray 1 + JumpList 1 + Glance 1 + Maintenance 3 + StorageAndUpdates 1）✓；③`GlobalHotkeySafetyContractTests`/`StartupRegistrationContractTests`：死链切片删除后 Settings 侧活契约保留完整。**但**裁剪面不完整——B-01（两个测试文件的 manifest 遗留 6 条死文件条目）。 |
| **DEF-127（ShellDropDelegator 死子系统删除）** | **✅ 干净闭合 + 1 处测试 manifest 遗留（并入 B-01）** | src/tests 零残留（§1.1）；`Shell32NativeMethods.cs` 仅删 `SHCreateItemFromParsingName` 并同步注释，`SHObjectProperties` 等活入口未动；`ModuleBoundaryContractTests.cs:32` 的 manifest 条目未随文件删除退役（B-01）。 |
| **DEF-036 / DEF-038 / DEF-087（onboarding 位点已死代码化，R14 补注）** | **✅ 补注成立** | 台账三条的 R14 补注（位点随旧版引导流删除）与当前树一致：`OnboardingWindow.Hotkey.cs` 整文件已删，`0x8443` 常量的活副本在 SettingsWindow.xaml.cs:57（Settings 侧异步录制器）；StartupRegistrationContractTests 的注释改判与 DEF-128 呼应。台账历史行无需再改。 |
| **DEF-055 / DEF-078 / DEF-097 / ANI-06 / DEF-080 / EXC-06 / 孤儿键 / DEF-090 等** | **维持（本轮补位点见 §2）** | 相关位点文件除 `fb96a004` 触碰者外零变更；`fb96a004` 未触碰任何挂账位点文件（Maintenance/Feedback/DataTools/SearchWidgetContent 均非其 diff 范围），round-14 复核结论直接沿用；`WmReservedHotkeyCapture` 删除使 DEF-087 家族的 onboarding 位点彻底消失（正向）。 |

---

## 4. 新发现问题清单

### B-01｜round-14 删除批遗留：两个契约测试的 ratchet manifest 共 6 条死文件条目未随删除退役，「只减不增」棘轮的记忆失真
- **优先级**：P3（代码卫生 + 质量工具失真；无生产行为影响）
- **位置**：
  - `tests/DeskBox.Tests/SettingsSliceOwnershipContractTests.cs:344`（`OnboardingWindow.Appearance.cs` = 10）、`:346`（`DesktopOrganization.cs` = 1）、`:347`（`Features.cs` = 1）、`:348`（`Hotkey.cs` = 15）、`:349`（`Storage.cs` = 5）——五个文件已被 round-14（DEF-128）整文件删除；
  - `tests/DeskBox.Tests/ModuleBoundaryContractTests.cs:32`（`ShellDataObjectBuilder.cs` = 5）——该文件已被 round-14（DEF-127，-232 行）删除。
- **触发条件**：无运行时触发——两个测试均以**磁盘实际文件**驱动断言循环（`ProductionSource()` 枚举现存 .cs），死条目永不匹配，测试恒绿；触发的是审查与维护面。
- **影响**：①两个 manifest 的自我定位都是「棘轮的记忆」（ModuleBoundaryContractTests.cs:16-22「exact violation manifests pin today's offenders file-by-file…the manifest is the ratchet's memory」；SettingsSliceOwnership 的测试名即 `FacadePassthroughAccess_OnlyShrinks`）——死条目让 manifest 声称仍在管束的边界事实失真：外观上 onboarding 五分部仍有 32 个 facade 直访预算、ShellDataObjectBuilder 仍有 5 个 P/Invoke 预算，实际代码已不存在；②与两个文件自身确立的惯例矛盾——SettingsSliceOwnershipContractTests 同字典内的既有注释明示「QuickCaptureWidgetWindow partials went away with the dead host (ad8febe, DEF-027/016), so their facade budgets are retired too」（:351-353），即删文件必须同步退役预算行；③round-14 整改方案声明「契约测试 4 文件裁剪」，本批恰是第 5、6 个应裁剪文件的遗漏；④误导后续轮次 grep 式复核（本轮即被命中）。
- **根因机制**：DEF-128/127 的「删除面清点」以「源码引用图闭合」为界（.cs/.xaml 引用反查），未把「测试内以**路径字符串**为键的 manifest 字典」纳入清点半径——这类引用不参与编译，构建与测试双双不可见。
- **证据**：
  ```csharp
  // tests/DeskBox.Tests/SettingsSliceOwnershipContractTests.cs:344-350（142 条目中 5 条死）
  ["src/DeskBox/Views/OnboardingWindow.Appearance.cs"] = 10,   // 文件已删（DEF-128）
  ["src/DeskBox/Views/OnboardingWindow.Completion.cs"] = 5,    // 活（37 行）
  ["src/DeskBox/Views/OnboardingWindow.DesktopOrganization.cs"] = 1,  // 已删
  ["src/DeskBox/Views/OnboardingWindow.Features.cs"] = 1,             // 已删
  ["src/DeskBox/Views/OnboardingWindow.Hotkey.cs"] = 15,              // 已删
  ["src/DeskBox/Views/OnboardingWindow.Storage.cs"] = 5,              // 已删
  // tests/DeskBox.Tests/ModuleBoundaryContractTests.cs:32（63 条目中 1 条死）
  ["src/DeskBox/Helpers/ShellDataObjectBuilder.cs"] = 5,       // 已删（DEF-127）
  ```
  磁盘存在性穷举：`SettingsSliceOwnershipContractTests` 142 条目 → 5 死；`ModuleBoundaryContractTests` 63 条目 → 1 死；tests 全目录 `"src/..."` 引用穷举的其余命中均甄别为映射表/有意 absence 断言/模板（§1.3），无第三处。
- **建议修法（最小侵入）**：删除上列 6 行 manifest 条目（对 ModuleBoundary 顺带补一行注释「ShellDataObjectBuilder went away with the dead drop-delegation path (DEF-127)」与同文件既有注释风格对齐）；无需改测试逻辑。随批跑一次测试确认恒绿。
- **置信度**：高（文件不存在性与条目行为均可机械复核；「恒绿」由测试的文件驱动循环结构保证）。

---

## 5. 观察项（不够立案标准）

1. **`SetupTaskStep2` 空方法**（`OnboardingWindow.TaskFlow.cs:29-31`）：`SetupStep` case 2 的唯一空实现，任务流设计残留（R14 已删其同类 `SetupTaskStep1`）。随下次 onboarding 触碰顺手清理即可，本轮不动。
2. **intro 期 logo 淡入位于不透明覆盖层之下**（`OnboardingWindow.IntroAnimations.cs:121-138`）：`brandFadeIn`（400ms）与 step/footer 同批完成后才淡出 `IntroOverlay`（220ms），logo 的淡入过程对用户不可见（淡出后直接以不透明态出现）。纯视觉时序细节，产品语义无差。
3. **`ShowMyFeedbackDialogAsync` 的 `_ = LoadAsync()` fire-and-forget**（`SettingsWindow.Feedback.cs:459` 与重试按钮 `:421`）：`FeedbackService.GetMyFeedbackAsync` 为 catch-all（`Services/FeedbackService.cs:210-245`，含 60s 超时落 Failure 分支的注释），仅序列化入参位于 try 之外（理论面）；失败已有 UI 呈现。正面留档，不并入 DEF-078。
4. **`ShowAboutMeButton_Click` 的 ContentDialog XamlRoot 重叠理论面**（`SettingsWindow.DataTools.cs:146-156`）：与 R12 观察项 2（SearchSettingsSection 录制手势）同型——同 XamlRoot 已有对话框时 `ShowAsync` 抛异常直穿 async void；触发需精确重叠，理论性。
5. **SettingsWindow 数据破坏性维护区与反馈区的卫生水位整体良好**：Maintenance.cs 全部 async void 处理器（导出诊断/数据备份/恢复/快照/附件健康/恢复默认）均 try/catch/finally + 失败对话框 + `_isClosed` 复检；Feedback.cs 提交链有 deferral finally + catch-all + 限流持久禁用三重防护（feedback #115 的自愈注释在位）。唯一例外即 §2.2 的 DataTools 三按钮（同文件对照强烈）。
6. **`SearchWidgetContent` 全文正面结论**：订阅协议（LanguageChanged + HistoryService 双订阅的 attach/detach 对称、Unloaded/Dispose 双路、`_historyRefreshQueued` TryEnqueue 失败复位、服务换绑先退订）完整无隙；4 处 Click + SizeChanged 处理器全配对；热键徽标 Ctrl/Alt/Shift/Space 字面量为键名惯例非本地化遗漏。
7. **`update-settings-search-catalog.ps1` 生成器正面结论**：HeaderKey/DescriptionKey 抽取规则与模板展开（SettingsSections 递归）逻辑清晰；生成物 204 条目与 en-US 零失配、与设置页 XAML 同步（DEF-072 修复在位）。
8. **`native-pe-contract.ps1` 抽样**：PE 解析辅助全部带边界检查（越界即 throw），RVA→offset 换算的 SizeOfRawData 防护在位；与 build-rust-native.ps1 的冻结契约 token 校验互补。
9. **持续未全读清单（维持）**：`publish-aot-audit.ps1`（10498 行，R12 起留档）、`run-aot-managed-ui-smoke.ps1`（6967 行）与其余 12 个 `run-aot-*` smoke 脚本、`SettingsWindow` 六个短分部（AotSmoke/AotDeepSmoke/SectionElements/DeferredSections/Startup/DesktopOrganization 合计约 0.8k 行）——本轮头部/结构抽样无异常信号，维持留档。
10. **round-09~14 各轮观察项全部维持**（`Exclusion.None` 防御缺口、Updater `RestartApp` 不确认存活、cleanup 脚本不清理空目录、`Localized.cs:88` 缩进、`WidgetToolDialogWindow` Enter 假设、`StackPopover.cs:2280` 同行双语句、依赖包下载无哈希校验、音量滑条同值回写、`WaitForDeskBoxDependencies` 固定轮询、SearchPopup 入场守卫理论超窗、DEF-120 记忆化进程级取舍、`WidgetManager.CapsuleArrangement` 重入丢弃、publish-aot-audit 未全读、Format-arity 扫描器固化建议）——相关文件本基线零变更（`fb96a004` 未触及）。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：1（B-01 round-14 删除批在两个测试 manifest 遗留 6 条死文件条目）
- **总立案数**：1（连续第十轮 P0/P1 = 0；立案数 3 → 1）
- **已知模式新位点**：4 组并入既有编号——DEF-116 家族 +1（Maintenance.cs 附件健康三键数据驱动 arity，含扫描器第四形态盲区留档）；DEF-078 家族 +3 位点（DataTools 破坏性维护三按钮，本批最强新位点）+ 2 位点（Onboarding 存储入口 toggle，理论性）；DEF-080 家族 +4 位点（Maintenance/Feedback 显示级时间戳）
- **存量复核**：round-14 修复批（DEF-127/128/129，38 文件 −5542 行）**逐面复核全部正确落地**——XAML 嵌套平衡与命名/事件/资源零悬空、230 孤儿键 ×12 零残留、IntroAnimations↔BrandLogoHost 交接协议全路径自洽、TaskStep 活流四面板映射闭合、契约测试 4 文件裁剪断言全数成立；唯 manifest 遗留 6 条死条目立案 B-01；无已修复项回退；DEF-036/038/087 的 R14 死代码补注与当前树一致。
- **正面结论**：12 语言 2667 键 ×12 parity / 占位符 arity / 索引空洞 / 孤立花括号 / 日期字母五项机械校验全绿；261 处 Format 调用点 arity 0 失配（字面量形态）；全仓 435 处 XAML 事件绑定 0 缺失、189 资源键 0 失效；`SettingsSearchCatalog` 204 条目 0 失配；SearchWidgetContent 与 SettingsWindow 四个新覆盖分部（Maintenance/Feedback/LocalizationAndWidgets/DataTools）订阅-退订-异常防护纪律整体良好；scripts 剩余文件抽查无新增危险删除点。**以挑剔视角验证 round-14 大删除批的结论：删除面干净，唯一残余是测试 manifest 的记忆失真（B-01）。**
