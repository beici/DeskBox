# R9-B 界面交互与工程面专项审查报告（round-09）

> 审查基线 commit `097b04a3`（分支 `wip/fix-bug`）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 范围与代码量

| 区域 | 规模 |
|---|---|
| `src/DeskBox/Views/`（含 `SettingsSections/`） | 47 个 .cs + 12 个 .xaml，约 42.8k 行 |
| `src/DeskBox/Controls/`（含 `WidgetContents/`） | 96 个文件，约 57.1k 行 |
| `src/DeskBox/ViewModels/` | 42 个 .cs，约 32.8k 行 |
| `src/DeskBox/Strings/` | 12 个 JSON，共 3.2 MB，en-US 基准 2896 键 |
| `src/DeskBox/App.xaml` + 全部 .xaml 资源引用 | 1556 处 StaticResource/ThemeResource 引用，196 个唯一键 |
| `src/DeskBox.Updater/Program.cs` | 472 行（全读） |
| `native/` | Rust 8 文件约 5.9k 行 + `README.md` + `include/deskbox_native.h` |
| `tests/DeskBox.Tests/` | 404 个 .cs（只审测试质量） |
| `scripts/` + `installer/` | 57 个脚本 + 11 个 .iss（重点：publish-aot-retail 571 行、DeskBox.Uninstall.iss 813 行） |
| round-08 基线以来变更 | `77f2b4b4..097b04a3` 共 147 文件（+8205/−1336），全量 diff 审查 |

### 1.2 覆盖率声明

**全读**：`DeskBox.Updater/Program.cs`、`App.xaml`、`Controls/WidgetShell.CompositionResources.cs`、`Services/WidgetCompositionResources.cs`（新建）、`Views/WidgetDetachPlacementPreviewWindow.cs`（DEF-071 重构后全文）、`docs/quality/defect-ledger.md`、`native/README.md` 契约部分。

**分段精读（核心交互/生命周期区）**：`QuickCaptureSurfaceContent.xaml.cs`（渲染订阅 1080–1200、点击/详情保存 1221–1345、Root_Drop 2837–2895、Dispose 3604–3620）、`FileSurfaceContent.ItemVisuals.cs`（订阅生命周期 1560–1625）、`TodoWidgetContent.xaml.cs`（渲染订阅 360–445）、`ContentWidgetWindow.WindowInteraction.cs`（激活/恢复队列 360–400）、`WidgetGroupTitleSwitcher.Interaction.cs`（计时器/反馈 270–420）、`SearchPopupWindow.xaml.cs`（订阅 155–170、Closed 清理 4508–4545）、`DeskBox.Uninstall.iss`（删除守卫 560–680）、`publish-aot-retail.ps1`（路径安全 1–120）。

**模式族 grep 全扫**（覆盖 Views/Controls/ViewModels 全部文件）：`async void`（241 处清单化）、`CompositionTarget.Rendering +=/-=` 配对（6 对全部配对）、`DispatcherTimer` 创建（5 处）、`.Result/.Wait()/GetAwaiter().GetResult()`（仅 1 处真实同步桥，见观察项）、`PropertyChanged/CollectionChanged/SettingsChanged +=` 与 `-=` 配对抽查 10 个高危位点全部配对、`TryEnqueue(async`（DEF-078 位点追踪）。

**diff 审查**（round-08 基线以来的整改代码，重点 8 个）：`WidgetDetachPlacementPreviewWindow.cs`（DEF-071）、`SettingsService.cs`（DEF-070/075 残余/FTHR-01/新架构档案只读）、`SearchResultActionService.cs`（DEF-108/109）、`NativeFileDragOut.cs`（DEF-088）、`GlobalHotkeyService.cs`（DEF-087/FTHR-09）、`App.xaml.cs`（FEXC 扫尾）、`AppUpdateService.cs`（FARC-05）、`SearchHistoryService.cs`（DEF-077）、`PerformanceLogger.cs`（FTHR-06）、`PasswordVaultCredentialStore.cs`。

