# 第 12 轮（从零）审查 · 维度 A（服务与数据面）

- 审查员工作方式：纯静态代码审查（只读，不构建/不运行/不改源码），未读取 `docs/audit/` 下任何历史文件；仅信当前代码。
- 基线：`wip/fix-bug` @ `9cfd9611`。
- 重点攻击对象：最近三个代码提交（`fe3103d5` 两阶段提醒协议、`a251103d` Glance 公历修复、`9a6cd7e3` 持久化 Move 包装），然后全量扫描。

## 概览

扫描范围与方法：

1. `git log --oneline -15` + `git show` 逐行精读三个最近修复提交的完整 diff，再精读其涉及文件的当前全量代码（TodoReminderService.cs 807 行、App.xaml.cs 相关段、TodoReminderRuntime、TodoSettingsCoordinator、TodoNotificationActivationRouter、GlanceWidgetViewModel、WeatherService、PersistenceFileMutations、ResilientJsonStore）。
2. 精读数据面核心：ResilientJsonStore、TodoWidgetStore、QuickCaptureStore、DeskBoxDataBackupService（3650 行，重点 restore/rollback/prune/staging/zip 校验）、CloudBackupService、WebDavBackupTransport、AttachmentStorageService、ArchiveZipService。
3. 精读 App 结束序（ShutdownApplicationAsync/ShutdownCoreAsync 全部步骤）、OnLaunched 启动流水、通知激活路由链。
4. 精读 DeskBox.Updater/Program.cs 全文、AppUpdateService 启动 helper 段。
5. 抽查原生互操作：native/Cargo.toml panic 策略、lib.rs 导出包装（validate→init→validate→run）、music_volume/shortcut 等模块头部。
6. 全量 grep 模式：`async void`、`.Result/.Wait()/GetAwaiter().GetResult()`、`DateTime.Now`、无 culture 的 `ToString/Parse`、`File.Delete|File.Move|Directory.Delete|Directory.Move` 分布、`GetLastWin32Error`、`Process.Start`、`catch (Exception)` 吞异常点，对命中逐一人工复核（大量误报如 `outcome.Result` 为记录属性、`candidate.Result` 为 LINQ 元素等已排除）。

命中统计：确认问题 9 条（P2×3，P3×6）；无 P0/P1。该代码面经过 11 轮修复后成熟度很高，本轮主要收获是"新修复自身的残留缺口"（A-1、A-2 直接攻击 a251103d 与 fe3103d5 的不完整面）。

## 问题清单

### A-1 【P2 · 置信度：高】公历修复只修了 Glance：Todo/提醒/速记/设置的大量日期仍按区域默认日历（ar-SA 回历）渲染

- 文件与行：
  - `src/DeskBox/ViewModels/TodoItemViewModel.cs:671-676`（`FormatDueDateTime`，条目截止日期主显示，调用点 :441）与 `:699-701`（`FormatSnoozeDateTime`，调用点 :465）：
    ```csharp
    private static string FormatDueDateTime(DateTimeOffset dueDate)
    {
        return dueDate.Second == 0
            ? dueDate.ToString("yyyy/M/d HH:mm")      // 无 culture → 区域默认日历
            : dueDate.ToString("yyyy/M/d HH:mm:ss");
    }
    ```
  - `src/DeskBox/Services/TodoReminderService.cs:680-701`（`FormatDueDate`，提醒 toast 的到期文案）：`localDueDate.ToString("yyyy/M/d HH:mm")` 同样无 culture。
  - `src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs:1265、:1304`：`DateTimeOffset.Now.ToString("yyyy/M/d HH:mm")`（速记详情"创建于"）。
  - `src/DeskBox/ViewModels/SettingsViewModel.AboutAndUpdates.cs:575-576`：`local.ToString("yyyy-MM-dd HH:mm")`（上次更新检查时间）。
