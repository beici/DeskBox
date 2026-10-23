# 第 14 轮（从零）审查 · 维度 B（UI/架构/本地化）

- 审查对象：`wip/fix-bug` @ `fa6321b0`（工作树干净）
- 审查方式：纯静态。只读代码与架构文档，未构建、未运行测试、未启动应用、未修改任何源码（本报告文件除外）。
- 输入纪律：未读取 `docs/audit/` 下任何历史审查文件；所有结论均由当前代码直接推出。
- 重点攻击面：最近 6 个提交（公历显示补全 11 位点、协议/文件名 InvariantCulture 清扫、Todo 会话键生命周期、DST 接缝、通知 Setting 探测位置），随后全量扫描 UI/ViewModel/Controls/Helpers/Strings/架构一致性。

## 概览

- 重点攻击的新改动整体质量较高：11 个新 DisplayCulture 位点的"显示用途"定性全部成立（详见 B-9 稳健区）；会话键生命周期修复的各中止路径经逐行核对无漏洞；DST 接缝与旧实现严格等价；通知 Setting 探测已回到 try 块内。
- 但"补全剩余显示位点"本身漏了同函数内的一处（**B-1**，本轮唯一 P1）。
- 深挖 `TodoWidgetViewModel.ApplyExternalStoreChange` 合并路径发现 VM 阴影字段与模型字段脱节，打开的 Todo 小部件对提醒侧 snooze 消费/设置完全不刷新显示（**B-2**）。
- 本地化程序化对比：12 文件 3091 键完全一致，唯一差异是 3 个语言本地化了 `FileInfo.FileModified` 的日期顺序 token（B-7，需人工定性是否违反"占位符一致"口径）。
- DisplayCulture 缓存键以 `Name` 混淆带/不带用户覆盖的实例来源（B-4），以及日期显示文化的"应用语言 vs OS 格式"双轨（B-5），均为低危语义含混。

| 编号 | 级别 | 置信度 | 一句话 |
|---|---|---|---|
| B-1 | P1 | 高 | 本地备份快照清单标题仍是环境文化的 `:g`——本轮修复同类位点漏掉的第 12 处 |
| B-2 | P2 | 高 | ApplyExternalStoreChange 只写模型字段，VM 阴影字段与 `DueStatusText` 绑定不刷新 |
| B-3 | P3 | 中高 | 云备份页语言切换不重建快照列表、不刷新连接状态行 |
| B-4 | P3 | 中 | DisplayCulture 缓存键混淆 `GetCultureInfo` 与 `CurrentCulture`（用户覆盖）两种来源 |
| B-5 | P3 | 中 | 日期显示文化双轨：Glance 用应用语言文化，其余位点用 OS 格式文化 |
| B-6 | P3 | 低-中 | 提醒 store-change 中继忽略 widgetId，跨部件 item id 冲突可达 |
| B-7 | P3 | 高（事实） | `FileInfo.FileModified` 在 fr-FR/pt-BR/ru-RU 本地化了日期格式 token |
| B-8 | P3 | 高（事实） | DefaultFormat 用 zh-CN 文本配 OS 格式文化求值（兜底路径） |

---

## 问题清单

### B-1 【P1｜置信度：高】本地备份快照清单标题仍是环境文化的 `:g`，本轮"公历补全"漏掉的同文件同函数位点

**位置**：`src/DeskBox/Views/SettingsWindow.Maintenance.cs:426`

```csharp
string title = $"{kind} · {snapshot.CreatedAtUtc.ToLocalTime():g}";
```

**证据链**：

