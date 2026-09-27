# R11-A 核心服务与数据面专项审查报告（round-11）

> 审查基线 commit `8a8a5bde`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R11-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 范围与体量

- `src/DeskBox/Services/`：316 个 .cs，约 95,000 行
- `src/DeskBox/Models/`：60 个 .cs；`src/DeskBox/Contracts/`：2 个 .cs（round-09/10 已全读，本轮未改动面）
- `src/DeskBox/Helpers/`：46 个 .cs；`src/DeskBox/Sync/`：4 个 .cs（R10 全读维持）
- `App.xaml.cs` + 非 smoke 分部（App.Startup / App.Tray / App.DesktopOrganization / App.DiagnosticsBundle / App.ImmediateHiddenWorkingSetTrim / App.QuiescenceWorkingSetTrim）

### 1.2 覆盖率声明（与 round-09/10 深读面的差异化）

**全文逐行读（本轮新覆盖，此前仅 grep 或从未被任何轮次点名）**：

- `QuickCaptureService.cs`（2,207 行全读——R9/R10 仅读头部 gate 布局，R11 首次全文）
- `FolderWatcherService.cs`（1,016 行全读）
- `DragDropPermissionService.cs`（1,242 行全读，含全部 P/Invoke 结构体/接口声明逐项核对）
- `DirectStartupService.cs`（642 行全读）+ `DirectStartupService.Modes.cs`（151 行全读）+ `DirectStartupTaskXmlReader.cs`（104 行全读）
- `GlanceTraditionalCalendarService.cs`（368 行全读——DEF-054 自 R8 后首次实读）
- 首次实读的未点名文件：`NativeNotificationActivationEnvelopeStore.cs`（642 行）、`NativeAppNotificationService.cs`（386 行）、`TodoNotificationActivationRouter.cs`（332 行）、`ThemeService.cs`（284 行）、`DisplayAreaWatcherService.cs`（270 行）、`QuickLookPreviewService.cs`（270 行）、`MemoryReclaimer.cs`（189 行）、`ReleaseNotesService.cs`（283 行）、`FeedbackService.cs`（361 行）、`ManagedStoragePathService.cs`（303 行）、`GlanceFestivalService.cs`（121 行）、`PasswordVaultCredentialStore.cs`（134 行）、`StartupService.cs`、`LegacySearchIndexCleanupService.cs`、`MarkdownDocumentService.cs`、`MarkdownInlineImageExtractor.cs`（全文）、`StoreAppUpdateService.cs`（211 行）、`AppRelaunchService.cs`、`SystemFontCatalogService.cs`

**grep 定向扫描 + 命中处抽读**：

- 其余 30+ 未点名小文件（Glance 布局计算器族、策略/菜单构造族、Tracker 族、`ManagedStorageDesktopShortcutService`、`QuiescenceWorkingSetTrimTracker`、`SearchResultCollectionReconciler` 等）：按 `async void` / 同步等待 / 进程句柄 / 文件删除 / 文化敏感格式化 / `AllocHGlobal` 模式扫描，命中位点逐个精读
- Helpers 全部 46 文件横向扫描（COM 释放 / `Marshal.ReleaseComObject` / 删除位点），命中处（`ExplorerShellLaunchService:169`、`NativeDropTarget` 六处自建文件清理）逐一核实为自建对象清理
- App 分部抽检：`App.Log` 管道（有界队列 + Interlocked + 信号量，线程安全）、`SafeFireAndForget`、`ThreadPool.RegisterWaitForSingleObject` 激活监听、通知激活回调 `QueueNativeNotificationActivation`（带 try）
- 台账位点锚定复核（§3）

**未覆盖及原因**：

- `SettingsService.cs` / `FileService` 家族 / `DeskBoxDataBackupService.cs` / `CloudBackupService.cs` / `WebDavBackupTransport.cs` / `AppUpdateService.cs` / `MusicVolumeService.cs` / `TodoReminderService.cs`（除锚定点外）：round-06~10 已全文主审，本轮按台账锚点定点复核，未再全文重读
- `WidgetManager` 各分部 / `WidgetLayerService.cs`：round-06~09 反复主审面，本轮未触碰
- `ManagedStorageMigrationDialog.cs`（UI 对话框，归 B 面）；12 语言 JSON（归 B 代理）
- `models` 60 文件：R10 已按 AOT source-gen 契约扫描，本轮无 AOT 面变更迹象，未重扫

### 1.3 证伪留档（避免后续轮次重复怀疑）