- 人话摘要：`a251103d` 把 Glance 主日期强制为该文化的公历变体，其提交信息声称"应用内其他日期本来就保持公历"——这个声明与代码不符。上述 Todo 列表/详情的截止日期、snooze 时间、提醒 toast 到期文案、速记创建时间、设置页更新检查日期全部直接 `ToString(...)` 无 culture；进程从不设置 `CultureInfo.CurrentCulture`（全仓 grep 无 `CurrentCulture =` 赋值），因此 CurrentCulture 就是 OS 用户格式文化。Windows 格式文化为 ar-SA（默认 Um Al-Qura 日历）的用户，其 Todo 截止日期主显示为回历"1448/4/13 09:30"——与 Glance 修复后确立的"主日期一律公历"约定直接矛盾，也和日历格、天气（已修公历）不一致。
- 触发路径：OS 区域格式 = ar-SA（或其他默认非公历日历文化）→ 打开任意 Todo widget 查看条目截止日期 / 收到提醒 toast / 打开速记详情 / 设置-关于-更新时间 → 看到回历年月日。
- 建议方向：把 Glance 的 `EnsureGregorianCalendar` 模式提为共享 helper，逐站点替换上述 6+ 处（TodoItemViewModel 两个格式化函数、TodoReminderService.FormatDueDate、QuickCaptureSurfaceContent 两处、SettingsViewModel.AboutAndUpdates 一处）；建议同时全仓再扫一遍 `ToString("yyyy` / `ToString("MM-dd` 无 culture 命中。
- 如何验证（不执行）：临时把进程 `CurrentCulture` 切到 `ar-SA` 后断言 `FormatDueDateTime(new DateTimeOffset(2026,10,25,...))` 含 "2026"；或 A/B 反向把 Glance 的修复撤掉对照。

### A-2 【P2 · 置信度：中高】提醒"已显示"信号过度乐观：原生 toast 被系统静默吞掉时仍返回 true，新两阶段协议照样消费提醒且跳过托盘兜底

- 文件与行：
  - `src/DeskBox/Services/NativeAppNotificationService.cs:287-288`：
    ```csharp
    AppNotificationManager.Default.Show(builder.BuildNotification());
    return true;
    ```
    `Show` 的返回值（`AppNotificationResult`）与 `AppNotificationManager.Setting`（应用级通知开关）都没有检查；整个文件无任何 `Setting`/`NotificationSetting` 引用（grep 证实）。
  - 消费端：`src/DeskBox/App.xaml.cs:2909-2917`（`TryShowNativeTodoReminderNotification` 返回 true 即认定已显示，不再走 `_trayIcon` 兜底）→ `src/DeskBox/Services/TodoReminderService.cs:231`（`PersistDisplayedRemindersAsync` 落盘 `ReminderDismissedForDueDate`/清 `SnoozedUntil`）。
- 人话摘要：`fe3103d5` 的核心是"只有确认显示过才消费提醒"。但"显示过"的判据是 `Show()` 没抛异常。Windows App SDK 在"该应用的通知被用户/组策略关闭"（设置→系统→通知→DeskBox 关闭，或 Focus/勿扰语义下）时 `Show` 通常不抛异常、通知被静默丢弃——此时 `TryShow` 返回 true，服务持久化消费标记，并且因为返回了 true 连托盘气泡兜底也不会尝试。这正是该提交想消灭的"提醒被无声消耗"类别的一个残余入口。
- 触发路径：用户在系统设置里关掉 DeskBox 的 toast（但托盘仍在）→ 到期提醒 → `Register()` 已成功、`Show()` 不抛 → 返回 true → 标记落盘 → 提醒永久消费、无任何可见提示。
- 建议方向：`TryShow` 里在 `Register()` 成功后检查 `AppNotificationManager.Default.Setting`（`DisabledForApplication`/`DisabledForSystem` 时返回 false），并检查 `Show` 返回的 `AppNotificationResult.Status`；失败即回落托盘路径，让两阶段协议的 retry 语义接管。
- 如何验证：临时用 `AppNotificationManager.Default.Setting` 探测 + 在测试机用系统设置关掉应用通知，观察日志 `[TodoReminder] Native notification shown` 是否仍出现且无托盘气泡。

### A-3 【P2 · 置信度：中】Updater "安装包缺失"路径不等父进程退出就重启应用：与单实例互斥体竞态，可留下"整机没有 DeskBox 在跑"的结局

