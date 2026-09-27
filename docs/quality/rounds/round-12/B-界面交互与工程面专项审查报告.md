# R12-B 界面交互与工程面专项审查报告（round-12）

> 审查基线 commit `7d201074`（分支 `wip/fix-bug`）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 与 round-09/10/11 的差异化声明

round-09~11 已精读并连续复核的面（WidgetToolDialogWindow、Glance/Weather/Todo 订阅链、SettingsWindow Closed 链、QuickCaptureSurfaceContent 核心交互区、MusicWidgetContent、FileSurfaceContent.StackPopover、cleanup 脚本、Migration.iss、publish-aot-retail.ps1、DeskBox.iss/arm64/Installation.iss/Dependencies.iss、Uninstall.iss 删除守卫区）本轮仅做定点/差异复核。**本轮把深读预算投向从未被覆盖的面**：

- **全读（本轮首次）**：
  - `Views/SettingsSections/DesktopOrganizationSettingsSection.xaml.cs`（977 行）+ `SearchSettingsSection.xaml.cs`（629 行）——SettingsSections 最后两个未读分部，至此该目录 8 个分部全部覆盖；
  - `Controls/WidgetShellContentHost.cs`（628 行）——统一内容窗架构的内容生命周期事务核心（Prepare/Commit/Rollback/Dispose 协议），历轮 B 报告从未涉及；
  - `FileSurfaceContent` 未精读分部 ×10：KeyboardNavigation、ScrollBars、ShortcutDrop、TransferState、RenderWindow、StackPopoverRename、Navigation、Opening、ImportProgress、StackAnimations（合计约 2.4k 行）；`SelectionAndMenus.cs`（1479 行）首次全文审读（round-08 仅读 :1293 动态键区）；
  - `Services/SearchResultActionService.cs`（160 行全文，DEF-108/109 修复后首次整体复核）；
  - `installer/DeskBox.DependencyCustomMessages.iss`（99 行）、`DeskBox.UninstallCustomMessages.iss`（27 行）、`DeskBox.HindiBengaliMessages.iss` / `DeskBox.NewLanguageCustomMessages.iss`（键位 parity 机械比对）；
  - `scripts/build-store-msix.ps1`（106 行）。
- **分段精读**：`SearchPopupWindow.xaml.cs` 未覆盖区段（Closed 全量清理链 4508-4542、图标入场动画与守卫计时器 2304-2430、rubber-band 多选与自动滚动 3390-3580、剪贴板/attach/save 动作区 3700-4210）；`SearchPopupViewModel.cs` 搜索/取消/Dispose 生命周期区；`WidgetShell.xaml.cs` 计时器与订阅结构区（marquee 计时器 3770-3850、标题栏双击防抖 5230-5266、构造/Unloaded 清理 370-489）；`FolderWatcherService.cs` StartAsync/Stop/ReconnectTimer_Tick 全路径（DEF-117 修复复核）；`FileSurfaceContent.xaml.cs` 键盘命令区（4380-4500）与订阅/Dispose 配对区（235-268、5308-5314）。
- **tests 抽查**（正确性方向，非全覆盖）：`CapsuleMorphZOrderQuietnessTests`（DEF-056/057/058 契约：verify-first 次序、HWND_TOP 哨兵、planner 单遍收敛 + 反例组）、`SettingsSharedStateConcurrencyTests`（DEF-070 并发守恒）、`R8AbWave12RemediationTests`（源码切片契约）、`LocalizationResourceContractTests` 覆盖面确认。
- **round-11 修复批复核**：`cfdf8059`（DEF-117/118/119）全量 diff + 当前树逐路径上下文（见 §3）。

### 1.2 机械校验（python 只读脚本，未写任何仓库文件）

