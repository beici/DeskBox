# R14-A 核心服务与数据面专项审查报告（round-14）

> 审查基线 commit `435b1a1a`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R14-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 差异化声明

- **基线漂移极小**：R13 基线（`7387efeb`）以来仅一个代码提交 `7c18ed5f`（R13 修复批），`git diff 7387efeb..HEAD` 确认源码仅 `FolderWatcherService.cs`（+13）与 `SearchPopupWindow.xaml.cs`（+11）两个文件变化，其余全部源文件与 round-09~13 五轮深读时的版本逐字节一致，前轮全文审读结论可直接继承。
- **本轮主攻方向**（按任务指令）：
  1. 以挑剔视角逐行验证 round-13 修复批（DEF-125 新守卫 + **FolderWatcherService 守卫网全貌**、DEF-126 子类对称卸载 + 全仓 14 处 comctl32 子类化位点对称性盘点）；
  2. 补齐盲区：`SearchEngineService` 全文（R12 只删过死代码）、`WeatherService` / `GlanceWidgetStore` / `TodoWidgetStore` 全文零视角重读、`TodoItemViewModel`（728 行，**首次全文**）、Helpers 中从未被任何轮次点名的 15 个文件（NativeDrop/Shell 拖放 COM 家族为主）、`NativeShellFileDragProvider`（仅 round-06 S08 提及，本轮首次全文）。
- **不再重复深读**：SettingsService、FileService 家族、DeskBoxDataBackupService、CloudBackup/WebDav/AppUpdate、MusicVolume、ResilientJsonStore、TodoRecurrence、QuickCaptureService、DirectStartup 族、DesktopOrganization 引擎 10 文件、LocalizationService、WidgetManager 全部分部、EverythingSearchService——round-06~13 已全文主审且基线无漂移，本轮仅做台账锚点定位复核。

### 1.2 覆盖率声明

**全文逐行读（本轮新覆盖或修复批复核）**：

- `Services/FolderWatcherService.cs`（1,087 行整读——DEF-125 修复后的第一轮完整复核，重点：五处 await 窗口 × 代际守卫全排列推演，见 §3.1/§3.4）
- `Views/SearchPopupWindow.xaml.cs` 修复段（:78-199、:4490-4553）+ 全仓 `SetWindowSubclass`/`RemoveWindowSubclass` 14 处位点 grep 盘点
- `Services/SearchEngineService.cs`（796 行全文——R9 全读 + R12 删死代码后的首次完整重读，另核其 `Dispose` 对 `EverythingSearchService` 的私有所有权与 `App.xaml.cs:4884-4941` 创建/失败回收/`DisposeSearchServices` 释放链）
- `Services/WeatherService.cs`（606 行全文零视角重读）、`Services/GlanceWidgetStore.cs`（338 行全文）、`Services/TodoWidgetStore.cs`（671 行全文，含 Normalize/收养/TryAdoptOrphanedStoreAsync/RebaseManagedAttachmentPathsAsync）
- `ViewModels/TodoItemViewModel.cs`（728 行，**Todo 家族最后一个首次全文审读的文件**）、`ViewModels/TodoAttachmentViewModel.cs`（88 行全文）、`ViewModels/WidgetViewModel.LayoutAndSettings.cs`（357 行全文）
- **Helpers 零点名文件（15 个中 13 个全文深读）**：`ShellDropDelegator.cs`（409）、`ShellDataObjectBuilder.cs`（232）、`NativeDropComDataReader.cs`（198）、`NativeDropDescriptionWriter.cs`（246）、`NativeDropEffectPolicy.cs`（140）、`NativeDropTargetComInterop.cs`（123）、`NativeDropImageManager.cs`（174）、`ShellDesktopDropTarget.cs`（149）、`ZoneIdentifierWriter.cs`（59）、`ShortcutFileLauncher.cs`（169）、`FileDropIntentPolicy.cs`（124）、`ShellContextMenuHelper.cs`（62）、`FileWidgetIconSizePolicy.cs`/`TextBoxEditorShortcutHelper.cs`/`NeutralInteractionBrush.cs`（模式扫描，零命中）
- `Controls/NativeShellFileDragProvider.cs`（368 行全文——仅 round-06 S08 提及过，本轮首次全文）+ `Helpers/NativeDropTarget.cs` 生命周期段（:43-360 注册/注销/Dispose、:500-540 launch 分派、:690-812 数据对象 AddRef/Release 配对）

