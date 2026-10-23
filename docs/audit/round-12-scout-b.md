# 第 12 轮（从零）审查 · 维度 B（UI/架构/本地化）

- 分支：`wip/fix-bug`（HEAD `9cfd9611`），工作树干净。
- 方法：纯静态。先 `git log --oneline -15` 锁定最近两个修复提交（`a251103d` Glance 公历渲染、`fe3103d5` Todo 提醒两阶段重构）做定向攻击；再对 ViewModels（~25.7k 行）/Controls（~52.7k 行）/Helpers（~18.5k 行）按缺陷模式 grep + 精读；本地化用脚本对 12 个 JSON 做程序化键/占位符全量 diff；XAML 资源键做定义-引用交叉检查；对照 `docs/architecture/current_architecture.md` 与 `[重要勿删]widget_zorder_lifecycle.md` 抽查不变量。未读 `docs/audit/` 下任何历史文件。
- 命中统计：P2 × 1（同一日历缺陷家族，覆盖 ≥10 个 UI 位点），P3 × 5；本地化 12 文件键集合与占位符完全一致（零缺陷）；XAML 资源引用零缺失。整体印象：经过 11 轮修复后 UI 层线程/生命周期纪律非常好，本轮主要残余风险集中在"区域性日历"这一刚被部分修复的缺陷家族上。

---

## 问题清单

### B-1 【P2 · 置信度：高】"公历渲染"修复只覆盖了 Glance 主日期，同一日历缺陷家族在 Todo/随记/设置/文件提示等 ≥10 个位点仍然存在

**人话摘要**：`a251103d` 修好了 Glance 大字日期在 ar-SA（默认 Um Al-Qura 回历）下显示回历的问题，并宣称"应用内其他所有日期本来就是公历"。这个断言不成立：一大批 UI 位点直接 `ToString("yyyy/M/d …")` 且不传 culture，`DateTime.ToString` 会用 `CultureInfo.CurrentCulture` 的**默认日历**分解日期——ar-SA 系统区域下 yyyy/M/d 输出的是回历年月日（如 1448/5/12）。受影响的是用户每天看的核心文本：**Todo 每一行的截止日期**、创建时间、提醒通知正文、随记详情时间戳、设置页更新时间、文件格子的"修改时间"提示。

**代码证据**（全部为不带 culture 参数的显式 y/M/d 格式）：

| 位点 | 摘录 |
|---|---|
| `src/DeskBox/ViewModels/TodoItemViewModel.cs:674-675` | `FormatDueDateTime`：`dueDate.ToString("yyyy/M/d HH:mm")`（Todo 行内截止日期主文本） |
| `src/DeskBox/ViewModels/TodoItemViewModel.cs:700-701` | `FormatSnoozeDateTime`：`snoozedUntil.ToString("yyyy/M/d HH:mm")` |
| `src/DeskBox/ViewModels/TodoItemViewModel.cs:113` | `CreatedText => Format("Todo.Detail.Created", CreatedAt.ToLocalTime().ToString("yyyy/M/d HH:mm"))` |
| `src/DeskBox/Services/TodoReminderService.cs:699-700` | 提醒 Toast 兜底日期：`localDueDate.ToString("yyyy/M/d HH:mm")` |
| `src/DeskBox/Services/TodoClipboardFormatter.cs:122-123` | 复制到剪贴板的截止日期：`localValue.ToString("yyyy/M/d HH:mm")` |
| `src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs:1265` | 新建详情时间戳：`DateTimeOffset.Now.ToString("yyyy/M/d HH:mm")` |
| `src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs:1304` | 打开详情：`item.ToModel().CreatedAt.ToLocalTime().ToString("yyyy/M/d HH:mm")` |
| `src/DeskBox/ViewModels/SettingsViewModel.AboutAndUpdates.cs:571` | `local.ToString("MM-dd HH:mm")`（同年分支，MM/dd 同样走默认日历） |
| `src/DeskBox/ViewModels/SettingsViewModel.AboutAndUpdates.cs:576` | `local.ToString("yyyy-MM-dd HH:mm")` |
| `src/DeskBox/Services/LocalizationService.cs:191-194` + `src/DeskBox/Models/WidgetItem.cs:131` | `Format` 用 `string.Format(CultureInfo.CurrentCulture, T(key), args)`，而 12 个语言文件的 `FileInfo.FileModified` 都带 `{1:yyyy/M/d HH:mm}`（fr-FR/pt-BR/ru-RU 为本地序）——文件格子 SecondaryInfo 的"修改时间"在 ar-SA 系统区域下渲染回历 |