- **`DirectStartupTaskXmlReader.cs` 裸 vtable 槽位**：对照本机 Windows SDK `taskschd.h`（10.0.26100.0）逐槽核对全部正确——`ITaskService`（IUnknown 0-2 + IDispatch 3-6 后）：`GetFolder`=7、`Connect`=10；`ITaskFolder`：`get_Name`=7、`get_Path`=8、`GetTask`=**13**（顺序 get_Name/get_Path/GetFolder/GetFolders/CreateFolder/DeleteFolder/GetTask/…）；`IRegisteredTask`：`get_Xml`=**20**（…19=get_Definition、20=get_Xml）；`Release`=2。`EmptyVariant` 24 字节 x64 布局、`CoInitializeEx(0,0)`=COINIT_MULTITHREADED、RPC_E_CHANGED_MODE 分支不配对 CoUninitialize 的语义均正确。**不立案**。
- **`DragDropPermissionService` 的 `IShellLinkW` 接口声明**：18 个方法与 IDL 顺序一致（GetPath…SetPath），仅调用 GetPath/GetArguments/写侧三方法，无槽位风险。**不立案**。
- **`DirectStartupService.DisableCore`（:290）持 `_registrationLock` 时调用 public `GetState()`**：C# Monitor 可重入，同线程无死锁。**不立案**。
- **`FolderWatcherService` 的 `StartAsync`/`Stop` 并发面**：全部调用点（VM 文件夹切换、`ReconnectTimer_Tick`、Dispose）均在 UI 线程串行，generation 检查完整，无交错窗口。**不立案**。
- **`QuickCaptureService` 的 `Changed` 事件在 `_gate` 持有期间同步触发**（`SaveCoreAsync` :1557-1564）：两个订阅者（`QuickCaptureWidgetViewModel.SettingsAndRefresh.cs:14` 走 `TryEnqueue`、`SearchEngineService.cs` fire-and-forget）均无同步再入服务方法路径，无信号量重入死锁面。**不立案**。

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| DEF-080 家族（文化敏感日期进用户可见文本/文件名） | `Services/TodoClipboardFormatter.cs:122-123` | `localValue.ToString("yyyy/M/d HH:mm")` current culture——剪贴板纯展示文本，无机器解析消费方，ar-SA 下仅显示历法/数字外观与用户预期不符 |
| DEF-080 家族（同上，文件名外观级） | `Services/DeskBoxDiagnosticsBundleService.cs:350` | 诊断包文件名 `generatedAtUtc.ToLocalTime():yyyyMMdd-HHmmss` current culture——ar-SA 下含阿拉伯-印度数字；与 `File.Exists` 冲突检测循环自洽，无解析消费方 |
| DEF-054 家族（历法近似公式） | `Services/GlanceFestivalService.cs:110-115` | 清明日经验公式 `shortYear*0.2422 + centuryConstant − shortYear/4`，世纪常数仅 1901–2099 两档（`year<2000?5.59:4.81`），2100 起及公式例外年份会错标清明；与 DEF-054 同属「近似算法」水位 |
| DEF-101 家族（裸 `?.Invoke` 广播无逐处理器隔离） | `Services/StoreAppUpdateService.cs:164`（`CheckCompleted`） | `AppUpdateService.cs:644` 的 Store 孪生实现（经 `AppUpdateServiceFactory` 二选一），同文件同族位点；订阅者异常中断后续通知 |
| THR-06 家族（跨线程裸读写状态字段，诊断面） | `Services/FolderWatcherService.cs:99,167,226,361,438,491,556,698,781` | `_lastError` 由 threadpool 回调裸写（部分写点在锁内、部分在锁外，:681/:892 在锁内）、`Health`（:167）无锁读；同文件的 `_lastEventAtTicks`/`_health` 已按 FTHR-05 用 Interlocked/Volatile，二者水位不一。仅诊断用途 |

---

## 3. 存量复核（范围内挂账条目现状）