**模式扫描 + 命中处精读**：

- 其余零点名小文件按 `async void` / 同步等待 / `Marshal.Release` / `AllocHGlobal` / `new Thread` / `File.Delete` 模式扫描——零命中
- 台账锚点 grep 定点复核（§3.3）
- 原生 vtable 槽位逐一对照本机 Windows SDK 10.0.26100.0 头文件（§3.4 证伪留档）

**未覆盖及原因**：

- round-06~13 已全文主审的大文件（见差异化声明）：基线无漂移，仅锚点复核
- `WidgetManager` 各分部：R13 刚完成 13 个非 ZOrder 分部模式扫描 + 主文件/Storage 全文，基线无漂移，本轮未触碰
- 12 语言 JSON（归 B 代理）；SettingsViewModel 各分部与 AotBindableProperties（UI/绑定装置面，归 B 侧）

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| DEF-070 家族（settings 活列表锁外枚举 × 后台锁内序列化竞态，读侧） | `Services/SearchEngineService.cs:287-288`（`BuildApplicationRecommendations` 经 `GetRecommendationsAsync` 的 `Task.Run` 在线程池枚举活 `Settings.Widgets`） | 与 R8 已记录读侧实例 `DesktopAutoOrganizationWatcher.cs:592-603` 同水位；毫秒级窗口，最坏该次推荐列表异常被上层吞 |
| DEF-070 家族（同上，读侧） | `Services/SearchEngineService.cs:553-555`（`BuildDeskBoxContentSnapshotAsync` 在快照刷新任务内枚举活 `Settings.Widgets`） | 同上；该任务由 `Task.Run`（:458）承载，非 UI 线程；两种快照构建路径合计构成搜索内容面的完整家族位点 |
| DEF-092 家族（死代码残留，内嵌 DEF-049 式恒等笔误） | `ViewModels/TodoItemViewModel.cs:388` `public string MarkerGlyph => HasRedMarker ? "\uE915" : "\uE915";` | 全仓（src+tests+XAML）零消费位点；两分支恒等的条件式本身即笔误化石，说明该属性从未接线即被遗弃；随卫生批删除（若未来接线需先定两态各应显示什么） |
| O-26 家族（await/异常后无代际复核的结算点） | `Services/FolderWatcherService.cs:819-829`（`RestartLegacyWatcherAsync` catch 块无条件 `BeginReconnect(path)`） | try 内全部调用（`ProbeFolderAccessAsync`/`TryStartLegacyWatcher`/`App.Log`）均自吞异常，实际近不可达（OOM 级残余）；与 O-26（ReconnectTimer_Tick catch）同族同水位，仅诊断/极端场景 |

---

## 3. 存量复核（范围内挂账条目现状）

### 3.1 round-13 修复批复核（本轮重点，逐行当前树验证）

| 条目 | 结论 | 证据 |
|---|---|---|
| **DEF-125（StartAsync 成功分支缺代际复核）** | ✅ 修复正确（守卫网全貌另见 §3.4） | `FolderWatcherService.cs:286-297`：query await（:285）返回后、`if (!nativeStarted && !queryStarted)` 判定**之前**插入锁内复核 `_isDisposed \|\| startGeneration != _watchGeneration → return`——一次守卫同时覆盖成功分支（:315-325 的 `SetHealth` + `_reconnectPath=null`）与进入失败分支前的窗口；失败分支原守卫（:302-308，探测 await 后）保持不变。被取代调用的 query 内部认领检查（:389-396，`generation != _watchGeneration` 或 `WatchedPath` 失配 → 释放 query 返回 false）与外部守卫互为冗余双保险。行为推演：旧调用（慢 query 延续）恢复后必然在外部守卫返回，不再覆盖新层健康状态、不再清掉新层已排程重连（R13 A-01 描述的「重连被静默取消」终态消除）。被取代调用已创建的 desktopIni watcher / legacy watcher 由后继调用 `Stop()`（:641/:652）经 Interlocked.Exchange / StopLegacyWatcher 确定性释放，无泄漏、无 double-dispose |
| **DEF-126（SearchPopupWindow 子类对称卸载）** | ✅ 修复正确 | `SearchPopupWindow.xaml.cs:4513-4522`：`OnWindowClosed` 顶部（事件退订链之前）以 `_isPopupCloseWatcherInstalled` 守卫执行 `RemoveWindowSubclass(_hwnd, _popupCloseWatcherProc, PopupCloseWatcherSubclassId)` 并复位字段——与安装点（:168-173）严格对称；`Closed` 触发时 hwnd 仍存活，RemoveWindowSubclass 合法。全仓 14 处 `SetWindowSubclass` 位点逐一 grep 复核，**现有全部位点均有配对 RemoveWindowSubclass**（AppLifecycleRecoveryWatcher/DisplayAreaWatcherService/GlobalHotkeyService/SearchHotkeyService/WidgetDisplayChangeWatcher/ContentWidgetWindow.NativeDragDrop/DesktopOrganizationWindow/OnboardingWindow/SearchPopupWindow/ReleaseNotesWindow/WidgetWindowBase.Bounds/WidgetWindowBase.Grouping/StackPopoverHostWindow/SettingsWindow），「全仓唯一无对称卸载」的状态消除。NC_DESTROY 兜底省略理由已按 R13 完善性审查留档 |