1. 提交 `aee38695`（fix(localization): finish the Gregorian rendering for the remaining display sites）修了本文件的两处（现第 248 行恢复确认体、第 438 行清单摘要），但同一函数 `RefreshBackupSnapshotInventoryAsync` 里第 426 行的**行标题**（`BackupSnapshotListItem.Title`，直接 `ItemsSource` 进 `BackupSnapshotsList`，用户可见）漏掉了。
2. `snapshot.CreatedAtUtc` 来自 `DeskBoxDataBackupService.GetSnapshotInventoryAsync`（`DeskBoxDataBackupService.cs:2663`）：`summary.CreatedAtUtc ?? file.LastWriteTimeUtc` —— 是**不可信文件系统时间戳**兜底的值，与提交信息里点名的 FileMetaService.DateDisplay 同一输入性质。
3. 后果 A（显示错误）：Windows 格式文化为 ar-SA（默认 Um Al-Qura 历）的机器上，标题里的年份是伊斯兰历年份，与设置页其余所有日期（本轮已修）口径不一致。
4. 后果 B（功能退化）：`LastWriteTimeUtc` 落在 Um Al-Qura 支持范围（1900-04-30..2077-11-16）之外（如被清零为 1601、或系统时钟设到 2099 的机器产的 zip 带回）时，`{:g}` 抛 `ArgumentOutOfRangeException`；该异常发生在 `.Select(...).ToArray()` 内（第 429 行），被第 440 行的 catch 吞掉后，**整个本地快照清单显示为"读取失败"**（`Settings.DataBackup.FailedBody`），而不是只跳过坏行。

**触发路径**：设置 → 数据维护（本地备份）→ 点击刷新快照清单，格式文化为非公历默认（ar-SA 等），或任一快照 zip 的 mtime 超出 Um Al-Qura 历范围。

**如何验证**（不执行，仅建议）：把 `CultureInfo.CurrentCulture` 设为 `GetCultureInfo("ar-SA")` 后调用 `RefreshBackupSnapshotInventoryAsync`，断言标题含 `DateTimeOffset.UtcNow.Year` 的公历值；再构造 mtime 为 1601-01-01 的 zip 断言清单仍能列出其余条目。

---

### B-2 【P2｜置信度：高】ApplyExternalStoreChange 合并只写 TodoItem 模型字段，VM 阴影字段与绑定不刷新——打开的 Todo 小部件显示陈旧 snooze 状态

**位置**：`src/DeskBox/ViewModels/TodoWidgetViewModel.cs:765-833`（合并）、`src/DeskBox/ViewModels/TodoItemViewModel.cs:279-291/421/429-472`（阴影字段与派生属性）、`src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml:400/409`（绑定）、`src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs:156-167`（入口）。

**机制**：`TodoItemViewModel` 的显示属性读**VM 阴影字段**（`_snoozedUntil`、`_completedAt` 等），不是 `Item` 模型。例如：

```csharp
// TodoItemViewModel.cs:279-291 —— VM 属性 setter 会写回模型并通知：
public DateTimeOffset? SnoozedUntil
{
    get => _snoozedUntil;
    internal set { if (SetProperty(ref _snoozedUntil, value)) { _item.SnoozedUntil = value;
        OnPropertyChanged(nameof(HasActiveSnooze)); OnPropertyChanged(nameof(DueStatusText)); } }
}
// HasActiveSnooze => SnoozedUntil is not null && ...（:421）
// DueStatusText 的"已暂停至 …"后缀由 HasActiveSnooze 门控（:463-466）
```

而合并路径写的是**模型字段**（`TodoWidgetViewModel.cs:785-789`）：

```csharp
match.Item.SnoozedUntil = changedItem.SnoozedUntil;   // 模型被更新
// 但 VM 的 _snoozedUntil 阴影未动，也没有任何 OnPropertyChanged
```

完成分支（:791-800）走了 `match.IsCompleted = true`（VM setter，通知齐全），唯独 snooze 族字段没有任何等价通路。`listChanged == false` 时唯一的"刷新"是 `OnPropertyChanged(nameof(IsInitialized))`（:831）——**全仓库 XAML 没有任何 `IsInitialized` 绑定**（Controls/Views 全部 .xaml grep 为空），该通知刷新零个绑定，是无效的安慰性通知。也没有周期性定时器兜底刷新 DueStatusText（grep 无 overdue/minute timer）。

**用户可见后果**（两方向都会错）：