- **12 语言键位 parity**：en-US 2896 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2896 × 12 = 34752 键全对齐）；
- **占位符 arity**（各语言与 en-US 同键 `{n}` 索引集合）→ **0 失配**；
- **.NET 非法日期格式字母**（DEF-039 模式收紧判定）→ **0 复发**；
- **本地化值孤立花括号** → **0 命中**；
- **Format 调用点 arity**（复跑 round-11 三形态方法：`Format("Key",…)` / `Format(T("Key"),…)`（含 T 闭括号修正）/ `Format(T(条件表达式),…)` 多键分支收集）→ 扫描 **264 处**，**0 失配**、**0 异构 arity 位点**（DEF-116 修复保持）；
- **XAML StaticResource/ThemeResource**：36 个 .xaml，195 个唯一引用键，其中 52 个仓外定义——逐一核对全部为 WinUI/XamlControlsResources 平台资源（`AccentButtonStyle`、`TextFillColorPrimaryBrush`、`CardStrokeColorDefaultBrush`、`SystemColor*` 等），**无失效引用**；
- **XAML 事件处理器绑定**：按事件名抽取共 **748 处 → 0 处缺失处理器**；
- **安装器消息 parity**：`DeskBox.iss`（5 基础语言 × 24 键）+ `NewLanguageCustomMessages.iss`（7 语言 × 24 键）+ `DependencyCustomMessages.iss`（12 语言 × 7 键）+ `UninstallCustomMessages.iss`（12 语言 × 2 键）+ `HindiBengaliMessages.iss`（Inno 6.7.3 内建键补充，与应用键集零重叠）→ 12 语言 **全对齐、无缺键、无重复定义**；
- **模式族扫描**：`TryEnqueue(async` 25 处全量盘点（见 §2.2）；`CompositionTarget.Rendering +=/-=` 7 对全部配对（含 `WidgetCompactAnimationCoordinator` 的 Win11/无 dispatcher 二选一订阅与 case-None 退订语义）；culture 敏感 Parse/ToString 扫描——WeatherService / WeatherWidgetViewModel.DataProcessing 各位点均为 DEF-080 挂账位点，无新解析消费方（显示级新位点见 §2.3）。

### 1.3 覆盖率声明

全读约 8.5k 行 + 结构扫描/抽样精读约 6k 行。仍未见逐行全读：`WidgetShell.xaml.cs` 正文大部分、`App.xaml.cs`、`ContentWidgetWindow` 各分部、其余 ViewModels/Views——沿用 round-09/10/11 的模式族扫描结论与维持条目证据；`publish-aot-audit.ps1`（10498 行）本轮仅结构抽样（profile 版本 63、fork-note 同步链、param/门禁结构在位），完整审读留待专项。`native/` 自 round-08 基线仅 `README.md` 4 行变动（round-10/11 已核），本轮 git 范围内无 Rust 变更，**ABI 零漂移结论沿用**。

---

## 2. 已知模式新位点（并入既有编号，不新立案）

### 2.1 DEF-097 家族（剪贴板写入无守卫）：FileSurfaceContent 复制路径位点

`src/DeskBox/Controls/WidgetContents/FileSurfaceContent.SelectionAndMenus.cs:1313-1341` `CopyPathsToClipboard`——`Clipboard.SetContent(package)` + `Clipboard.Flush()`（:1333-1334）无 try/catch 无重试。入口：①堆叠菜单「复制内容路径」（:1240，同步 Click lambda）；②`Ctrl+Shift+C`（`FileSurfaceContent.xaml.cs:4463`，async void KeyDown 无外层 catch）；③菜单构建器 `FileItemMenuBuilder.cs:108/240`。对照本仓其余全部剪贴板写入点均已守卫：QuickCaptureSurfaceContent.xaml.cs:2779（DEF-097 修复批）、TodoWidgetContent.ClipboardSelection.cs（外层 try + Flush 细分注释）、SearchPopupWindow.xaml.cs:3753/3981/4216（三处全 try/catch）、FileSurfaceContent.xaml.cs:4917（经 `RunAsync` catch-all 兜底）——唯独此文件漏。剪贴板被其他进程占用时异常直穿 → 静默失败无反馈。修法同族（try/catch + `WidgetFeedbackSeverity.Error` 反馈），并入 DEF-097 挂账清单。