对照组：`GlanceWidgetViewModel.FormatDateText`（853-875 行）已先 `EnsureGregorianCalendar(culture)` 再格式化，证明团队已知该机理；但同一家族的其他位点没有跟进。

**触发路径**：Windows 区域格式设为 ar-SA（默认日历 Um Al-Qura）→ 打开任意 Todo 格子查看带未来/过去日期的任务行 → 截止日期显示回历年份（1448 等）；同理文件悬停提示、随记详情、更新历史页。注意与"应用语言"解耦：这些位点跟随**系统 CurrentCulture**，即使用户把应用语言切成英文，只要系统区域是 ar-SA 仍然出现；反之 Glance 修复跟随的是**应用语言** culture——两条策略本身也不一致（B-1 的次级问题）。

**附带风险（同根，罕见）**：`UmAlQuraCalendar` 仅支持 1900-04-30 ~ 2077-11-16。系统时钟 >2077（或文件时间戳 <1900）时，上述 `ToString` 会抛 `ArgumentOutOfRangeException`；其中 `TodoItemViewModel.DueStatusText`/`CreatedText`、`WidgetItem.SecondaryInfo` 是绑定 getter，异常会导致绑定失败甚至冒泡。

**建议方向**：把 `EnsureGregorianCalendar` 提升为共享 helper（如 `LocalizationService.FormatDateFixed`/`GregorianDisplayCulture(culture)`），所有显式 y/M/d 位点与 `LocalizationService.Format`（改为用"应用语言 culture 的公历变体"格式化）统一走它；顺带统一"显示跟随应用语言还是系统区域"的策略口径。回归面就是上表 + `{1:yyyy/M/d HH:mm}` 格式 token 的求值。

**如何验证**（不执行，建议）：在系统区域 ar-SA 下运行，逐条比对 Todo 行日期、提醒 Toast、文件 tooltip、随记详情时间戳与 Glance 日期是否同为公历；或写单测把 `CurrentCulture` 切到 `ar-SA` 断言这些格式化输出含公历 2026/2027。

---

### B-2 【P3 · 置信度：中高】提醒两阶段重构后，Snooze 触发的重试没有任何宽限上限——通知面长期不可用时每 30 秒无限重试

**人话摘要**：`fe3103d5` 的设计是"显示失败 → 释放会话键 → 下次扫描重试，直到 missed-reminder 宽限窗关闭"。Due 触发确有 1 分钟宽限（`MissedReminderGrace`，`ShouldNotifyDue` 里 `now <= dueDate + MissedReminderGrace`）；但 **Snooze 触发**（`TryGetReminderTrigger` 743-747 行：`item.SnoozedUntil is { } snoozedUntil → return now >= snoozedUntil`）没有任何上界。当通知面长期不可用（原生 toast 不可用且托盘图标为 null——`ShowTodoReminderNotificationCore` 返回 false），每 30 秒扫描都会重新选择同一批 snooze 项 → `_notify` 失败 → 释放键 → 下一轮再来，**整个会话期无限循环**（每轮全量 LoadAsync 各 Todo store + 一次失败日志）。不是崩溃/丢数据类缺陷，但是与提交说明"bounded by the missed-reminder grace window"不符的死循环重试 + 日志噪音。

**代码证据**：`src/DeskBox/Services/TodoReminderService.cs:743-747`（Snooze 触发无上界）对照 `:782`（`return now <= dueDate + MissedReminderGrace;` 仅 Due 分支）；`CheckCoreAsync` 218-229 行失败释放键后按 30s `ScanInterval` 重来。

**触发路径**：建一条带提醒的任务 → 等提醒弹出后点"稍后提醒 10 分钟" → 在 snooze 到期前后让通知面不可用（如无托盘场景/平台 toast 失败）→ 观察 30 秒一次的重试日志持续到会话结束。

**建议方向**：给 Snooze 触发加对称的宽限（如 `snoozedUntil + MissedReminderGrace`），或对"连续 N 次显示失败"做退避。

