# 第 14 轮（从零）审查 · 维度 A（服务与数据面）

- 审查对象：`E:\DeskBox`（分支 `wip/fix-bug`，HEAD = `fa6321b0`）。
- 审查方式：纯静态。先精读最近 6 个功能提交（`eeb1c918`、`aee38695`、`041a7274`、`1a5514dc`、`c004c5c6`、`387c3180`）的完整 diff 与涉及文件当前全量，再对 `src/DeskBox/Services/`、`src/DeskBox/App*.cs`、`src/DeskBox/Helpers/`、`src/DeskBox.Updater/`、`native/deskbox-native/src/` 做全量模式扫描（区域性、async void、持锁跨 await、fire-and-forget、先删后写、GetLastWin32Error 时序、WebDAV/zip 协议面）并逐点核对命中。
- 未读取 `docs/audit/` 下任何历史结论；本文件是本轮唯一写入的文件。
- 分级：P0（数据永久丢失/崩溃/安全）、P1（用户可感知的数据或功能损害）、P2（特定环境或小概率的用户可感知损害/诊断退化）、P3（轻微/卫生/加固建议）。每条附置信度与验证建议。

## 概览

- 最近 6 个提交本身的改动质量较高：`string.Create(Invariant)` 的 15+2 处逐点核对语义等价（见"已核实稳健"）；公历显示补全 11 处均正确路由 `DisplayCulture.GregorianCurrent`；通知 Setting 探测入 try 后所有失败路径仍返回 false；提醒会话键的新生命周期（abort 释放、`_shownWithoutPersistKeys`、`ResetSessionClaimedKeys`）在主路径上逻辑自洽；DST 接缝重载与隐式 Local 路径逐点等价。
- 本轮未发现 P0/P1。发现 P2 两项（zip "./" 根条目拒绝解压；云备份选项切换与成功戳的竞态），P3 十一项。
- 总体印象：备份/恢复/存储层（ResilientJsonStore、DeskBoxDataBackupService 的校验与原子提交、TodoWidgetStore、Sync stores）防御密度很高且明显经过多轮打磨；本轮新问题集中在较边缘的服务（右键解压、WebDAV 解析细节、SMTC 初始化、图片缓存裸写）与并发时序残余。

---

## 问题清单

### A-1（P2 · 置信度：高）ArchiveZipService：含 `"./"` 根目录条目的合法 zip 会被整体拒绝解压，并留下半解压目录

- 位置：`src/DeskBox/Services/ArchiveZipService.cs:298-328`（`ExtractEntry`）。
- 证据：
  ```csharp
  string entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);   // "./" → ".\"
  fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entryPath));        // 规范化为 destinationRoot 本身
  bool isInsideRoot = fullPath.StartsWith(
      destinationRoot + Path.DirectorySeparatorChar,
      StringComparison.OrdinalIgnoreCase);                                       // 根自身不带尾分隔符 → false
  if (!isInsideRoot)
  {
      throw new InvalidDataException(
          "The archive contains an entry that would extract " +
          "outside the destination folder.");
  }
  if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(fullPath); return; }  // 目录分支在检查之后才到达
  ```
- 触发路径：用户在格子右键"全部解压"一个由常见工具产出的、含 `"./"`（或 `"."`）目录条目的 zip（macOS/Info-ZIP 的 `zip -r out.zip .` 风格很常见）。`Path.Combine(root, ".\")` 规范化后等于 `destinationRoot`（无尾分隔符），`StartsWith(root + '\')` 必为 false → 以"条目会解压到目标文件夹之外"这种误导性错误中止**整个**解压；此前的条目已写入磁盘，残留半解压目录。
- 目录条目分支（330 行）本可无害处理 `"./"`，但 containment 检查排在它前面。自身产物不受影响（`WriteZip` 写 `relativePath + "/"`，备份 zip 只写 `data/...`），风险全部来自第三方压缩包。
- 影响：一类合法归档的"全部解压"功能完全不可用 + 误导性错误文案。功能损害、非数据丢失，故 P2。
- 验证建议：构造含 `"./"` 条目的 zip（`zip -r t.zip .`）走右键"全部解压"，确认抛出与残留；修复方向是 containment 判断对"规范化后等于根"的目录条目放行（或在规范化前剥离 `.` 段）。