### 2.2 DEF-078 家族（async void / TryEnqueue(async) 静默失效）：盘点与新位点

本轮 25 处 `TryEnqueue(async` 全量盘点：`FileSurfaceContent.xaml.cs:615`（内层 try/catch）与 `App.xaml.cs:4634`（内层 try）自带守卫；其余 23 处均落在台账 DEF-078 挂账位点族内（WidgetManager ×4、WidgetManager.ZOrder ×4、ContentWidgetWindow.Commands ×3、WindowInteraction ×2 等）。**新点名位点 1 处**：`FileSurfaceContent.xaml.cs:4719` QuickLook 键盘导航 `TryEnqueue(async () => await manager.ContinueQuickLookNavigationAfterNativeAsync(…))` 无内层守卫（陈旧预览目标异常直穿全局兜底）。并入 DEF-078 清单。

### 2.3 ANI-06 家族（SetIsTranslationEnabled 不复位）：3 文件 5 位点

`FileSurfaceContent.Navigation.cs:330/368/392`（文件夹导航滑动动画）、`FileSurfaceContent.StackAnimations.cs:270`（堆叠成员退出动画）、`SearchPopupWindow.xaml.cs:2375`（应用图标入场动画）均 `SetIsTranslationEnabled(element, true)` 后从不置回 false——与台账 ANI-06「挂账、未触及」同族同机制（宿主元素生命周期结束后翻译属性残留；当前无可见破损，维持 P3 挂账口径，补位点）。

### 2.4 DEF-080 家族（culture 敏感格式化，显示级）：3 文件 4 位点

`Models/OrganizationHistoryEntry.cs:90`（`ToString("MM-dd HH:mm")`）、`ViewModels/QuickCaptureItemViewModel.cs:395`、`ViewModels/SettingsViewModel.AboutAndUpdates.cs:518/521`——均为**纯显示、无解析消费方**的 current-culture 时间戳（ar-SA 下呈阿拉伯-印度数字），比台账已挂账的 Weather 解析位点危害低一档；并入 DEF-080 观察面，不单独升级。

---

## 3. 存量复核（round-11 修复批 + 范围内挂账条目现状）

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-117（FolderWatcherService 出线程化）** | **✅ 修复目标达成 + ⚠️ 引入一处重入语义回归（本轮立案 B-03，见 §4）** | ①双失败分支 `await ProbeFolderAccessAsync`（FolderWatcherService.cs:267）+ 探测后 `lock` 代际守卫镜像（:268-274）与主路径 :234-240 一致；②`ReconnectTimer_Tick`（:851，async void 自带 try/catch + `_isDisposed` 复检）与 StartAsync 入口解析均包 `Task.Run`（:213-216、:874-880），回退语义钉死 path；③离线 UNC 的 UI 阻塞消除。**但** await 前移了「Stop + 认领代际」的时序窗口，慢解析的旧调用可在新调用完成后抢占（机制见 B-03）。 |
| **DEF-118（Process 全量 Dispose）** | **✅ 修复复核正确** | `DragDropPermissionService.cs:801-833`：`GetProcessesByName` 数组在 try/finally 中全量 Dispose（含 `FirstOrDefault` 选中实例与其余元素）；早退分支（not-running / open-error）同样经过 finally；`OpenProcess` 句柄内层 finally `CloseHandle` 保持。比 round-11 报告建议更彻底，无回归。 |
| **DEF-119（用户级 .NET 探测点）** | **✅ 修复复核正确** | `installer/DeskBox.Dependencies.iss:108-112` 与 arm64 版同位点：`IsDotNet10RuntimeInstalledAt(ExpandConstant('{localappdata}') + '\Microsoft')`——`IsDotNet10RuntimeInstalledAt`（:68-98）拼 `dotnet\dotnet.exe` 后实际探测 `{localappdata}\Microsoft\dotnet\dotnet.exe`，与 .NET 用户级默认布局一致；提权上下文注释在位；两个变体同步修改。 |
| **DEF-097 / DEF-078 / ANI-06 / DEF-080 家族** | **维持（本轮补位点，见 §2）** | 相关文件在本基线无 diff 变更（`7d201074` 相对 round-11 修复批仅 4 文件），既有位点结论沿用。 |
| **测试时序纪律（DEF-067/114 类）** | **修复保持** | 抽查 `CapsuleMorphZOrderQuietnessTests` 的 planner 理论（反例组 `CBA→ABC` 等 6 例 + 单遍收敛断言 + 二遍空计划断言）、`SettingsSharedStateConcurrencyTests`（96 并发 × 8 轮守恒）、`R8AbWave12RemediationTests`（源码切片断言意图明确）——断言方向正确、无时间窗漏报型弱断言复发。 |