「维持」= 本轮当前树锚定/沿用 R10 证据且未见机制漂移。

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| DEF-048（WeatherCodeMapper 小写比较未实现） | 维持 | `Helpers/WeatherCodeMapper.cs:99`：注释仍在，`string d = description.Trim()` 无 ToLower（行号 98→99 微移） |
| DEF-049（MSN icon 29→29） | 维持 | `WeatherCodeMapper.cs:209`：`29 => 29`（行号微移） |
| DEF-050（MSN 日期解析失败追加空行） | 维持 | 沿用 R10 证据（`WeatherService.cs:496-503`），本轮未重读该段 |
| DEF-051（远程 JSON 无大小上限） | 维持 | 沿用 R10 证据；注：本轮实读的 `ReleaseNotesService` / `MarkdownInlineImageExtractor` / `NativeNotificationActivationEnvelopeStore` 三处新面均自带读取上限，无同族新增 |
| DEF-052（FileMetaService 失败 null 永久缓存） | 维持 | `FileMetaService.cs:164`：`GetOrAdd(key, k => LoadIconAsync(k))` 无失效机制（在途不淘汰已修维持） |
| DEF-053（内容刷新任务引用竞态） | 维持 | `SearchEngineService.cs:515`：finally lock 内无条件 `_deskBoxContentRefreshTask = null`（DEF-112 删除后位点 :553→:515） |
| DEF-054（Saka/Bangla 历近似漂移） | 维持（**本轮首次实读复核**） | `GlanceTraditionalCalendarService.cs:187-260`：Saka 侧固定起点（闰年 3/21、常年 3/22）+ Chaitra 30/31 + 5×31 + 6×30 与印度国家历定义一致，近似成分主要在 Bangla 侧——固定 4 月 14 日起点（孟加拉国 2019 修订版；bn-IN 用户偏差一日）+ 均匀月长表（:251）。同机制同位点，不重复立案 |
| DEF-056（Migration_1_To_2 与 2_To_3 重复段） | 维持 | 未触碰（R9 证据沿用） |
| DEF-076（遗留 settings.json 一次性迁移非原子） | 维持 | `SettingsService.cs:941`：`await File.WriteAllTextAsync(_settingsPath, json)` 仍裸写 |
| DEF-078（SMTC `Try*Async` 裸调簇） | 维持 | 沿用 R10 证据（`MusicSessionService.cs:235-266`），本轮未重读 |
| DEF-079（JIT 音量后端 Volume RCW 不释放） | 维持 | 沿用 R10 证据（`MusicVolumeService.cs:538-541`），本轮未重读 |
| DEF-080（ar-SA 天气解析/缓存错乱） | 维持 | `WeatherService.cs` 当前树 grep `ToString("yyyy-MM-dd` 仍 4 处；家族新位点见 §2 |
| DEF-082 / MEM-01 / MEM-02 / EXC-06 / THR-06 | 维持 | 沿用 R10 证据，本轮未见变化迹象 |
| DEF-090（自适应阶梯死代码删除延后）/ DEF-096 / DEF-101（泛化） | 维持 | 按台账维持；DEF-101 新位点见 §2（StoreAppUpdateService） |
| O-1~O-14（R9/R10 观察项） | 维持 | 本轮未发现任何一条可解除或升级的证据；O-14（`DirectStartupTaskBackend.RunSchtasks` 同步 Wait 10s）在本轮 `DirectStartupService` 全读中与调用面（`GetStateCore` 三次 `_taskBackend.Read()` 串行）吻合，水位不变 |

### round-09/10 修复批复核（本轮重点）

| 条目 | 结论 | 证据 |
|---|---|---|
| DEF-110（提醒去重集合） | ✅ 在位 | `TodoReminderService.cs:31`：`ConcurrentDictionary<string, byte>`；:31 锚定 |
| DEF-111（四重 BOM） | ✅ 在位 | `head -c 6 \| od -c` → `357 273 277 u s i`，单 BOM |
| DEF-112（死代码删除） | ✅ 在位 | 全仓 grep `GetRecentNotesAsync\|GetUpcomingTodosAsync` 零命中 |
| DEF-113（更新器 restart-failed） | ✅ 在位 | `DeskBox.Updater/Program.cs:66`：`RestartAfterIncompleteUpdate(options, "restart-failed")` |
| DEF-114（测试时间窗） | ✅ 在位 | R10 双代理已复核，本轮未重读测试文件 |
| **DEF-115（循环克隆 StorageMode + 引用扫描）** | ✅ 修复正确 | `TodoRecurrenceService.cs:86`：克隆初始化器已补 `StorageMode = attachment.StorageMode`；`TodoWidgetViewModel.DetailAndAttachments.cs:389-407`：`DeleteAttachmentAsync` 物理删除前以 `Items.Any(other => …shared.FilePath == attachment.FilePath…)` 做同 store 引用扫描（OrdinalIgnoreCase），扫描跳过自身（`!ReferenceEquals(other, item)`），删除包 try/catch + 日志。语义与 R10 契约用例一致 |
| **DEF-116（Format arity 对齐）** | ✅ 修复正确 | `TodoItemViewModel.cs:582`：`Format("Todo.RecurrenceHistory.Collapse")` 单参、死实参已去；`SearchPopupViewModel.cs:691-697`：PartialResults 分支独立 `string.Format` 仅传 `TotalResultCount`（{0}），Results 分支保留双参——与键占位符逐一对应 |