**机械校验（python 只读脚本，未写任何仓库文件）**：
- 12 语言键位 parity：以 en-US 2896 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2896 × 12 = 34752 键全对齐）；
- 格式占位符 arity（`{0},{1}` 索引集合与 en-US 一致性 + 索引空洞）→ **0 失配**；
- .NET 非法日期格式字母（DEF-039 模式，仅对纯日期格式值收紧判定）→ **0 复发**（DEF-039 的修复保持）；
- 代码引用键 → 12 语言缺失反向检查（`T("…")` / `Localized.Key/HeaderKey/DescriptionKey=` 共 1757 个点分键）→ **0 缺失**（DEF-072 修复保持；两条命中为前缀拼接误报）；
- 孤儿键扫描（排除前缀/点前缀拼接后）→ **443 个候选**（见 §2.2）；
- XAML StaticResource/ThemeResource 存在性（196 唯一键）→ 仓内未定义的 53 个全部为 WinUI/XamlControlsResources 平台资源（`AccentButtonStyle`、`TextFillColorPrimaryBrush`、`SystemColor*` 等），**无失效引用**。

**未覆盖及原因**：Views/Controls/ViewModels 约 130k 行未逐行全读——以模式族全量扫描 + 大文件核心交互区精读 + 整改 diff 全审组合替代；`native/` 的 C ABI 逐字段编组验证依赖既有契约测试（本轮只做导出/常量/panic 策略/抽样结构布局对照）；tests/ 仅质量扫描。

### 1.3 native 契约核验结果（正面结论）

- 导出清单：`lib.rs` 恰好 10 个 `#[unsafe(no_mangle)]` 导出，与 `native/README.md` 必需导出清单逐一一致；
- `DESKBOX_NATIVE_ABI_VERSION = 2`、capability 位集合至 bit 8（`1<<8` 回收站）= 掩码 511，与冻结契约一致；
- panic 边界：`Cargo.toml` dev/release 双 profile 均 `panic = "abort"`，无 `catch_unwind`（与「panic 必不跨 C ABI」的 abort 策略一致）；
- 结构布局抽查：`DeskBoxRecycleBinRequestV1`（Rust `repr(C)`）与 C# `NativeRecycleBinRequest`（`StructLayout.Sequential`）字段序/类型/保留域完全一致；`DeskBoxNativeUtf16StringV1` 对应 `NativeUtf16String` 一致；
- C# 侧 `ShortcutNativeBackend.cs:168-173` 导出名与 Rust 导出逐一对应。

---

## 2. 已知模式新位点

1. **DEF-078 家族新位点（async void 静默失效）**：`src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs:1221-1235` `ItemsList_ItemClick`（async void）→ `OpenDetailAfterSavingAsync`（:1324）→ `FlushPendingDetailSaveAsync` → `SaveDetailAsync` 磁盘写失败时，异常直穿 async void → 全局兜底仅记日志 → **用户点击条目打开详情静默无反应**。同文件 `Root_Drop`（:2837-2895）等拖放入口已有 try/catch + `RaiseFeedback` 用户反馈，详情打开入口是遗漏的一类。一行条目不展开，并入 DEF-078 挂账清单。