### A-2（P2 · 置信度：中）CloudBackupService：`UpdateOptions`（UI 线程）与在途上传的 `MarkSuccessAsync`/`RecordFailureAsync`（后台）存在丢失更新竞态

- 位置：`src/DeskBox/Services/CloudBackupService.cs:72-109`（`UpdateOptions`，无云备份 gate、无 settings `_lock`）、`758-794`（`MarkSuccessAsync`）、`803-826`（`RecordFailureAsync`）。
- 时序（上传进行中用户改目标端点）：
  1. 后台 `RunBackupCoreAsync` 已完成 PUT/校验，进入 `MarkSuccessAsync`，第 767 行用**旧** `_options` 通过 `DestinationIdentityChanged` 检查；
  2. UI 线程 `BackupBackend.RefreshOptions → UpdateOptions`：先写 `CloudBackupLastSuccessUtcTicks = 0`（第 90 行，且随后 `_options = options` 在第 107 行才交换）；
  3. `MarkSuccessAsync` 第 775 行 `_options = _options with { LastSuccessUtc = now }` —— 若读旧引用在交换之后写入，旧目标的成功戳被**回填**到新目标的 options/切片上；第 776 行把 `now` 写回切片，覆盖第 2 步刚写的 0；随后的 `SaveDebounced`（UI）与 `SaveAsync`（后台）的最后落盘者决定最终值。
- 后果：新目标端点被盖上从未真实运行过的 `LastSuccessUtc` —— 正是 767-772 行注释声称要防止的"虚假安全感"，表现为新目标的首个备份被推迟一个完整 interval，状态行显示错误的成功/失败时间。窗口窄（一次上传收尾 + 一次设置保存重叠），但 `RefreshOptions` 在每次设置变更都会触发，长期用户可命中。
- 附带：`UpdateOptions` 对 `Settings.CloudBackup` 切片的直写不在 `SettingsService._lock` 下，与后台 `SaveToFileOnlyAsync` 序列化（`WriteSettingsTempFileAsync` 持 `_lock`，`SettingsService.cs:1286-1350`）并发时可能持久化出混合状态（成功戳来自旧目标、失效清零丢失一半）。
- 验证建议：单测注入慢 transport，在 `RunBackupCoreAsync` 的 verify 之后挂起，调用 `UpdateOptions(新端点)`，再放行，断言 `CloudBackupLastSuccessUtcTicks == 0` 且 `Options.LastSuccessUtc == MinValue`；当前代码大概率断言失败。

### A-3（P3 · 置信度：高，机制确定）MusicSessionService.InitializeAsync 无 await 后重入检查，首个初始化窗口重叠即双重订阅

- 位置：`src/DeskBox/Services/MusicSessionService.cs:77-95`。
- 证据：`_isInitialized` 仅在方法入口（第一个 await 之前）检查；`RequestAsync()` 期间方法挂起，第二个调用者照样进入，完成后各自执行 `_manager = manager; _manager.SessionsChanged += ...; AttachCurrentSession(...)`。
- 触发路径：不需要跨线程竞态 —— UI 线程上两个 `_ = RefreshXxxAsync()` fire-and-forget（`MusicWidgetViewModel.cs:745/765/782` 一带大量存在）在首次初始化未完成时重叠即可：timeline 事件与 playback 事件几乎必然同帧到达。
- 后果：第一个 manager 包装实例的事件订阅永不解除（`Dispose` 只退订最后一个 `_manager`），SMTC 事件双份到达 → 音乐控件刷新风暴（有 generation 防抖兜底，实际多为重复刷新）；COM 订阅泄漏至进程结束。功能影响轻，定 P3。
- 验证建议：单测并发两次 `InitializeAsync()`（或 UI 模拟两个 `_ = Refresh...` 首帧重叠），断言 `SessionsChanged` 每个系统事件只回调一次；修复即在 await 之后复查 `_isInitialized`/`_manager` 已设置则直接返回。