1. 提醒服务消费 snooze（`TodoReminderService.PersistDisplayedRemindersAsync`：`item.SnoozedUntil = null; item.SnoozeLastNotifiedAt = now` → `PublishStoreChanged` → App 中继 → 本合并）：打开的小部件里该项**永远显示"已暂停至 …"**，直到用户自己再编辑该项。
2. 用户在 toast 上点"稍后提醒"（`SnoozeUntilAsync` 写入 `SnoozedUntil = X` 并发布）：打开的小部件里**不出现** snooze 后缀。

次级影响：完成于通知按钮时 `match.Item.CompletedAt = ...`（模型）写入了但 `_completedAt` 阴影陈旧，`TodoWidgetViewModel.FilteringAndAppearance.cs:217` 的已完成排序 `item.CompletedAt ?? item.UpdatedAt` 用到陈旧值（排序落点不准，低危）。

**边界确认（为何不是数据损坏）**：持久化从 `Item` 模型序列化（`CloneTodoItem(item.Item)`、TodoWidgetStore.SaveAsync 路径），模型字段被正确更新，下次用户保存不会回滚提醒簿记——DEF-043 的持久化目标达成，缺陷纯在显示层。

**如何验证**：打开 Todo 小部件 + 触发一条 snooze 提醒的 toast 并等待其消费持久化（或直接单测：构造 VM 后调 `ApplyExternalStoreChange(changedItem(SnoozedUntil:null))`，断言 `DueStatusText`/`HasActiveSnooze` 变化；当前实现下它们不变）。

---

### B-3 【P3｜置信度：中高】云备份设置页语言切换后：远端快照列表与连接状态行保持旧语言

**位置**：`src/DeskBox/Features/Backup/BackupSettingsViewModel.SettingsSurface.cs:608-633`（RefreshLocalization）、:581-601（SyncRemoteSnapshotItems）、:555-579 + :38 + :178-182（ConnectionStatusText）。

`RefreshLocalization()` 重投影了选项表、StatusText、警告行等一大串属性，但：

1. **不重跑 `SyncRemoteSnapshotItems()`**——列表项 `details` 是 `Format("Settings.CloudBackup.SnapshotDetails", device, size)` 的本地化产物（:597），语言切换后保持旧语言，直到下一次快照刷新（网络往返或页面重进）才自愈。
2. **不重发 `ConnectionStatusText`**——该属性 getter 读缓存字段 `_connectionStatusText`（:38、:178-182），字段内容来自 `ApplyMessageToConnectionStatus()` 的本地化 `Format/Localize`（:555-579），同样陈旧。

顺带核对：`RefreshLocalization` 修过的 `StatusText` 里的日期用 `GregorianCurrent`（OS 格式文化），语言切换不影响其日期部分，只需重发文本部分——已覆盖；缺失的正是上述两项。属低危陈旧显示，自愈路径存在。

**如何验证**：设置 → 云备份 → 完成一次测试连接（状态行出现文案）→ 切换应用语言 → 状态行与快照列表条目语言不变，其余区域已切换。

---

### B-4 【P3｜置信度：中】DisplayCulture 缓存键只取 culture.Name，混淆"带用户覆盖"与"不带用户覆盖"的实例来源

**位置**：`src/DeskBox/Helpers/DisplayCulture.cs:18/33`。

```csharp
private static readonly ConcurrentDictionary<string, CultureInfo> s_gregorianByCultureName = new(StringComparer.OrdinalIgnoreCase);
...
return s_gregorianByCultureName.GetOrAdd(culture.Name, _ => CreateGregorianClone(culture));
```

同名 `CultureInfo` 实例可以有不同来源与内容：`CultureInfo.CurrentCulture`（带 Windows"区域格式"用户覆盖：自定义日期分隔符、模式等）vs `CultureInfo.GetCultureInfo(name)`（只读、无用户覆盖）。本项目两条来源**都在调用** `EnsureGregorian`：

- `DisplayCulture.GregorianCurrent`（CurrentCulture）——全部 26 个显示位点；
- `GlanceWidgetViewModel.GetCulture()`（`GetCultureInfo(CurrentCultureName)`，:941-951）→ `FormatDateText`/`WeekdayText`。