---

## 4. 新发现问题清单

### A-01 ｜ FolderWatcherService 重连/双失败路径在 UI 线程同步探测失效文件夹（含 UNC 阻塞） ｜ P3

- **位置**：`src/DeskBox/Services/FolderWatcherService.cs:265-271`（双失败分支同步 `ProbeFolderAccess`）、`:288-310`（同步探测体：`Directory.Exists` + `Directory.EnumerateFileSystemEntries().Take(1).ToList()`）、`:861-866`（`ReconnectTimer_Tick` 内同步 `FileService.TryResolveExistingPathForTraversal`）、`:211-216`（`StartAsync` 入口同款同步解析）；对照主路径 `:233` 已用 `Task.Run`（`ProbeFolderAccessAsync`）
- **触发条件**：文件夹格子指向离线的网络路径（断开的映射盘/UNC），或本地目录被移除后进入重连循环。每次重连 tick（2s 起、退避至 180s 平台）都在 UI 线程执行 `StartAsync`；当原生 watcher 与 item-query 双双启动失败（离线路径必然如此），:267 额外执行一次同步 `ProbeFolderAccess`。
- **影响**：`Directory.Exists` / 属性枚举对不可达 SMB 目标同步阻塞直至网络超时（实测常识量级：数秒），期间 UI 冻结；退避期内每个重连 tick 重复一次。同路径解析 `TryResolvePathSegments` 逐段做属性查询，同样是调用线程同步 IO。
- **根因机制**：同一文件内「主路径探测走 `Task.Run`、双失败分支与重连入口裸用同步探测」的不一致；重连 tick 本身是 `DispatcherQueueTimer.Tick`（UI 线程）。与 THR-07/DEF-035（UI 线程同步 IO）同族，但触发面限于网络文件夹离线场景，故 P3。
- **证据**：
```csharp
// FolderWatcherService.cs:265-271（StartAsync 尾段，UI 线程）
bool nativeStarted = TryStartLegacyWatcher(folderPath);
bool queryStarted = await TryStartQueryWatcherAsync(folderPath, generation);
if (!nativeStarted && !queryStarted)
{
    SetHealth(ProbeFolderAccess(folderPath) == FolderWatcherHealth.AccessDenied   // ← 同步
        ? FolderWatcherHealth.AccessDenied
        : FolderWatcherHealth.Unavailable);
    BeginReconnect(folderPath);
}
// :233（同方法主路径）—— FolderWatcherHealth availability = await ProbeFolderAccessAsync(folderPath);
```
- **建议修法（最小侵入）**：:267 的 `ProbeFolderAccess(folderPath)` 改用现成的 `await ProbeFolderAccessAsync(folderPath)`（方法已在 async 上下文，零行为差异）；`ReconnectTimer_Tick`/`StartAsync` 的 `TryResolveExistingPathForTraversal` 可包 `Task.Run` 或容忍首帧延迟。仅消除 UI 阻塞，探测结果与健康分类语义不变。
- **置信度**：高（线程归属、同步 IO 与双失败分支可达性均可静态确证；实际阻塞时长取决于 OS/网络栈，未运行验证）。

### A-02 ｜ DragDropPermissionService 诊断路径的 Process 对象未 Dispose，进程句柄延迟到终结器释放 ｜ P3

