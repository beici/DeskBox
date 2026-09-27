# R9-A 核心服务与数据面专项审查报告（round-09）

> 审查基线 commit `097b04a3`（wip/fix-bug）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R9-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 范围与体量

- `src/DeskBox/Services/`：316 个 .cs，约 84,000 行
- `src/DeskBox/Models/`：60 个 .cs，约 5,500 行
- `src/DeskBox/Contracts/`：2 个 .cs（IFileDropTarget / ITodoReminderPresenter）
- `src/DeskBox/Helpers/`：46 个 .cs，约 18,000 行
- `src/DeskBox/Sync/`：4 个 .cs（SyncDomains / SyncEnvelope / SyncJsonContext / SyncProjection）
- `App.xaml.cs`（5,280 行）+ `App.Tray.cs` + `App.Startup.cs` + `App.QuiescenceWorkingSetTrim.cs` 等非 AOT-smoke 分部；30 个 `App.Aot*Smoke.cs` 分部仅抽样 grep（它们是测试装置，非生产路径）

### 1.2 覆盖率声明

**全文逐行读**（核心风险区）：
- 持久化层：`ResilientJsonStore.cs`、`SettingsService.cs`（3,738 行全读）、`TodoWidgetStore.cs`、`QuickCaptureStore.cs`、`SearchHistoryService.cs`、`FileMetaService.cs`、`GlanceWidgetStore.cs`、`WeatherCacheStore.cs`
- 文件面：`FileService.cs`（3,767 行全读）、`FileService.TransferProgress.cs`（全读）、`FileService.ShellTransfer.cs`（全读，含 IFileOperation 裸 vtable 逐槽位核对）、`DeskBoxDataBackupService.cs`（约 2,700/3,401 行核心路径全读）、`OrganizerService.cs`、`DesktopOrganizationTransaction.cs` + `.Restore.cs`
- 内容服务：`WeatherService.cs`、`EverythingSearchService.cs`、`SearchEngineService.cs`、`GlanceImageService.cs`、`CitySearchService.cs`、`MusicSessionService.cs`、`MusicVolumeService.cs`（核心段）、`TodoReminderService.cs`、`LocalizationService.cs`、`WidgetStyleBackupProjection.cs`
- 系统服务：`FolderWatcherService.cs`、`DesktopAutoOrganizationWatcher.cs`、`StoreStartupService.cs`
- App：`App.xaml.cs` 启动管线（OnLaunched 全段）、内存清理族（Schedule/Cancel/可见空闲维护/后台深清理）、关机链（ShutdownCoreAsync）、日志管道

**grep 定向扫描 + 抽读**：
- 其余 WidgetManager 分部（`WidgetManager.cs` 创建路径、`FeatureWidgets.cs` 变更点、`Groups.cs` / `Storage.cs` / `ZOrder.cs` 已由 round-06/07/08 反复主审，本轮按 DEF-070 家族位点 grep 复核）
- Helpers 46 文件：按台账位点 + 并发/Dispose/COM 释放模式 grep，抽读 `WeatherCodeMapper`、`NativeFileDragOut`、`IconHelper` 缓存结构、`NativeDropTarget` 生命周期、`DesktopOrganizationRecoveryStore`
- Models：抽读 `AppSettings.cs`（facade 结构）、`WidgetItem.cs` 格式化位点；其余 50+ 小模型文件按 AOT 序列化契约 grep（全部走 source-gen JsonContext，无反射序列化新位点）
- Sync 4 文件全读（均为未接线的纯 wire 类型，DEF-107 已闭环）
- 全范围横向 grep：`async void`、`.Wait(`/`.Result`/`GetAwaiter().GetResult()`、`DateTime.Parse`/文化敏感 `ToString`、锁外 `Widgets.*` 变更、CTS/Timer 生命周期

**未覆盖及原因**：
- `ManagedStorageMigrationDialog.cs`（725 行，UI 对话框，归 B/视图面更合适）仅 grep 无深读
- `DirectStartupService/DirectStartupTaskBackend`（1,467 行）仅 grep 同步等待模式，未逐行
- `CloudBackupService.cs` / `WebDavBackupTransport.cs` / `AppUpdateService.cs`：仅按 DEF-104/105/084 位点定点复核，未全文
- `WidgetLayerService.cs` / `WidgetManager.ZOrder.cs` / `QuickCaptureService.cs`（2,207 行）全文主审已由 round-06/07/08 完成，本轮仅按台账位点复核；`QuickCaptureService.cs` 读了头部并发结构与 gate 布局
- `GlanceTraditionalCalendarService.cs`（DEF-054）本轮未读，不给出改判