### 3.2 FolderWatcherService 守卫网全貌（DEF-117→123→125 三轮累积后的整网复核）

对 `StartAsync` 与 `ReconnectTimer_Tick` 的全部 await 窗口 × 代际守卫做了完整清点（不限于 R13 修复点）：

| await 窗口 | 守卫 | 状态 |
|---|---|---|
| StartAsync 入口路径解析（:226-229） | entryGeneration 快照（:213-217）+ await 后复核（:231-237） | 闭合 |
| StartAsync 可用性探测（:254） | startGeneration 快照（:248-252）+ 复核（:255-261） | 闭合 |
| StartAsync WatchedPath 认领（:272-281） | 认领前复核（:272-277） | 闭合 |
| StartAsync query 启动（:285） | **DEF-125 新守卫（:286-297）** | 闭合（本轮验证） |
| StartAsync 双失败分支探测（:301） | 复核（:302-308） | 闭合 |
| Tick 路径解析（:916-922） | entryGeneration 快照（:892-898）+ 复核（:924-930） | 闭合（R12 W1） |
| Tick 可用性探测（:932） | 复核（:933-939） | 闭合（R12 W1） |

关键交错推演（补充验证，非重复 R13 结论）：`TryStartQueryWatcherAsync` 内部认领（:389-399）与本轮新外部守卫（:286-297）之间**无 await**（`return true` 后 `:285` 的 await 延续在 UI 线程同步完成），故「内部认领成功 × 外部守卫失败」的窗口不存在——内部检查失败必然蕴含外部守卫失败，二者判定源同为本调用快照代际与同一把锁内的 `_watchGeneration`，语义自洽。**守卫网五个 await 窗口全部闭合，未发现新的缺口**；残余仅 §2 所列 O-26 家族两个近不可达 catch 结算点与 O-21 的 BeginReconnect 毫秒级 TOCTOU（均维持既有水位）。

### 3.3 挂账条目锚点复核

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| DEF-050（MSN 日期解析失败仍追加空行） | 维持 | `WeatherService.cs:497-504`：`TryParse` 失败时 `dateStr` 保持空串仍 `daily.Time.Add(dateStr)`（本轮全文重读确认） |
| DEF-051（远程 JSON 无大小上限） | 维持 | `WeatherService.cs:83,413,442`：三处 `GetStringAsync` 无上限（本轮全文重读确认；GlanceImageService 位点沿用 R9 证据，基线无漂移） |
| DEF-053（内容刷新任务引用竞态） | 维持 | `SearchEngineService.cs:511-517`：finally `lock (_deskBoxContentRefreshLock)` 内无条件 `_deskBoxContentRefreshTask = null`，不校验 ReferenceEquals（本轮全文重读确认，行号与 R13 一致） |
| DEF-080（ar-SA 天气解析/缓存错乱） | 维持 | `WeatherService.cs:501,526,528,561` 四处 `ToString("yyyy-MM-dd…")` 无 InvariantCulture（本轮全文重读逐行确认） |
| O-2（WeatherService 缓存字段组跨线程无同步） | 维持 | `WeatherService.cs:37-40` 字段、`:146-160` 读、`:194-207` 写，机制未变（本轮全文重读确认） |
| O-8（Stop 读 `_queryWatcher` 锁覆盖不对称） | 维持 | `FolderWatcherService.cs:629-637`：Stop 的读+置 null 仍不持锁，依赖 UI 线程串行化（本轮整读确认） |
| O-21（守卫-结算间毫秒级 TOCTOU）/ O-26（Tick catch 无代际复核，现位点 :956-971） | 维持 | 本轮整读确认机制未变；O-26 家族新位点见 §2（RestartLegacyWatcherAsync catch） |
| DEF-048/049/052/056/076/078/079/082/EXC-06/MEM-01/02/DEF-070 家族/DEF-101 等 | 维持（基线无漂移） | 本轮未读文件沿用 R13 证据（`git diff 7387efeb..HEAD` 确认未漂移）；DEF-070 家族本轮另有读侧新位点两处（§2） |
| ARC-04（DI 注册未接线，WeatherService 单例） | 维持（O-22 所有权陷阱依旧成立） | `ServiceRegistry.cs:42` 单例注册仍在、无消费方；`WeatherWidgetContentProvider` 逐格子新建的所有权结构未变（基线无漂移） |
| **正面再证：SearchEngineService 对 EverythingSearchService 无 O-22 式所有权陷阱** | — | `App.xaml.cs:4895-4900` 二者成对 new、`DisposeSearchServices` 只 Dispose `_searchEngineService`（其 `Dispose` 内部释放 `_everythingSearchService`，SearchEngineService.cs:794）后同置 null——私有所有权链路完整，先关 popup 再逐层释放的次序正确 |