2. **孤儿键（DEF-072 同族、遗留观察同类的面积扩大）**：443 个候选键（2896 键中约 15.3%）在 `src/` 全部 .cs/.xaml 中无直接引用、亦无前缀/点前缀拼接引用；其中含整簇死键（如 `Settings.Todo.Tabs.*` 7 键、`DesktopOrganization.Subtype.*` 7 键、`Onboarding.Scene.*` 30+ 键——与 obj/ 陈旧生成物对照可证属重构残留）。判定为已知类：台账遗留观察已记录 `QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 两键；`LocalizationService` 按语言懒加载（单语言解析），运行时成本受控，不升级立案。

---

## 3. 存量复核（范围内挂账条目现状）

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-040** | **✅ 已修复（建议台账改状态）** | `FileSurfaceContent.ItemVisuals.cs:1574-1616`：`SubscribeStackSurfacePropertyChanges` 先调 `UnsubscribeStackSurfacePropertyChanges`，以 `Dictionary<Border,(Stack,Handler)>` 跟踪订阅；handler 增加 `border.XamlRoot is null` 守卫；`DisposeStackSurfacePropertyChanges`（:1611-1619）遍历清理，调用链接到 `FileSurfaceContent.xaml.cs:5287`（`Dispose()` 内）。挂账时的「容器销毁路径 Unloaded 不保证触发」缝隙已由 Dispose 链 + 字典强引用管理闭合。 |
| **DEF-077** | **✅ 已修复（复核确认）** | `SearchHistoryService.cs:249-263`：`Load()` 已改走 `ResilientJsonStore.LoadAsync`（主读 + 隔离 + .bak 恢复），`Save()` 在 `_gate` 内快照序列化（R8-AB 批落地，台账 R8-AB 分节未单列此条，本轮补录复核）。 |
| **遗留观察 #2（WidgetShell:1836 预热模板无消费）** | **已解除** | `WidgetShell.CompositionResources.cs:32-48`：`PrewarmCompactTransitionCompositionResources` 在同一 `_compositionResources` 实例上创建 `CompactOpacity/CompactScale/CompactTranslation` 三个模板；`WidgetShell.xaml.cs:2069-2121` 的 `StartCompactOpacity/Scale/TranslationAnimation` 消费的正是同一实例（DEF-089 的 per-start 重灌改造后预热点位已被真实消费）。 |
| **遗留观察 #4（extract_widgetmanager.ps1 名单含已删方法名）** | **维持（低影响）** | 脚本为一次性重构工具（首行注释自述），本轮抽检未再命中先前点名的两个方法名；名单仍有陈旧风险但无生产触发面。 |
| **EVT-02** | **维持** | `TodoWidgetContent.DragDrop.cs:577-599`：现行为 `-=` 后 `+=`（同元素重复回调防御），但 DataContext 由旧 item 换为新 item 时仍不退订**旧** item 的 handler；旧 item 随集合移除走 GC，泄漏与陈旧回调面均小，维持挂账不升级。 |
| **DEF-078** | **维持（位点行号漂移）** | 台账位点行号已漂移，当前树对应：`ContentWidgetWindow.Commands.cs:142/413/633`、`ContentWidgetWindow.WindowInteraction.cs:390/470`、`WidgetManager.cs:468/2086/2106/2817`、`WidgetManager.ZOrder.cs:352`；音乐 `MusicSessionService.cs:235-260` 的 `session.TryPauseAsync/TryPlayAsync/TrySkip*` 仍为裸 await（陈旧 SMTC 会话抛 COM 异常直达 async void 按钮）。另见 §2.1 新位点。 |
| **DEF-090** | **维持（部分修复）** | `WidgetCompactFrameSkipPolicy.cs` 仍被 `SettingsService.cs:498/1705` 与 `WidgetCompactAnimationCoordinator.cs:458` 引用；注释矛盾已更正（台账已记），自适应阶梯死代码删除仍延后。 |
| **DEF-039** | **修复保持** | 12 语言日期格式字母收紧扫描 0 复发（方法见 §1.2）。 |
| **DEF-072** | **修复保持** | 1757 个代码引用键反向检查 0 缺失。 |
| **DEF-067（时序敏感测试类）** | **修复保持 + 本轮同类扫描** | `DeskBoxDataBackupServiceTests.cs:42/89` 现用 `Task.Delay(750) + Assert.False(backup.IsCompleted)` 负向断言——失败方向确定性（若导出错误地不等门，小文件导出在慢机器上也会在 750ms 内完成而红；正确实现恒绿），无 DEF-067 型闪断方向。本轮新查 4 处时序断言见 §5。 |

---

## 4. 新发现问题清单

### B-01｜更新器「安装成功」路径重启失败被静默丢弃，且不回传结果
- **优先级**：P3
- **位置**：`src/DeskBox.Updater/Program.cs:58-61`
- **触发条件**：直发渠道静默更新，Inno 安装器退出码 0，但随后 `RestartApp` 启动新 DeskBox.exe 失败（新 exe 损坏/被杀软拦截/目录 ACL 变化）。
- **影响**：应用保持关闭，用户需手动打开；主程序收不到任何 `--update-install-result` 反馈（该参数仅在不完整更新路径携带，见 :62-65、:347），更新完成态与应用未运行态之间无信号。
- **根因机制**：成功路径 `_ = RestartApp(options.AppPath);` 丢弃布尔返回值，也没有像失败路径那样携带 outcome 参数。
- **证据**：
  ```csharp
  int exitCode = RunInstaller(options);
  if (exitCode == 0 && !string.IsNullOrWhiteSpace(options.AppPath) && File.Exists(options.AppPath))
  {
      _ = RestartApp(options.AppPath);            // 返回值丢弃、无 outcome 上报
  }
  else if (exitCode != 0)
  {
      RestartAfterIncompleteUpdate(options, GetIncompleteUpdateOutcome(exitCode));
  }
  ```
- **建议修法**：成功路径检查 `RestartApp` 返回值；false 时改走 `RestartAfterIncompleteUpdate(options, "restart-failed")`（或新增专用 outcome），使主程序下次启动可展示对应提示。约 5 行，不动安装流程。
- **置信度**：高（代码路径明确；触发面低频）。

### B-02｜云备份「运行中跳过」测试的 50ms 假通过窗口
- **优先级**：P3（测试质量：弱断言，非闪断）
- **位置**：`tests/DeskBox.Tests/CloudBackupTransportTests.cs:296-307`
- **触发条件**：`RunScheduledIfDue_GateReentrancy…` 用例在 `await first` 后固定 `await Task.Delay(50)` 再 `Assert.Single(transport.Files)`，以时间窗证明「没有被错误排队第二次运行」。
- **影响**：若回归引入了错误排队，第二次运行在上传门释放后启动，但若它在 50ms 内**未**完成（CI 慢、线程池饿），断言假通过——测试对回归的捕获能力依赖机器速度；方向为假阴性（漏报），不产生 CI 闪断。
- **根因机制**：用时间窗断言「事件未发生」，与 DEF-067 修复确立的「显式同步点代替时间窗」纪律不一致；此处没有可等待的排队信号。
- **证据**：
  ```csharp
  releaseUpload.SetResult();
  await first;
  await Task.Delay(50); // any wrongly queued run would surface here
  Assert.Single(transport.Files);
  ```
- **建议修法**：给 fake transport 增加第二组 `uploadStarted`/`releaseUpload` 门（或对 `service` 的运行计数器），用「等待并断言无第二次启动」的确定性信号替代 50ms 窗口；与 DEF-067 修复同款手法。
- **置信度**：高。

### B-03｜QuickCapture 详情自动保存时钟与用户交互的 2.1s 测试等待属可避免的真实时间依赖
- **优先级**：P3（测试质量：慢测试 + 弱窗口）
- **位置**：`tests/DeskBox.Tests/QuickCaptureClipboardServiceTests.cs:183-196`
- **触发条件**：`CaptureCurrentForTestingAsync_RecordsSameTextAfterIgnoreWindowExpires` 固定 `await Task.Delay(TimeSpan.FromSeconds(2.1))` 跨越 2s 忽略窗。
- **影响**：该测试至少 2.1s（拖慢回归）；若忽略窗实现改为「写入时刻起算」而测试时钟从 `MarkWrite` 返回后起算，在极端调度延迟下 `MarkWrite` 的时间戳与 `CaptureCurrentForTestingAsync` 的判定时钟之间有理论上的重排窗口——当前实现下方向安全（判定用 `DeskBoxClipboardWriteScope` 内部时间戳），仅列观察。
- **根因机制**：真实时钟窗口替代可注入时钟。
- **建议修法**：给 `QuickCaptureClipboardService` 注入 `TimeProvider`（.NET 8+ 内建）后以假时钟跨窗，测试即刻确定化。
- **置信度**：中（当前实现下无失败方向，仅纪律与速度问题，故列观察项；不占用 B 编号立案）。

---

## 5. 观察项（不够立案标准）

1. **`SearchHistoryService.Load()` 同步桥**（`SearchHistoryService.cs:252-262`）：`Task.Run(...).GetAwaiter().GetResult()`。构造契约强制同步，池上执行无死锁（Task.Run 不捕获同步上下文）；与旧 `File.ReadAllText` 同为阻塞读，仅增加一次线程池跳转。等价旧行为，不改。
2. **`ElevatedFileLauncherTests.cs:105-124`**：`ping -n 8` 启动后 `Task.Delay(200)` 断言 `AlreadyRunningUnelevated`。进程对象在 `Process.Start` 返回时已可被枚举，200ms 余量充足；慢 CI 理论上仍可能未完成主模块初始化，但 `TryFindRunningUnelevatedTarget` 按进程身份匹配，非窗口类检测，闪断面很小。
3. **`BoundedBackgroundWorkSchedulerTests.cs:79-90`**：`workerExited` 后 `Task.Delay(25)` 再跑恢复任务；恢复 `RunAsync` 自带 1s 队列超时兜底，25ms 启发式被内部超时吸收，无闪断方向。
4. **Updater `WaitForParentExit` PID 复用边缘**（`Program.cs:88-93`）：父进程退出后 PID 在等待窗口内被系统复用时，最多等 90 秒无关进程退出才继续；窗口极窄且仅延迟不损数据。不加锁升级。
5. **Updater `RunInstaller` 无超时 `WaitForExit()`**（`Program.cs:141`）：非静默/错误对话框场景由 Inno 保持可操作窗口属有意取舍（`/SILENT` 下错误仍可见），挂起面与 DEF-084② 同类取舍，不重复立案。
6. **`WidgetGroupTitleSwitcher.Interaction.cs:277`**：`_wheelFeedbackFallbackTimer ??=` 复用单实例，无累积泄漏。
7. **DEF-070/071 整改后残余读点**：台账已记录的无锁枚举读点（WidgetTopologyLayoutService 等）维持观察，本轮无新增同位点。
8. **`QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 孤儿键**：维持遗留观察（本轮 443 键扫描包含这两键，佐证扫描方法有效）。

