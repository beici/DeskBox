# R10-A 核心服务与数据面专项审查报告（round-10）

> 审查基线 commit `e3ad11ad`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R10-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 范围与体量

- `src/DeskBox/Services/`：316 个 .cs，约 84,000 行
- `src/DeskBox/Models/`：60 个 .cs，约 5,500 行
- `src/DeskBox/Contracts/`：2 个 .cs（IFileDropTarget / ITodoReminderPresenter，全读）
- `src/DeskBox/Helpers/`：46 个 .cs，约 18,000 行
- `src/DeskBox/Sync/`：4 个 .cs（SyncDomains / SyncEnvelope / SyncJsonContext / SyncProjection，全读）
- `App.xaml.cs` + 非测试分部（`App.Startup.cs`、`App.Tray.cs`、`App.DesktopOrganization.cs`、`App.ImmediateHiddenWorkingSetTrim.cs`、`App.QuiescenceWorkingSetTrim.cs`、`App.DiagnosticsBundle.cs`）；`App.Aot*Smoke.cs` 测试装置仅 grep 抽样

### 1.2 覆盖率声明

**全文逐行读**（本轮新深读，区别于 round-09 已覆盖面）：

- `CloudBackupService.cs`（819 行全读）、`WebDavBackupTransport.cs`（307 行全读）、`AppUpdateService.cs`（819 行全读）——round-09 仅定点复核的三个云/更新面服务本轮补全
- `MusicVolumeService.cs`（787 行全读，含全部 COM 接口声明逐槽核对）
- `JumpListService.cs`、`MusicSettingsStore.cs`、`AttachmentStorageService.cs`、`ResilientJsonStore.cs`（413 行全读）
- `TodoRecurrenceService.cs`（全读）→ 本轮唯一新立案（A-01）
- `HookHealthWatchdog.cs`、`ServiceRegistry.cs`、`BoundedPathChangeBuffer.cs`、`StartupPipeline.cs`、`WindowTrackingRegistry.cs`、`CompositorClockBoostCoordinator.cs`、`FileTransferSessionRegistry.cs`、`TrayToggleRequestQueue.cs`、`AppLifecycleRecoveryWatcher.cs`（全读）
- `DirectStartupTaskBackend.cs`（815 行全读）、`DirectStartupService.cs`（核心段）
- `BoundedStaOperationRunner.cs`、`BoundedBackgroundWorkScheduler.cs`、`ShellThumbnailProxy.cs`（批处理调度段）、`ShellContextMenuProxy.cs`（fire-and-forget 位点段）、`ElevatedFileLauncher.cs`（native 段）、`App.ImmediateHiddenWorkingSetTrim.cs`、`App.QuiescenceWorkingSetTrim.cs`、`App.Startup.cs`、`App.DesktopOrganization.cs`（全读）
- `TodoReminderService.cs`（DEF-110 修复后全文件复核）、`TodoWidgetViewModel.DetailAndAttachments.cs` 附件删除段、`SearchResultActionService.cs`（DEF-108/109 修复段复核）
- round-09 修复批 diff（commit `8e918498`）逐文件复核

**grep 定向扫描 + 抽读**：

- 横向模式扫描（全范围）：`GetAwaiter().GetResult()`/`.Wait(`/`.Result`、`async void`、`Task.Run` fire-and-forget、`DateTime.Parse`/`ToString("格式")` 文化敏感位、`AllocHGlobal/AllocCoTaskMem` 配对、`JsonSerializer` 反射序列化（AOT）、`Activator.CreateInstance`/反射、`while(true)` 循环、CTS/Timer 生命周期、静态可变集合
- Models：`WidgetItem.cs` 格式化位点、`TodoAttachment.cs`、`FileCutStatePolicy.cs`、`QuickCaptureDetailRestorePolicy.cs` 全读；其余 50+ 小模型文件按 AOT source-gen 契约 grep（全部走 JsonContext，无反射序列化新位点）
- 存储层抽查：`WeatherCacheStore`、`TodoWidgetStore`/`QuickCaptureStore` 的 per-path 门控与 FMEM-01 裁剪、`SettingsService.cs:1009` DEF-086 守卫、`GlanceWidgetStore` 门控
- App.xaml.cs：关机链（ShutdownCoreAsync）、`FlushSettingsForEndSession`、`SafeFireAndForget`、日志管道、托盘创建重试（App.Tray.cs:230-330）