### A-4（P3 · 置信度：高）QuickCaptureService.SaveImageFileAsync：内容寻址文件裸写（无 temp+rename），崩溃截断会永久污染该内容哈希

- 位置：`src/DeskBox/Services/QuickCaptureService.cs:2084-2098`。
- 证据：
  ```csharp
  string imagePath = Path.Combine(_store.ImageDirectory, $"{contentHash}{extension}");
  if (!File.Exists(imagePath))
  {
      await File.WriteAllBytesAsync(imagePath, bytes);   // 直接写最终路径
  }
  ```
- 触发路径：写途中断电/崩溃 → 磁盘上留下以该内容 SHA-256 命名的**截断**文件；后续任何相同内容的截图看到 `File.Exists` 为真即复用坏文件（同文件还参与 `GetThumbnailPath` 的缩略图键），直到用户手动清缓存。内容寻址缓存把"一次部分写"固化为"该内容的永久错误版本"。
- 概率低（单张图片写入窗口内崩溃），但静默且自愈不能，P3。修复方向：写 `hash.ext.guid.tmp` 后 `File.Move` 提交（仓库其它存储层均已是该模式，此处是漏网点）。
- 验证建议：在 `File.WriteAllBytesAsync` 注入中途异常，再重复捕获同一图片字节，确认复用了截断文件。

### A-5（P3 · 置信度：高，机制确定；触发面为环境切换）WidgetBackgroundCustomization：背景亮度用 ambient culture 写读往返，跨区域设置迁移时静默变值

- 位置：`src/DeskBox/Services/WidgetBackgroundCustomization.cs:123、128、255、304、360`。
- 证据：写入 `config.Metadata[DimMetadataKey] = dim.ToString("0.#")`（ambient），读取 `double.TryParse(value, out ...)`（ambient）。en-US 下写 `"0.5"`；用户把 Windows 格式切到 de-DE 后，下一会话 `"0.5"` 被当作千位分组解析为 `5`（或 fr-FR 下 `"0,5"` 与 en-US 读取的组合互相错位），再经 clamp/归一化写回，亮度静默漂移（0.5 → 500% 量级错误）。
- 协议/持久化用途应固定 InvariantCulture（本轮 `eeb1c918` 的原则未覆盖到这个数字往返）。P3（需要用户跨区域格式迁移），但一旦发生即用户可感知的显示异常。
- 验证建议：单测先在 en-US 下 `SetDim(0.5)` 序列化 metadata，再切 `CurrentCulture = de-DE` 读取，断言解析值仍为 0.5（当前会得到 5）。

### A-6（P3 · 置信度：高，机制确定；影响面小）WebDavBackupTransport.ParseMultistatus：href 二次 Unescape + 集合自跳过比较用"解码路径 vs 未解码请求路径"

- 位置：`src/DeskBox/Services/WebDavBackupTransport.cs:233-262、285-286`。
- 证据一（二次解码）：257/261 行先 `Uri.UnescapeDataString(...)` 得 `decodedPath`，285 行又对 `decodedPath` 的末段再 `Uri.UnescapeDataString` 一次 —— 文件名含字面 `%XX`（如 `Report%2020%20final.zip`）时显示名与真实名不一致；含 `%2F` 时合成出带 `/` 的名字（下游 `IsSafeSnapshotBasename` 会拒之，安全面无损）。
- 证据二（跳过比较错位）：264 行用 `decodedPath` 对比 `requestedPath`（来自 `requestedUri.AbsolutePath`，**保留转义**）。远程目录名本身含空格/非 ASCII（`%20` 形式出现在 href 与 AbsolutePath 中）时，目录自身条目不会被跳过，混入列表 —— 但 retention/列表路径都以 `IsSafeSnapshotBasename` + `!IsCollection` 过滤，快照名固定格式且不含 `%`，故主要影响是非快照文件的显示名错误与多余条目。
- 综合定 P3。验证建议：用返回 `href=/dav/My%20Folder/` 的多状态响应单测 `ParseMultistatus`，断言集合自条目被跳过、名字只解码一次。

