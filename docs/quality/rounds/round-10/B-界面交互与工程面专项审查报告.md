# R10-B 界面交互与工程面专项审查报告（round-10）

> 审查基线 commit `e3ad11ad`（分支 `wip/fix-bug`）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 范围与代码量

| 区域 | 规模 |
|---|---|
| `src/DeskBox/Views/`（含 `SettingsSections/`） | 63 个 .cs + 13 个 .xaml，约 36.3k 行（.cs） |
| `src/DeskBox/Controls/`（含 `WidgetContents/`） | 117 个文件，约 49.1k 行（.cs） |
| `src/DeskBox/ViewModels/` | 73 个 .cs，约 32.8k 行 |
| `src/DeskBox/Strings/` | 12 个 JSON，en-US 基准 2896 键 |
| `src/DeskBox.Updater/Program.cs` | 479 行（全读） |
| `native/` | Rust 8 文件约 9k 行 + `include/deskbox_native.h` |
| `tests/DeskBox.Tests/` | 406 个 .cs（只审测试质量） |
| `scripts/` + `installer/` | 57 个脚本 + 11 个 .iss |
| round-09 基线以来变更 | `097b04a3..e3ad11ad` 共 16 文件（3 commits：8e918498 修复批 / 21835a48 static_gate / e3ad11ad 台账），代码面 5 文件 + 1 测试，全量 diff 审读 |

### 1.2 round-09 基线以来 diff 审读（全量）

`src/DeskBox.Updater/Program.cs`（DEF-113）、`src/DeskBox/Services/TodoReminderService.cs`（DEF-110）、`src/DeskBox/Services/LocalizationService.cs`（DEF-111，BOM 归一）、`src/DeskBox/Services/SearchEngineService.cs`（DEF-112 死代码删除）、`src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs`（DEF-078 两处收口）、`tests/DeskBox.Tests/CloudBackupTransportTests.cs`（DEF-114）、`scripts/quality/static_gate.py` + `static-baseline.json`（Windows 兼容）。逐行复核结论见 §3。

### 1.3 机械校验（python 只读脚本，未写任何仓库文件）

- **12 语言键位 parity**：en-US 2896 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2896 × 12 = 34752 键全对齐）；
- **占位符 arity**：各语言与 en-US 同键的 `{n}` 索引集合比对 + 索引空洞检查 → **0 失配**；
- **.NET 非法日期格式字母**（DEF-039 模式，西里尔/区域字母收紧判定）→ **0 复发**；并逐语言人工核对 `FileInfo.FileModified` 12 值（fr-FR `dd/MM/yyyy`、ru-RU `dd.MM.yyyy` 变体字母全部合法）；
- **代码引用键存在性**：`T("…")` / `HeaderKey|DescriptionKey|TitleKey|SubtitleKey|LabelKey|TextKey|MessageKey=` 共 1500 个点分键 → **精确缺失 0**；2 处命中为前缀拼接（`DesktopOrganization.Exclusion.`、`DesktopOrganization.Layout.RetentionHelp.`），**已逐一展开验证**：
  - `DesktopOrganization.Exclusion.{Enum}`：枚举 12 成员（`Models/DesktopOrganizationModels.cs:63-77`）中 11 个有键；`None` 无键但不可达（见 §5.1）；
  - `DesktopOrganization.Layout.RetentionHelp.{Enum}`：`DesktopOrganizationRetentionReason` 6 成员与 6 键完全对齐；
  - `Widget.Stack.Category.{Enum}`（`WidgetViewModel.Stacks.cs:2250`、`FileSurfaceContent.SelectionAndMenus.cs:1293`）：`WidgetStackCategory` 13 成员与 13 键完全对齐；
  - `DesktopOrganization.Subtype.{id}` / `DesktopOrganization.Category.{id}`：`DesktopOrganizationSubtypeIds` 迭代集与 7 键、类别集与 6 键完全对齐；
- **Format 调用点 arity**（全仓 `Format("Key", args…)` 实参数 vs en-US 占位符数，顶层逗号计数修正版）→ **1 处失配**（立案 B-01，含第二位点）；
- **孤儿键**：通用点分字符串引用模式扫描 → 573 候选（round-09 方法报 443；两者均为方法相关候选集，簇分布一致：`Onboarding.Scene` 36、`Settings.Search` 36、`Onboarding.Task` 31、`DesktopOrganization.Preview` 25 等——维持既有观察，不重复立案）；
- **XAML StaticResource/ThemeResource**：197 个唯一引用键中 54 个仓外定义，逐一核对全部为 WinUI/XamlControlsResources 平台资源（`AccentTextFillColorPrimaryBrush`、`DefaultTextBoxStyle`、`PivotSegmentedStyle`、`SystemFillColorCriticalBrush` 等），**无失效引用**；
- **XAML 事件处理器绑定**：Views/Controls 全部 .xaml 按 40+ 事件名抽取 `Event="Handler"` 共 **799 处 → 0 处缺失处理器**；
- **本地化值孤立大括号**（非 `{n}` 的 `{`/`}`，会让 `string.Format` 抛 FormatException）→ 0 真实命中（2 条为 `{1:yyyy…}` 格式串正则误报）。