**未覆盖及原因**：

- `QuickCaptureService.cs`（2,207 行）、`FileService` 家族、`SettingsService.cs`（3,738 行）、`DeskBoxDataBackupService.cs`：round-06~09 已全文主审，本轮仅按台账位点 + 最近 commit（`c943b139`、`6070dabc`）diff 复核，未再全文重读
- `WeatherService.cs`/`GlanceImageService.cs`/`EverythingSearchService.cs`/`SearchEngineService.cs`/`MusicSessionService.cs`：按台账位点定点复核（见 §3），未全文
- `ManagedStorageMigrationDialog.cs`（725 行 UI 对话框，归 B 面）、`GlanceTraditionalCalendarService.cs`（DEF-054，round-09 起连续未读，不给出改判）
- 12 语言 JSON（归 B 代理）

### 1.3 正面复核（round-09 修复批，commit `8e918498`）

| 条目 | 结论 | 证据 |
|---|---|---|
| DEF-110（提醒去重集合） | ✅ 修复正确 | `TodoReminderService.cs:27-31` 改 `ConcurrentDictionary<string,byte>`；`:444` `TryAdd(key, 0)` 语义与原 `HashSet.Add` 等价；`Clear` 三处（:96/:124/:138）在 `ConcurrentDictionary` 上线程安全 |
| DEF-111（四重 BOM） | ✅ 修复正确 | `head -c 6 \| od -c` → `357 273 277 u s i`，单 BOM + `using`，零内容变更 |
| DEF-112（死代码删除） | ✅ 修复正确 | 全仓 grep `GetRecentNotesAsync\|GetUpcomingTodosAsync` 零命中；`TruncateText`/`Search.Todo.Due` 保留（活代码在用） |
| DEF-113（重启失败 outcome） | ✅ 修复正确 | `DeskBox.Updater/Program.cs:60-65`：`RestartApp` 布尔检查 + `RestartAfterIncompleteUpdate(options, "restart-failed")`，与失败路径对称 |
| DEF-114（测试时间窗） | ✅ 修复正确 | `CloudBackupTransportTests.cs`：第二次上传**启动**信号（`Interlocked.Increment == 2` → TCS）+ 250ms 有界负向断言，断言方向与契约（skip-rather-than-queue）一致 |
| DEF-078（部分收口） | ✅ 两处新位点已包 try/catch | `QuickCaptureSurfaceContent.xaml.cs:1244`、`:2322` 均带 `quick-detail-open-error` 反馈 |

