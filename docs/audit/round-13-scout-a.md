# 第 13 轮（从零）审查 · 维度 A（服务与数据面）

- 仓库：E:\DeskBox，分支 `wip/fix-bug`，基线提交 `9f261cbb`（工作树干净）。
- 方式：纯静态审查。未构建、未运行测试、未启动应用、未修改任何源码（本文件是唯一写入物）。
- 输入纪律：未读取 `docs/audit/` 下任何历史结论；仅读代码与允许的架构文档（`docs/architecture/*-native-abi-*.md`、`native/README.md` 用于核对原生契约）。

## 概览

**重点攻击区（最近 5 个修复提交，`git log --oneline -8`）**：

| 提交 | 主题 | 审查方式 |
|---|---|---|
| `b96129f6` | DisplayCulture 公历渲染（15 个位点 + LocalizationService.Format） | 全 diff + DisplayCulture.cs/TodoReminderService/LocalizationService/各 ViewModel 位点当前全量精读 |
| `f5a01f91` | snooze 提醒宽限上限（代码随 b96129f6 落在 TodoReminderService） | 全量精读 TryGetReminderTrigger 与消费协议交互 |
| `df24c5fe` | 通知 Setting 探测（NativeAppNotificationService.TryShow） | 全 diff + 文件全量 + 全部调用点核对 |
| `387c3180` | Updater 等待顺序前移 | 全 diff + Program.cs 全量 + 各重启路径推演 |
| `fe3103d5` | 提醒两阶段协议（先显示后落盘 + staleness 守卫） | 全 diff + TodoReminderService/App.xaml.cs 位点全量 + 与 toast 按钮（完成/小睡）的双向交错推演 |