### A-7（P3 · 置信度：中）WebDavBackupTransport：共享 HttpClient 的 5 分钟 Timeout 覆盖整个请求（含上传/下载体），大快照 + 慢端点必然失败且每次都从头重传

- 位置：`src/DeskBox/Services/WebDavBackupTransport.cs:19-24、121-139`。
- 证据：`Timeout = TimeSpan.FromMinutes(5)` 作用于 `SendAsync` 全程（对 GET 下载是含 `CopyToAsync` 体拷贝的整个内容读取，PUT 同理含 `StreamContent` 全部发送）。附件域全开的多 GB 备份在 ≤2MB/s 的 NAS/远端上超时 → `EnsureSuccessAsync` 实为 `TaskCanceledException`（HttpClient 超时）→ 上传失败 → 下个 interval 全量重传，永不收敛。
- 代码注释已把 100s 调到 5min，说明作者知道该面，但对"附件 + 慢链路"仍可能不够；属容量假设问题而非逻辑错误，P3。
- 验证建议：读 `CloudBackupRunResult` 失败日志中的超时特征（`TaskCanceledException` 无用户取消），或用本地限速代理测大载荷。

### A-8（P3 · 置信度：中）TodoReminderService：`CheckNowAsync` 的 `_isChecking` check-then-act 跨线程窗口 + `CheckCoreAsync` 内层 try 之前的段落处于 async void 暴露面

- 位置：`src/DeskBox/Services/TodoReminderService.cs:161-181（检查与赋值）、176-197（内层 try 之前的读取段）、511-514（async void Timer_Tick）`。
- 证据一：`if (_disposed || _isChecking) return ...; return _activeCheck = CheckCoreAsync(now);` —— `_isChecking = true` 在 `CheckCoreAsync` 开头才执行，两条指令间可被调度切换。`RunDelayedInitialCheckAsync`（线程池续延）与 UI 定时器并发时窗口真实存在：两扫描并行，各自 TryAdd 抢到不同条目的键 → 同一到期批次拆成两条 toast；先完成者把 `_isChecking` 置 false 还会放行第三个扫描。无数据损害，P3。
- 证据二：`Timer_Tick` 是 async void；`CheckCoreAsync` 的异常兜底是**内层** try（198 行起），其之前的 `Settings` 读取、`NormalizeTodoReminderOffsetMinutes`、`settings.Widgets.Where(...)`、`settings.DeletedWidgetIds.Contains(...)`（176-195 行）若抛出将直达 async void → 进程崩溃。当前这些输入经字段默认值 `[]`、`WidgetLayoutStore.NormalizeStructure` 与 settings 归一化链路多层兜底，找不到现实触发器（低置信）；但该结构把"永不崩溃"的契约押在远处的不变量上，属于脆弱点而非现症。
- 验证建议：把 `_isChecking` 换为 `Interlocked.CompareExchange` 门闩；或至少将 176-195 行并入内层 try。并发拆 toast 可用两个 `CheckNowAsync` 并发 + 各持一个条目的 store 断言。

### A-9（P3 · 置信度：高，影响小）GlanceImageService：Content-Type 变化导致同 id 异扩展名孤儿文件；`GetThumbnailPath` 同步整文件读