另核：R8-AB 的 `SettingsService` 锁内 API（`AddWidget`/`GetWidgetsSnapshot`/`RecordOrganizationHistoryEntry`，commit `c943b139` diff 逐行复核）实现正确；DEF-086 的「新架构档案只读」守卫在当前树 `SettingsService.cs:1009`（`loadedSchemaVersion > CurrentSchemaVersion` 拒写）与 `WidgetLayoutStore.cs:98`（`CanWrite`）均在位。

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| DEF-080 家族（文化敏感日期格式化进机器可读串） | `CloudBackupService.cs:607` | `BuildRemoteSnapshotName` 用 `$"{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}"`（current culture）；ar-SA 下快照名含阿拉伯-印度数字/Hijri 年 → `ParseSnapshotTimestamp`（:632，InvariantCulture TryParseExact）恒解析失败，排序静默回退 `LastModified`（优雅降级，无数据损坏；与 `:395` 同文件内 ProcessId 已显式 Invariant 形成对照） |
| DEF-080 家族（同上） | `AppUpdateService.cs:395` | updater helper 目录名 `$"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{ProcessId.ToString(InvariantCulture)}"`——同一插值内 ProcessId 显式 Invariant 而日期部分漏掉；仅目录名外观，无解析消费方 |
| DEF-080 家族（同上，纯本地文件名外观级） | `AttachmentStorageService.cs:45`、`DeskBoxDataBackupService.cs:263,319,380,2897`、`DeskBoxDragData.cs:264,535`、`ResilientJsonStore.cs:372` | 附件回退名/备份 zip 名/拖入默认名/损坏隔离文件名均用 current culture 格式化时间戳；无机器解析消费方，仅排序/可读性外观 |
| DEF-101 家族（裸 `?.Invoke` 广播无逐处理器隔离） | `CloudBackupService.cs:207,284,358`（`BackupRunCompleted`）、`AppUpdateService.cs:644`（`CheckCompleted`） | 订阅者异常会中断后续通知乃至上传收尾；同文件 `FileTransferSessionRegistry.RaiseStateChanged`（:318-330）已用 `GetInvocationList` 逐订阅者隔离，可作规范模板 |
| THR-06 同族（跨线程状态字段裸读写，诊断面） | `AppDiagnosticsService.cs:121-160`（`_uiHeartbeatReceived`/`_watchdogMissCount`） | 后台 Timer 写/读、UI 线程心跳清除；已有 `Interlocked` 于 miss 计数、bool 无 volatile 但仅诊断用途（同 ledger THR-06 水位，不单独立案） |

---

## 3. 存量复核（范围内挂账条目现状）

