# 第 11 轮（从零）审查 · 维度 A（服务与数据面）

- 审查对象：`wip/fix-bug` 分支当前工作树（基线提交 `7fcede2c`）。
- 审查方式：纯静态阅读。系统性 grep 扫描（`async void`、`.Result/.Wait()/GetAwaiter().GetResult()`、`DateTime.Now`、无 culture 的 `Parse/TryParse/ToString`、`File.Delete/Directory.Delete/File.Move`、`Process.Start`、`GetLastWin32Error`、`FileSystemWatcher`、`SemaphoreSlim` 持锁跨 await 等）+ 对命中文件逐个精读推演触发路径。未读 `docs/audit/` 任何历史结论；注释中"已处理/安全"声明均经代码验证。
- 扫描范围：`src/DeskBox/Services/`（383 个文件中的核心持久化/备份/事务/搜索/更新/天气/音乐/回收/生命周期系列约 60 个精读）、`src/DeskBox/App*.cs`（OnLaunched/启动序/恢复/通知路由/托盘/关机 flush 段）、`src/DeskBox/Models`、`src/DeskBox/Contracts`、`src/DeskBox.Updater/Program.cs`、`src/DeskBox/Helpers` 与 `src/DeskBox/Platform` 的 P/Invoke 后端、`native/deskbox-native/src/*.rs`（lib.rs 全部导出 + recycle_bin.rs 精读 + Cargo profile）。
- 命中统计：报告问题 16 条（P0×0、P1×0、P2×3、P3×13），另有 1 条设计确认项（非缺陷）。整体结论：持久化主链路（ResilientJsonStore、备份/恢复事务、DesktopOrganization WAL、SettingsService 双文件提交）经多轮加固后剩余缺陷面很小；本轮命中集中在提醒消费语义、历法区域性、侧车文件卫生、关闭期竞态四个簇。

分级定义：P0=数据永久丢失/崩溃/安全；P1=用户可感知的数据或功能损害；P2=诊断/性能退化、特定环境退化、小概率功能损害；P3=轻微/卫生。

---

## 问题清单

### A-1 【P2 · 置信度：高】Todo 提醒的"已通知"标记先落盘、后显示；显示失败或保存失败后该提醒永久/整个会话丢失

**文件**：`src/DeskBox/Services/TodoReminderService.cs`、`src/DeskBox/App.xaml.cs`

**人话摘要**：提醒扫描把"这条提醒已发过"的持久化标记（`ReminderLastNotifiedAt` / `ReminderDismissedForDueDate = DueDate`、小睡路径的 `SnoozedUntil = null`）写进 todo.json 之后，才去真正弹通知。持久化和显示之间任何一步失败（保存后进程退出、通知层抛异常、关机时 UI 队列已停），该条提醒再也不会响——`ReminderDismissedForDueDate == DueDate` 是永久性闸门。另有一个会话内变体：去重键先加入内存集合、保存却失败时，本会话内该提醒被静默跳过。

**代码证据**：
- `TodoReminderService.cs:470-490`（`CollectWidgetCandidatesAsync` 的 MutateAsync 回调内）：
  ```csharp
  string reminderKey = GetReminderKey(...);
  if (!_sessionNotifiedKeys.TryAdd(reminderKey, 0)) { continue; }   // 476：先占会话去重键
  ...
  item.SnoozeLastNotifiedAt = now; item.SnoozedUntil = null;        // 483-485：小睡标记
  item.ReminderLastNotifiedAt = now;                                 // 488
  item.ReminderDismissedForDueDate = item.DueDate;                   // 489：持久化"已消费"
  ```
  该回调运行于 `store.MutateAsync` 内（`TodoWidgetStore.cs:177-193`），返回 true 即在同一个 gate 里执行 `SaveUnsafeAsync` 落盘。