- 文件与行：
  - `src/DeskBox.Updater/Program.cs:70-75`：
    ```csharp
    if (string.IsNullOrWhiteSpace(options.InstallerPath) || !File.Exists(options.InstallerPath))
    {
        Log("Installer not found.");
        RestartAfterIncompleteUpdate(options, "failed");   // 未先 WaitForParentExit
        return 2;
    }
    WaitForParentExit(options.ParentProcessId);            // 正常路径才等
    ```
  - 父进程仍在运行的证据：`src/DeskBox/Services/AppUpdateService.cs:332` `Process.Start(startInfo)` 后立即返回 `Started()`，应用随后才走 `ShutdownForUpdateAsync`（`App.xaml.cs:5606`）→ `ShutdownApplicationAsync`（各步骤累计可达 15s+ 级别）。
- 人话摘要：安装包在下载完成到 updater 启动之间消失（典型：杀毒软件隔离刚下载的 exe）时，updater 立刻重启 DeskBox.exe 而不等旧进程退出。旧实例还握着单实例互斥体（仓库文档明确："在跑实例持有锁使新实例静默退出"），新实例要么静默退出、要么去激活一个正在死去的实例。DEF-113 注释表明"更新失败后机器上没有 DeskBox 在跑"正是他们要防的结果——这个路径绕过了防护。安装包被 AV 隔离并非罕见。
- 触发路径：下载完成 → AV 隔离安装包 → 用户点"安装并重启" → updater 启动（旧 app 开始关闭流水）→ updater 发现安装包没了 → 立即重启 app → 新 app 撞上旧 app 的互斥体 → 双方都退出/旧实例死掉 → 桌面无 DeskBox。
- 建议方向：把 `WaitForParentExit(options.ParentProcessId)` 提到 installer 存在性检查之前（或缺失路径里也先等再重启）。
- 如何验证：构造 `--installer` 指向不存在文件、`--pid` 指向一个存活 10s 的父进程，观察 updater 重启的 app 是否撞单实例锁（日志/进程列表）。

### A-4 【P3 · 置信度：高】提醒扫描中途中止时 `_sessionNotifiedKeys` 的会话键搁浅（新协议只覆盖了"显示失败"路径的回收）

- 文件与行：`src/DeskBox/Services/TodoReminderService.cs:192-194` 与 `:196-199`：
  ```csharp
  foreach (var widget in widgets)
  {
      if (_disposed || !ShouldBeRunning()) return 0;   // widget1 的键已 TryAdd，直接返回
      await CollectWidgetCandidatesAsync(...);
  }
  if (candidates.Count == 0 || _disposed || !ShouldBeRunning()) return 0;
  ```
  对比显示失败路径 `:218-229` 有显式 `TryRemove` 回收；收集阶段异常（外层 catch :234）同样不回收已领的键。
- 人话摘要：多 widget 扫描进行到一半时功能被禁用/服务被退役/某个后位 widget 的读盘抛错，已收集候选的会话去重键留在字典里，这些条目本次会话内不再提醒。缓解因素：`Refresh()` 在功能禁用时清空键（:135-143），进程重启也清空；且 `TodoWidgetStore.LoadAsync` 走 ResilientJsonStore 基本不抛。影响限于"禁用→快速重开"窗口和极端 IO 异常，属小概率功能损害。
- 建议方向：把 :192/:196 的提前返回改成与 `!shown` 相同的键回收（或把收集循环包 try/finally 统一回收）。
- 如何验证：单测里放两个 todo widget，让第二个 store 构造/读取抛错，断言第一个 widget 的提醒在下一轮扫描仍会触发。

### A-5 【P3 · 置信度：高】snooze 触发的提醒重试无上限：显示面永久不可用时每 30 秒空转直到会话结束