### 1.3 一次重要的自我证伪

`FileService.ShellTransfer.cs` 的 `IFileOperationNative` 裸 vtable 调用（MoveItem=Table[14]、CopyItem=Table[16]、PerformOperations=Table[21]、GetAnyOperationsAborted=Table[22]）初判疑似「IFileOperation 忘记 RenameItems 导致全表槽位偏移一」。经对照 Microsoft Learn 的 IFileOperation 方法全集（**20 个方法，无 SetApplicationName**）逐一推演，全部槽位与 IDL 顺序精确吻合（Advise=3/Unadvise=4/SetOperationFlags=5/SetOwnerWindow=9/MoveItem=14/CopyItem=16/PerformOperations=21/GetAnyOperationsAborted=22），IShellItem::GetDisplayName=5 亦正确。**证伪，不立案**。此记录留档，避免后续轮次重复怀疑。

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| DEF-070 家族（settings 活列表锁外变更，与后台防抖保存的锁内序列化竞态） | `WidgetManager.FeatureWidgets.cs:707,709,759-760,890` | R8-AB 修复收口了全部 `Widgets.Add`（改走 `AddWidget`），但同文件的 `Widgets.Remove`/`RemoveAll`/`DeletedWidgetIds.Add`（legacy 空壳修复、去重、重置功能格子三条维护路径）仍为锁外写 |
| DEF-070 家族（同上） | `DesktopOrganizationTransaction.cs:180-184,275-277`；`DesktopOrganizationTransaction.Restore.cs:361,368-373` | 整理提交的 `settings.Widgets.RemoveAll`、失败回滚的 `settings.Widgets = originalWidgets` / `Entries = originalHistory` 赋值、`RemoveUncommittedWidgets` 的 `Widgets.Remove/RemoveAll` 均持 `OperationGate` 但不持 `SettingsService._lock` |
| EXC-06 家族（静态双检锁无 volatile 发布） | `LocalizationService.cs:313-467` | 12 个语言表 getter 同一 DCL 模式（`if (_zhCn is not null) return …` 锁外读）；发布发生在锁内、锁外读无 acquire 屏障，机制与已挂账的 `CitySearchService.cs:53-92` 完全同型 |
| DEF-078 家族（陈旧 SMTC 会话 `Try*Async` 裸调、无 try/catch） | `MusicSessionService.cs:233-236,242,248,254,260,266` | 台账 DEF-078 所列位点的当前树确认（同位点维持，非严格新位点，列出供本轮复核锚定） |

---

## 3. 存量复核（范围内挂账条目现状）