- 位置：`src/DeskBox/Services/GlanceImageService.cs:539-563（DownloadAsync 覆盖同扩展名，不清理其它扩展名旧文件）、2181-2187（GetThumbnailPath 回退 ReadAllBytes 同步读整图）`。
- 证据：目标文件名为 `{Id}{ext}`，扩展名由响应 Content-Type 决定；服务器对同一 URL 改派 png/jpg 时，旧扩展名文件不再被任何 catalog 条目引用，`TrimAndSaveCatalogAsync` 只删 catalog 内路径，孤儿永久留在 `images/`（仅 `ClearCacheAsync` 能清）。另外 `GetThumbnailPath` 对无文件名场景同步 `File.ReadAllBytes`（在 `_gate` 内的路径上调用，大图会卡 gate）。
- P3 卫生项。验证建议：缓存统计页观察 images 目录数与 catalog 数差随时间扩大。

### A-10（P3 · 置信度：低概率、机制确定）全量恢复成功但 marker 删除失败时，下次启动误报"恢复失败"

- 位置：`src/DeskBox/Services/DeskBoxDataBackupService.cs:1285-1317（成功路径 TryDeleteFile/TryDeleteDirectory 全部吞异常）、1329-1334（catch → DeletePendingRestoreCore + 失败结果）`。
- 时序：`Directory.Move(stagedDataDirectory, DataDirectory)` 成功后，若 `TryDeleteFile(PendingRestoreMarkerPath)` 因 AV/句柄短暂锁定失败（吞掉），staging 已删；下次启动 marker 仍在、`FullRestoreConfirmed` 仍为 true → `ValidateRestoreData` 对不存在的 staging 抛 `FileNotFoundException` → catch 删除 marker 并返回失败结果（数据其实已恢复成功）。一次性误报，P3。
- 验证建议：注入 marker 只读属性重跑启动恢复流程。

### A-11（P3 · 置信度：高，卫生）`{DataDirectory}-pre-recovery-*` 暂存目录与 `PrunePreRestoreBackups` 之外的旁路目录无任何清理

- 位置：`src/DeskBox/Services/DeskBoxDataBackupService.cs:2486-2493（parked 目录创建后无人回收）`。仅在中断恢复自愈这一罕见路径产生，但产生后永存（restore-rollback 候选目录同理由 `TryDeleteDirectory` 尽力而为，失败即残留）。P3。

### A-12（P3 · 置信度：高，卫生）`_shownWithoutPersistKeys` 会话内无上限、无淘汰

- 位置：`src/DeskBox/Services/TodoReminderService.cs:40、640-643`。每条"已显示但持久化失败"的提醒键常驻至进程退出；只有持久化持续失败的病态环境才会累积（每条 ≈ 100 字节），实际无害。与 1a5514dc 的意图一致（跨 Reset 抑制重放），记录在案以便未来加"随成功持久化或条目变更而移除"的回收。P3。

### A-13（P3 · 置信度：高，外观一致性）提醒/剪贴板时间字符串仍走 ambient culture 数字形状，与同函数的 `GregorianCurrent` 回退不一致

- 位置：`src/DeskBox/Services/TodoReminderService.cs:737-739（"HH:mm"/"HH:mm:ss"）vs 752-753（"yyyy/M/d HH:mm" 用 DisplayCulture）`；`src/DeskBox/Services/TodoClipboardFormatter.cs:109-110` 同型。
- 纯时间模式不会触发非公历日历异常（无日期字段），但 ar-SA 等 digit-substitution 文化下"今天 14:30"渲染阿拉伯-印度数字而跨日回退渲染拉丁数字，同一条通知内两种数字形状并存。属 `aee38695` 清扫的余留显示面，非功能缺陷。P3。

---

## 最不确定待复核清单