缓存按 Name 合并后，**首个到达者的克隆被所有同名实例共享**：若 glance 的 `GetCultureInfo("ar-SA")` 先播种，之后所有 `GregorianCurrent` 消费者拿到的是无用户覆盖的克隆（用户在 Windows 里自定义的格式静默丢失）；反之 glance 会意外收到带覆盖的克隆。结果非确定（取决于调用顺序）。影响限于格式细节（分隔符、模式微调），不产生错误日历，故 P3。另注意 `CreateGregorianClone` 的 catch 兜底会在克隆失败时返回原文化（非公历），公历保证静默失效——实践中 ar-SA 等文化的 OptionalCalendars 均含 Gregorian 变体，可视为已防护（见稳健区）。

**如何验证**：在 Windows"区域设置"里给 ar-SA 自定义短日期分隔符，先触发 Glance 渲染再触发云备份状态行（与相反顺序对照），观察两处日期分隔符是否随调用顺序漂移。

---

### B-5 【P3｜置信度：中（定性分歧）】日期显示文化双轨：Glance 用"应用语言文化"，其余位点用"OS 格式文化"

**位置**：`src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:941-951`（GetCulture → `GetCultureInfo(_localizationService.CurrentCultureName)`）+ :764-771；对比本轮 11 位点与其余 15 位点统一用 `DisplayCulture.GregorianCurrent`（CurrentCulture 派生）。

当应用语言显式设为 A（如"简体中文"）而 Windows 格式文化为 B（如 de-DE / ar-SA）时：Glance 的主日期/日期文本按 A 文化渲染，设置页状态行、搜索结果 DateDisplay、反馈卡片等按 B（公历变体）渲染。同一应用两套日期格式并存。这是两条历史路径（Glance 自有 GetCulture 管线 vs 本轮统一的 GregorianCurrent 管线）叠加的结果，`aee38695` 只把 Glance 的 `WeekdayText` 换成 `EnsureGregorian(culture)`（保持其文化来源），未统一口径。属设计定性问题而非错误输出；提请维护者决策是否统一（统一到哪种口径都有代价：Glance 需跟随应用语言，其余跟随系统格式是 Windows 惯例）。

---

### B-6 【P3｜置信度：低-中（结构性）】提醒 store-change 中继忽略 widgetId，跨部件 item id 冲突可达且无防线

**位置**：`src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs:156-167`；`src/DeskBox/App.xaml.cs:2432-2438`（中继无过滤）；`src/DeskBox/ViewModels/TodoWidgetViewModel.cs`（整个 VM 不持有 WidgetId——grep 无 `WidgetId`/`widgetId` 成员）。

`OnTodoStoreChangedByReminder(string widgetId, ...)` 收到参数后**完全不用**，直接 `ViewModel.ApplyExternalStoreChange(...)`；合并按 `item.Item.Id` 全局匹配。item id 是 GUID，正常运行唯一；但 `DeskBoxDataBackupService` 的 scoped restore 合并（:1110-1148）**只对单个目标部件的 seenIds 去重，不跨目标去重**——把同一备份先后 scoped-restore 进两个不同的 Todo 部件，两个部件会持有相同 item id。此后 A 部件该项的提醒写（消费/snooze）会经中继污染 B 部件同 id 项的模型字段，并随 B 的下一次用户保存**落盘进 B 的 store**。概率低（需要双重 scoped restore + 提醒恰好命中），但当前架构（VM 无 widgetId）连过滤的可能性都没有。修法方向：让 `TodoWidgetViewModel` 持有并暴露自己的 widgetId，中继处过滤。

---

### B-7 【P3｜置信度：高（事实成立，需定性）】`FileInfo.FileModified` 在 fr-FR / pt-BR / ru-RU 本地化了日期格式 token，偏离"占位符一致"口径

**程序化对比结果**（详见下节）：12 文件唯一的内容差异——

| 文件 | 值 |
|---|---|
| 其余 9 个 | `'{0} · {1:yyyy/M/d HH:mm}'` |
| fr-FR / pt-BR | `'{0} · {1:dd/MM/yyyy HH:mm}'` |
| ru-RU | `'{0} · {1:dd.MM.yyyy HH:mm}'` |