---

### B-3 【P3 · 置信度：中】`Refresh()`/`Start()` 在检查进行中清空 `_sessionNotifiedKeys`：不经重启也可能重复弹同一条提醒

**人话摘要**：`PersistDisplayedRemindersAsync` 的 catch 注释说"通知已显示但标记持久化失败 → 会话键保持占用，本会话不再重复；重启后至多重放一次"。但 `Start()`/`Refresh()`（86-144 行）在计时器重启时会 `Clear()` 整个 `_sessionNotifiedKeys`，且该 Clear 发生在 UI 线程、与线程池上仍在飞的 `CheckCoreAsync` 并发（键表是 ConcurrentDictionary，Clear 本身安全）。序列：检查 A 已显示 Toast 但持久化抛异常（键按设计保留）→ 用户在设置里关掉再打开 Todo 提醒（或特性开关抖动）→ `Refresh()` 清键 → 下一轮扫描把同一条已提醒过的任务再弹一次。提交说明里"restart re-fires at most once"低估了实际触发面：**无重启的开关循环同样重放**。

**代码证据**：`src/DeskBox/Services/TodoReminderService.cs:100,125-133`（Clear 点）；`:586-594`（persist 失败保留键的注释与实现）；`:708-710`（选择时 TryAdd 占键）。

**触发路径**：见上。概率低（需要持久化失败 + 开关循环同窗），影响是重复通知，非数据损坏。

**建议方向**：`Refresh()` 重启计时器时不做全量 Clear，或 Clear 前检查 `_isChecking` 推迟到当前检查收尾；或持久化失败时把键迁移到"本会话已展示"的独立集合。

---

### B-4 【P3 · 置信度：中】Glance 在线刷新保留当前图片时只回写 `_currentIndex` 不更新 `CurrentImage`：元数据滞后一拍，扩展名变化时可能留下断图

**人话摘要**：`RefreshOnlineInBackgroundAsync.Apply`（645-659 行）里，若当前图片的 Id 在刷新后的列表中仍存在，只 `_currentIndex = existingIndex`，**不重新赋值 `CurrentImage`**。两个后果：(1) 刷新拉回的同 Id 条目若 Title/Author（photo info 行）变了，UI 要等下一次手动/轮播切换才可见——纯滞后；(2) 下载目标路径是 `{Id}{按 Content-Type 决定的扩展名}`（`GlanceImageService.cs:544-545`、`562`：`candidate.LocalPath = destination`）。同一 Id 的新图如果 Content-Type 从 jpg 变为 webp，目标文件名不同；旧扩展名文件随目录清理被删后，`CurrentImagePath` 仍指向旧实例的旧路径 → 当前显示的图断链，而 `HasCurrentImageContextAction`（用 `CurrentImage` 判断）与计数都说一切正常。

**代码证据**：`src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:648-659`（`if (existingIndex >= 0) { _currentIndex = existingIndex; }` 分支无 `CurrentImage = _images[existingIndex]`）；`src/DeskBox/Services/GlanceImageService.cs:544-562`（destination = Id+扩展名、覆盖移动、LocalPath 回写）。

**触发路径**：Bing 源同一 hsh/urlbase 图片跨日刷新且响应 Content-Type 变化（或目录 trim 删除旧扩展名文件），当前图片为该 Id 时。多数情况同 Id 同扩展名同路径（覆盖写），故影响面小。

**建议方向**：命中 existingIndex 时也执行 `CurrentImage = _images[existingIndex]`（同引用比较会触发一串 OnPropertyChanged，代价可接受）；或下载时固定扩展名策略。

---

### B-5 【P3 · 置信度：中（仅注释与实现不符，行为良性）】`WidgetShellContentHost.ApplyContentAppearance` 注释宣称"重入不得把较新外观标记为已应用"，实现防不住同步重入

**人话摘要**：`ApplyContentAppearance`（272-287 行）先 `content.ApplyAppearance()`，成功后才 `stamp.Version = version`。注释说"A failed or reentrant update must not mark a newer appearance as applied"。若 `ApplyAppearance()` 同步地再次进入 `ApplyContentAppearance`（同一 content、同一 version），嵌套调用看到的 stamp.Version 还是旧值 → 会再执行一次 `ApplyAppearance()`（外层尚未盖章）。后果只是重复应用（幂等），不产生错误状态；但注释承诺的保护与代码不符，未来若 ApplyAppearance 变贵或非幂等会踩坑。