1. **A-2 的现实触发率**：需要"上传收尾"与"设置保存"重叠。建议确认 `App.xaml.cs:3584/3609` 的 `RefreshOptions` 是否严格 UI 线程（若是，竞态只剩后台 `MarkSuccessAsync` 单侧，窗口仍真实存在但更窄）。
2. **A-8 的跨线程可达性**：`RunDelayedInitialCheckAsync` 的 `CheckNowAsync` 续延线程（`Task.Delay` 之后无 `ConfigureAwait(false)`，在非 UI 上下文即线程池）与 30 秒 `DispatcherQueueTimer` 的 tick 是否可能同帧重叠 —— 静态推断可能，需要运行期日志证实一次双扫描。
3. **A-10 的 marker 删除失败率**：`TryDeleteFile` 吞掉的一切失败都会走到该路径；是否曾于真实日志出现 `[DataBackup] Applied pending restore` 之后下次启动又见 `Pending restore failed` 可作为佐证。
4. **A-7 的载荷规模**：默认 scope（todo+quick-capture 附件）是否可能生成 >5 分钟传输量的 zip，取决于用户附件使用面；可在诊断包统计字段里加压缩包尺寸分布验证假设。
5. **A-1 的样本面**：需要确认目标用户群（中文环境常见压缩工具：360 压缩、Bandizip、WinRAR 的 zip 写出）是否会写 `"./"` 条目 —— Info-ZIP/macOS `zip -r x.zip .` 会写；Bandizip/WinRAR 一般不写。影响该条从"一类合法归档全挂"到"罕见归档挂"的严重度判断（仍建议修，修复成本一行级）。

## 已核实稳健区域留痕（本轮逐点验证、未发现问题）