AGENTS.md 口径是"identical resource-key and formatting-placeholder coverage"。键集合与占位符序号一致，但格式说明文本被本地化。这可能是**有意的区域化**（日期顺序本地习惯），且技术上无害：`{1:dd/MM/yyyy}` 等自定义 pattern 经 `LocalizationService.Format` 的 `GregorianCurrent` provider 求值，日历安全（公历变体），也不与公历修复冲突。但若按严格口径，它是一次未被记录的口径例外；若是政策，建议在 AGENTS.md 或键注释里写明"允许本地化日期顺序 token"。请维护者定性。

---

### B-8 【P3｜置信度：高（事实成立，影响面小）】DefaultFormat：zh-CN 文本 + OS 格式文化求值的静态兜底

**位置**：`src/DeskBox/Services/LocalizationService.cs:197-205`。

`DefaultText` 返回 zh-CN 文本（设计如此），而 `aee38695` 把 `DefaultFormat` 的 provider 从 CurrentCulture 改为 `GregorianCurrent`（仍是 CurrentCulture 派生）。兜底路径（`WidgetItem`/`OrganizationHistoryEntry`/`TodoItemViewModel`/`DragDropPermissionService` 在拿不到 LocalizationService 实例时）若带日期参数，会出现"中文句子 + 阿拉伯格式日期"的混合渲染。仅静态兜底可达、正常运行不触发；日期安全（公历）保住了，只是语言混合。记录在案，不建议为此单独改动。

---

## 本地化专项结果（程序化）

方法：Python 脚本逐文件解析 12 个 JSON，比较键集合、占位符 token（`{N}` / `{N:fmt}`）、花括号配平；en-US 为基准。

1. **键覆盖**：12 文件全部 3091 键，无缺失、无多余。✔
2. **花括号配平**：无失衡项。✔
3. **标准日期 token（`{N:g/d/D/t/T/f/F/M/O}`）**：无任何键使用。日期一律以显式 pattern 或预格式化字符串传入。✔（这意味着 `Format` 的 provider 切换不会在语言文件层引入文化依赖的标准模式语义。）
4. **占位符 token 差异**：仅 B-7 所列 `FileInfo.FileModified` 一键三文件（fr-FR/pt-BR/ru-RU 的日期顺序本地化）。
5. 信息项：与 en-US 值完全相同的键数量——de-DE 121、fr-FR 98、pt-BR 80、es-ES 56、ja-JP 42、bn-BD 35、zh-CN 31、zh-TW 31、ar-SA 33、ru-RU 30、hi-IN 28。多为符号/品牌/单位类字符串，未逐条人工审译（不在本轮静态一致性范围内）。
6. 带日期 token 键的求值文化：`Format` → `GregorianCurrent`（公历保证）；`DefaultFormat` 同（B-8）。

## 最不确定、待复核清单

1. **B-4 的实际可见度**：需要一台 Windows 格式文化为 ar-SA 且自定义过区域格式的机器，按两种调用顺序观察格式漂移；纯代码推演成立，实测幅度未知。
2. **B-5 的口径**：Glance 跟应用语言 vs 其余跟系统格式——是否为既定产品决策，需维护者确认；若非决策，应统一。
3. **B-6 的可达性**：双重 scoped restore 复制 item id 的场景是否被上游计划（如"恢复到指定部件"重复操作）真实暴露，需结合产品使用面评估。
4. **B-7 的政策性**：三处日期 token 本地化是否是有意为之（对比 1.4.x 翻译史）；决定"修复一致"还是"豁免并记录"。
5. **`_shownWithoutPersistKeys` 的会话不可逆性**：持久化失败后该键在本会话内永不重试（有意，注释明确）；若 IO 长期故障后恢复，用户本会话失去该提醒——这是提交 `1a5514dc` 明示接受的权衡，非缺陷，但值得产品面知悉。

## 已核实稳健区域留痕