### 3.4 证伪留档（避免后续轮次重复怀疑）

- **`NativeDropImageManager` 的 IDropTargetHelper 裸 vtable 槽位**：初判疑似「DragEnter/DragLeave 槽位互换」（凭印象该接口 IDL 以 DragLeave 起始）。经本机 SDK `shobjidl_core.h`（10.0.26100.0，:12035 起）逐槽核对，`IDropTargetHelper` 实际顺序为 DragEnter=3 / DragLeave=4 / DragOver=5 / Drop=6 / Show=7——代码常量（NativeDropImageManager.cs:14-18）**全部正确**，疑点证伪，不立案。
- **`ShellDropDelegator` 的 IDropTarget 槽位**（DragEnter=3/DragLeave=5/Drop=6/Release=2）：经本机 SDK `oleidl.h` 核对 `IDropTarget` 顺序 DragEnter/DragOver/DragLeave/Drop，**全部正确**（虽然该方法体本身为死代码，见 A-01）。
- **`NativeOleDataObject` 的 IDataObject 槽位**（GetData=3/QueryGetData=5/SetData=7）与 **`NativeComStreamReader` 的 ISequentialStream::Read=3**：经本机 SDK `objidl.h` 逐槽核对（GetData/GetDataHere/QueryGetData/GetCanonicalFormatEtc/SetData=3/4/5/6/7；Read/Write=3/4），**全部正确**。
- **ZoneIdentifierWriter `HostUrl=` 注入疑点**（换行注入伪造 ZoneTransfer 行）：逐一核对三处调用点——`DeskBoxDragData.cs:510` 传 `uri.AbsoluteUri`（Uri 类型不允许裸换行）、`DeskBoxDragData.cs:596` 与 `NativeDropTarget.cs:1431` 传常量 `VirtualDropSource`——**无注入向量**，不立案。
- **`NativeShellFileDragProvider.SetDataObject` 的 vtable[4]**（`IDataObjectProvider`，GUID 3D25F6D6-…）：该接口为 WinRT 内部接口、SDK 头文件不可核对；查询/QI/Release 三处自建槽位（0/2）正确，vtable[4] 与社区已知互操作技法一致且特性按实测工作。**可核对性边界，非缺陷**，留档避免反复怀疑。
- **`TodoWidgetStore` 门控对读路径的覆盖**：`SearchEngineService.cs:561` 每次 `BuildDeskBoxContentSnapshotAsync` 以 `new TodoWidgetStore(widget.Id).LoadAsync()` 读 todo 数据——构造器副作用（`Directory.CreateDirectory` + `TrimPathGates`）为微秒级、门为静态 per-path 共享（TodoWidgetStore.cs:33-34,66），**读与写同门串行，不构成 DEF-108/109 式旁路**，不立案。

---

## 4. 新发现问题清单

### A-01 ｜ ShellDropDelegator 委托发射子系统整体为死代码，且其设计依据文档与在用路径的实测结论互相矛盾 ｜ P3