**代码证据**：`src/DeskBox/Controls/WidgetShellContentHost.cs:272-287`。

**触发路径**：任何 `IWidgetContent.ApplyAppearance` 同步触发布局→`ApplyAppearance(true)` 的链路（当前未发现实际调用者形成此环）。

**建议方向**：进入时先占位（stamp.Version 在调用前写入，失败回滚），或注释如实降级为"失败不盖章"。

---

### B-6 【P3 · 置信度：中（边界场景）】Glance 旧版存储迁移：共享 legacy 文件被"最先加载的那个 widget"独吞，两个 Glance 格子并发迁移可复制数据

**人话摘要**：每个 widget 的 `GlanceWidgetStore` 都带同一条 `_legacyStorePath`（`glance/glance.json`，`ForWidget` 65-83 行）。`MigrateLegacyStoreIfNeededLockedAsync`（179-208 行）在 per-store 信号量内做"读到自己的 store 文件 + 删除 legacy"。门是**按 store 实例**的，不同 widget 的两个 store 之间无互斥：升级后存在两个 Glance 格子时，legacy 偏好被随机归属于先加载者（另一个拿到默认值）；并发场景两者都能读到 legacy（第二次读时文件可能已删，也可能未删）→ 迁移结果不确定。仅影响"旧单格子版本 → 新多格子版本"的升级首启。

**代码证据**：`src/DeskBox/Services/GlanceWidgetStore.cs:65-83`（所有 widget 共用 legacy 路径）、`:179-208`（迁移无跨 store 互斥；成功后删除 legacy）。

**触发路径**：老版本（单 glance.json）升级后已有 2+ Glance widget 的安装首启。

**建议方向**：迁移用跨实例的静态门（如按 legacy 路径的信号量）+ 迁移归属规则（如按 widget 创建时间或仅首个迁移并广播）。

---

## 本地化一致性专项结果（程序化逐文件 diff，非目测）

- **键集合**：12 个文件（en-US、zh-CN、zh-TW、ja-JP、de-DE、fr-FR、es-ES、pt-BR、ru-RU、ar-SA、hi-IN、bn-BD）**完全一致**，en-US 基准 3091 键，无 missing/extra。
- **占位符**：所有键的 `{N}`（数字占位符）多重集合逐键一致（含数量）。占位符**出现顺序**未强约束——位置语义由 `string.Format` 保证，属合法翻译重排，不算缺陷。
- **格式化 token**：仅两个键带格式 token：`FileInfo.FileModified` 的 `{1:yyyy/M/d HH:mm}`（fr-FR `{1:dd/MM/yyyy HH:mm}`、pt-BR 同、ru-RU `{1:dd.MM.yyyy HH:mm}`，其余 `yyyy/M/d`）与 `Search.Status.Results` 的 `{1:F0}`。逐文件 token 一致（各自本地日期序是有意为之）；但 `FileInfo.FileModified` 的 token 求值走 `CurrentCulture`（见 B-1 表末行），是 B-1 的载体之一，不是翻译缺陷。
- **空值/异常 token**：无空字符串值；除上述两个键外无非法花括号 token。
- **硬编码 UI 字符串**：抽到的面向用户位点基本都走 `T()/Format`；`GetHotkeyHint`（SearchPopupViewModel.cs:1440-1476）输出 "Ctrl/Alt/Shift/Space" 拉丁字面量——热键字形属于通用符号，可接受；`GetLanguageDisplayName` 内 "हिन्दी/Español/Français/العربية/বাংলা/Русский/简体中文/繁體中文" 是语言自称惯例，正确。
- **ar-SA RTL**：字符串层面未见方向性坑；时间/数字由 .NET 数字替换呈现（Glance 的 `date.Day.ToString(culture)` 等保留本地数字形状，符合 a251103d 意图）。RTL 版式问题静态审查不可见，留运行期验证。

## 架构一致性检查结果