以下逐条给出当前树证据；「维持」= 机制仍在当前树；「已修复（现状核实）」= 台账仍挂账但当前树已无该机制。

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| DEF-048（WeatherCodeMapper 声明小写比较未实现） | 维持 | `Helpers/WeatherCodeMapper.cs:98-99`：注释「Normalize: trim, lowercase for comparison」，实际 `string d = description.Trim();` 无 ToLower，switch 大小写敏感 |
| DEF-049（MSN icon 29→29 恒等映射笔误） | 维持 | `Helpers/WeatherCodeMapper.cs:207`：`29 => 29, // Pass through for night-specific` |
| DEF-050（MSN 日期解析失败仍追加空行） | 维持 | `Services/WeatherService.cs:497-504`：`TryParse` 失败时 `dateStr` 保持 `string.Empty` 仍 `daily.Time.Add(dateStr)`，同日其余字段照常追加 → 错位空行 |
| DEF-051（远程 JSON 无大小上限） | 维持 | `Services/GlanceImageService.cs:614-620`（`GetJsonAsync` 直接 `JsonDocument.ParseAsync` 无限流）；`Services/WeatherService.cs:83,413,442`（`GetStringAsync` 无上限） |
| DEF-052（FileMetaService LRU 软缺陷） | 部分改善后维持 | 在途不淘汰已修（`FileMetaService.cs:210-239` trim 跳过未完成任务）；失败 null 永久缓存仍在（`:163-164` `GetOrAdd` 的 null 结果无失效机制） |
| DEF-053（DeskBox 内容刷新任务引用竞态） | 维持 | `SearchEngineService.cs:553-559`：finally `lock` 内无条件 `_deskBoxContentRefreshTask = null`，不校验 `ReferenceEquals` → 旧任务 finally 可清掉新任务引用，允许两个刷新并发（数据安全，仅冗余+乱序通知） |
| DEF-056（Migration_1_To_2 与 Migration_2_To_3 重复段） | 维持 | `SettingsMigrationService.cs:216-238` 与 `:245-267` 两方法体逐字相同（同一 FollowDefault+WheelSwitch 修复循环） |
| DEF-074（迁移失败仍强置最新版本） | 已闭环（R8 复核确认，本轮再证） | `SettingsMigrationService.cs:73-100`：copy-on-write 管线按 checkpoint 推进 `working.SchemaVersion = version`，失败步弃整份 copy |
| DEF-075（损坏回退档案以 SchemaVersion=1 落盘） | 已修复（R8-AB 收口批，本轮再证） | `SettingsService.cs:856-870`：外层 catch 盖 `CurrentSchemaVersion` 并置 `HasResolvedInitialFileWidgetSetup = true` |
| DEF-076（遗留 settings.json 一次性迁移非原子） | 维持 | `SettingsService.cs:925-947`：`MigrateLegacySettingsIfNeededAsync` 仍裸 `ReadAllTextAsync` + `WriteAllTextAsync`（:941），未走 ResilientJsonStore temp+replace |
| DEF-077（SearchHistoryService 裸 WriteAllText） | **已修复（现状核实）** | `SearchHistoryService.cs:298-308`：Save 已改走 `ResilientJsonStore.SaveAsync`；Load 同样走弹性加载（`:250-265`）。台账 round-08 记「维持」，当前树机制已消除 |
| DEF-078（async void / TryEnqueue 静默失效簇） | 维持（本范围内音乐子项） | `MusicSessionService.cs:233-266` 六个 `Try*Async` 仍裸调 SMTC（见 §2） |
| DEF-079（JIT 音量后端 ISimpleAudioVolume RCW 泄漏） | 维持 | `MusicVolumeService.cs:538-541`：`AudioSessionHandle.Dispose()` 仅 `ReleaseComObject(Control)`，`Volume` RCW 仍不释放 |
| DEF-080（ar-SA 文化下天气解析/缓存错乱） | 维持 | `WeatherService.cs:501`（`dt.ToString("yyyy-MM-dd")`）、`:526,528`（sunrise/sunset）、`:561`（hourly）均无 InvariantCulture；ar-SA 下 Umm al-Qura 历渲染 Hijri 年份串进缓存 |
| DEF-081（托管拖入撤销按路径无身份校验） | **已修复（现状核实）** | `OrganizerService.cs:461-476`：undo 现以 `FileService.UndoReceiptStillMatches(item.DestinationPath, item.DestinationIdentity)` 校验对象身份；无 receipt 的 legacy 条目无自动撤销权（与桌面整理撤销对齐） |
| DEF-082（StripBlockingAttributes 崩溃窗口遗留属性丢失） | 维持 | `FileService.cs:1161-1206`：机制不变（改-用-还原非原子窗口）；`:1159` `s_attributeStripGate` 只串行化并发剥层，不消除窗口。台账已定性观察项 |
| DEF-084（更新器备忘簇） | 维持（未复核变更） | `AppUpdateService.cs` 定点抽查未见结构变化；本轮未全文复核 |
| DEF-087（钩子线程同步握手占 UI 线程） | **已修复（R8-AB，本轮再证）** | `GlobalHotkeyService.cs:124-133`、`SearchHotkeyService.cs:95-99`：新增 async 孪生（`useAsyncHandshake: true`）并注明阻塞孪生仅完成同步 await；同步孪生保留且注释了不死锁的理由 |
| DEF-088（拖出看门狗 `using var` 提前 Dispose） | **已修复（现状核实）** | `Helpers/NativeFileDragOut.cs:255-271`：watchdog 改显式 `new Timer` + 注释钉死「随拖队工作项退出释放」 |
| DEF-089/090/091（Composition 关键帧重灌/自适应阶梯死代码/注释残留） | 按台账维持 | 属动画专项（round-08 S02），本轮不重复核验 |
| DEF-102（QuickCaptureStore 无 per-path 门控） | **已修复（R8-AB，本轮再证）** | `QuickCaptureStore.cs:27-44,59`：`s_pathGates` per-path 门控 + FMEM-01 裁剪，与 TodoWidgetStore 对称 |
| DEF-103（Everything Dispose 裸 `_isDisposed` + 阻塞门） | **已修复（R8-AB，本轮再证）** | `EverythingSearchService.cs:64,643-682`：`volatile bool _isDisposed`；1s `Wait` 超时跳过 CleanUp 并记日志 |
| DEF-104/105/106（备份恢复坏数据归一化） | **已修复（R8-AB，本轮再证）** | `DeskBoxDataBackupService.cs:1706-1721`（重复 manifest 显式判别）；`:1540-1547,1617-1626`（`item.Id is null` 走 InvalidDataException）；`WebDavBackupTransport.cs:245-251`（`Uri.TryCreate` 跳过畸形 href） |
| DEF-108/109（搜索动作绕过服务门控直写 store） | **已修复（R8-AB，本轮再证）** | `SearchResultActionService.cs:58`（todo 走 `MutateAsync`）、`:118`（随记走 `AddExternalLinkedFileItemAsync`） |
| MEM-02（3 处 SoftwareBitmap 未确定性释放） | 维持 | `Services/IQuickCaptureClipboardReader.cs:99,124`：`encoder.SetSoftwareBitmap(await decoder.GetSoftwareBitmapAsync())` 两处仍无 using/Dispose |
| MEM-01（托盘旧 Icon 延迟释放） | 维持（位点更新） | 热替换路径现于 `App.Tray.cs:1026-1030` 显式 `previousIcon?.Dispose()`；台账所述残余（析构路径延迟释放）未复核出变化 |
| THR-06（QuickCaptureClipboardService 状态字段裸读写） | 维持（未复核变更） | 本轮未深读该文件，按 round-08 结论维持 |
| EXC-06（CitySearchService.Predefined 无防护 + DCL 无 volatile） | 维持 | `CitySearchService.cs:53-92`：机制不变（且 §2 已列同型新位点 LocalizationService） |
| 遗留观察 3（FolderWatcherService.LastEventAt UTC ticks） | 已落地 | `FolderWatcherService.cs:97,145-153,170-171`：`_lastEventAtTicks` 以 UTC ticks 经 Interlocked 存取 |
| DEF-016 / LAY-05~08 / QC-06~15 / ANI-03~06 / EVT-* / ARC-* | 不在或部分不在本范围 | 未复核，按台账维持 |