### 1.4 模式族扫描（Views/Controls/ViewModels 全量）

- `async void` 241 处（与 round-09 持平；位点清单与 DEF-078 挂账对齐，无新增高频入口）；
- `TryEnqueue(async` 26 处（均在 DEF-078 挂账位点族内）；
- `CompositionTarget.Rendering +=/-=` 7 对（QuickCapture segmented、FileSurface popover、Todo segmented、FeatureWidgets、TrayAnimation、CompactAnimationCoordinator）——逐对检查退订路径均有 Unloaded/Cancel/Dispose 兜底（重点核实 `QuickCaptureSurfaceContent.xaml.cs:1108-1195` 队列-稳定帧-取消协议在 `:686/:705/:944/:1059/:1083` 五条清理路径全覆盖）；
- `DispatcherTimer`/`CreateTimer()` 约 60 处——抽查 12 个高危点（SearchPopup 三计时器、Glance 内容 6 计时器、GlanceWidgetSettingsSection 3 计时器、WidgetToolDialogWindow 无计时器）全部有 Stop/去重/一次性模式；
- 事件订阅无退订候选 16 文件——逐个排查：全部为「同生共死」自持对象（自有子元素、自有集合、随窗口销毁）或已有等价清理，无一成立（详见 §1.5 抽查记录）；
- `.Result`/`.Wait()`/`GetAwaiter().GetResult()`：范围文件 0 真实同步桥（命中均为 `.Result` 匿名属性/字符串误报）。

### 1.5 专项精读（除 diff 外）

`WidgetToolDialogWindow.cs`（全读，DEF-063 基建）、`WeatherWidgetContentAdapter.cs`（全读）+ `WeatherWidgetViewModel` 订阅/Dispose 链、`GlanceWidgetContent.xaml.cs` 构造/Unloaded 区、`GlanceWidgetSettingsSection.xaml.cs` 订阅与刷新链、`GlanceWidgetStore.LoadAsync` 弹性读取、`SettingsWindow.xaml.cs` Closed 清理链（337-400）+ `SettingsWindow.CloudBackup.cs` 钩子区、`SettingsWindow.Navigation.cs` 搜索导航区、`App.xaml.cs` 设置窗口生命周期（3099-3123）、`DesktopOrganizationTaskView.Retained.cs`（全读）+ `.Presentation.cs` retained 源、`Services/Localized.cs`（全读）、`Services/ThemeService.cs` 窗口跟踪、`SearchPopupWindow.xaml.cs` 三计时器管理点、`scripts/cleanup-deskbox-install.ps1`（全读 179 行）、`installer/DeskBox.Migration.iss`（全读 118 行）、`TodoWidgetContent.xaml.cs:605-660`、`WidgetViewModel.Windowing.cs:78-120`、`WeatherWidgetViewModel.RefreshAndLayout.cs:37-120`。

### 1.6 覆盖率声明

Views/Controls/ViewModels 约 118k 行未逐行全读——以模式族全量扫描 + 机械校验（本节 9 项）+ round-09 以来 diff 全审 + 上述专项精读组合替代。`scripts/`、`installer/` 自 round-08 基线（`77f2b4b4`）**零漂移**（仅 `native/README.md`、static_gate 两文件变动，见 §1.1），沿用 round-08/09 的逐行结论有效；本轮新增全读 `cleanup-deskbox-install.ps1` 与 `DeskBox.Migration.iss`。tests/ 仅质量扫描。

### 1.7 native 契约核验结果（正面结论）

- 导出清单：`lib.rs` 恰好 10 个 `#[unsafe(no_mangle)]` 导出（abi_version/capabilities + shortcut×4/music_volume/explorer_shell_launch/quick_access/recycle_bin），与冻结契约一致；
- `DESKBOX_NATIVE_ABI_VERSION = 2`（lib.rs:15）、capability 位 0..8 → 掩码 511（:22-41），零漂移；
- panic 边界：workspace `native/Cargo.toml` dev/release 双 profile `panic = "abort"`，全仓 `catch_unwind` 0 命中；
- 自 round-08 基线 `native/` 除 README 4 行外零变更——round-09 的结构布局抽查结论（`DeskBoxRecycleBinRequestV1`/`NativeUtf16String` 逐字段一致）继续有效。