- 文件与行：`src/DeskBox/Services/TodoReminderService.cs:743-747`（snooze 分支 `return now >= snoozedUntil;`，无上限）对比 `:763-783` `ShouldNotifyDue` 有 `now <= dueDate + MissedReminderGrace`（1 分钟）封顶。
- 人话摘要：due 触发的提醒在显示面不可用时最多重试到 due+1min 后放弃；snooze 触发的提醒没有对应天花板。若通知平台坏了且托盘图标为 null（`App.xaml.cs:2919-2927` 返回 false），snooze 条目每 30 秒重扫+重试+两条日志，持续整个会话（可数小时）。正确性无损害（一旦显示成功即消费），但这是无限期的日志/IO 噪声与 futile 循环。
- 建议方向：给 snooze 触发也加一个"最后可提醒窗口"（如 `snoozedUntil + MissedReminderGrace` 或以 `SnoozeLastNotifiedAt` 封顶），或对连续失败次数设上限后转日志一次性放弃。
- 如何验证：`dispatcherQueue:null` + notify 恒 false 的 harness，固定时钟推进 1 小时，观察每 30s 一轮的扫描行为。

### A-6 【P3 · 置信度：中】WebDAV PROPFIND 条目名二次 URL 解码：含字面 `%XX` 的远端文件名会被错误寻址（保留清理有删错文件的理论窗口）

- 文件与行：`src/DeskBox/Services/WebDavBackupTransport.cs:257/261`（`decodedPath = Uri.UnescapeDataString(...)` 得到已解码路径），随后 `:285-286` 又对解码结果再解码一次：
  ```csharp
  string name = Uri.UnescapeDataString(
      decodedPath.TrimEnd('/').Split('/').Last());
  ```
- 人话摘要：文件名里含字面 `%2D` 之类序列时（服务器 href 会编成 `%252D`，第一次解码还原为 `%2D`，第二次解码变成 `-`），`name` 与真实文件名不符。该 `name` 用于上传验证的列表匹配（`CloudBackupService.VerifyUploadAsync:598-599`）与保留清理删除（`ApplyRetentionAsync:726-742`，删错要求错误名恰好也通过 `IsSafeSnapshotBasename` 且远端存在同名快照，概率极低）；更常见后果是验证降级/条目匹配失败。
- 建议方向：`name` 直接取 `decodedPath` 最后一段，去掉第二次 `UnescapeDataString`。
- 如何验证：单测构造 href `.../DeskBox-CloudBackup-2026%252D10-aaaaaaaa.zip`，断言解析出的 Name 含字面 `%2D`。

### A-7 【P3 · 置信度：中】全量恢复"swap 成功→删除 rollback 前"崩溃窗口会遗留整份数据大小的 `restore-rollback/<guid>` 目录，且无任何清理路径

- 文件与行：`src/DeskBox/Services/DeskBoxDataBackupService.cs:1296-1315`（成功路径在 marker 删除/回滚删除前崩溃则 rollback 残留）；`TryRecoverStrandedRollback:2456-2507` 因 `HasUserDocuments(DataDirectory)` 为 true（恢复后的数据有文档）直接 return，不会动它；全文件对 `restore-rollback` 的其余引用只有当次运行的 `rollbackRoot`（:1291、finally :1336-1341）与恢复成功后删除 candidate（:2497）。
- 人话摘要：正确的保守设计（不该恢复时不恢复）副作用是：这个窗口里崩溃后，旧数据的完整副本永远躺在 `restore-rollback/<旧guid>/`，下一次恢复又建新 guid 目录。每次发生泄漏一份全量数据（可达数百 MB），无上限累积。数据安全无损（多一份副本），属磁盘空间/卫生问题。
- 建议方向：`TryRecoverStrandedRollback` 或启动维护里加"live 数据健康时清理超过 N 天的旧 rollback 目录"。
- 如何验证：手工构造 `<root>/restore-rollback/<guid>/data`（含 settings.json）+ 健康的 live data，多次重启观察目录是否永存。

### A-8 【P3 · 置信度：中】ArchiveZipService 用户级压缩/解压的三个健壮性缺口