---

## 4. 新发现问题清单

### A-01 ｜ TodoReminderService 提醒去重集合跨线程无同步访问 ｜ P3

- **位置**：`src/DeskBox/Services/TodoReminderService.cs:26`（字段）、`:91,119,133`（UI 线程 `Clear`）、`:439`（线程池 `Add`）
- **触发条件**：30s 扫描 tick 的 `MutateAsync` 回调恰与用户切换 Todo 提醒开关/功能开关（`SettingsChanged → Refresh()`）毫秒级重叠。
- **影响**：`HashSet<string>` 并发 `Clear`+`Add` 造成内部 buckets/count 不一致——最轻是本轮去重键丢失（重复提醒）或当轮键漏加（漏提醒），最坏后续操作抛 `IndexOutOfRange`/状态损坏异常（`CheckNowAsync` 的 catch 吞为一次失败；`Refresh` 的 `Clear` 在 UI 线程抛出则被全局兜底吞，EVT-03 面）。
- **根因机制**：`CollectWidgetCandidatesAsync` 以 `await store.MutateAsync(...).ConfigureAwait(false)` 执行，mutate 回调在信号量获取后的线程池延续上运行；而 `_sessionNotifiedKeys` 的 `Clear` 三处都在 `Start`/`Refresh`（DispatcherQueue UI 线程）。该集合是无锁普通 `HashSet`。
- **证据**：
```csharp
// TodoReminderService.cs:26
private readonly HashSet<string> _sessionNotifiedKeys = new(StringComparer.Ordinal);
// :119/133（UI 线程 Refresh）          // :428-461（MutateAsync 回调，线程池）
_sessionNotifiedKeys.Clear();          _ = await store.MutateAsync(current => {
    ...                                    ...
                                           if (!_sessionNotifiedKeys.Add(reminderKey))  // :439
                                       }).ConfigureAwait(false);
```
- **建议修法**：最小侵入——把 `_sessionNotifiedKeys` 换成 `ConcurrentDictionary<string, byte>`（`TryAdd`/`Clear`），或给 Add/Clear 两侧包同一把 `object` 锁；不改变提醒语义。
- **置信度**：高（线程归属可从 `ConfigureAwait(false)` 链路确定推导）。