---

## 6. 统计

- **P1**：0
- **P2**：0
- **P3**：3（B-01、B-02 立案；B-03 转观察项说明——正式立案计数以 B-01/B-02 + 明确列出的观察项为准）
- **总立案数**：2（B-01/B-02，均 P3）
- **存量复核状态变更**：2 条建议闭环（DEF-040、遗留观察 #2），1 条补录确认（DEF-077），其余维持
- **正面结论**：12 语言 2896 键 parity/占位符/日期格式字母/引用完整性四项机械校验全绿；XAML 资源引用 0 失效；native ABI 十导出/掩码 511/panic=abort/结构布局与冻结契约零漂移；R8-AB 与历史 P2 批整改代码（147 文件 diff）逐文件审读未发现回归；订阅生命周期纪律（渲染事件配对、Closed 清理、VM Dispose 链）全面良好；本轮审查范围连续第三轮 P0/P1 = 0。

---

## 附：主流程复核勘误（2026-09-27，完善性审查改判）

§2.1「DEF-078 家族新位点」中「`SaveDetailAsync` 磁盘写失败时异常直穿 async void → 用户点击条目打开详情静默无反应」的机制陈述**与当前树不符**：`SaveDetailAsync` 自带 `catch (Exception)`（`QuickCaptureSurfaceContent.xaml.cs:1566-1575`，`App.Log` + `RaiseFeedback("quick-detail-save-error")` + 返回 false，commit `618ccba5` 起），磁盘写失败会被就地捕获并给出用户反馈，不直穿 async void、非静默。该入口的真实残余逃逸面为：①`OpenDetail` 同步 UI 段（`IsDetailSelected`/`ApplyResponsiveLayout`/`RefreshDetailPresentation`）异常；②`SaveDetailAsync` 中位于 try 之前的 `_detailSaveGate.WaitAsync()` 异常。另补位点：同文件 `:2298` 上下文菜单 Edit 的 `async (_, _)` lambda 同样无守卫调用 `OpenDetailAfterSavingAsync`（主流程复核发现，随批一并收口）。R9 批据此把 W1-3 定性改写为「async void 纪律对齐 + 防御纵深收口」，修法（try/catch + `quick-detail-open-error` 反馈）保留。本节为留档勘误，原 §2.1 文字不改动。