**无其他改判**：本基线 `7d201074` 相对 round-11 收口（`8a8a5bde`）代码 diff 仅 `cfdf8059` 的 4 文件，其余挂账条目所在文件零变更，round-11 复核结论直接沿用。

---

## 4. 新发现问题清单

### B-01｜桌面整理界面三处硬编码 CJK 顿号「、」作列表分隔符，违反本仓既有的语言感知分隔符模式
- **优先级**：P3（本地化卫生，非中文用户全部可见）
- **位置**：
  - `src/DeskBox/Views/SettingsSections/DesktopOrganizationSettingsSection.xaml.cs:844`（`BuildRuleSummary`：`$"{string.Join("、", values)} · {state}"`）
  - `src/DeskBox/Views/SettingsSections/DesktopOrganizationSettingsSection.xaml.cs:865`（`BuildRuleTokens`：`string.Join("、", values.Take(4))`）
  - `src/DeskBox/Controls/DesktopOrganizationTaskView.xaml.cs:295`（卡住项列表 `string.Join("、", stuck.Take(3)…)`）
- **触发条件**：任何非中文界面语言下查看桌面整理设置页的规则摘要 / 规则启停卡的类型串联 / 撤销恢复提示。
- **影响**：en-US/ja-JP/de-DE 等全部 11 个非中文语言 UI 中出现中文顿号（如 `PDF、Word、Excel · Enabled`），与同页其余本地化文案风格割裂。
- **根因机制**：本仓已确立语言感知分隔符范式——`SettingsViewModel.Performance.cs:279` 与 `SettingsViewModel.AppearanceOptions.cs:629` 均写作 `_localizationService.IsChinese ? "、" : ", "`；这三处为绕过该范式的手写拼接（本轮机械校验的盲区：parity/arity 校验只覆盖 JSON 键，不覆盖代码内硬编码连接符）。
- **证据**：
  ```csharp
  // SettingsViewModel.Performance.cs:277-280（既有正确范式）
  return string.Join(
      _localizationService.IsChinese ? "、" : ", ",
      selected.Select(GetContinuousDecorativeAnimationDisplayName));
  // DesktopOrganizationSettingsSection.xaml.cs:844（硬编码）
  return $"{string.Join("、", values)} · {state}";
  ```
- **建议修法（最小侵入）**：三处改为 `string.Join(T("Common.EnumerationSeparator") …)` 新增 1 键（12 语言同步），或直接复用 `IsChinese ? "、" : ", "` 三元式与既有范式对齐。
- **置信度**：高（代码事实直接可验；范式对照同仓在位）。

### B-02｜快捷方式文件名后缀硬编码英文 `" - Shortcut.lnk"`，非英文 UI 下拖放创建的快捷方式带英文命名
- **优先级**：P3（文件系统可见产物命名，非 UI 文本，故不升级）
- **位置**：`src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ShortcutDrop.cs:96-110`（`GetShortcutDisplayName`，消费点 `:143-145` `FileService.GetAvailablePath(Path.Combine(destination, GetShortcutDisplayName(source))…)`）
- **触发条件**：用户向管理文件夹格子拖放文件/文件夹并按 Alt（或映射为快捷方式的 drop intent）创建快捷方式。
- **影响**：中文 UI 下创建 `报告 - Shortcut.lnk`（Explorer 原生 Alt-drag 生成 `报告 - 快捷方式.lnk`），所有非英文语言一致偏差；`name` 为空时的兜底名也硬编码 `"Shortcut.lnk"`。
- **根因机制**：显示名生成未走本地化服务；同文件 `CreateShortcutFilesAsync` 对快捷方式描述文字已用 `T("Widget.CreateShortcut")`（:151），唯独文件名后缀遗漏。
- **证据**：
  ```csharp
  return string.IsNullOrWhiteSpace(name)
      ? "Shortcut.lnk"
      : name + " - Shortcut.lnk";
  ```