以下逐条给出当前树证据（本轮定点复核或 R9 证据仍准确者均注明）；「维持」= 机制仍在当前树。

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| DEF-048（WeatherCodeMapper 声明小写未实现） | 维持 | `Helpers/WeatherCodeMapper.cs:98-99`：注释「Normalize: trim, lowercase for comparison」，实际 `string d = description.Trim();` 无 ToLower |
| DEF-049（MSN icon 29→29 恒等映射） | 维持 | `Helpers/WeatherCodeMapper.cs:207`：`29 => 29, // Pass through for night-specific` |
| DEF-050（MSN 日期解析失败仍追加空行） | 维持 | `Services/WeatherService.cs:496-503`：`TryParse` 失败时 `dateStr` 保持 `string.Empty` 仍 `daily.Time.Add(dateStr)` |
| DEF-051（远程 JSON 无大小上限） | 维持 | 按台账维持（`GlanceImageService.cs:614-620`、`WeatherService.cs` GetStringAsync 位点）；本轮未重读，round-09 证据未发现变化迹象 |
| DEF-052（FileMetaService 失败 null 永久缓存） | 维持 | `Services/FileMetaService.cs:164`：`_iconCache.GetOrAdd(key, k => LoadIconAsync(k))`；在途不淘汰已修（R9 确认）维持 |
| DEF-053（内容刷新任务引用竞态） | 维持（位点更新） | `SearchEngineService.cs:511-517`（DEF-112 删除 93 行后位点前移）：finally `lock (_deskBoxContentRefreshLock)` 内无条件 `_deskBoxContentRefreshTask = null`，不校验 ReferenceEquals |
| DEF-054（Saka/Bangla 历近似漂移） | 维持（未复核） | round-09 起连续未读该文件，不给出改判 |
| DEF-056（Migration_1_To_2 与 2_To_3 重复段） | 维持 | 按 R9 证据维持（`SettingsMigrationService.cs`），本轮未重读 |
| DEF-074/075（迁移失败推进版本/损坏回退版本戳） | 已闭环（再证） | `SettingsService.cs:856-870` 外层 catch 盖 `CurrentSchemaVersion`（R8-AB 收口批）；`SettingsMigrationService` copy-on-write 管线（R8 复核） |
| DEF-076（遗留 settings.json 一次性迁移非原子） | 维持 | `SettingsService.cs:941`：`await File.WriteAllTextAsync(_settingsPath, json)` 仍裸写（当前树 grep 确认唯一裸位点） |
| DEF-077（SearchHistoryService 裸写） | 已修复（再证） | round-09 已改判；本轮 grep 确认其 Save 走 ResilientJsonStore |
| DEF-078（SMTC `Try*Async` 裸调簇） | 维持 | `MusicSessionService.cs:235-266`：六处 `Try*Async` 裸调仍在（当前树 grep）；本轮范围该文件深读归 R9 已覆盖位点 |
| DEF-079（JIT 音量后端 Volume RCW 不释放） | 维持（本轮全读确认） | `MusicVolumeService.cs:538-541`：`AudioSessionHandle.Dispose()` 仅 `ReleaseComObject(Control)`，`Volume`（:296-297 经 `as` QI 的 ISimpleAudioVolume RCW）仍不释放。另注：`TryFindSession`（:228-292）的租约转移（count==1 不重复释放）与 finally 兜底核对正确，无额外泄漏位点 |
| DEF-080（ar-SA 天气解析/缓存错乱） | 维持 | `WeatherService.cs:501,526,528,561` 四处 `ToString("yyyy-MM-dd…")` 当前树确认无 InvariantCulture；家族新位点见 §2 |
| DEF-082（StripBlockingAttributes 崩溃窗口） | 维持 | 按 R9 证据维持（观察项定性） |
| DEF-084（更新器备忘簇） | 维持 | 本轮 `AppUpdateService.cs` 全读：`:31` Timeout=20s 仅到响应头、直发渠道仅 hash 校验（`:243`）机制未变；与 R9 结论一致 |
| DEF-086（settings.json 缺只读防护） | 已修复（R8-AB，本轮再证） | `SettingsService.cs:1009`（schema 版本高于本构建拒覆写）在位 |
| DEF-087（钩子线程同步握手） | 已修复（R8-AB，本轮再证） | `App.xaml.cs:1633-1645` 生命周期恢复链 await 异步变体；`GlobalHotkeyService`/`SearchHotkeyService` async 孪生在位（本轮 grep + App 段复核） |
| DEF-102（QuickCaptureStore 无 per-path 门控） | 已修复（R8-AB，本轮再证） | `QuickCaptureStore.cs:35-37,59` per-path 门控 + `:96-110` FMEM-01 裁剪，与 `TodoWidgetStore.cs:33-66` 对称 |
| DEF-104/105/106（备份恢复坏数据归一化） | 已修复（R8-AB，本轮再证） | `WebDavBackupTransport.cs:249-255`（DEF-105 畸形 href 跳过）当前树确认；其余两条按 R9 证据维持闭环 |
| DEF-108/109（搜索动作绕过服务门控） | 已修复（R8-AB，本轮再证正确性） | `SearchResultActionService.cs:59-90`（todo 走 `store.MutateAsync` + `NotifyExternalStoreChanged` 中继）、`:112`（随记走 `AddExternalLinkedFileItemAsync`）；机制与中继语义逐行复核无误 |
| DEF-110~114（R9 修复批） | 已修复（复核通过） | 见 §1.3 |
| MEM-02（SoftwareBitmap 未确定性释放） | 维持 | 按 R9 证据维持（`IQuickCaptureClipboardReader.cs:99,124`） |
| MEM-01（托盘旧 Icon 延迟释放） | 维持 | `App.Tray.cs` 托盘创建/重试路径（:230-330）本轮复核无新位点；台账所述热替换残余按 R9 维持 |
| EXC-06（CitySearchService DCL 无 volatile） | 维持 | 按 R9 证据维持 |
| O-1~O-10（round-09 观察项） | 维持 | 本轮未发现任何一条可解除或升级的证据 |

---

## 4. 新发现问题清单