- 文件与行：
  1. `src/DeskBox/Services/ArchiveZipService.cs:302-328`：`./` 或 `.` 形式的根条目（Linux `zip -r x.zip ./` 常见）经 `Path.GetFullPath` 归一为 destinationRoot 本身后，`StartsWith(destinationRoot + sep)` 为 false → 整包解压以"条目会解到目标文件夹外"的误导性错误失败。
  2. 同文件 `ExtractAllAsync:200-243` 无任何文件数/字节预算（对比恢复管线 `MaxRestoreFileCount/MaxRestoreFileSizeBytes/MaxRestoreTotalSizeBytes`），恶意/损坏 zip 可把磁盘写满。
  3. `CompressAsync:62-78` + `WriteZip:81-128`：直接以最终名 `FileMode.CreateNew` 写档，中途失败（源文件被删/IO 错）在目标位置留下残缺 .zip，无 temp+rename、无清理。
- 人话摘要：用户"解压全部"对某些 Linux 产物直接报错且文案误导；解压无配额；压缩失败留半个损坏压缩包，用户可能之后误用。
- 建议方向：对归一后等于 destinationRoot 的目录条目跳过而非报错；解压复用恢复管线的预算常量；压缩改 temp+`File.Move` 提交并 finally 清理。
- 如何验证：构造含 `./` 条目的 zip 走 ExtractAllAsync；构造超大声明条目；压缩中途删除源文件观察残留。

### A-9 【P3 · 置信度：低】snooze"明天 9 点"在 DST 切换日的无效本地时刻处理是近似的

- 文件与行：`src/DeskBox/Services/TodoNotificationActivationRouter.cs:258-268`（`GetTomorrowAtNine`：`GetUtcOffset(localTomorrowAtNine)` 对无效本地时刻返回标准偏移的近似）。9:00 本地几乎不会落入 DST 空档（切换通常在凌晨），误差最多一小时且仅极少数时区/日期。仅记录，不建议立即处理。

## 最不确定、需要主流程复核的条目

1. **A-2 的平台行为前提**：Windows App SDK 在"应用通知被系统设置关闭"时 `AppNotificationManager.Show` 究竟是抛异常还是静默丢弃。我依据 WinAppSDK 文档语义（`Setting` 属性存在的意义即 Show 不抛）判断为静默丢弃，置信度中高；需要一次真机实验或对照 SDK 源码确认。若 Show 在该场景确实抛异常，则 A-2 降级为"Focus 勿扰场景仍乐观"的 P3。
2. **A-3 的竞态窗口大小**：取决于旧实例 `ShutdownApplicationAsync` 从 Process.Start(updater) 到互斥体释放的实际时长（通常秒级，最长可到 backstop 5-8s）。 updater 无条件立即重启时撞锁概率高但非 100%；需要实测定级（也可能维持 P2 或降 P3）。
3. **A-1 的用户面口径**：是否所有非公历默认文化都要强制公历（`EnsureGregorianCalendar` 只找 `GregorianCalendar` 变体，找不到时回落原日历）。ar-SA 场景机制确凿；其他文化（如 th-CN 佛历格式文化）是否同样受影响未逐一验证。
4. **A-4 的可达性**：`TodoWidgetStore.LoadAsync` 实际上几乎不可抛（ResilientJsonStore 全捕获），我按"禁用→快速重开"窗口评估为 P3；若存在我未发现的 LoadAsync 抛出路径（如 gate WaitAsync 的 OOD），可达性会升高。

## 已核实认为稳健的重点区域（留痕，防下轮重复劳动）