- 通知显示在所有 widget 的持久化全部完成之后才发生：`TodoReminderService.cs:200-202` `_notify(BuildNotification(candidates));`；异常被 `CheckCoreAsync` 的 catch（204-208 行）吞掉只记日志。
- 永久闸门：`TodoReminderService.cs:679-683` `ShouldNotifyDue` 中 `if (item.ReminderDismissedForDueDate is { } d && DateTimeOffset.Equals(d, dueDate)) return false;`——与 DueDate 相等即永不重发。
- 显示端自身可静默丢弃：`App.xaml.cs:2886-2890` `dispatcherQueue.TryEnqueue(() => ShowTodoReminderNotification(notification));`——`TryEnqueue` 返回 false（关机/队列停摆）时没有回退路径也没有日志；`App.xaml.cs:2902-2905` 托盘图标为 null 时直接 return（无通知、无撤销）。
- 会话内变体：`SaveUnsafeAsync` 抛出（瞬时 IO/共享冲突）时异常穿透 `CollectWidgetCandidatesAsync` → `CheckCoreAsync` catch（204 行）→ 返回 0，但 476 行已加入的 `_sessionNotifiedKeys` 键不清除，下一次 tick 的 `TryAdd` 失败 → 本会话该提醒不再尝试；且因落盘失败，`ReminderDismissedForDueDate` 未持久化，重启后会重发——损失窗口为"本次会话"。

**触发路径**：
1) 扫描 tick 命中到期项 → MutateAsync 落盘成功 → `_notify` 前进程被杀/用户关机（`TryEnqueue` 失败或托盘为 null）→ 该 DueDate 的提醒永久静默。
2) 保存抛 IOException → 会话键已占用 → 本会话不再提醒（重启后恢复）。

**用户可感知后果**：到期任务不弹提醒（场景 1 为永久、场景 2 为本次运行）。这是提醒类应用的核心功能损害，但触发需要"落盘成功与显示成功之间断裂"或"保存瞬时失败"，故定 P2。

**建议方向**：把"已消费"标记的落盘移到通知显示成功之后（或改为双阶段：先内存占用、显示确认后再落盘；`PublishStoreChanged` 已有 UI 线程回调可复用）；`TryEnqueue` 失败与托盘缺失路径至少记日志并回滚内存去重键；MutateAsync 保存失败时从 `_sessionNotifiedKeys` 移除本次加入的键。验证接缝：`TodoReminderService` 已有 `storeFactory`/`clock`/`notify` 三个注入点，单测可用抛错的 notify 与抛错的 store 模拟两段断裂。

---

### A-2 【P2 · 置信度：中】重复任务的下次到期时间跨 DST 会漂移 1 小时；月度候选在 DST 不存在的本地时刻上被静默平移

**文件**：`src/DeskBox/Services/TodoRecurrenceService.cs`

**人话摘要**：Daily/Weekly 推进用"LocalDateTime.AddDays 再包回 DateTimeOffset"，跨夏令时边界时 23/25 小时的天数会让时间点漂移一小时；Monthly 用本地挂钟分量重组日期，落在春季跳变不存在的时间（如 02:30）上时被 `new DateTimeOffset(DateTime)` 静默映射到偏移+1 的时刻。

**代码证据**：
- `TodoRecurrenceService.cs:257-261`：
  ```csharp
  private static DateTimeOffset AddLocalDays(DateTimeOffset value, int days)
  {
      DateTime localDateTime = value.LocalDateTime.AddDays(days);
      return new DateTimeOffset(localDateTime);
  }
  ```
  `AdvanceByFixedDays`（185-197）逐日用它推进。春季跳变日 `new DateTimeOffset(local)` 对不存在的本地时间取下一区偏移，跨一次 DST 后到期时刻整体平移 1 小时，且每次跳变累计。
- `TodoRecurrenceService.cs:235-250` `CreateMonthlyCandidate`：用 `anchorLocal.Hour/Minute/Second` 重构 `new DateTime(..., DateTimeKind.Local)` 再 `new DateTimeOffset(localCandidate)`——同样对无效挂钟时间静默映射。
- 对照：`TryGetNextDueDate`（106-129）明确选择"本地挂钟语义"（先 `ToLocalTime()`），注释只考虑了跨时区未考虑 DST。

**触发路径**：DST 时区（如美欧）用户给每日任务设 09:00 提醒，春季跳变后完成的这一次生成 10:00（或 08:00，取决方向）的下次实例；任务列表/提醒按漂移后的时间显示和触发。

**用户可感知后果**：DST 地区的重复任务到期/提醒时刻每年漂移 1-2 小时。非 DST 时区（如中国）不受影响。

**建议方向**：推进时保留 `TimeSpan timeOfDay = local.TimeOfDay`，用日期加法后重设时刻并容忍映射（显式记录偏移选择）；或按 `TimeZoneInfo` 规则显式解析。验证：单测注入固定 `TimeZoneInfo`（`TryCreateNextOccurrence` 是纯静态，可直接用 DateTimeOffset 跨 2026-03-08 美东跳变断言）。