**全量扫描**：`async void`、`.Result/.Wait()/GetAwaiter().GetResult()`、吞异常、`DateTime(.Now/.UtcNow)` 无 culture 插值/ToString、无 culture 的 `Parse/TryParse`、`File.Delete/Move/Replace`、`Process.Start`、`GetLastWin32Error`、XAML 日期格式化（未发现 StringFormat 日期绑定）。精读文件约 45 个（Services 重点 20+、App partial 位点、Helpers 原生后端、native/*.rs 两个模块与 lib.rs 导出层）。

**命中统计**：P0 无；P1 无；P2 共 3 条（A-1、A-2、A-4）；P3 共 9 条。总体印象：存储/备份/恢复/更新/原生层经多轮加固后相当扎实，本轮新改动方向正确、无回归性 P0/P1；主要残留在**非公历区域格式机器上的"协议/文件名未钉 InvariantCulture"泄漏簇**（正是 DisplayCulture 提交自己声明应保持 InvariantCulture 的领域，仍有漏网），以及新 snooze 宽限在"通知面长期不可用"环境下的静默丢失权衡。

---

## 问题清单

### A-1 ·【P2 · 置信度：高】Wikimedia"精选图片"分类名用 CurrentCulture 渲染年份，非公历格式机器上该类别静默返回空

- **文件:行**：`src/DeskBox/Services/GlanceImageService.cs:421`
- **人话摘要**：在线图片"精选（Pictures of the day）"分类是按当前年份拼出的 Wikimedia Commons **分类名（协议标识符）**，却用 CurrentCulture 格式化。Windows 格式为 ar-SA（默认乌姆库拉历）的机器上 `yyyy` 渲染成回历年份（如 1448），拼出 `Category:Pictures of the day (1448)` —— Commons 上不存在该分类，API 正常返回空 `categorymembers`，**不抛异常**，错误路径根本不触发，"精选"类别从此静默无图。
- **代码证据**：
  ```csharp
  GlanceOnlineImageCategory.Featured => $"Category:Pictures of the day ({DateTime.UtcNow:yyyy})",
  ```
  插值格式化走 CurrentCulture；同文件解析侧全部显式 `CultureInfo.InvariantCulture`，唯独这个**发向远端的标识符**没有。
- **触发路径**：区域格式=ar-SA（或任何非公历默认历法的格式，如 th-TH 佛历）→ 打开 Glance 在线图片"精选/Wikimedia" → 请求 URL 带 1448 → 空列表。
- **建议方向**：`DateTime.UtcNow.ToString("yyyy", CultureInfo.InvariantCulture)`。这正是 b96129f6 提交说明里"文件名/协议用途仍应 InvariantCulture"的自留地，属该工作的漏网点（该位点早于本提交存在，但与本轮修复目标同族）。
- **如何验证**：单元测试断言 `categoryTitle` 在 `CultureInfo.CurrentCulture = new CultureInfo("ar-SA")` 下仍含公历年份；或抓取该环境下的请求 URL。

### A-2 ·【P2 · 置信度：高】云备份远端快照名用 CurrentCulture 生成，ar-SA 机器上解析回环（ParseSnapshotTimestamp）必然失败

- **文件:行**：`src/DeskBox/Services/CloudBackupService.cs:648`（生成）与 `:673-717`（解析）、`:730-732`（保留排序）
- **人话摘要**：远端快照名 `DeskBox-CloudBackup-{UtcNow:yyyyMMdd'T'HHmmss'Z'}-<device8>.zip` 用 CurrentCulture 插值。非公历格式机器上传的快照名内嵌回历年份数字，而解析侧 `ParseSnapshotTimestamp` 用 `InvariantCulture`（公历）`TryParseExact` **必然解析失败**返回 null。后果：(1) 恢复选择器排序退化为服务器 LastModified；(2) 界面展示的时间戳取自回历文件名时语义错误；(3) 同一账户下机器间历法不同（或用户中途切换 Windows 格式）会产生 1448/2026 混排命名组，跨设备"嵌入时间戳优先于 LastModified"的排序初衷被破坏，保留策略可能删掉实际较新的快照。
- **代码证据**：
  ```csharp
  return $"{SnapshotFilePrefix}{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{deviceSuffix}{SnapshotFileExtension}";
  ...
  DateTimeOffset.TryParseExact(parts[^2], "yyyyMMdd'T'HHmmss'Z'",
      System.Globalization.CultureInfo.InvariantCulture, ...)
  ```
- **触发路径**：ar-SA 格式机器配置 WebDAV 云备份 → 自动上传成功（功能不中断）→ 恢复选择器时间戳列与排序退化；跨设备混排后保留策略次序不可靠。
- **建议方向**：生成侧钉 InvariantCulture（该名字本来就是给机器回读的协议串）。已存在的回历名无法修复，但解析失败已有 LastModified 兜底，属可接受的遗留。
- **如何验证**：单测：`CultureInfo.CurrentCulture=ar-SA` 下 `BuildRemoteSnapshotName()` 后 `ParseSnapshotTimestamp()` 应成功（修复前失败）。

### A-3 ·【P3 · 置信度：高】本地备份/拖拽/隔离区文件名簇同样未钉 InvariantCulture（回历年份进入用户可见文件名）

- **文件:行**：
  - `src/DeskBox/Services/DeskBoxDataBackupService.cs:276/332/393/2489/3115`（`DeskBox-Auto-…zip`、`DeskBox-Backup-…zip`、`DeskBox-CloudBackup-…zip`、`…-pre-recovery-…`、`DeskBox-PreRestore-…zip`）
  - `src/DeskBox/Services/DeskBoxDragData.cs:252/523`（`Dropped image/file yyyyMMdd-HHmmss`）
  - `src/DeskBox/Services/AttachmentStorageService.cs:45`（`Attachment-{DateTime.Now:yyyyMMdd-HHmmss}` 回退名）
  - `src/DeskBox/Services/AppUpdateService.cs:439`（更新器 helper 目录名 `{UtcNow:yyyyMMddHHmmss}-{pid}`）
  - `src/DeskBox/Services/ResilientJsonStore.cs:236/527`（`.unreadable-…` / `.corrupt-…` 后缀）
- **人话摘要**：同一插值模式，全部走 CurrentCulture。ar-SA 机器上这些文件/目录名内嵌回历年份。数据**不丢**（备份列表排序用 `File.GetLastWriteTimeUtc`，如 DeskBoxDataBackupService.cs:265），但：(1) 用户可见文件名年份错误；(2) `PruneQuarantinedCopies`（ResilientJsonStore.cs:559-561）按**文件名 Ordinal 序**保留"最新两个"，若用户中途切换 Windows 日历格式，混杂命名组下"1448…"恒小于"2026…"，会保留回历命名副本而删掉更新副本——仅取证副本选择错误，不影响用户数据；(3) UmAlQura 历法有效范围 1900-04-30..2077-11-16，系统时钟被拨到 2078+ 时这类 `ToString` 直接抛 `ArgumentOutOfRangeException`（备份创建失败走 catch 返回 Failed，不崩溃）。另 `src/DeskBox.Updater/Program.cs:552` 日志时间戳同模式（纯日志）。
- **建议方向**：统一改 InvariantCulture 插值（`string.Create(CultureInfo.InvariantCulture, $"…")` 或先 `ToString(…, InvariantCulture)`）。低危但量大，宜一次性清扫。
- **如何验证**：ar-SA 格式机器上导出一次备份看文件名；单测覆盖 PruneQuarantinedCopies 混合命名组。

### A-4 ·【P2 · 置信度：中】snooze 宽限上限（新）× 通知面长期不可用：snooze 及其宿主 due 提醒永久静默，且过期 SnoozedUntil 永久滞留 store

- **文件:行**：`src/DeskBox/Services/TodoReminderService.cs:744-751`（snooze 触发加 `now <= snoozedUntil + MissedReminderGrace` 上限，b96129f6 落地）、`:322`（SnoozeUntilAsync 在用户小睡时即写 `item.ReminderDismissedForDueDate = item.DueDate`）、`src/DeskBox/Services/NativeAppNotificationService.cs:200-205` + `src/DeskBox/App.xaml.cs:2925-2929`（Setting 探测 + 无托盘返回 false）
- **人话摘要**：新改动组合出的边界：当应用通知被系统禁用（df24c5fe 后 TryShow 返回 false）**且**托盘面不可用（`_trayIcon is null` 或托盘通知失败）时，`CheckCoreAsync` 走 `!shown` 分支释放会话键，30 秒后重试；但 snooze 触发的重试窗只有 `snoozedUntil + 1 分钟`（f5a01f91 引入）。两次重试失败后：(1) snooze 提醒永久丢失；(2) 由于用户当初点击小睡时已把 `ReminderDismissedForDueDate = DueDate` 落盘（due 提醒已被消费），该事项**所有**提醒永久静默；(3) 过期的 `SnoozedUntil` 永远留在 store —— `TryGetReminderTrigger` 的 snooze 分支（744 行）无条件拦截，即使宽限关闭后也**永不回落**到 due 分支评估。修复目标（停止整会话重试）合理，但"从未成功显示"的 snooze 既不消费也不放回 due 通道，是一个单向静默丢失。旧代码（无上限）在此环境下会一直重试，用户重新启用通知后还能收到；新代码不能。
- **代码证据**：
  ```csharp
  if (item.SnoozedUntil is { } snoozedUntil)
  {
      triggerKind = ReminderTriggerKind.Snooze;
      return now >= snoozedUntil && now <= snoozedUntil + MissedReminderGrace;   // 宽限关闭后永久 false，无回落
  }
  ```
  ```csharp
  item.SnoozedUntil = snoozedUntil;
  item.SnoozeLastNotifiedAt = null;
  item.ReminderDismissedForDueDate = item.DueDate;                               // due 提醒提前消费
  ```
- **触发路径**：用户在 toast 上点"小睡 10 分钟" → 用户随后在 Windows 设置里关闭 DeskBox 通知（或组策略禁用）且托盘面不可用 → 10 分钟后两次尝试失败 → 1 分钟宽限关闭 → 该事项再无任何提醒；即使几分钟后用户重新启用通知也追不回。
- **建议方向**（供产品取舍）：宽限窗口关闭且从未成功显示时，把 `SnoozedUntil` 清掉（恢复 due 分支评估，配合已有的 `ReminderDismissedForDueDate` 语义决定是否再触发），或在"从未显示过任何面"的失败分类中豁免宽限上限。至少应在日志/诊断中标记"因宽限关闭而放弃的 snooze 数"。
- **如何验证**：测试环境禁用应用通知 + 无托盘（或 mock `_notify` 恒 false），snooze 后越过 1 分钟再启用通知，断言提醒是否可恢复（当前实现：不可）。

### A-5 ·【P3 · 置信度：契约变化=高 / 实际触发=中】TryShow 新增的 `AppNotificationManager.Default.Setting` 探测在 try 块之外，破坏了"TryShow 永不抛"的隐含契约

- **文件:行**：`src/DeskBox/Services/NativeAppNotificationService.cs:200-205`（df24c5fe 新增），对照 `:207` 才开始的 try
- **人话摘要**：改动前 TryShow 全函数体在 try/catch 内、任何失败都返回 false；新探测是裸的 WinRT COM 属性读取。`Setting` 在通知平台半死状态（WpnService 崩溃/恢复中、注册成功后平台异常）下抛异常时将直接冒泡。多数调用点有 `?.`/try 兜底，但 `TryShowNativeTodoReminderNotification → ShowTodoReminderNotificationCore`（App.xaml.cs:2907-2929）在 UI 线程回调里无本地 catch：异常进 `OnUnhandledException`（Handled=true 吞掉）→ `completion.TrySetResult` 永不执行 → 该次 `CheckCoreAsync` 挂起 → `DisposeAsync` 的 `Task.WhenAll(_initialCheck, _activeCheck)` 挂起 → 关闭序列 `todo-reminders` 步骤（App.xaml.cs:5825-5827，`abortFollowingStepsOnTimeout: true`）超时后**跳过后续全部关闭步骤**（含 settings-flush、单实例互斥体释放）。链条长、概率低（UI 线程在关闭序列期间仍在泵消息，A-6 的 TCS 在正常关闭中会完成），但契约回归本身是实的。
- **代码证据**：
  ```csharp
  AppNotificationSetting setting = AppNotificationManager.Default.Setting;   // ← try 之外
  if (setting != AppNotificationSetting.Enabled) { ... return false; }
  try { ... AppNotificationManager.Default.Show(builder.BuildNotification()); ... }
  catch (Exception ex) { ... return false; }
  ```
- **建议方向**：探测移入 try（或单独 try/catch 返回 false）。顺带给 `ShowTodoReminderNotificationAsync` 的 `completion.Task` 挂上 `_lifetime` 取消或超时，消除无界等待面（与 A-5 同根，一并处理）。
- **如何验证**：代码审阅即可；运行时复现需要人为让 Setting 抛异常（mock 层注入）。

### A-6 ·【P3 · 置信度：高】Updater 等待前移后的新延迟面：installer 缺失路径最长 90 秒的无谓等待 / PID 复用等错进程

- **文件:行**：`src/DeskBox.Updater/Program.cs:70-82`（387c3180），`:115-135`（WaitForParentExit）
- **人话摘要**：修复方向正确（消除重启与父进程互斥体的竞态），但副作用：installer 不存在时，现在也要先等父进程退出（最长 90 秒）。两个退化场景：(1) 父进程挂死（关机卡死）时，"装不上、重启"这件事被推迟 90 秒；(2) `Process.GetProcessById(parentProcessId)` 在父进程已退出、PID 被系统复用后会命中**无关进程**，更新器对着无关进程白等 90 秒（复用概率低但存在）。属可接受的新权衡，记录备查；可在等待前校验进程名是否仍为 DeskBox。
- **如何验证**：更新流程传入已死 PID（已复用）观察日志 "Waiting for DeskBox process N to exit." 后的 90s 停顿。

### A-7 ·【P3 · 置信度：高（分析）/ 影响低】DisplayCulture 静态缓存按 Name 共享可变克隆实例

- **文件:行**：`src/DeskBox/Helpers/DisplayCulture.cs:20-29`
- **人话摘要**：`EnsureGregorian` 把按 Name 缓存的 `CultureInfo` 克隆跨线程共享。当前所有消费点只做只读格式化（`string.Format`/`ToString`），无实际错误；但共享实例是可变对象，任何未来调用方若改动其 `DateTimeFormat` 将污染全进程。另外：缓存键是 `Name`，同名但 user-override/实例来源不同的 culture 会共享**第一次**的克隆；以及 .NET 10 的已知 quirk（克隆后 `CultureInfo.Calendar` 属性仍报旧历法）使幂等性依赖"GetOrAdd 按 Name 命中"而非快速路径——当前推演成立（再次传入缓存克隆会走 GetOrAdd 分支拿回自身），但对未来改动脆弱。建议在 XML 注释里显式写明"返回实例必须视为只读"。
- **如何验证**：审阅即可；可加一个"两次 EnsureGregorian 引用相等且 DateTimeFormat.Calendar 为公历"的守护测试（现有 DisplayCultureTests 已部分覆盖）。

### A-8 ·【P3 · 置信度：中】MusicSessionService.InitializeAsync check-then-act 双初始化竞态

- **文件:行**：`src/DeskBox/Services/MusicSessionService.cs:77-95`、`:505-515`
- **人话摘要**：`InitializeAsync` 无锁：并发首次调用（音乐小部件首帧 + 轮询定时器同时触发 `GetCurrentSessionInfoAsync`）都能通过 `_isInitialized` 检查 → 两次 `RequestAsync`、双份 manager 事件订阅、`_manager` 后写覆盖。被覆盖的 manager 永不退订，其 `SessionsChanged` 仍会触发 `Manager_SessionsChanged`（处理器不校验 `sender == _manager`）→ 双份事件、旧 manager 泄漏。后果限于重复刷新事件与轻微泄漏，不损数据。
- **代码证据**：
  ```csharp
  if (_isDisposed || _isInitialized) { return; }
  var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();   // await 后才置位
  ...
  _isInitialized = true;
  ```
- **建议方向**：`SemaphoreSlim(1,1)` 门内二次检查，或 `Interlocked.CompareExchange` 抢占标志。
- **如何验证**：并发 20 次 `GetCurrentSessionInfoAsync` 后断言 `RequestAsync` 只订阅一次（事件计数）。

### A-9 ·【P3 · 置信度：高】SyncOutboxStore.DequeueAsync 只删主文件，`.bak` 轮转产物无限累积

- **文件:行**：`src/DeskBox/Core.Persistence/SyncOutboxStore.cs:126-136`；对照 `ResilientJsonStore.cs:383-388`（SaveAsync 的 File.Replace 把旧条目轮转进 `<op>.json.bak`）
- **人话摘要**：每次 Enqueue 经 File.Replace 在 `data/sync/outbox/<domain>/` 留下 `<op>.json.bak`；Dequeue 只 `File.Delete(path)` 主文件，`.bak` 永久残留。`ListAsync`/`PendingCount` 只看 `*.json`（`.bak` 不匹配模式），功能正确，纯属磁盘碎片缓慢累积（同步域每操作一个）。同步引擎尚未投产时清理成本最低。
- **建议方向**：Dequeue 时一并 `TryDelete(ResilientJsonStore.GetBackupPath(path))`。
- **如何验证**：入队→出队循环后枚举目录，断言无 `.bak` 残留。

### A-10 ·【P3 · 置信度：中】QuickCaptureStore 标签去重用 CurrentCultureIgnoreCase

- **文件:行**：`src/DeskBox/Services/QuickCaptureStore.cs:293`
- **人话摘要**：`Distinct(StringComparer.CurrentCultureIgnoreCase)` 用于持久化数据的标签归一化：tr-TR 等区域性下 "i"/"İ" 折叠行为与 Ordinal 不同，同一份 store 在不同机器上归一化结果不同（标签数量随机器漂移）。数据归一化应使用 `StringComparer.OrdinalIgnoreCase`。
- **如何验证**：tr-TR 文化下加载含 `["image","İmage"]` 标签的 store，观察 Normalize 后保留哪个/几个。

### A-11 ·【P3 · 置信度：高】CitySearch 去重键用 CurrentCulture 数值格式化（良性，记录备查）

- **文件:行**：`src/DeskBox/Services/CitySearchService.cs:205/226`（`$"{r.Latitude:F2},{r.Longitude:F2}"`）
- **人话摘要**：逗号小数点区域（de-DE/fr-FR）下键形如 `"52,52;13,41"`，与点号文化不同——但合并双方使用同一次运行内的同一文化，键一致性成立；仅当未来与跨文化持久化的键比较时才会失配。当前无实际错误，建议改 `FormattableString.Invariant(...)` 或 `ToString("F2", InvariantCulture)` 以绝后患。

### A-12 ·【P3 · 置信度：低】ArchiveZipService 细节三项（不阻塞）

- **文件:行**：`src/DeskBox/Services/ArchiveZipService.cs:112-125`、`:342`、`:586-636`
- **人话摘要**：(1) WriteZip 对目录源不写中间目录项，空子目录在 zip 中丢失（解压后不还原空目录；Explorer 同样丢失，兼容性可接受）；(2) zip 内两个同名条目时第二个 `ExtractToFile(overwrite: false)` 抛 IOException，留下**半解压目录**且无清理（恶意/损坏 zip 场景下的用户体验问题，非安全问题——Zip Slip 已正确防护：绝对路径与 `..` 均被 `StartsWith(destinationRoot + separator)` 检查拦截）；(3) `ResolveEntryEncoding` 的 U+FFFD 探测法对**合法含替换符的 UTF-8 名**（罕见）会误判走 ANSI 重解码。均为边缘质量问题。

---

## 最不确定、需要主流程复核的条目

1. **A-1（Wikimedia 分类名）**：机制确凿（CurrentCulture 插值），但"精选"类目空结果后上游是否还有其他回退（如自动落到 Bing provider）未完全追完调用链；若产品上有回退，可降为 P3。
2. **A-4（snooze 宽限权衡）**：这是**产品语义取舍**而非纯缺陷——"通知面长期不可用"是低频环境，且旧行为（无限重试）本身是被 f5a01f91 认定的缺陷。是否值得加"宽限关闭且从未显示则释放 SnoozedUntil"的补救，请维护者按产品意图裁决；我的分析基于三个代码事实（:322 提前消费、:744 无回落、df24c5fe 后无托盘必 false），推演本身置信度高。
3. **A-5（Setting 抛异常的现实概率）**：`AppNotificationManager.Default.Setting` 在"Register 已成功"之后抛异常的真实场景我无法静态确证（WpnService 中途死亡是候选）；契约回归与 TCS 无界等待链是确定的，触发概率请以实际环境评估。
4. **A-9（outbox .bak 残留）**：已确认 `DequeueAsync` 是唯一删除点且不删 `.bak`；若 sync 域尚未在 UI 暴露，影响面趋近于零，级别可保持 P3。
5. **A-2/A-3 的现实影响范围**：仅非公历默认历法的 Windows 格式（ar-SA、th-TH 等）用户可感知；该人群规模请结合用户分布判断是否合并为一次 InvariantCulture 清扫。

## 核实过认为稳健的重点区域（留痕防重复劳动）

- **ResilientJsonStore**（temp + File.Replace + .bak、读失败不动原文件、unreadable-primary 移侧保全、ERROR_UNABLE_TO_REMOVE_REPLACED 重试 + 校验式就地回退、隔离区限量）——本轮全量精读，未发现新问题。
- **TodoWidgetStore / QuickCaptureStore / WeatherCacheStore 的路径门模型**（同路径共享信号量、FMEM-01 门修剪的 `CurrentCount==1` + 保护根前缀 + `TryRemove(KeyValuePair)` 精确移除、MutateAsync 全程持门）。
- **SettingsService 复合保存**（layout 先行提交、settings 提交失败回滚 layout、新 schema 只读拒绝、`_widgetLayoutMerged` 防空切片覆盖窗口）与防抖生命周期（debounce CTS/代数）。
- **DeskBoxDataBackupService 恢复协议**（rollback-root 换入换出 + 内层失败回搬 + `TryRecoverStrandedRollback` 启动回收 + 未确认 scoped/full restore 丢弃 + 恢复前安全备份）。
- **fe3103d5 两阶段提醒协议本体**：`!shown` 释放会话键、PersistDisplayedReminders 的 staleness 守卫（DueDate/SnoozedUntil 相等性、IsCompleted/IsDeleted 跳过）、与 toast 按钮（完成/小睡）的两种交错顺序（先 persist 后小睡 / 先小睡后 persist）推演均无丢失更新；persist 失败保留会话键防重复 toast，重启重燃受宽限约束——除 A-4 的环境边界外自洽。
- **NativeAppNotificationService.Register/Unregister**（失败退订、5 分钟重试节流、IsSupported 前置）与 **df24c5fe 探测本体**（探测→托盘回退路径在全部调用点成立）。
- **WebDavBackupTransport**（DTD 禁用、href 仅取名不寻址防穿越、405/201 MKCOL 容错、Invariant 解析）与 **CloudBackupService**（目的地变更失效成功/失败/未验证戳、superseded-options 不落戳、PUT-accepted 与 verify 降级语义、保留钳位）。
- **AppUpdateService 下载**（temp + SHA-256 + Move、架构匹配 pin、可信域白名单含 GitHub 路径 pin）；**Updater** 其余路径（安装目录校验、注册表一致性、Shell 完整性降权重启三连试 + 开始菜单快捷方式兜底）。
- **EverythingSearchService**（DEF-044 超时竞速 + 网关"继承式独占释放"协议、Dispose 1s 门超时跳过 CleanUp）；**FolderWatcherService** 重连代际（`_watchGeneration` 双重复查）与 UI 线程弹射。
- **原生层**：`shortcut.rs`（COM init 守卫含 RPC_E_CHANGED_MODE、PIDL 释放守卫、两段式 buffer-too-small 协议、SLR 超时高字编码）、`recycle_bin.rs`（STA + 守卫）、lib.rs 导出层（信封校验 + 预写空结果）、`panic=abort` 双 profile 符合契约文档；C# 侧 RecycleBinNativeBackend 的 fixed 封送与结果一致性断言。
- **同步阻塞桥接抽查**（DesktopOrganizationRecoveryStore.Save / SearchHistoryService.Save / MusicSettingsStore.WaitForPendingPersist / GlobalHotkeyService 等的 `.GetAwaiter().GetResult()`）——均有池线程桥接或非 STA 上下文说明，未见死锁面。
- **DisplayCulture 的 15 个消费位点复核**（TodoReminderService/TodoClipboardFormatter/TodoItemViewModel/QuickCaptureItemViewModel/QuickCaptureSurfaceContent/SettingsViewModel.AboutAndUpdates/ContentWidgetWindow/GlanceWidgetViewModel/OrganizationHistoryEntry/LocalizationService.Format）：全部正确传 `DisplayCulture.GregorianCurrent`；`HH:mm` 类纯时间格式与历法无关，无需钉。

—— 审查完毕。无 P0/P1；建议优先处理 A-1/A-2（一次性 InvariantCulture 清扫可同时覆盖 A-3/A-6 日志、A-11），并对 A-4 做产品裁决。