1. **fe3103d5 两阶段提醒协议主体**：选择阶段在 store gate 下只读（`CollectWidgetCandidatesAsync` 只 LoadAsync+TryAdd）；notify 返回 bool 门控消费；`PersistDisplayedRemindersAsync` 的陈旧性守卫完备（完成/删除跳过、due 变更跳过、re-snooze 后 `SnoozedUntil` 不等跳过、Due 分支不清 SnoozedUntil 所以 toast 上再 snooze 的用户新计划保留）；显示失败释放会话键在 grace 窗口内重试；持久化失败保留键（有界重启重放）注释与实现一致；`DisposeAsync` 等待 `_initialCheck/_activeCheck` 且两个 task 均有全捕获不可能 fault；`ShowTodoReminderNotificationAsync` 的 TCS 用 `RunContinuationsAsynchronously`，UI 线程 enqueue 失败返回 false 进重试；shutdown 顺序（todo-reminders 先于 notifications/tray-icon/service-container）保证在飞行检查的 persist 能完成。多 widget 会话键按 `widget:item:trigger:due:snooze` 区分，`DateTimeOffset?` 守卫用 `!=`（按 UTC 瞬时比较，offset 差异不误伤）。AOT smoke 委托已同步改为 `Task.FromResult(true)` 语义（App.AotTodoNotificationActivationSmoke.cs:220、App.AotTodoRecurrenceReminderSmoke.cs:204-208），契约一致。
2. **TodoReminderRuntime/TodoSettingsCoordinator 生命周期**：Reconcile 的 retire/refresh/create 失败回滚、`_retiringSessions` 修剪与重复 DisposeAsync 幂等、relay 按实例订阅/退订——未发现泄漏或双启。
3. **ResilientJsonStore**：temp+`File.Replace` 提交、ERROR_UNABLE_TO_REMOVE_REPLACED 双 retry + 守卫后的验证式就地回退、不可读主文件先 move-aside 再覆盖（带 3 份上限修剪）、corrupt 隔离 + 2 份修剪、备份恢复主文件路径均成立。`RevertLastCommit` 的前提（bak 存在）在两种提交路径下都成立。
4. **TodoWidgetStore/QuickCaptureStore**：按路径共享的串行 gate、FMEM-01 gate 字典修剪的保护根判定、`Normalize` 的提醒标记一致性清理（due 变更清 dismissed/snooze 是有设计的行为）、孤儿店收养的 claimed-id 排除与附件路径 rebase 顺序。
5. **DeskBoxDataBackupService 恢复管线**：全量恢复 move-aside + 回滚 + `TryRecoverStrandedRollback`（HasUserDocuments 守卫使其不会覆盖已恢复数据）；scoped 恢复"staged payload 缺失即硬失败再删任何 live 文件"的前置校验（:1409-1426）、确认门（未确认 marker 下次启动丢弃）、重试预算与 pinned pre-restore 归档防叠加；zip 提取的 zip-slip 守卫、声明长度+实际字节双层预算、SHA-256 完整性清单；快照拷贝的稳定性协议（长度+mtime 双检 4 次重试）；FileSafety 元数据的 OperationGate→settings 写锁顺序。
6. **CloudBackupService/WebDavBackupTransport 核心**：UTC+设备后缀命名（同分钟双机不互踩）、`IsSafeSnapshotBasename` 防 traversal、删除路径白名单（DeleteSnapshotAsync 只允许四个托管目录）、PROPFIND DTD 禁用、propstat 200 过滤、数字/日期解析全部 InvariantCulture、最终一致服务器的"上传未验证"降级语义、配置中途变更不盖 last-success 戳。
7. **原生互操作**：workspace `panic = "abort"`（dev+release 双档，native/Cargo.toml:9-16）满足"panic 不过 C ABI"契约；lib.rs 导出包装一律 validate_result_envelope → initialize → validate_request → 分发，失败路径统一 write_failure，未发现无守卫的裸解引用；`expect/unwrap` 仅存在于 #[cfg(test)] 段。
8. **Updater 正常路径**：安装目录 pin（/DIR）+ 注册表一致性复核（InstallRegistrationMismatchExitCode）、scope 判定链、shell 完整性重启三级回退；`--pid`/`--installer-lang` 解析均 InvariantCulture 且语言白名单。
9. **同步等待点**：`DesktopAutoOrganizationSuppressionRegistry.LoadLedger`、`DesktopOrganizationHistoryStore.SaveChecked`、`FlushSettingsForEndSession` 三处 sync-over-async 均为 `Task.Run(...).GetAwaiter().GetResult()`/`Wait(3s)` 形态且注释明确规避 STA 死锁，属有意设计。`ShellContextMenuProxy` 的 `.Result`/`CitySearchService` 的 `candidate.Result` 是记录属性/LINQ 元素，非 Task.Result。
10. **天气两笔 invariant 修复（6e1c1145/39826268）与 Glance 公历修复（a251103d）本身**：diff 与描述一致、解析点均带 `DateTimeStyles.None`、排序键 TryParse 失败回落 MaxValue 语义未变、Glance 的 culture 克隆失败回落安全（其声明范围之外的问题见 A-1）。