---

### A-3 【P2 · 置信度：中】`EverythingSearchService.Dispose` 在存在排队等待者时 Dispose 信号量，`ObjectDisposedException` 可逃逸到搜索管线

**文件**：`src/DeskBox/Services/EverythingSearchService.cs`

**人话摘要**：Dispose 拿到 gate 后释放并直接 `Dispose()` 信号量；此刻若有别的线程已通过入口的 `_isDisposed` 检查、正在（或即将）`WaitAsync` 排队，会收到 `ObjectDisposedException`，而 `SearchPageAsync` 的 catch 清单不含它。

**代码证据**：
- `EverythingSearchService.cs:637-666`：`bool acquired = _nativeGate.Wait(TimeSpan.FromSeconds(1)); if (acquired) { ... _nativeGate.Release(); _nativeGate.Dispose(); }`
- 入口检查与拿 gate 之间存在窗口：`SearchPageAsync:188` `if (_isDisposed || ...) return Empty;` → `EnsureConnectionForQueryAsync`（435-452，内部 `RefreshConnectionAsync` 于 118 行 `await _nativeGate.WaitAsync`）与 229 行 `await _nativeGate.WaitAsync(cancellationToken)` 都可能撞上已 Dispose 的信号量。
- catch 清单（282-306 行）仅覆盖 `OperationCanceledException`/`EverythingIpcException`/DllNotFound 家族，`ObjectDisposedException` 直接向上抛。

**触发路径**：应用关闭时 Dispose 与搜索弹窗最后一次刷新并发（用户关弹窗瞬间退出应用）。窗口极窄，后果是搜索路径一次未捕获异常（是否成崩取决于上层 VM 的兜底 try/catch）。

**用户可感知后果**：关机竞态下一条未捕获异常/诊断噪音；正常使用无影响。

**建议方向**：Dispose 不 Dispose 信号量（进程退出由运行时回收），或 catch 清单补 `ObjectDisposedException` 归一化为 Empty。验证：单测并发调用 `SearchPageAsync` 与 `Dispose` 断言无异常逃逸。

---

### A-4 【P3 · 置信度：高】约 10 处文件名/远端名用非 InvariantCulture 的日期格式化；非公历默认日历区域（ar-SA/th-TH 等）产生 Hijri/佛历年份与本地数字

**文件与行**（生成文件名的全部位点）：
- `DeskBoxDataBackupService.cs:276` `DeskBox-Auto-{DateTime.Now:yyyyMMdd-HHmmss}.zip`；`:332`（Backup）、`:393`（CloudBackup 本地导出）、`:2489`（parked 目录）、`:3115`（DeskBox-PreRestore）
- `CloudBackupService.cs:648` `BuildRemoteSnapshotName()`：`{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}`（无 culture 参数）
- `AttachmentStorageService.cs:45`、`DeskBoxDragData.cs:252/523`、`QuickCaptureService.cs:2211`、`DeskBoxDiagnosticsBundleService.cs:431`

**人话摘要**：应用从不设置 `CultureInfo.CurrentCulture`（全仓库无赋值，已验证），线程文化即用户区域。ar-SA 的 .NET 默认日历是 Umm al-Qura 且数字为阿拉伯-印度数字，th-TH 默认佛历——这些机器上生成的备份名内嵌 ١٤٤٨/2569 之类年份。保留/清理逻辑按 `LastWriteTimeUtc` 排序不受影响，但：
- `CloudBackupService.ParseSnapshotTimestamp`（673-717）用 InvariantCulture 解析自家生成的名字会失败 → 退化为服务器 LastModified 排序（功能上仍正确，属降级）；
- 本地快照名失去字典序可排性、取证时与日志时间对不上。

**触发路径**：区域=ar-SA（应用官方支持该语言）或 th-TH 的机器上触发任一自动/手动备份或云上传。

**建议方向**：文件名生成统一加 `CultureInfo.InvariantCulture`（`FormattableString.Invariant(...)`，仓库内 `GetLocationKey` 已是范例）。验证：`CultureInfo.CurrentCulture = new CultureInfo("ar-SA")` 下跑备份命名单测断言名字为公历 ASCII。

---