1. **`aee38695` 的 11 个新位点定性逐个核对**：云备份状态行/快照标题（`"g"`）、两处恢复确认（`"g"`）、快照摘要（`"g"`）、反馈卡片（`"d"`）、运行诊断生命周期戳（`"g"`）、FileMetaService（`"M"/"d"`，调用处有 try/catch 且 UmAlQura 越界由公历变体消除）、LocalizationService.DefaultFormat、Glance `WeekdayText`（`"dddd"` 无日历算术，EnsureGregorian 纯防御）——显示用途均成立，无协议/文件名位点混入。
2. **`eeb1c918` InvariantCulture 清扫**：17 处全部为协议标识符（Wikimedia 分类名、远端快照名——`ParseSnapshotTimestamp` 回读方为 invariant，闭环成立）或用户可见文件名（备份/拖放/附件/恢复侧车/诊断/更新器目录与日志）；`CitySearchService` 的 `{F2}` 去重键亦已固定。Updater 工程无残留文化敏感格式（grep 全清）。
3. **`1a5514dc` 会话键生命周期**：逐中止路径核对——widget 循环中途 `_disposed/!ShouldBeRunning` 释放已认领键；`_notify` 失败/异常释放；外层 catch 兜底释放且不会误放已展示键（`PersistDisplayedRemindersAsync` 全部循环体在 per-group try 内，实际不可抛出到外层）；`_shownWithoutPersistKeys` 在 Start/Refresh 重置后继续拦截、重启后由 1 分钟宽限窗限界。`_isChecking` 门防单飞重入。修复自洽。
4. **`c004c5c6` DST 接缝等价性**：旧 `value.LocalDateTime` / `new DateTimeOffset(kind=Local)` 与新 `TimeZoneInfo.ConvertTime(value, tz)` / `new DateTimeOffset(unspecified, tz.GetUtcOffset(...))` 在 `tz == Local` 时逐表达式等价（含歧义/无效时刻的解析都走同一 `GetUtcOffset` 语义），纯测试缝、无行为变化。
5. **`041a7274` 通知探测位置**：`NativeAppNotificationService.TryShow` 现在唯一可能抛异常的平台调用全部在 try 内；入口 `Register()` 自身整体 try/catch、`IsSupported` 探测在 try 内；never-throws 契约恢复。提醒路径的 TCS（`App.xaml.cs:2884-2905`）在 `TryEnqueue` 失败时同步返回 false，UI 线程存活期间必达。
6. **DisplayCulture 克隆安全网**：`CreateGregorianClone` 失败时回退原文化（渲染永不断）；ar-SA 等目标文化的 OptionalCalendars 含 Gregorian 变体，实践路径不会走到回退。
7. **架构一致性**：`WidgetKind` 枚举（9 值）与 current_architecture.md 的 7 个可创建种类 + Tags/SystemMonitor 占位 + Productivity 仅存在于迁移清理（`SettingsService.cs:2745-2753`）完全一致；`QuickCaptureWidgetWindow`/旧 `WidgetWindow` 宿主确已删除（Views 目录无此类文件）；z-order 文档（[重要勿删]）声称的单宿主（ContentWidgetWindow）与代码相符，`WidgetShellContentHost` 的每条切换/保留路径都调用 `DisposeContentOnce`。
8. **UI 事件配对抽查**：`TodoWidgetContent`（Loaded/Unloaded 对称增删 TodoStoreChangedByReminder/LanguageChanged/AppearanceChanged）、`ContentWidgetWindow`（构造订阅、Closed 内成对退订 + 内容释放）、`DesktopOrganizationTaskView.Appearance`（配对齐全）、`WeatherWidgetContent`、`GlanceWidgetSettingsSection`——未发现失配对/静态根泄漏。
9. **备份 VM 线程面**：`ListCoreAsync` 等 await 从 UI 线程发起、无 ConfigureAwait(false)，`RemoteSnapshots` 赋值/`SyncRemoteSnapshotItems` 均回 UI 线程；`OnUploadCompleted` 经 `_tryEnqueue` 显式编组。
10. **转换器**：`SettingsBindingConverters` 两个转换器 null 安全、解析失败有默认值、颜色 hex 走 InvariantCulture。
11. **本地化加载**：12 表懒加载双检锁模式正确（null 快路径 + lock 内 `??=`），`T()` 回退链 table→en-US→zh-CN→key 完整。
