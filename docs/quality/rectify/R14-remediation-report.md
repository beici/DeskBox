# R14 整改报告（round-14 闭环批次，2026-09-27）

> 输入：`rounds/round-14/全量代码缺陷审查总报告.md`（DEF-127~129，死代码/清理类，W1→W2→W3 依赖序）。
> 方案：`R14-remediation-plan.md`（独立完善性审查**首审 NO-GO**——M1 W3 漏 DesktopOrganization.cs 与 3 个活方法内死分支 + 未声明 AutoStart 激活期自纠行为退役；M2 契约测试爆炸半径 4 文件 6 方法非计数类缺失——按指令全面重写 v2 后复审 **GO**，另采纳 5 条建议 S1~S5）。
> 门禁总览：Debug x64 构建 **0 错误**（20 警告，较基线 22 净降 2——死代码删除连带消除）；`static_gate.py` **PASS**（12 语言 **2667** 键对齐；async void 249→247、空 catch 245→241，均为删除驱动下降）；x64 全量回归 **4291/4291**（4297 − 6 个死路径用例）；`publish-aot-audit.ps1 -Platform x64` **通过**（WMC1510 = 742 恰等于既定上限、Rust ABI 2/掩码 511/十导出完整、45 文件 97.4 MiB、260 秒）；新实例 **PID 2696** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-127 | `Helpers/ShellDropDelegator.cs` | 删除死类 `ShellDropDelegator`（TryDelegateDrop/BindShortcutDropTarget/CallDragEnter/CallDrop/CallDragLeave/InvokeEffectCall/ReleaseObject/GetVtableEntry + 5 槽位常量 + 3 Guid + ShellPointL，:97-409）；**保留活成员** `ShellDropLaunchOutcome`/`ShellDropLaunchResult`/`ShortcutDropOutcomePolicy`（:12-96） |
| DEF-127 | `Helpers/ShellDataObjectBuilder.cs` | **整文件删除**（232 行，审查核实无活成员） |
| DEF-127 | `Platform/Shell32NativeMethods.cs` | 删除 `SHCreateItemFromParsingName`（唯一调用方是死链）+ 文档措辞更新；SHObjectProperties/SHParseDisplayName（活）保留 |
| DEF-127 | `Helpers/NativeDropTarget.cs:513+` | 分派注释改为与 ShellExecuteEx 实现一致并注明 IDropTarget 委托方案的实测否决依据（drop_on_shortcut_open.md §10.2） |
| DEF-127 | `tests/…/ShellDataObjectBuilderTests.cs` | 整文件删除（6 用例全部守护死路径） |
| DEF-129 | `Views/OnboardingWindow.xaml` | `BrandLogoHost` Grid（原 :855-892，含 BrandLogo/BrandLogoShine/Transform）整体迁出 `Step1Panel`，置于 RootGrid Row 0（36px 标题行）水平居中 + `Grid.Row="0"` + `IsHitTestVisible="False"`；全部引用走 DependencyObject（无 TargetName 字符串），零代码改动；deskbox.svg 唯一展示位从「永久不可见」恢复为真实可见，shine 动画空转随之消除 |
| DEF-128 | `Views/OnboardingWindow.xaml` | 删除七个 Collapsed 死面板：TaskStep1Panel（:173-259）、Step1Panel（:846-967）、Step2Panel、Step3Panel（实为 Grid，114 行）、Step4Panel、**StepOrganizationPanel（:1341-1395，审查与 B 代理均未单列的第七个死面板，实施时经平衡匹配发现并纳入）**、Step5Panel——均按 x:Name 结构边界删除 |
| DEF-128 | `Views/OnboardingWindow.Appearance.cs`、`.DesktopOrganization.cs`、`.Features.cs`、`.Storage.cs`、`.Hotkey.cs` | **5 个分部整文件删除**（Appearance/Features/Storage/Hotkey 全部成员死；DesktopOrganization.cs 全部成员仅被第七死面板与内部互调引用——实施中发现其 `OrganizationChangePath_Click` 活接线位于第七死面板而非审查所述 Step4Panel:1374，死集结论不变） |
| DEF-128 | `Views/OnboardingWindow.Completion.cs`、`.TaskFlow.cs`、`.xaml.cs` | 死成员删除：SetupStep5、SetupTaskStep1、StartStep1CardAnimation、StartSearchDemoAnimation、RunSearchDemoAsync、OnHotkeyKeyDown；孤儿字段/常量 7 项（_hotkeyRecordingHook/_keycapPulseStoryboard/_searchDemoCts/_hotkeyDemoCts【本就恒 null】/_startupToggleRefreshGeneration/_isRecordingHotkey/WmReservedHotkeyCapture/PresetAccentColors）；3 个活方法内死分支（NavigateToStepAsync/WindowSubclassProc/ApplyResponsiveLayout Step3 段）；**`UpdateFooterState` 无 Step3 段，未动（审查更正）** |
| DEF-128 | **行为退役声明（审查 M1）** | `OnboardingWindow_Activated → RefreshStartupToggleFromSystem` 链随死面板退役——移除「窗口激活时把 OS 启动状态回写 Settings.AutoStart」的激活期自纠行为；决议：随旧流退役（TaskStep 流无启动项开关），Settings.AutoStart 活语义由 SettingsWindow 承担不受影响 |
| DEF-128 | 12 语言 JSON | 孤儿键 230 个删除（2897→2667）：候选 286 个逐键全仓精确 grep 分类，56 个活键保留（含审查指定的全部例外：Onboarding.Step2.TrayActionTitle、Onboarding.Step4.PinTitle、Onboarding.Task.Step2.Warning.* ×5、Task.Step4.FeatureEntry 及全部 Task.* 活流键） |
| DEF-128 | 契约测试 ×4（**保 Settings 半段，只裁 Onboarding 半段**） | ①`GlobalHotkeySafetyContractTests`：删 Onboarding 半段断言（保留 Settings 侧 IsInternalMaskKey 排序守卫）；②`StartupRegistrationContractTests`：删 Onboarding 读取与两条断言；③`OnboardingExperienceTests` ×3：activeFlow 切片终止标记 `Step1Panel`→`FooterNav`；④`FolderPickerModernizationContractTests`：Onboarding 条目退役（援引本文件 QuickCapture 先例）、totalCalls 8→7 |
| DEF-128 | 台账 | DEF-036/038/087 的 onboarding 位点补注「位点已随旧版引导流死代码删除（机制修复的历史有效性保留）」 |