### A-5 【P3 · 置信度：高】`ResilientJsonStore` 的 `.unreadable-*` 侧车未被备份过滤排除

**文件**：`src/DeskBox/Services/DeskBoxDataBackupService.cs:3235-3237`、`src/DeskBox/Services/ResilientJsonStore.cs:236`

**代码证据**：`IsInternalStoreRecoveryArtifact` 只识别 `.json.bak` 与 `.json.corrupt-`；而 `PreserveUnreadablePrimary` 生成 `{store}.json.unreadable-{yyyyMMdd-HHmmss-fff}`（ResilientJsonStore.cs:236）并保留至多 3 份（`MaxUnreadableAsides`）。`ShouldIncludeInBackup`（3239-3295）对它返回 true → 完整备份与（若路径恰为 `quick-capture/quick-capture.json.unreadable-*` 则不在域内、其余 widget 域不在域内）自动快照都会携带这些取证文件。

**触发路径**：任一 store 曾因共享冲突读失败后发生过保存 → 侧车文件存在 → 之后每次全量备份多带最多 3 份可能数 MB 的旧 JSON。

**用户可感知后果**：备份体积无谓膨胀；恢复到目标机器会落地外来机器的侧车（无害但残留）。属卫生级。

**建议方向**：`IsInternalStoreRecoveryArtifact` 增加 `.json.unreadable-` 匹配（attachments/ 白名单已优先，不受影响）。

---

### A-6 【P3 · 置信度：高】`SyncOutboxStore.DequeueAsync` 只删主文件，`.bak`/`.corrupt-*` 侧车永久残留

**文件**：`src/DeskBox/Services/SyncOutboxStore.cs:126-136`

**代码证据**：`DequeueAsync` 仅 `File.Delete(path)`；而 `EnqueueAsync` 走 `ResilientJsonStore.SaveAsync`，同一 operation_id 重复入队时 `File.Replace` 会产生 `<id>.json.bak`；`ListAsync` 的损坏路径还会留 `.corrupt-*`（有 2 份上限）但 `.bak` 无任何清理。注释明言该目录是设备本地协议态、永不进备份——残留即永久。

**触发路径**：同一 operation_id 重推（网络重试常见）后出队成功 → `.bak` 永久留在 `data/sync/outbox/<domain>/`。

**后果**：同步目录缓慢积累含已发送载荷的侧车文件（隐私/体积）。P3。

**建议方向**：`DequeueAsync` 同时删除 `ResilientJsonStore.GetBackupPath(path)`；或入队协议对 outbox 改用无备份的写入。

---

### A-7 【P3 · 置信度：中】QuickCapture/附件导入发生在服务 gate 之外，落盘失败时留下孤儿附件文件（有后续 GC 兜底）

**文件**：`src/DeskBox/Services/QuickCaptureService.cs:128-137、561-579、700-713`

**代码证据**：`AddDetailedItemWithResultAsync` 在 `_gate.WaitAsync()`（137 行）之前执行 `ExtractInlineMarkdownImagesAsync`（130 行，写 `attachments/{itemId}/` 内联图片）；`AddItemWithAttachmentsAsync`/`AddAttachmentsAsync` 先 `AttachmentStorageService.ImportPathAsync` 复制文件（565-573 / 705-713），之后才进 gate——若 gate 内 `SaveCoreAsync` 抛出、或（AddAttachmentsAsync）目标 item 不存在（719-724 行 return null），已复制文件无记录引用。

**缓解**：`CleanupOrphanedAttachmentDirectoriesCore`（1887-1921 行）在后续任何一次成功保存后的 GC 中删除无引用的 `attachments/{itemId}/` 目录——孤儿是暂态的。仅在之后再也没有成功写路径运行时才残留。

**触发路径**：拖拽导入多文件时目标条目已被并发删除、或首次保存时磁盘满。

**后果**：暂态磁盘垃圾，自愈。P3。

**建议方向**：AddAttachmentsAsync 可在 gate 内先确认 item 存在再导入（将导入拆为 gate 外仅校验、gate 内复制），或在失败路径直接删除本次导入目录。

---

### A-8 【P3 · 置信度：中】`WeatherCacheStore.UpdateAsync`：瞬时读失败得到默认态后直接写回，另一半缓存被清

**文件**：`src/DeskBox/Services/WeatherCacheStore.cs:148-170、172-188`