### A-01 ｜ TodoRecurrenceService 克隆附件时遗漏 StorageMode，且跨事项共享物理附件无删除协调 ｜ P3

- **位置**：`src/DeskBox/Services/TodoRecurrenceService.cs:79-88`（克隆体）；关联 `src/DeskBox/Models/TodoAttachment.cs:16`（`StorageMode` 默认 `LinkedStorageMode`）、`src/DeskBox/ViewModels/TodoWidgetViewModel.DetailAndAttachments.cs:391-399`（附件删除删除物理文件）、`src/DeskBox/ViewModels/TodoWidgetViewModel.FilteringAndAppearance.cs:724-733`（规范克隆的对照）
- **触发条件**：带**托管附件**（`StorageMode="managed"`，拖入/粘贴导入）的循环 Todo 完成时，`TryCreateNextOccurrence` 为下一期生成附件克隆。触发面本身高频（循环任务+附件），但可见后果需后续删除/健康扫描才暴露。
- **影响**：
  1. **存储模式误分类**：克隆未复制 `StorageMode`，`TodoAttachment.StorageMode` 默认 `LinkedStorageMode` → 下一期附件被当作"外部链接"，实际 FilePath 指向托管目录 `data/attachments/<源事项Id>/…`。后果：从下一期删除该附件时 `DeleteAttachmentAsync`（仅对 `IsManagedCopy` 删物理文件，:393）不清理 → 托管文件成孤儿；`DeskBoxAttachmentHealthService` 扫描亦按 linked 归类，文件缺失时报"缺失的链接文件"而非"缺失的托管副本"，诊断归因失真。
  2. **跨事项共享无协调**：克隆是浅共享（两侧 `FilePath` 指向同一物理文件，且新 Id 使去重失效）。源事项（保留 `managed`）删除附件时 `File.Delete` 物理文件 → 下一期的附件引用悬空，附件内容对用户消失（该托管副本往往是唯一副本）。反向（从下一期删）因误分类不删文件，反而"幸运地"无此问题——两侧行为不对称。