- **WidgetKind 路径**：`WidgetRegistry.CreateDefault`（Services/WidgetRegistry.cs:57-71）与 `WidgetContentFactory.DescriptorList`（26-138 行）均无 `Productivity`；`Productivity` 仅存在于 `SettingsService.cs:2745-2755` 的清理迁移（读侧清理，非创建路径）——符合"不得回流活跃路径"的边界。`File` kind 无 IWidgetContentProvider（走 `CreateExistingContent` 包装既有视图），`Todo/Music/Weather/Glance/Search` 均有 provider，`Tags/SystemMonitor` 为 placeholder provider——与 `current_architecture.md` 第 127-267 行的 R8-AB 清单一致，无文档漂移。
- **z-order 文档不变量抽查**：① `NormalizeIdleWidgetZOrder`（WidgetManager.ZOrder.cs:806-884）维持 peer-only（无 `MoveToDesktopBottom`/`SetWindowToBottom` 调用，后者仅存在于 WidgetLayerService 的显式落底 API）；② 坑 #9 所述 `RequestRestoreRaisedWidgetsToDesktopLayer` 现状与文档一致（1130-1146 行：仅记 `held until=next-toggle` 日志即返回 true，`QueueRequestedLayerRestoreCheck` 1148-1158 行定义但无调用者——仍是已知死代码）；③ 交互泄漏看门狗（1171-1193 行）存在且按文档 10s；④ `BringGroupTemporarilyToFront`（WidgetLayerService.cs:808 起）保持同步三连，无中途 await。均为**符合**。
- **模块边界**：`tests/DeskBox.Tests/ModuleBoundaryContractTests.cs` 的"棘轮"契约（SettingsViewModel 不得直写 Todo/QuickCapture 字段等）与现行 SettingsViewModel 写法（经 coordinator）不冲突。

## 最不确定、建议主流程复核的条目

1. **B-1 的 UmAlQura 越界异常**（>2077/<1900 时 `ToString` 抛 `ArgumentOutOfRangeException`）：机理确定（.NET 日历支持范围），但"绑定 getter 抛异常在 WinUI 3 是否会升级为进程崩溃"取决于 `UnhandledException` 路由，静态不可判。建议只把它当作 B-1 的附注，不单独立案。
2. **`ShowTodoReminderNotificationAsync` 的 TCS 与关机交互**（App.xaml.cs:2884-2905 + ShutdownStep.Bounded("todo-reminders", 15s, abortFollowingStepsOnTimeout:true)）：正常关机流中 UI 线程异步 await 期间 dispatcher 继续泵送，入队委托能执行、TCS 能完成；仅当 DispatcherQueue 已进入 shutdown 而检查仍在飞时，`DisposeAsync` 会挂到 15s 上限并 **中止其后所有关机步骤**（含 settings-flush/tray/mutex）。分析倾向"几乎不可达"，但需要运行期在"提醒弹出的瞬间关机"复现确认。
3. **`EnsureGregorianCalendar` 的 `?? new GregorianCalendar()` 分支**：.NET 的 `DateTimeFormatInfo.Calendar` setter 按 `Calendar.ID` 匹配 OptionalCalendars（非实例引用），因此"手工 new 的 Gregorian 不在 OptionalCalendars 里会被拒"只在文化**完全没有**公历可选日历时发生（现网文化无此例），整个 try/catch 兜底也齐。我判定稳健，但因依赖运行时校验语义，建议用一条 ar-SA 单测钉死（`FormatDateText(new DateTime(2026,10,24), ar-SA, true)` 含 "2026"）。
4. **B-4 的扩展名漂移触发**：依赖 Bing/在线源对同一 Id 返回不同 Content-Type 且旧文件被清理——目录 trim 策略未逐行核对（`TrimAndSaveCatalogAsync`），置信度中，建议以日志复现后再定级。

## 核实过认为稳健的重点区域（留痕，防下轮重复劳动）