- **位置**：`src/DeskBox/Services/DragDropPermissionService.cs:804-828`（`GetExplorerTokenSnapshot`：`Process.GetProcessesByName("explorer").FirstOrDefault()`，`explorer` 与返回数组的其余元素均无 using/Dispose）
- **触发条件**：设置页打开「拖放权限诊断」（`SettingsViewModel.DragDropDiagnostics.cs:52`）或执行修复（`:93` → `Repair` 内 `Diagnose()` 前后各一次）。每次调用泄漏 ≥1 个 `Process` 实例（`GetProcessesByName` 数组中未被选中的元素同样各自持一个进程句柄）。
- **影响**：进程句柄与关联的非托管资源延迟至 GC 终结器释放；诊断属低频操作，无增长型泄漏，属卫生面。与本仓已确立的显式释放规范相悖——同库 `QuickLookPreviewService.cs:191-199`（`foreach … using (process)`）与 `MemoryReclaimer.cs:175`（`using Process process`）均已用 using。
- **根因机制**：`Process` 实现 IDisposable 且其句柄依赖终结器兜底；`FirstOrDefault()` 只保留一个引用，数组其余元素被遗弃。
- **证据**：
```csharp
// DragDropPermissionService.cs:804-806
Process? explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
if (explorer is null) { … }
IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)explorer.Id);
// explorer 全程无 using/Dispose；对照 QuickLookPreviewService.cs:191-194:
//   foreach (Process process in Process.GetProcessesByName(ProcessName)) { using (process) { … } }
```
- **建议修法（最小侵入）**：`using var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();`（`Process` 为引用类型，`using var` 对 null 安全），其余逻辑不变。
- **置信度**：高（机制教科书级且同仓对照可证）；影响评级 P3（低频、非增长）。

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-15 | `QuickCaptureService.GetThumbnailPath`（:2144-2150）的兜底 `ComputeContentHash(File.ReadAllBytes(imagePath))` 为同步全量读盘，但仅当路径文件名（去扩展名）为空时触发（如 `dir\.png`），而所有ImagePath 均由 `SaveImageAsync`/`SaveImageFileAsync` 以 SHA256 内容哈希命名，实际不可达；如做防御可改为异步或直接返回 null | `QuickCaptureService.cs:2144-2150` | 高（机制）/ 无（触发面） |
| O-16 | `FeedbackService.ClientId` 惰性 `??= LoadOrCreateClientId()`（:94）无双检锁：两线程并发首调会各铸一个 id，tmp+move 落盘者胜、另一调用方本次会话用落败 id；客户端 id 仅用于匿名反馈归档，理论性竞态 | `FeedbackService.cs:94,269-304` | 高（机制）/ 低（后果） |
| O-17 | `FolderWatcherService.IconDebounceTimer_Tick`（:980-998）在 `WatchedPath` 判空后锁外逐个 `FolderIconChanged?.Invoke`：若 tick 与 `Stop()` 竞争，事件仍可能以刚失效的路径列表发出一次（订阅方 VM 侧有 generation/ disposed 防御，实际影响外观级） | `FolderWatcherService.cs:980-998` | 中 |
| O-18 | `NativeAppNotificationService._activated` 回调在 WinRT `NotificationInvoked` 事件线程同步执行、handler 本身无 try/catch；App 侧 `QueueNativeNotificationActivation`（App.xaml.cs:1870 起带 try）有防御，但该约定未被服务自身固化（若未来订阅者更换需自行保证） | `NativeAppNotificationService.cs:368-385` | 高（机制）/ 低（当前触发面） |
| O-19 | `StoreAppUpdateService` 的 `_pendingUpdates`（:16,48,104,131）在 Check/Download 间共享且无锁；两个实现均为设置页 UI 线程序列调用，当前无并发触发面——若未来加入自动检查定时器需先串行化 | `StoreAppUpdateService.cs:16,104-113` | 高（机制）/ 无（当前触发面） |

---

## 6. 统计

- **新立案**：2 条 —— P0×0，P1×0，P2×0，**P3×2**（A-01 FolderWatcher UI 线程同步探测、A-02 Process 句柄延迟释放）
- **观察项**：5 条（O-15 ~ O-19）
- **已知模式新位点**：5 行（DEF-080 家族×2、DEF-054 家族×1、DEF-101 家族×1、THR-06 家族×1）
- **存量复核**：范围内挂账条目 20+ 条逐条给出当前树证据，**无改判**（维持 15 / 已闭环再证 6 / 沿用 R10 证据若干）；DEF-110~113 修复批再证在位，**DEF-115/116（round-10 修复批）逐行复核全部正确落地、无回归**
- **证伪留档**：4 项（DirectStartupTaskXmlReader 裸 vtable 五槽位经本机 SDK 头文件逐槽核对全对、IShellLinkW 18 方法声明、DisableCore 锁内 GetState 可重入、QuickCaptureService.Changed gate 内触发无重入死锁面）
- **总体结论**：连续第六轮 P0/P1 = 0；本轮首次实读的 20+ 个从未点名文件（通知激活面、凭据存储、反馈客户端、Markdown 管线、内存回收器等）整体工程化水位高（边界限幅、原子写入、超时/取消区分、广播隔离均有先例可循），新立案仅剩两处低频卫生级位点。QuickCaptureService（2,207 行）全文审读未发现可立案问题，其 tombstone/undo-window/附件 GC 的设计自洽性经逐路径验证。