- **根因机制**：`TryCreateNextOccurrence` 手写对象初始化器逐字段复制时遗漏 `StorageMode`；同仓库的规范克隆 `TodoWidgetViewModel.CloneTodoItem`（FilteringAndAppearance.cs:730）正确复制了该字段，证明是遗漏而非有意取舍。浅共享 FilePath 的设计缺口（无引用计数/删除前引用检查）是独立存在的第二层。
- **证据**：
```csharp
// TodoRecurrenceService.cs:79-88（遗漏 StorageMode）
Attachments = sourceItem.Attachments
    .Select(attachment => new TodoAttachment
    {
        Id = Guid.NewGuid().ToString("N"),
        FilePath = attachment.FilePath,
        DisplayName = attachment.DisplayName,
        Type = attachment.Type,
        AddedAt = completedAt          // ← StorageMode 未复制（默认 "linked"）
    })
    .ToList(),
// TodoAttachment.cs:16
public string StorageMode { get; set; } = LinkedStorageMode;
// 对照 FilteringAndAppearance.cs:730 —— StorageMode = attachment.StorageMode（正确）
```
- **建议修法（最小侵入）**：①克隆体补一行 `StorageMode = attachment.StorageMode`；②`DeleteAttachmentAsync` 删除托管文件前，先在 store 内扫描是否仍有其他事项引用同一 `FilePath`（OrdinalIgnoreCase），有则跳过物理删除（可顺带消孤儿）；若做②则①的浅共享语义可接受，否则考虑复制文件到新事项的 `attachments/<新Id>/` 目录。
- **置信度**：高（字段默认值、两个克隆位点对比、删除路径与健康服务分类逻辑均可静态确证；唯一未验证的是 UI 侧是否有隐藏的 StorageMode 修补点——全仓 grep `new TodoAttachment` 的 10 处构造位点已逐一核对，无其他修补）。

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-11 | `JumpListService.EnsureShortcutAumid`：`SHGetPropertyStoreFromParsingName(..., out IntPtr storePtr)` 返回调用方持有的引用，`Marshal.GetObjectForIUnknown(storePtr)` 创建的 RCW 会对 COM 对象自行 AddRef（CoreCLR `GetOrCreateObjectForComInstance` 语义），finally 的 `ReleaseComObject(propStore)` 只释放 RCW 侧引用，原始 `storePtr` 引用永不释放 → 每次进程启动一次性泄漏 1 个 IPropertyStore COM 引用（不增长、进程级、仅启动一次）。修法一行：包装后 `Marshal.Release(storePtr)`。未运行验证 RCW AddRef 语义，故不入主清单 | `JumpListService.cs:245-252,287` | 中（机制）/ 低（后果） |
| O-12 | `MusicSettingsStore.SaveAsync`（public API）在锁内更新 `_cached` 后**直接**调 `PersistAsync`，绕过 `Update()` 的 `_writeChain` 串行链：若与并发的 `Update()` 交错，链上旧快照可能后落盘覆盖较新内容（FCFG-05 的顺序保证被旁路）。当前全仓零生产调用方（仅 `Load`/`Update` 被使用），属"有旁路复活风险"的语义陷阱 API（DEF-112 同族反面：不是死代码删除而是补链或删除）。`WaitForPendingPersist`（UI 线程同步等待，链在池线程运行）机制本身复核正确 | `MusicSettingsStore.cs:131-149,152-163` | 高（机制）/ 无（当前触发面） |
| O-13 | `CloudBackupService.VerifyUploadAsync` 对 `ListAsync` 逐次重试（800ms/2s/4s）期间持 `_gate`；一个 listing 缓慢但可用的端点会拖长"排他窗口"，期间手动"立即备份"返回 InProgress。属设计取舍（PUT 与 prune 互斥的有意代价），仅记录 | `CloudBackupService.cs:544-574` | 高（行为）/ 无（取舍） |
| O-14 | `DirectStartupTaskBackend.RunSchtasks` 同步 `WaitForExit(10s)`；`TryRegister/TryDelete/Read` 由 `_registrationLock` 串行。若调用方在 UI 线程（设置页开关），最坏阻塞 10s×2（注册+回读）。round-09 未深读该文件，本轮确认其同步契约与超时上限，无异常卫生问题；是否迁移到调用方异步化属体验面（B 代理）决策 | `DirectStartupTaskBackend.cs:741-801` | 高（行为）/ 低（触发面） |

---

## 6. 统计

- **新立案**：1 条 —— P0×0，P1×0，P2×0，**P3×1**（A-01 TodoRecurrence 附件克隆缺陷簇）
- **观察项**：4 条（O-11 ~ O-14）
- **已知模式新位点**：5 行（DEF-080 家族×3 组位点、DEF-101 家族×2、THR-06 同族×1）
- **存量复核**：范围内挂账条目 25+ 条逐条给出当前树证据；**无改判**（维持 14 / 已闭环再证 9 / 未复核沿用 R9 证据 2）
- **round-09 修复批复核**：DEF-110~114 五条全部复核通过（字节级、diff 级、grep 级验证），DEF-078 两处新位点收口确认；R8-AB 关键修复（DEF-086/087/102/108/109）再证无回退
- **重要正面结论**：Sync/Contracts 为纯 wire 类型且全部 JSON 走 source-gen JsonContext（无反射序列化/AOT 风险）；全范围 `AllocHGlobal/AllocCoTaskMem` 均配对释放（除 O-11 一次性 RCW 引用）；`while(true)` 位点全部为有界队列/看门狗循环；`FileTransferSessionRegistry.RaiseStateChanged` 已是广播隔离的正面范式
- **总体结论**：连续第五轮 P0/P1 = 0；核心服务与数据面在 round-09 修复后处于历史最高水位，本轮新增问题仅剩低频边界场景（循环任务托管附件的克隆/删除一致性）