净变化：删除 5 个分部文件 + 2 个 Helper/测试文件 + 七个死面板 + 230 孤儿键 ×12；**累计约 3,900+ 行死代码/死键清除**；无签名/序列化/ABI/依赖变更。

## 2. 门禁证据

1. 分段增量构建全部通过；最终 Debug x64 构建 0 错误（20 警告 ≤ 基线）。
2. x64 全量回归 → **4291/4291 通过、0 失败**（2m06s）。
3. `static_gate.py --json rectify/r14-static-gate.json` → **PASS**：[1] 12 语言 2667 键 parity；[2] async void 249→247（删除驱动）；[3] 剪贴板 8/8 配对；[4] 同步等待 147 持平、空 catch 245→241（删除驱动）、反射 6；[5] 契约重放新增 0。
4. `publish-aot-audit.ps1 -Platform x64` → **通过**（auditProfileVersion 63；WMC1510 = 742 = 既定上限；RustAbiVersion 2 / Capabilities 511 / 十导出完整；publish 45 文件 97.4 MiB；260,326 ms；留档 `.artifacts/aot-audit/win-x64/summary.json`，gitCommit=435b1a1a + 本批工作树）。
5. 新实例 PID 2696 @ 规范 Debug 路径。

## 3. 人工复验清单（GUI，待运行窗口）

- OnboardingWindow：intro 覆盖层淡出后 BrandLogo（deskbox.svg）在标题行居中可见、shine 扫光动画作用于可见元素；四步 TaskStep 面板切换正常、无布局重叠（Logo 位于标题行、IsHitTestVisible=False 不挡拖拽）。
- 首启引导/RestartIntro 全流程回归目检。

## 4. 延后维持（与总报告 §5 一致）

新位点 4 组（DEF-070 读侧 ×2、DEF-092 ×1、O-26 ×1、DEF-078 ×1）、观察项 8 条（O-27~O-29 + B 面 5 条）。