---

## 2. 已知模式新位点

1. **DEF-078 家族**：`src/DeskBox/Views/SettingsSections/GlanceWidgetSettingsSection.xaml.cs:73-78` `OnLoaded` 为 async void 且 `await RefreshFromStoreAsync()`（`RefreshInstancesAsync` 无 catch，`:130-141`）——逃逸异常直穿 async void → 全局兜底仅记日志 → 设置页 Glance 区静默不刷新。触发面收窄：存储读取已走 `ResilientJsonStore`（坏数据自愈），残余为 UI 更新段异常，理论性较强；并入 DEF-078 挂账清单，一行条目不展开。

2. **孤儿键（维持既有观察，不重复立案）**：本轮通用模式重扫 573 候选（round-09 报 443，方法差异所致），簇分布与 round-09 §2.2 一致；`QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 两键仍含其中。

---

## 3. 存量复核（范围内挂账条目现状）

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-113** | **✅ 修复复核正确** | `src/DeskBox.Updater/Program.cs:58-68`：`if (!RestartApp(options.AppPath)) { RestartAfterIncompleteUpdate(options, "restart-failed"); }`——布尔检查到位；`RestartAfterIncompleteUpdate`（:346-355）自带 AppPath 存在性守卫；"restart-failed" 经 `RestartApp(appPath, outcome)`（:369-394）携带 `--update-install-result` 回传主程序。与失败路径（:69-72）完全对称。 |
| **DEF-114** | **✅ 修复复核正确** | `tests/DeskBox.Tests/CloudBackupTransportTests.cs:283-322`：`uploadStartCount` Interlocked 计数 + `secondUploadStarted` TCS；负向断言改为 `await Assert.ThrowsAsync<TimeoutException>(() => secondUploadStarted.Task.WaitAsync(250ms))`——断言对象从「完成数」改为「启动信号」，错误排队在门释放瞬间即置位信号，250ms 只覆盖线程调度而非上传时长；机器速度不再影响判定方向。`Interlocked` 计数与 TCS 用法正确（第二次启动也复置 `uploadStarted` 为无害 TrySet）。 |
| **DEF-110** | **✅ 修复复核正确** | `src/DeskBox/Services/TodoReminderService.cs:29` `ConcurrentDictionary<string, byte>`（`StringComparer.Ordinal` 语义与原 HashSet 一致）、`:444` `TryAdd(reminderKey, 0)` 替代 `Add`、三处 `Clear()`（:96/:124/:138）语义不变。跨线程安全性闭合。 |
| **DEF-111** | **✅ 修复复核正确** | `src/DeskBox/Services/LocalizationService.cs:1` 单 BOM（diff 字节级验证 `﻿﻿﻿﻿using` → `﻿using`），零内容变更。 |
| **DEF-112** | **✅ 修复复核正确** | `SearchEngineService.cs` 两方法整段删除，`GetRecentNotesAsync`/`GetUpcomingTodosAsync` 全仓 0 残留引用；推荐链路仍活（`GetRecommendationsAsync`/`BuildApplicationRecommendations` :238-276 供 `SearchPopupViewModel.LoadRecommendationsAsync` :292 消费）；`SearchRecommendationItem` 模型仍被 `SearchPopupWindow.xaml:726/783` DataTemplate 绑定消费（非死模型）；`TruncateText`（:755，活调用 :538）与 `Search.Todo.Due`（:597）保留正确。 |
| **DEF-078（round-09 两处收口）** | **✅ 修复复核正确** | `QuickCaptureSurfaceContent.xaml.cs:1234-1245` 与 `:2312-2327`：两处调用点均包 try/catch + `App.Log` + `RaiseFeedback(T("Common.OperationFailedRetry"), Error, "quick-detail-open-error")`；上下文菜单 lambda 额外在 catch 后 `return`，防止半初始化态继续 `BeginDetailEditing()`。与 round-09 勘误附录定位的残余逃逸面（详情打开同步 UI 段 + gate WaitAsync）对齐。 |
| **DEF-055** | **维持** | `SettingsWindow*.cs` + `SettingsSections/*.cs` 合计 async void 91 处，抽两分部仅 4 处 `_isClosed` 复检；Closed 全量退订链存在（`SettingsWindow.xaml.cs:337-400`）但 handler 内复检未泛化，与挂账口径一致。 |
| **DEF-083** | **维持（位点漂移至 :827-833）** | `ContentWidgetWindow.xaml.cs:831-833` `_autoRestoreTimer` 到点直呼恢复的逻辑未变，仍缺「托盘唤起会话」护栏（自愈型外观级）。 |
| **EVT-02** | **维持** | `TodoWidgetContent` DataContextChanged 旧 item 订阅不退订机制未变（round-09 已核，本轮无新变化）。 |
| **DEF-090** | **维持（部分修复）** | `WidgetCompactFrameSkipPolicy` 仍被 `SettingsService.cs` 与 `WidgetCompactAnimationCoordinator.cs:458` 引用，自适应阶梯死代码删除仍延后。 |
| **MEM-01 / THR-06 / EXC-06 / DEF-048/049/051/052/053/056/076/079/080/082** | **维持** | 本轮范围内无触及这些位点的代码变更（round-09 基线以来 diff 不含相关文件）；round-09 复核结论继续有效。 |
| **DEF-067（时序敏感测试类）** | **修复保持 + 同类扫描** | `DeskBoxDataBackupServiceTests.cs:42/89` `Task.Delay(750)` 负向断言维持 round-09 结论（失败方向确定）；本轮同类全扫结果见 §5.2/§5.3（新增 2 条观察，无立案级闪断）。 |
| **DEF-072 / DEF-039** | **修复保持** | 1500 引用键反向检查 0 缺失；日期格式字母收紧扫描 0 复发。 |
| **遗留观察 #4（extract_widgetmanager.ps1 名单陈旧）** | **维持** | 脚本未变动。 |

---

## 4. 新发现问题清单

### B-01｜Format 调用点实参数与键占位符数失配：`string.Format` 静默吞掉多余实参，格式契约靠容错维系
- **优先级**：P3（代码卫生/契约漂移；当前无用户可见破损——`string.Format` 对多余实参不抛异常）
- **位置**：
  - 位点 1：`src/DeskBox/ViewModels/TodoItemViewModel.cs:582` —— `Format("Todo.RecurrenceHistory.Collapse", HiddenRecurringHistoryCount)`，而 en-US `Todo.RecurrenceHistory.Collapse = 'Hide history'` **无任何占位符**（对照兄弟键 `Expand = 'Show {0} more history items'`）；
  - 位点 2：`src/DeskBox/ViewModels/SearchPopupViewModel.cs:691-697` —— 单一 `string.Format` 以 **2 个实参**（`TotalResultCount, Elapsed.TotalMilliseconds`）服务两个条件键：`Search.Status.Results = '{0} results · {1:F0}ms'`（2 占位符，匹配）与 `Search.Status.PartialResults = '{0} results found · Still searching'`（**仅 1 占位符**，第二实参被静默吞掉）。
- **触发条件**：Todo 条目展开循环历史时点击「Hide history」；搜索进行中（PartialResults 分支）。
- **影响**：当前无功能破损。风险在契约面：①Collapse 分支显然曾按「带计数」意图书写（与 Expand 对称传参），文案与调用意图漂移已发生；②位点 2 依赖 `string.Format` 对多余实参的容错，若后续有人给 `PartialResults` 文案加 `{1}` 之外的改动、或重构为共用格式化辅助并校验 arity，行为悄然改变；③任何新语言翻译若把字面 `{`/`}` 引入这两键将直接 `FormatException`（全局兜底吞掉后状态栏/切换开关显示为空），而现有测试不覆盖调用点 arity。
- **根因机制**：调用点实参数按「分支并集的最大占位符数」书写，未与各键实际占位符对齐；本仓既有 1750+ 引用键校验、12 语言 parity 测试均不检查「调用点实参 ↔ 键占位符」这一层（本轮扫描为本仓首次覆盖）。
- **证据**：
  ```csharp
  // TodoItemViewModel.cs:581-584
  public string RecurringHistoryToggleText => IsRecurringHistoryExpanded
      ? Format("Todo.RecurrenceHistory.Collapse", HiddenRecurringHistoryCount)  // 键无 {0}
      : Format("Todo.RecurrenceHistory.Expand", HiddenRecurringHistoryCount);

  // SearchPopupViewModel.cs:691-697
  return string.Format(
      _localizationService.T(response.IsComplete
          ? "Search.Status.Results"          // {0} + {1:F0}
          : "Search.Status.PartialResults"), // 仅 {0}
      response.TotalResultCount,
      response.Elapsed.TotalMilliseconds);   // PartialResults 分支吞掉第二参
  ```
- **建议修法（最小侵入）**：位点 1 的 Collapse 分支改用 `T("Todo.RecurrenceHistory.Collapse")`（文案「Hide history」不带计数是合理 UX，只去掉死实参）；位点 2 按分支分别 Format，或给 `PartialResults` 补 `{1}` 占位符（后者需 12 语言同步，前者一行零风险）。可在 `static_gate.py` 或既有 JSON 契约测试中追加「`Format("Key",…)` 调用点 arity」静态扫描，把本轮脚本方法固化。
- **置信度**：高（机械事实：键值与实参数均为当前树直接验证）。

---

## 5. 观察项（不够立案标准）

1. **`DesktopOrganization.Exclusion.None` 键 12 语言全缺**（`Models/DesktopOrganizationModels.cs:65` 成员存在、键全缺）：当前不可达——`GetRetainedPreviewItems` 在返回前把 `IsEligible`（即 None）条目统一改写为 `UserChoice`（`DesktopOrganizationTaskView.Presentation.cs:121-122`），`_lastExecutionPlan.ExcludedItems` 分支按规划器约定不包含 eligible 项；且 `T()` 三级回退最终返回原始键名而非抛错。属防御性缺口：若未来规划器把 None 项放入 ExcludedItems，将直显键名。置信度：中（可达性论证依赖规划器约定，未穷举全部计划生成路径）。
2. **`QuickCaptureClipboardServiceTests.cs:227`**：`RaiseContentChanged()` 后 `Task.Delay(100)` 正向等待事件传播——慢 CI 下若捕获链路超 100ms 则假红（DEF-067 纪律的弱化同类，方向为闪断而非漏报）；内存内 fake 路径下 100ms 余量充足。置信度：高（机制）/ 低（实际触发频率）。
3. **`QuickCaptureServiceTests.cs` 9 处 `Task.Delay(5)`**：用于让相邻 `AddItemAsync` 的 `UpdatedAt` 时间戳分离以断言排序。依赖 .NET 在 Windows 上 `DateTimeOffset.UtcNow` 的高精度实现；若计时器粒度退化（15.6ms）两次写入可能同戳，排序断言闪红。现 CI 长期全绿，仅记录。
4. **Updater `RestartApp` 以 `Process.Start` 成功即返回 true**（`Program.cs:386-387`）：不等待确认新进程存活（秒退的损坏 exe 仍报成功）。与 DEF-084① 同为直发更新器既有取舍，DEF-113 修复后至少 restart 失败有信号，不再升级。
5. **`cleanup-deskbox-install.ps1` 仅删叶子文件、不清理遗留空目录**（`Remove-StaleInstallEntries` 只 `Test-Path -PathType Leaf`）：升级后可能残留空目录树，纯外观；脚本的卷根拒绝、`DeskBox.exe` 哨兵、manifest 路径逃逸拒绝、fail-closed 守卫均完备（本轮全读确认）。
6. **`Services/Localized.cs:88`**：`public static void RefreshAll` 行首缩进异常（8 空格，文件内独一处），纯格式。
7. **`WidgetToolDialogWindow` Enter 键依赖 TextBox 不吞 Enter**（`root.KeyDown` 冒烟）：单行 TextBox 的 Enter 不标记 Handled 故可冒泡到根（DEF-063 设计成立）；若未来编辑器加入 `AcceptsReturn` 多行框，Enter=保存失效（Esc 仍有效）。当前边距/标题外观编辑器均为单行，无触发面。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：1（B-01，含 2 个位点）
- **总立案数**：1
- **已知模式新位点**：1（DEF-078 家族 +1）+ 孤儿键观察维持
- **存量复核**：round-09 五项修复（DEF-110~114）+ DEF-078 两处收口全部复核为正确落地、无回归；维持类条目（DEF-055/083/090/EVT-02/MEM-01 等）均给当前树证据；无「已修复项回退」。
- **正面结论**：12 语言 2896 键 parity / 占位符 arity / 日期格式字母 / 引用键完整性 / 动态键族（Stack.Category 13、RetentionHelp 6、Subtype 7、Category 6）六项机械校验全绿；XAML 资源引用 0 失效、799 处事件绑定 0 缺失处理器；Rust ABI 十导出 / ABI 2 / 掩码 511 / 双 profile panic=abort 零漂移；订阅生命周期纪律（渲染事件 7 对全配对、Glance/Weather 三层退订、SettingsWindow Closed 全量清理、Localized 弱引用、ThemeService 自动退订）全面良好；scripts/installer 自 round-08 零漂移；测试面无未 await 的 ThrowsAsync / 永真断言 / DEF-114 型漏报窗口复发。本轮范围内连续第五轮 P0/P1 = 0。