- **位置**：`src/DeskBox/Helpers/ShellDropDelegator.cs:149-248`（`TryDelegateDrop` 及 `BindShortcutDropTarget`/`CallDragEnter`/`CallDrop`/`CallDragLeave`/`InvokeEffectCall` 全链）、`src/DeskBox/Helpers/ShellDataObjectBuilder.cs:56-146`（`TryCreateHdropDataObject`）；陈旧注释 `src/DeskBox/Helpers/NativeDropTarget.cs:506-509`
- **触发条件**：静态存在（死代码 + 文档矛盾），无运行时触发；风险在未来接线/复用。
- **影响**：
  1. **死子系统**：`TryDelegateDrop`（Shell IDropTarget 委托发射引擎，约 200 行）与 `TryCreateHdropDataObject`（合成 CF_HDROP 数据对象，约 90 行）在全仓（src + tests，含 XAML/反射面 grep）**零调用点**；`ShellDropDelegator.ReleaseObject` 仅被同样已死的 `TryCreateHdropDataObject` 引用。活的发射路径是 `NativeDropTarget.LaunchDropHandler → ContentWidgetWindow.NativeDragDrop.HandleNativeLaunchDrop` 与 `FileSurfaceContent.ItemVisuals.cs:1244,1353`，二者均走 `ShortcutFileLauncher.TryLaunchWithFiles`（ShellExecuteEx）。
  2. **设计依据互相矛盾**：`ShellDropDelegator.cs` 类注释宣称这是「the exact code path Explorer runs when files land on a .lnk……so exe targets launch with the dropped files as arguments」；而 `ShortcutFileLauncher.cs:30-34` 的实测注释明确记载**正是这条 IDropTarget 委托路径在真机上「DragEnter 接受、Drop 回答 DROPEFFECT_NONE、Windows 弹 Open with 选择器」**（docs/architecture/drop_on_shortcut_open.md §10.2），因此被放弃并改走 ShellExecuteEx。同一机制的两份文档结论完全相反，且与死代码同存的 `NativeDropTarget.cs:506-509` 注释仍描述「delegates to the Shell drop target synchronously while the source data object is alive」——活分派点描述与实际实现脱节。
  3. **复活风险**（DEF-112 反面教材同族）：未来维护者读到 `ShellDropDelegator` 的类注释（自称与 Explorer 行为一致、AOT 安全、环境已擦洗）而 `TryDelegateDrop` 恰好是现成的完整实现，极可能「顺手接线」，把已被实测否定的「Open with 弹窗」行为与第二套发射语义重新引入；`BuildHdropBytes` 另有 6 个测试用例钉死其序列化契约（ShellDataObjectBuilderTests），但生产侧无消费方——测试守护的是死路径。