- **建议修法（最小侵入）**：新增 1 本地化键（如 `Widget.ShortcutFileNameSuffix`，en-US 值 ` - Shortcut.lnk`、zh-CN 值 ` - 快捷方式.lnk`），`name + T(后缀键)`；12 语言同步补键。
- **置信度**：高（代码事实）；影响面为每次快捷方式拖放，但产物仅为文件名，故 P3。

### B-03｜DEF-117 修复使 `FolderWatcherService.StartAsync` 可重入：慢解析（离线 UNC）的旧调用可在新调用完成后抢占，watcher 持久错挂旧目录
- **优先级**：P3（边界触发面 + 静默错位、可自愈；与 DEF-117 同场景族，P2 曾议、因需要「离线路径 + 重连 tick 在途 + 用户恰在此窗口改目录」三重条件而维持 P3）
- **位置**：`src/DeskBox/Services/FolderWatcherService.cs:213-231`（await 在 Stop()/代际认领之前）；并发对撞面 `:267-279`、`:851-914`；调用方 `ViewModels/WidgetViewModel.SortingAndWatchers.cs:219-263`
- **触发条件**：格子映射目录为离线网络路径（UNC/网络驱动器，解析耗时数秒）时：①`ReconnectTimer_Tick` 的 `StartAsync(旧路径)` 进入 `await Task.Run(解析)`（UI 线程让出）；②用户此时把格子改挂到另一目录 → `ConfigureFolderWatchersAsync(新路径)` 同步 `Stop()` 后调 `StartAsync(新路径)` 并完整启动成功；③旧调用的解析在数秒后返回并恢复。
- **影响**：恢复后的旧调用执行 `Stop()`（停掉新目录刚建立的 watcher）、覆写 `_requestedPath` 为旧目录、重新读取**当前**代际并通过守卫，最终 watcher 监视旧目录而 UI 内容是新目录——`ProcessFolderChangedAsync` 的 `IsCurrentWatcherBatch` 恒过滤掉变更批次，新目录静默失去实时刷新；健康状态反而显示 Watching。直到用户下一次触发 `ConfigureFolderWatchersAsync`（重新导航/重启）才自愈。
- **根因机制**：DEF-117 修复把「Stop → 写 `_requestedPath` → 读代际」整段从同步段移到了首个 await 之后，但代际认领在 await **之后**才发生——旧调用重新读取的是最新代际，守卫无法识别「自己在 await 期间已被取代」。修复前该段同步一口气执行完毕，语义天然是「后到者赢」；修复后变为「解析完成晚者赢」，恰好让最慢的离线旧调用赢得竞态。
- **证据**：
  ```csharp
  folderPath = await Task.Run(() =>            // :213 让出 UI 线程（新调用可完整插队）
      FileService.TryResolveExistingPathForTraversal(...));
  Stop();                                       // :218 旧调用恢复后 Stop——停掉新目录的 watcher
  lock (_lock) { _requestedPath = requestedPath; } // :224 覆写为旧路径
  int startGeneration;                          // :227-231 此刻才读代际 = 最新值
  lock (_lock) { startGeneration = _watchGeneration; }
  ...
  lock (_lock) { if (_isDisposed || startGeneration != _watchGeneration) return; } // :236 恒通过
  ```