### A-02 ｜ LocalizationService.cs 文件头连写 4 个 UTF-8 BOM ｜ P3

- **位置**：`src/DeskBox/Services/LocalizationService.cs:1`
- **触发条件**：静态存在（文件内容缺陷），无需运行时触发。
- **影响**：Roslyn 把 U+FEFF 当空白，编译无警告；但 (a) 文件以 `EF BB BF ×4 + using` 开头，任何按 BOM 判编码/做字节级 diff/拼接的工具链都会看到异常前缀；(b) 与仓库其余全部源文件（单 BOM 或无 BOM）不一致，属编码卫生漂移，易在后续脚本化处理中扩散。
- **根因机制**：历史上多次以带 BOM 的 UTF-8 追加/重写文件头累积所致（hexdump：`357 273 277` 连续 4 组后才是 `using`）。
- **证据**：`head -c 12 | od -c` → `357 273 277 357 273 277 357 273 277 357 273 277 u s i n g`；`grep -rl '\xef\xbb\xbf\xef\xbb\xbf' src/DeskBox --include="*.cs"` 全仓仅此一文件。
- **建议修法**：重写文件去掉多余 3 个 BOM（保留 0 或 1 个），零行为变更。
- **置信度**：高（字节级实证）。

### A-03 ｜ SearchEngineService 两个私有推荐方法为死代码 ｜ P3

- **位置**：`src/DeskBox/Services/SearchEngineService.cs:400-440`（`GetRecentNotesAsync`）、`:742-791`（`GetUpcomingTodosAsync`）
- **触发条件**：静态存在。
- **影响**：约 90 行不可达代码。两者各自构造 `new QuickCaptureStore()` / `new TodoWidgetStore(widget.Id)`，若未来被「顺手复用」，会以旁路服务的形态回到 DEF-108/109 修掉的直连 store 模式（当前两者已走 per-path 门控，风险被门控吸收，但语义上仍是绕过 `QuickCaptureService` 缓存的第二个读点）。同族先例 DEF-092（死代码残留）。
- **根因机制**：搜索空态推荐改为仅 `BuildApplicationRecommendations` 后遗留。
- **证据**：全仓 grep `GetRecentNotesAsync|GetUpcomingTodosAsync` 仅命中声明处（`SearchEngineService.cs:400,742`），无任何调用点；`GetRecommendationsAsync`（:238，有调用）只调 `BuildApplicationRecommendations`。
- **建议修法**：随下一批卫生提交删除；或若保留意图是未来接线，至少在方法头注明。
- **置信度**：高。

---