**代码证据**：`UpdateAsync` 先 `LoadCoreAsync()`；`LoadCoreAsync` 捕获一切异常（含 `ResilientJsonStore` 因共享读失败返回的默认 `WeatherCacheState`）返回 `new WeatherCacheState()`，随后 `update(state)` 只设置本次的一半（如 `LastForecast`）并整体落盘——此前有效的 `LastLocation`（或反之）被默认空值覆盖。

**触发路径**：保存 forecast 时 primary 恰被杀软占用读失败。缓存类数据，下次定位/拉取自愈；仅损失"离线时显示上次预报/位置"的体验。P3。

**建议方向**：`LoadCoreAsync` 区分"读失败"与"确实为空"，读失败时跳过本次写入。

---

### A-9 【P3 · 置信度：中】`HasUserDocuments` 判定不全：仅含 quick-capture.json/音乐/速览等文档的活动数据目录会被 `TryRecoverStrandedRollback` park 掉

**文件**：`src/DeskBox/Services/DeskBoxDataBackupService.cs:2514-2538、2456-2507`

**代码证据**：`HasUserDocuments` 只认 `settings.json`、`widget-layout.json`、`widgets/**` 与 `quick-capture/attachments/**` 的任意文件；`quick-capture/quick-capture.json`（含全部速记内容）、`music-store.json`、glance store、`desktop-organization-history.json` 均不算。`TryRecoverStrandedRollback` 在判定"无用户文档"时把当前 `DataDirectory` park 成 `-pre-recovery-*` 再回滚旧快照。

**触发路径**：需要 settings.json 与 widget-layout.json 同时缺失（两者经 ResilientJsonStore 双写，极难同时消失）且存在搁浅 rollback 目录——灾难恢复链叠加的边缘场景。

**后果**：parked 目录保留全部字节（不丢数据），但应用视角"速记内容消失"直到人工找回。P3 低概率。

**建议方向**：`HasUserDocuments` 增加 `quick-capture/quick-capture.json` 与其余根级 store 文件的存在性判定。

---

### A-10 【P3 · 置信度：高】`Process.Start` 返回的句柄未释放（3 处）

**文件**：`src/DeskBox/Services/AppUpdateService.cs:332`、`src/DeskBox/Services/AppRelaunchService.cs:50`、`src/DeskBox.Updater/Program.cs:498`

**代码证据**：三处均 `Process.Start(startInfo);` 直接丢弃返回值（Updater 的 RunInstaller 170 行倒是 `using` 了）。进程/线程内核句柄滞留至 GC 终结。均为低频路径（更新/重启各一次）。P3 卫生。

**建议方向**：`using var _ = Process.Start(...)` 或 `Process.Start(...)?.Dispose();`。

---

### A-11 【P3 · 置信度：中】ArchiveZipService：压缩丢空子目录、输出非原子、解压中断留残目录

**文件**：`src/DeskBox/Services/ArchiveZipService.cs:81-147、200-243`

**代码证据**：
- `WriteZip`（112-125）只枚举文件创建条目，空子目录没有目录条目 → 压缩一个含空子文件夹的文件夹再解压，空目录消失（WriteTar 149-185 同样只走文件）。
- zip/tar 直接以 `FileMode.CreateNew` 写最终路径（86-90/135-139），取消或崩溃留下半成品 zip 且文件存在（用户可能以为有效）。
- `ExtractAllAsync`（221-242）逐条解压无事务性：中途失败（如 `ExtractToFile(overwrite:false)` 撞重复条目抛 IOException）保留半解压目录，无清理。

**后果**：轻度数据保真/体验问题，重试可恢复。P3。

**建议方向**：目录枚举补目录条目；写临时名成功后改名；解压失败时删除本次目标目录。

---

### A-12 【P3 · 置信度：低】`DesktopOrganizationTransaction.RecordCompleted` 对回传路径做 `First(...)`，不匹配即把整批当失败

**文件**：`src/DeskBox/Services/DesktopOrganizationTransaction.cs:108-120`