- **建议修法（最小侵入）**：进入 `StartAsync` 时（await 前）先快照 `int entryGeneration = _watchGeneration`；`:213` 的 await 之后、`Stop()` 之前补一段 `lock { if (_isDisposed || entryGeneration != _watchGeneration) return; }`——任何插队的 `Stop()`（调用方 `ConfigureFolderWatchersAsync:223` 或后到 `StartAsync`）都会使代际前移，旧调用在恢复点即退出，恢复「后到者赢」。`ReconnectTimer_Tick` 的解析 await（:874）同理补快照校验（tick 的恢复段若发现已被取代，直接返回即可，健康闪烁亦随之消除）。
- **置信度**：机制高（交错序列可从代码逐行推出）；实际触发频率低（三重条件叠加）。

### B-04｜`CompleteTrackedImportAsync` 跨线程封送 lambda 无守卫：完成路径抛异常时 TCS 永不置位，后台调用方永久挂起且导入忙态卡死
- **优先级**：P3（低频但失败方向为挂起而非静默）
- **位置**：`src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ImportProgress.cs:231-245`（封送 lambda :238-242）；异常源 `CancelAndResetTrackedImport` `:363-372` 的 `_activeImportCancellation.Cancel()`（:367）
- **触发条件**：导入从非 UI 线程收尾（ItemVisuals 传输完成路径 :518-556/:736-764/:2026-2035 等多处）时，封送到 UI 线程的完成流程里 `CancelAndResetTrackedImport` 的 `Cancel()` 恰好因某个取消注册回调抛出 `AggregateException` 而异常。
- **影响**：封送 lambda 在 `completion.TrySetResult(true)` 之前抛出 → 非线程调用方 `await completion.Task`（:243）**永久挂起**（该传输完成任务泄漏），且 `_isImportBusy` 滞留 true：进度卡/取消按钮/命令栏禁用态滞留，下一次导入前不再恢复；异常本体成为 dispatcher 未观察异常（全局兜底仅记日志）。
- **根因机制**：本仓在其他两处明确承认「`Cancel()` 会同步调用注册回调、回调可能抛 AggregateException」并做了防御——`ImportCancelButton_Click:322-334` 专门 `catch (AggregateException)`（且刻意 `Task.Run(cancellation.Cancel)` 使回调离开 UI 线程），`ObserveStackTransitionAsync` 亦然；封送完成路径是唯一漏防的 `Cancel()` 直调点，而它恰好被用于跨线程等待协议的关键置位之前。
- **证据**：
  ```csharp
  DispatcherQueue.TryEnqueue(async () =>
  {
      await CompleteTrackedImportAsync(completionState); // 内部最终调 CancelAndResetTrackedImport
      completion.TrySetResult(true);                     // 异常时永不执行
  });
  await completion.Task;                                 // :243 永久挂起
  ```
- **建议修法（最小侵入）**：封送 lambda 包 try/finally——`try { await CompleteTrackedImportAsync(…); } finally { completion.TrySetResult(true); }`（调用方对该结果只用作「UI 段已完成」的信号，异常语义由 `CompleteTrackedImportAsync` 内部的状态机承担）；可选加固：`CancelAndResetTrackedImport` 的 `Cancel()` 仿 ：324 包一层 `catch (AggregateException)`。
- **置信度**：机制高（代码路径直接可验）；触发需回调恰好抛异常，低频。

---

## 5. 观察项（不够立案标准）