1. **`eeb1c918` Invariant 清扫（15+2 处）语义等价**：逐处比对插值孔 —— `Program.cs:552`（仅日期+字符串消息）、`AppUpdateService.cs:439`（日期+ProcessId，原先已显式 Invariant）、`AttachmentStorageService.cs:46`、`CloudBackupService.cs:649`、`DeskBoxDataBackupService.cs` 五处（均仅日期孔）、`DeskBoxDiagnosticsBundleService.cs:432`、`DeskBoxDragData.cs` 两处、`GlanceImageService.cs:422`、`QuickCaptureService.cs:2212`、`ResilientJsonStore.cs` 两处（日期+`Guid:N` 本就 culture 无关）。`CitySearchService.cs:205/226` 的 `{F2}` 键两侧同用 Invariant，去重语义自洽。未发现"非日期孔被连带改义"的情况。
2. **`aee38695` 公历显示补全（11 处）**：八个文件的 `ToString("g"/"d", DisplayCulture.GregorianCurrent)` 与 `LocalizationService.DefaultFormat` 改造均正确；`DisplayCulture`（Helpers/DisplayCulture.cs）按文化名缓存 Gregorian 克隆、`OptionalCalendars` 优先、失败回退原文化，线程安全（发布后只读）。
3. **`041a7274` 通知 Setting 探测入 try**：`NativeAppNotificationService.TryShow` 的 `Register()`（自身全程 try/catch，`NativeAppNotificationService.cs:106-150`）在 try 外、探测在 try 内；所有失败路径返回 false，"TryShow 永不抛"契约恢复；提醒路径的 `TaskCompletionSource` 不再有挂死面。
4. **`1a5514dc` 会话键生命周期**：abort 释放（`ReleaseCandidateKeys`）覆盖中途退出、候选为空、`_notify` 失败、内层异常四条路径；已持久化条目由磁盘 `ReminderDismissedForDueDate` 保护；`_shownWithoutPersistKeys` 在 `CollectWidgetCandidatesAsync:548` 挡住 Reset 后重放；`ResetSessionClaimedKeys` 只清 `_sessionNotifiedKeys` 语义正确。配套测试（`TodoReminderServiceTests.cs:280-373`）两个场景均锁定行为。残余问题仅 A-8/A-12 两点 P3。
5. **`c004c5c6` DST 接缝等价性**：`CreateMonthlyCandidate`/`AddLocalDays` 显式重载与原实现逐点等价 —— `anchorDueDate.LocalDateTime ≡ TimeZoneInfo.ConvertTime(v, Local).DateTime`（LocalDateTime 的 Kind=Local 只影响 Kind 标记，本实现后续用 6 参构造显式给 Kind）；`new DateTimeOffset(localCandidate /*Kind=Local*/) ≡ new DateTimeOffset(localCandidate /*Unspecified*/, Local.GetUtcOffset(localCandidate))`（前者内部即按本地时区 `GetUtcOffset` 解析，含歧义/不存在时间的解析规则相同）。生产路径全部经 `TimeZoneInfo.Local` 委托重载，测试专用重载不改变行为。
6. **恢复归档校验**（`ExtractAndValidateManifestAsync`，1784-2080）：重复 manifest 显式判别（DEF-104）、manifest 1MB 双重上限（声明长度+实际膨胀字节）、条目白名单（反斜杠拒绝、`data/` 前缀、widget-style 白名单）、规范化路径 containment、重复路径拒绝（含不同写法归一后同路径）、逐文件与总量预算、hash 完整性（含 widget-style 声明字段）、schema 上下界。路径穿越/zip 炸弹面已闭合。
7. **备份原子提交**（`CreateArchiveFromSnapshotAsync`）：临时文件 + `File.Move(overwrite:false)` 提交、SHA-256 清单、失败清理 temp；`CopyStableSnapshotFileAsync` 的长度+写时间双重稳定性重试。
8. **ResilientJsonStore 提交协议**：File.Replace 重试、ERROR_UNABLE_TO_REMOVE_REPLACED 的核实性 in-place 回退（先备份原字节并验证）、unreadable primary 的 aside 保全与 3 份裁剪、corrupt 隔离 2 份裁剪、transient 读失败与内容损坏的严格分流（不把锁冲突当损坏）。
9. **SettingsService 双文件对**：布局先行 + 失败回滚（`RevertLastCommit`）、`_widgetLayoutMerged` 门闩防空布局覆盖、新 schema 只读拒写（settings 与 widget-layout、sync state 一致策略）、保存失败必设 `LastPersistenceFailure` 且 `SaveAsync` 永不外抛。
10. **TodoWidgetStore / WeatherCacheStore / SyncOutboxStore / SyncStateStore**：按路径静态 gate；`MutateAsync` gate 内 load-mutate-save（DEF-043）；outbox "解析成功但身份不符"fail-closed 走隔离通道而非静默丢列；operation_id 文件名安全校验拒绝而非清洗。
11. **云备份恢复路径**：`DownloadSnapshotAsync` 的 basename 白名单（无分隔符/无 `..`）、`IsSafeSnapshotBasename` 在 retention/list/verify 三处一致；部分下载文件会因归档校验失败被拒绝（含在恢复确认之前）；`VerifyUploadAsync` 对最终一致 DAV 的降级语义（不把未收敛当失败）与 retention 尽力而为互不干扰。
12. **AppUpdateService / Updater**：下载 hash 校验 + temp 提交 + 失败分类清理；manifest 来源钉死（https + deskbox.fun 子域 + 本项目 GitHub release 路径，防被篡改 manifest 配对哈希）；`387c3180` 的 `WaitForParentExit` 前移覆盖 installer-missing 重启路径；`PrepareDetachedUpdaterHelper` 清理旧目录失败仅记日志、新目录带 PID 时间戳防自覆盖。
13. **原生互操作**：`native/Cargo.toml` dev/release 双 profile `panic = "abort"`，panic 无法跨 C ABI（以进程 abort 兜底）；导出函数逐指针校验 envelope 后再解引用。C# 侧 `SetLastError(0)` → 调用 → `Marshal.GetLastWin32Error()` 的时序抽查（ExplorerShellLaunchService、WidgetLayerService、FileTransfer 系列）无错位。
14. **FolderWatcherService**：DEF-123/125 的代数守卫覆盖全部 await 窗口（入口、probe、query start、settle），重连路径与失败分支一致。
15. **App.Log**：有界队列 + 单写者 + 截断保护；`SafeFireAndForget` 统一兜底 async void。

## 统计

- P0：0；P1：0；P2：2（A-1、A-2）；P3：11（A-3 ~ A-13）。
- 高置信 6 条（A-1、A-3、A-4、A-6、A-9、A-11、A-12、A-13 中机制部分），中置信 4 条，低置信/待环境证实 3 条（见"最不确定待复核清单"）。