**代码证据**：`var item = journal.Items.First(candidate => string.Equals(candidate.SourcePath, result.SourcePath, OrdinalIgnoreCase));`——若 shell 移动层回传的 `FileTransferResult.SourcePath` 与计划字符串有任何规范化差异（尾随空格、短路径 8.3 展开、前置 `\\?\`），`First` 抛 `InvalidOperationException`；该回调在 `_transfer.MoveAsync` 的进度回调里抛出会被 159-164 行的 catch 当作"本批传输失败"，导致本批其余可移动项全部转 `RetainedItems`（不丢数据，仅本次不移动）。

**触发路径**：理论性——传给 `FileTransferPlan` 的就是同一字符串，只有传输层内部规范化才会引入差异。低置信度、低危害，留档供复核。

**建议方向**：改 `FirstOrDefault` + null 跳过并记日志。

---

### A-13 【P3 · 置信度：中】`CloudBackupService.DownloadSnapshotAsync` 下载失败留下截断 zip，无清理

**文件**：`src/DeskBox/Services/CloudBackupService.cs:508-536`

**代码证据**：`File.Create(destinationPath)` 先创建目标，`transport.DownloadAsync` 中途抛出（超时/断流）时无 finally 清理，截断文件留在调用方给的目录。上游 `PrepareScopedRestoreAsync` 的 zip 校验会拒绝它（安全），仅残留垃圾。P3。

**建议方向**：失败时 `TryDelete(destinationPath)`。

---

### A-14 【P3 · 置信度：低】`QuickCaptureStore` 标签去重用 `CurrentCultureIgnoreCase`

**文件**：`src/DeskBox/Services/QuickCaptureStore.cs:293`

**代码证据**：`item.Tags.Distinct(StringComparer.CurrentCultureIgnoreCase)`——tr-TR 等区域下 "i"/"I" 折叠行为异常（点/无点 i 不对称），可能保留用户视为重复的标签或反之；同时语言比较比序数比较慢。P3 卫生。

**建议方向**：`OrdinalIgnoreCase`（若产品想保留语义折叠，先做标准化）。

---

### A-15 【P3 · 置信度：低】`MusicSessionService.InitializeAsync` 检查-后-行动双初始化竞态

**文件**：`src/DeskBox/Services/MusicSessionService.cs:77-95`

**代码证据**：`if (_isDisposed || _isInitialized) return;` 后无锁地 `RequestAsync` 并订阅两个事件、覆写 `_manager`。两个并发首次调用会双订阅、泄漏第一个 manager 的事件连接（其事件持续触发造成重复刷新）。当前调用点均在 UI 线程（widget 计时器 + 命令），实际难以并发——留档为隐患。P3 低。

**建议方向**：`Interlocked`/lock 保护初始化，或请求完成后比较引用再订阅。

---

### A-16 【P3 · 置信度：低】`DesktopOrganizationRecoveryStore.Save` / `HistoryStore.SaveChecked` 在 STA 上同步阻塞池线程写盘

**文件**：`src/DeskBox/Services/DesktopOrganizationRecoveryStore.cs:61-62`、`DesktopOrganizationHistoryStore.cs:109`

**代码证据**：`Task.Run(() => SaveAsync(journal)).GetAwaiter().GetResult();`——注释已说明为避免 STA 死锁而走池线程，方向正确；但每个移动项完成都同步阻塞 STA 一次（含 `Flush(flushToDisk:true)` 强制刷盘），大数量整理时在 Shell 批处理间隙逐项卡 UI 线程（每次几十毫秒级）。功能正确、性能卫生问题。P3。

**建议方向**：批量完成 receipts 后合并写一次（journal 已在内存中累积），或接受现状并监控。

---

## 设计确认项（非缺陷，请主流程知悉）

### D-1 Rust 原生层以 `panic = "abort"` 兑现"panic 不穿越 C ABI"

**证据**：`native/Cargo.toml` `[profile.dev] panic = "abort"`、`[profile.release] panic = "abort"`；`native/deskbox-native/src/` 全目录无 `catch_unwind`。任何潜在 panic 直接终止宿主进程（WinUI 应用整体消失，不走 App 的异常/诊断管道）。这符合仓库规约的字面要求，但与"把原生错误转成 status 码"的错误处理风格形成反差——属有意决策还是成本权衡，建议在 ABI 契约文档层面确认（本轮未读契约全文结论，`lib.rs` 导出全部带 envelope 校验与 status 回写，未发现可由输入触发的显式 panic 路径；`recycle_bin.rs` 的 COM 调用全为 match 显式处理）。

---

## 最不确定、需要主流程复核的条目

1. **A-1**：`TryEnqueue` 失败/托盘为 null 是否确有现实触发（需要真机关机时序实验）；`NativeAppNotificationService.TryShow` 内部是否已有自己的异常吞并（若已吞，场景 1 的窗口只剩进程死亡）。
2. **A-2**：产品对"重复任务跨 DST 保持挂钟时刻"的期望语义（现状是漂移而非保持）；`AdvanceMonthly` 的 `MonthsBetween` 在跨年 DST 场景是否也需对齐。
3. **A-12**：`FileService.FileTransferResult.SourcePath` 是否可能与其计划字符串不同（需读 `FileService.ShellTransfer.cs` 全文确认；本轮只确认了计划构造处同源）。
4. **A-9**：`settings.json`+`widget-layout.json` 双缺失是否真有可达路径（如"恢复中断后首个启动序"里 store 先于恢复创建空目录的次序，需要按 `App.OnLaunched` 的 `apply-pending-restore` 步骤前后的目录创建时序复核）。
5. **D-1**：panic=abort 是否为有意为之的契约（读 `docs/architecture/*-native-abi-*.md` 的 panic 章节即可定论）。

---

## 已核实稳健的重点区域（留痕，防重复劳动）

以下区域本轮逐行读过并尝试攻击，未发现可报告缺陷，后续轮次可降优先级：

- **ResilientJsonStore.cs 全文**：瞬时读失败与损坏分流、`.bak` 恢复、`ERROR_UNABLE_TO_REMOVE_REPLACED` 重试+校验回写兜底、`.corrupt-*`/`.unreadable-*` 修剪、temp+replace 提交协议——健壮。
- **DeskBoxDataBackupService.cs 全文（3650 行）**：zip-slip 防护（全路径前缀校验+大小/数量双预算）、预恢复安全网 pin 机制、全量恢复 move-aside/回滚/搁浅恢复、scoped restore 的"staged 载荷存在性证明先于删除"+确认闸门+重试预算、快照的稳定拷贝（长度+mtime 双检重试）、metadata 双 gate 一致性。除 A-4/A-5/A-9 外未再命中。
- **SettingsService 持久化核心**（LoadAsync/SaveToFileOnlyAsync/SaveDebounced/RevertLastCommit 组合、更新 schema 只读姿态、双文件提交次序与回滚）与 **WidgetLayoutStore**（fail-closed 采纳、CanWrite 新 schema 拒写）。
- **DesktopOrganizationTransaction(.Restore).cs**：WAL 先行、identity-only 恢复授权、checked-save 线性化点、undo 生命周期与 abandoned 终态、回滚方向性（move-aside 而非反向搬移）。
- **CloudBackupService + WebDavBackupTransport**：目的地变更使成功/失败戳失效、验证降级语义、DTD 禁用、href 防注入、basename 白名单、DTD/逃逸防护（除 A-4/A-13 命中点）。
- **TodoWidgetStore/QuickCaptureStore 的 path-gate + FMEM-01 修剪、MutateAsync 全序列列化**；QuickCaptureService 的 `_dataTrusted` 清理闸门与 FQC-13 孤儿附件 GC。
- **EverythingSearchService 的 DEF-044 超时竞速与 gate 释责约定**（除 A-3 的 Dispose 角落）；**FolderWatcherService** 的代数校验与原子 watcher 换新。
- **EverythingSearchService/AppUpdateService 的来源钉扎（https + 域名/路径白名单）与哈希校验下载**；**Updater Program.cs** 的安装目录/注册表一致性校验、shell 完整性重启、失败重启兜底。
- **WeatherService** 的 Invariant 解析/格式化与共享缓存克隆纪律（近三轮修复点保持完好）；**CitySearchService**；**SyncOutboxStore/SyncStateStore/SyncRevisionsStore**（除 A-6）；**GlanceWidgetStore/MusicSettingsStore** 的 gate 纪律；**NativeNotificationActivationEnvelopeStore** 的 claim 恢复（含 PID 复用考虑）；**DisplayTopologyTransitionCoordinator** 的代数/稳定器状态机。
- **互操作卫生**：`ElevatedFileLauncher`（SetLastError=true + finally CloseHandle + GetLastWin32Error 紧邻调用）、`ExplorerShellLaunchService:300-302`（SetLastError(0) 显式清零再取）；`ShortcutNativeBackend` 的 LoadLibraryEx 定向加载与 ABI/能力协商；Rust `lib.rs` 全部导出的 envelope 先校验后执行。