1. **TryEnqueue(async) 守卫自检**：`FileSurfaceContent.xaml.cs:615`（磁盘对账）与 `App.xaml.cs:4634`（轻量内存清理）的 async lambda 内层均有 try/catch（前者 catch-all，后者 try 包裹主体），不并入家族清单；`ImportProgress.cs:238` 的挂起方向已升级为 B-04，不重复计入家族。
2. **搜索热键录制与并发 ContentDialog**：`SearchSettingsSection.xaml.cs:476-500` 录制 KeyDown（async void）→ `ConfirmSearchReservedHotkeyOverrideAsync` `ShowAsync`——同一 XamlRoot 已有打开的 ContentDialog 时 `ShowAsync` 抛异常直穿 async void（全局兜底仅记日志）。触发需要录制手势与另一个对话框打开精确重叠，理论性较强。
3. **`Application.Current.Resources["TextFillColorSecondaryBrush"]` 直接索引器访问**（`DesktopOrganizationSettingsSection.xaml.cs:117/141/562/817`）：平台键当前恒存在；与同文件 `SearchSettingsSection`/`FileSurfaceContent` 的 `TryGetValue` 防御式取用不一致，若平台键更名会由静默缺失变抛错——纯风格一致性观察。
4. **`publish-aot-audit.ps1`（10498 行）未全读**：结构抽样确认 profile 版本 63 与 fork-note 要求的同步链（start-aot-preview / run-aot-*-smoke / 契约测试）在位；完整审读建议随下次 AOT 管线变更专项进行（AGENTS.md 明示该脚本是 WMC1510 漂移的唯一防线）。
5. **`SearchPopupWindow` 入场动画守卫窗口**（:2339-2345）：1200ms 守卫按「最坏 stagger + duration」设定；推荐应用数量极大时 `index * staggerMs` 理论可超窗（当前推荐位数量级下余量充足）。
6. **round-10 §5 / round-11 §5 观察项全部维持**：相关文件本基线零变更（`Exclusion.None` 防御缺口、Updater `RestartApp` 不确认存活、cleanup 脚本不清理空目录、`Localized.cs:88` 缩进、`WidgetToolDialogWindow` Enter 假设、`StackPopover.cs:2280` 同行双语句、依赖包下载无哈希校验、音量滑条同值回写、`WaitForDeskBoxDependencies` 固定轮询）。
7. **孤儿键**：本轮未重扫（方法依赖候选集，R9/R10/R11 三轮分别报 443/573/172，簇分布一致）；`QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 维持遗留观察。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：4（B-01 硬编码顿号 ×3 位点；B-02 快捷方式英文后缀；B-03 FolderWatcher 重入竞态〔DEF-117 修复引入〕；B-04 导入完成封送挂起）
- **总立案数**：4（连续第七轮 P0/P1 = 0；立案数 3 → 4，其中 1 条为上轮修复引入的回归，实质新缺陷 3 条）
- **已知模式新位点**：4 组并入既有编号（DEF-097 +1 文件位点、DEF-078 +1 位点、ANI-06 +5 位点、DEF-080 显示级 +4 位点）
- **存量复核**：round-11 修复批 DEF-117（目标达成但引入 B-03 回归）/ DEF-118 / DEF-119 逐路径复核；无其他改判、无已修复项回退
- **正面结论**：12 语言 2896 键 parity / 占位符 arity / 日期格式字母 / 孤立花括号 / Format 调用点三形态 arity（264 处 0 失配）五项机械校验全绿；XAML 195 资源键 0 失效、748 事件绑定 0 缺失处理器；安装器 4 个消息 .iss 的 12 语言键位 parity 机械对齐；Rust ABI 零漂移（本基线无 native 变更）；`WidgetShellContentHost` 内容事务协议（Prepare/Commit/Rollback/单次 Dispose/ConditionalWeakTable 防重）全文审读零立案；SearchPopupWindow Closed 清理链、rubber-band 协议、入场守卫、SearchPopupViewModel 取消/代际链全部配对；tests 抽查（z-order 契约、并发守恒、R8-AB 源契约）断言方向正确；SettingsSections 8 分部至此全覆盖、订阅-退订纪律良好。

---

## 附：审查方法留档

- 机械校验脚本均为临时 python（stdin 直读），仅读仓库文件、未写任何仓库路径；Format arity 扫描器含 C# 字符串转义态、T() 闭括号、三元 T 多键分支三种形态处理（修正了「T() 闭括号被误计为调用结束」的一版假阳性，见扫描器 v2 结果 0 失配）。
- B-03 的交错序列为静态推导（UI 线程单队列 + `Task.Run` 让出点 + 代际读取位置），未做运行时复现；修法（entryGeneration 快照）不改变 DEF-117 已达成的「解析出线程化」目标。