## 5. 观察项（不够立案标准）

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-1 | `WidgetStyleBackupProjection.RestoreJournalSnapshotAsync` 恢复写用裸 `File.WriteAllTextAsync` 直写 live settings.json/widget-layout.json 及其 .bak（非原子、不持 `SettingsService.FileWriteLock`）。该路径本身就是崩溃恢复，若恢复写自身被中断：live 半截 + .bak 仍是恢复前内容时 ResilientJsonStore 可兜底，但两文件分步写的中间态无 marker 保护。恢复路径运行频率≈0，且 `CleanupJournalAsync` 的有序删除已防误回滚 | `WidgetStyleBackupProjection.cs:426-435` | 中 |
| O-2 | `WeatherService` 缓存字段组（`_cachedData/_cacheTimestamp/_cacheLocationKey/_cacheSourceKey`，:37-40）跨线程读写无同步；两格子不同坐标并发刷新可短暂读到「数据与 key 不配套」的组合，返回错位置缓存直至下一轮刷新。危害低、窗口毫秒级 | `WeatherService.cs:146-160,194-207` | 中 |
| O-3 | `EverythingSearchService.SearchPageAsync` 3s 超时后返回空页，`inFlightQuery` 若随后 fault（如 `EverythingIpcException`）成为未观察任务异常，静默丢失（仅设计取舍注释在先）。可加 `_ = inFlightQuery.ContinueWith(…, OnlyOnFaulted)` 记日志 | `EverythingSearchService.cs:273-289` | 高（机制）/低（后果） |
| O-4 | `MusicSessionService.InitializeAsync` 无锁双检 `_isInitialized`（:77-95）：两线程并发首调会双订阅 `SessionsChanged/CurrentSessionChanged` → 下游通知翻倍（各订阅者均有防御，且调用面当前 UI 串行） | `MusicSessionService.cs:77-95` | 中 |
| O-5 | `SettingsService._appearancePreviewCts` 无锁访问（:419,1348-1385）：`RequestAppearancePreview` 的 Cancel/Dispose 有 ODE 防护，但 `NotifyAppearancePreviewNow` 的裸 `Cancel()` 无防护；当前调用面全在 UI 线程，无实际竞态 | `SettingsService.cs:1348-1385` | 中 |
| O-6 | `DeskBoxDataBackupService.ApplyPendingRestoreAsync` 全量恢复的双 Move 序列：staged→DataDirectory 失败且 rollback→DataDirectory 也失败时，DataDirectory 留空且无 marker 指向 `restore-rollback/<guid>`（数据仍在盘上，但无自动找回路径）。需连续两次目录 Move 失败，概率极低 | `DeskBoxDataBackupService.cs:1245-1293` | 中 |
| O-7 | `SearchHistoryService.Save()` 以 `Task.Run(...).GetAwaiter().GetResult()` 在调用线程（搜索提交路径，UI）同步等待整份历史落盘；弹性存储在 .bak 退让重试时最长可阻塞 ~200ms。构造期 Load 同型。注释声明为保持既有同步契约的有意取舍 | `SearchHistoryService.cs:250-265,279-309` | 高 |
| O-8 | `FolderWatcherService` 的 `_queryWatcher` 赋值持 `_lock`（:346-356）而 `Stop()` 的读+置 null（:586-594）不持锁；依赖 generation 检查与 UI 线程串行化保证安全，锁覆盖不对称属卫生面 | `FolderWatcherService.cs:346-356,586-594` | 中 |
| O-9 | `QuickCaptureStore.NormalizeItems` 的 Tags 去重用 `StringComparer.CurrentCultureIgnoreCase`（:266），与仓库其余去重的 OrdinalIgnoreCase 惯例不一致；应用不切换进程 CurrentCulture，行为稳定，仅文化敏感性维度不齐 | `QuickCaptureStore.cs:263-267` | 高 |
| O-10 | `SettingsService.LoadAsync:846-849` 在 `_lock` 外替换 `_settings.RecentOrganizationHistory` 列表引用（引用赋值原子、竞态良性），与该文件其余全部变更器的持锁纪律不一致 | `SettingsService.cs:846-849` | 高 |

---

## 6. 统计

- **新立案**：3 条 —— P0×0，P1×0，P2×0，**P3×3**（A-01 跨线程 HashSet、A-02 四重 BOM、A-03 死代码）
- **观察项**：10 条（O-1 ~ O-10）
- **已知模式新位点**：4 行（DEF-070 家族×2、EXC-06 家族×1、DEF-078 家族锚定×1）
- **存量复核**：范围内挂账条目 30+ 条逐条给出当前树证据；其中 **DEF-077、DEF-081、DEF-088 三条由「挂账」改判「已修复（现状核实）」**，DEF-074/075/087/102/103/104/105/106/108/109 再证闭环，DEF-048/049/050/051/052/053/056/076/078/079/080/082/MEM-02/EXC-06 维持
- **重要证伪**：`FileService.ShellTransfer.cs` IFileOperation 裸 vtable 槽位经 Microsoft Learn 方法全集核对全部正确（详见 §1.3），不立案
- **总体结论**：无 P0/P1；持久化与文件安全面（ResilientJsonStore、双存储提交回滚、manifest+身份校验、zip 解压限额）在当前树处于本仓库历史最高加固水位；本轮范围内仅余低频/卫生级新问题