1. **提醒两阶段重构核心**（fe3103d5）：选择阶段在 store 门内只读；`PersistDisplayedRemindersAsync` 的过期守卫（Due 分支比对 `DueDateAtSelection`、Snooze 分支比对 `SnoozedUntilAtSelection`、完成/删除跳过）经逐路径推演成立——Toast 上"完成/再稍后"与后台 persist 的竞争均以用户写为准；`_isChecking` 重入门在首个 await 前置位，单线程无隙；两个 AOT smoke 的 `_notify` 委托均已改 `Task<bool>` 且返回值语义正确（`Task.FromResult(true)`）。
2. **`EnsureGregorianCalendar` 本体**（GlanceWidgetViewModel.cs:884-905）：克隆-换历-兜底 catch 结构正确；`FormatDateText` 的星期 token 剥离正则 `(?<!d)d{3,4}(?!d)` 对已知文化的 LongDatePattern 安全（未发现带引号字面量 "ddd" 的文化；此点静态不可全证，列入上面第 3 条一并钉测）；`FormatCompactCalendarDateText` 用 `date.Day`（DateTime 结构属性恒公历）无此问题；`WeekdayText` 的 "dddd" 与日历无关（星期名不受默认日历影响）。
3. **TodoWidgetStore**：按路径共享信号量（DEF-043）+ `MutateAsync` 门内 load-modify-save + `Normalize` 的提醒字段一致性规则（DueDate 变更即清 dismissal/snooze）自洽；FMEM-01 门表修剪有保护区。
4. **TodoWidgetViewModel.ApplyExternalStoreChange**：changedItem 缺失时早退（不复活已删项、不插入 recurrence 后继）语义正确；VisibleItems 同步算法（SynchronizeVisibleItems）位置维护正确。
5. **本地化 12 文件键/占位符 parity**：程序化验证零差异（本轮已做，下轮不必重做，除非新增键）。
6. **XAML 资源键**：全仓 `{StaticResource|ThemeResource}` 引用与 `x:Key` 定义交叉检查后，18 个候选全部是 WinUI 通用运行时资源（DefaultButtonStyle 等），无缺失键。
7. **事件卫生**：静态事件（`WindowsCompatibilityService.TextScaleFactor/AdvancedEffectsEnabled`、`WidgetTextShadow.ShadowSettingChanged`、`App.MemoryCleanupEpochAdvanced`）订阅点均有配对退订（FileSurfaceContent.TextScaling / WeatherWidgetContent / MarkdownDocumentView / WidgetWindowBase.Collapse 等已逐一核对）；MaterialCapabilityAdvisor 的常驻订阅为注释明示的进程级设计。VM 的 SettingsChanged/LanguageChanged 订阅（Settings/QuickCapture/Glance/Widget/Weather/Music/Todo）全部配对。
8. **DispatcherTimer 生命周期**：全部创建点（Glance ×2、Music、Weather ×2、QuickCapture ×3、WidgetShell ×2、Stacks、TitleSwitcher、MarkdownDocumentView、FileItemMenuBuilder 局部 hover timer）都在 teardown 有 Stop + Tick -=。
9. **SearchPopupViewModel**：generation/CTS 纪律完整（搜索代际、推荐代际、防抖 1s、LoadMore 互斥 `Interlocked.Exchange`）、Dispose 完整退订与释放；`EnrichVisibleResultAsync` 读已释放 CTS 的 `.Token` 抛 ODE 已被 catch 覆盖。
10. **MusicWidgetViewModel**：隐藏期 `_hiddenMediaStateDirty` 在 `OnWindowRevealCompleted` 恰好消费一次（Interlocked.Exchange）；SMTC 回调线程经 HasThreadAccess 判定后 TryEnqueue 回 UI；世代号防过期回写。
11. **Weather**：`TryParseIsoDate`/`FindCurrentHourIndex`/`ResolveNowInLocation`（TimeZoneInfo 解析 + 失败回落本机时钟）自洽；`WeatherService` 9 处 ISO 解析/格式化均带 `InvariantCulture`（39826268/6e1c1145 无回归）；`ParseDateToDayLabel` 的 "ddd" 不涉日历年月；静态 HttpClient 不随实例 Dispose。
12. **QuickCapture**：`SaveRecentItemToRecordsAsync` 生成**新 Id**（不存在 Items/RecentItems 同 Id 交叉，`SyncVisibleItems` 的搜索态 concat 不会产生重复 Id 插入）；`RefreshVisibleItemsForViewSwitchAsync` 的 `_cachedData` 消费由 Changed 事件驱动刷新，链路闭合。
13. **模块边界契约测试**（ModuleBoundaryContractTests）：棘轮式清单与现行代码不冲突。