- **根因机制**：发射方案切换（IDropTarget 委托 → ShellExecuteEx）时未删除旧实现及其注释；切换决策只写进了新实现的注释，旧实现的注释保留着被推翻前的旧结论。
- **证据**：
```csharp
// 全仓 grep（src+tests）："TryDelegateDrop" / "TryCreateHdropDataObject" 零命中（自身定义除外）
// 活分派链（NativeDropTarget.cs:211,522-528）：
internal Func<NativeDropLaunchRequest, ShellDropLaunchResult>? LaunchDropHandler
    ... ShellDropLaunchResult launch = launchHandler(new NativeDropLaunchRequest(...));
// 唯一赋值（ContentWidgetWindow.NativeDragDrop.cs:113）：
target.LaunchDropHandler = HandleNativeLaunchDrop;   // 内部走 ShortcutFileLauncher.TryLaunchWithFiles
// 矛盾注释（ShortcutFileLauncher.cs:30-34）：
// "Deliberately not using the shortcut's own IDropTarget: on real hardware its
//  DragEnter accepts but Drop answers DROPEFFECT_NONE and Windows then shows
//  its 'Open with' picker (measured 2026-09-12 ...)"
// vs ShellDropDelegator.cs:100-104："the exact code path Explorer runs when files land on a .lnk"
```
- **建议修法（最小侵入）**：随下一批卫生提交整体删除 `ShellDropDelegator.TryDelegateDrop` 链与 `ShellDataObjectBuilder.TryCreateHdropDataObject`（`ReleaseObject` 若无他引用随之删除；`BuildHdropBytes` 及其测试若保留需在生产侧注明预期消费点，否则一并删除）；同时把 `NativeDropTarget.cs:506-509` 的分派注释改为与 `HandleNativeLaunchDrop`/`ShortcutFileLauncher` 一致。若保留意图是未来回切，至少在 `ShellDropDelegator.cs` 头部注明「与 ShortcutFileLauncher 实测结论冲突，见 drop_on_shortcut_open.md §10.2」。
- **置信度**：高（零调用点为全仓 grep 实证；文档矛盾为两处注释逐字对照；死代码判定不受运行时行为影响）。

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-27 | `TodoWidgetStore.RebaseManagedAttachmentPathsAsync`（孤儿 store 收养后的附件路径重写）采用分离 `LoadAsync`/`SaveAsync`（门在两次调用间释放），属 DEF-043 家族残留形态；若孤儿收养的毫秒级窗口内恰有 `TodoReminderService` 对目标事项 `MutateAsync`，重写侧整文档保存可覆盖提醒标记（重复提醒一次）。收养是每次新 Todo 组件创建的一次性路径、目标 id 此前从未存在，需提醒扫描恰好命中且时序重叠，近理论；如收口可改 `MutateAsync` 一处调用 | `Services/TodoWidgetStore.cs:650-668` | 高（机制）/ 极低（触发面） |
| O-28 | `SearchEngineService.EnsureDeskBoxContentSnapshotAsync` 对 `forceRefresh:true` 的请求若遇在途刷新会直接复用该任务（:443-446）：QuickCapture 变更落在在途刷新启动之后时，其内容要等下一次刷新（≥1s 间隔节流）才进入快照——自愈式短暂滞后，语义属 1s 去抖的有意代价；另注每次快照刷新按 widget 数构造临时 `TodoWidgetStore`（:561，构造器含 `Directory.CreateDirectory` + `TrimPathGates`，微秒级）。仅记录 | `Services/SearchEngineService.cs:443-446,561` | 高（机制）/ 无（取舍） |
| O-29 | `SearchEngineService.Dispose` 先 `_deskBoxContentLifetimeCts.Cancel()` 再 `Dispose()`：在途快照刷新任务的取消路径由 `RefreshDeskBoxContentSnapshotAsync` 的 OCE catch（:500-503）吸收，`Task.Run(..., token)` 的预热取消转为 Canceled 状态任务（无 UnobservedTaskException 面）——生命周期语义核验正确，唯 `_deskBoxContentLastRefreshMs` 在取消分支不更新属无影响细节。仅留档 | `Services/SearchEngineService.cs:471-518,781-795` | 高 |

---

## 6. 统计

- **新立案**：1 条 —— P0×0，P1×0，P2×0，**P3×1**（A-01：ShellDropDelegator/ShellDataObjectBuilder 死发射子系统 + 设计文档互相矛盾）
- **观察项**：3 条（O-27 ~ O-29）
- **已知模式新位点**：4 行（DEF-070 家族读侧×2、DEF-092 家族×1（内嵌 DEF-049 式恒等笔误）、O-26 家族×1）
- **round-13 修复批复核**：DEF-125/126 逐行复核**全部正确落地**；FolderWatcherService 五个 await 窗口的守卫网全貌清点闭合（§3.2），DEF-123→125 三轮累积后**未发现新缺口**；全仓 14 处 comctl32 子类化位点对称卸载盘点完毕；O-20（R12）/DEF-125（R13）两代守卫缺口均确认消除
- **证伪留档**：6 项（IDropTargetHelper / IDropTarget / IDataObject / ISequentialStream 四组 vtable 槽位经本机 SDK 10.0.26100.0 头文件逐槽核对全部正确、ZoneIdentifierWriter 注入疑点无向量、TodoWidgetStore 门控覆盖读路径）
- **零审查历史盘点结论**：Helpers 15 个从未点名文件中 13 个全文深读（NativeDrop/Shell 拖放 COM 家族约 2,300 行）+ Controls/NativeShellFileDragProvider 首次全文；TodoItemViewModel 全文审读后 **Todo 双 ViewModel 家族全部成员至此均被全文审读过一轮**；SearchEngineService/WeatherService/GlanceWidgetStore/TodoWidgetStore 零视角重读除 §2/§3.3 所列既有水位位点外零新增
- **总体结论**：连续第九轮 P0/P1 = 0。round-13 修复批质量良好：DEF-125 守卫一次到位且与其余四个守卫位点语义一致，DEF-126 补齐了全仓最后一处子类化不对称。本轮唯一立案为死代码与文档卫生级位点；拖放 COM 家族（此前从未被审读的高风险互操作面）经全文审读 + SDK 逐槽核对处于良好工程水位（限额、配对释放、AOT 安全边界均有先例可循），核心服务与数据面维持历史最高加固水位。
