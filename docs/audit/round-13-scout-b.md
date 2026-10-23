# 第 13 轮（从零）审查 · 维度 B（UI/架构/本地化）

- 分支：`wip/fix-bug` @ `9f261cbb`（工作树干净）
- 审查方式：纯静态。只读源码与 `docs/architecture/current_architecture.md`、`docs/architecture/[重要勿删]widget_zorder_lifecycle.md`；未读 `docs/audit/` 任何历史结论；未构建、未运行测试、未启动应用、未改任何源码（本文件除外）。
- 重点攻击对象：最近 5 个代码提交 `b96129f6`（DisplayCulture 公历渲染接入 15 位点 + LocalizationService.Format 换文化 + snooze 宽限生产代码）、`f5a01f91`（snooze 宽限测试）、`df24c5fe`（通知开关守卫）、`387c3180`（更新器等待顺序）、`a251103d`（glance 日期公历化）。

## 概览

扫描范围与方法：

1. 逐行精读上述 5 个提交的完整 diff（`git show`），随后精读受影响文件的当前全量状态：`Helpers/DisplayCulture.cs`、`Services/LocalizationService.cs`、`Services/TodoReminderService.cs`、`Services/NativeAppNotificationService.cs`、`Services/TodoClipboardFormatter.cs`、`Models/OrganizationHistoryEntry.cs`、`Models/WidgetItem.cs`、`ViewModels/{Glance,TodoItem,QuickCaptureItem,SettingsViewModel.AboutAndUpdates}.cs`、`Views/ContentWidgetWindow.xaml.cs`、`Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs`、`DeskBox.Updater/Program.cs`、`tests/.../DisplayCultureTests.cs`。
2. 全仓 grep 攻击 DisplayCulture 漏接位点：`ToString("yyyy|M/d|MM-dd|d|D|g|G|M")`、`ToShortDateString/ToLongDateString`、插值格式 token `{N:...}`、显式 `CultureInfo.CurrentCulture` 的日期格式化 —— 逐个命中读上下文定性（显示用途 vs 协议用途）。
3. 本地化一致性：Python 程序化 diff 12 个语言 JSON（键集合、重复键、占位符索引集合、格式 token 内容），非目测。
4. 架构一致性：`WidgetRegistry` → `WidgetContentFactory` → `ContentWidgetWindowFactory`（唯一 `new ContentWidgetWindow` 位点）→ `ContentWidgetWindow.Closed` 销毁链 → `WidgetShellContentHost.DisposeContentOnce` → 各 Adapter Dispose；`Productivity` 残留全仓检索；`WidgetManager.ZOrder.cs` 恢复监视器双档间隔与 z-order 文档抽查对照；`tests/DeskBox.Tests/ModuleBoundaryContractTests.cs` 契约意图确认。
5. 线程安全/生命周期：`TryEnqueue` 返回值使用形态全量扫；三大事件（`LanguageChanged`/`SettingsChanged`/`TodoStoreChanged`）订阅/退订逐文件配对统计；`FolderWatcherService`→`WidgetViewModel` 线程模型与防竞态；Music/Weather/SearchPopup/QuickCapture/Glance 的防抖、代际、取消令牌。
6. XAML：全部 XAML 的 `StaticResource/ThemeResource` 键程序化比对声明集合；`DueStatusText` 等绑定路径核对；`StringFormat=` 使用检索（为零）。

命中统计：问题 6 条（P2 ×1，P3 ×5），观察项 1 条；本地化矩阵干净（1 处有意模式差异）；确认稳健的重点区域 12 项。

分级口径：P0=高危崩溃/数据丢失主路径；P1=功能显著破坏；P2=明确缺陷、触发面窄或影响中等；P3=边角/低概率/行为确认项。

---

## 问题清单

### B-1 【P2｜置信度：高】DisplayCulture 接入遗漏：10 个用户可见日期位点仍走文化默认日历（ar-SA 渲染 Hijri；超范围日期抛异常），与 b96129f6 "render every user-facing date on the Gregorian calendar" 的声明直接矛盾

人话摘要：b96129f6 声称把所有用户可见日期统一到公历，实际只接了 15 个位点；设置页的云备份状态行/快照标题/恢复确认对话框/快照摘要、反馈卡片、运行时诊断"上次生命周期事件"、以及搜索结果的 `DateDisplay`（`FileMetaService`）仍在用当前文化的默认日历格式化。Windows 格式为 ar-SA（默认 Um Al-Qura）的用户，在刚刚被修复成公历的 todo/glance/天气旁边，这些界面继续显示 Hijri 年份；且 `ToString("g"/"d"/"M")` 在日期超出 Um Al-Qura 支持范围（1900-04-30..2077-11-16）时抛 `ArgumentOutOfRangeException` —— 这正是该提交信息里自述要消除的异常类别。

代码证据（全部为用户可见的显示文本，非协议/文件名用途）：

- `src/DeskBox/Features/Backup/BackupSettingsViewModel.SettingsSurface.cs:532-548`（设置页云备份状态行）
  ```csharp
  string status = successTicks > 0
      ? Format("Settings.CloudBackup.LastSuccess",
          new DateTimeOffset(successTicks, TimeSpan.Zero).ToLocalTime().ToString("g"))
  // :540 failureTicks、:547 unverifiedTicks 同型 ToString("g")
  ```
- `src/DeskBox/Features/Backup/BackupSettingsViewModel.SettingsSurface.cs:590`（远程快照列表项标题——显式传 CurrentCulture 也没用，日历仍是默认的）
  ```csharp
  title = createdUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
  ```
- `src/DeskBox/Views/SettingsWindow.CloudBackup.cs:397`（恢复确认对话框正文日期）`preparation.BackupCreatedAtUtc.ToLocalTime().ToString("g")`
- `src/DeskBox/Views/SettingsWindow.Maintenance.cs:251` 与 `:438`（数据备份恢复确认正文、快照汇总行）同型 `ToString("g")`
- `src/DeskBox/Views/SettingsWindow.Feedback.cs:472`（"我的反馈"卡片元信息行）
  ```csharp
  Text = $"#{item.Id} · {typeLabel} · {StatusText(item.Status)} · {item.CreatedAt.ToLocalTime().ToString("d")}"
  ```
- `src/DeskBox/ViewModels/SettingsViewModel.RuntimeDiagnostics.cs:46`（运行状况"上次生命周期事件"）
  ```csharp
  string lastLifecycle = snapshot.LastLifecycleEventAt is { } lifecycleAt
      ? lifecycleAt.ToLocalTime().ToString("g")
  ```
- `src/DeskBox/Services/FileMetaService.cs:433-438`（搜索结果 DateDisplay）
  ```csharp
  public static string FormatDate(DateTime date)
  {
      return date.Year == DateTime.Now.Year
          ? date.ToString("M")
          : date.ToString("d");
  }
  ```
  调用链 `FillStats`（`FileMetaService.cs:350/364`）→ `SearchResultItem.DateDisplay`。注意文件系统时间不受控（时钟偏移可产生 1601 或 2099 年的文件），超范围时 `ToString` 抛异常，但 `FillStats` 外层有 try/catch（`:371`），只导致该条目统计信息缺失，不崩溃。

触发路径：Windows 区域格式设为 ar-SA（或任何默认非公历日历的格式，如 fa-IR 波斯历）→ 打开设置 → 云备份页/数据备份页/反馈页/运行诊断，或执行一次文件搜索。同机上 todo 日期、glance 主日期、`FileInfo.FileModified`（`WidgetItem.SecondaryInfo`）已是公历，这些界面是 Hijri —— 同屏双历法。异常分支需系统时钟或数据时间戳超出 1900-04-30..2077-11-16（FileMetaService 路径现实可达：异常文件时间戳）。

建议方向：与既有 15 个位点同法处理——`ToString("g", DisplayCulture.GregorianCurrent)`；`FileMetaService.FormatDate` 同改。建议同时补一条契约测试枚举全部用户可见日期格式化位点（grep 级 ratchet），否则这类"遗漏位点"会随新设置页代码再次回归（本轮已是第二轮漏网：a251103d 只修 glance，b96129f6 又漏了这 10 处）。

如何验证（不执行）：把 `CultureInfo.CurrentCulture` 设为 `CultureInfo.GetCultureInfo("ar-SA")` 后调用 `StatusText`/`FormatDate`，断言年份含 "2026" 而非 Hijri 年（1448）；再喂 `new DateTime(2099,1,1)` 断言不抛。

### B-2 【P3｜置信度：高】`LocalizationService.DefaultFormat` 仍用 raw `CurrentCulture`，兜底路径与 `Format` 行为分叉（Hijri + 可抛异常）

人话摘要：实例方法 `Format` 已换成 `DisplayCulture.GregorianCurrent`，但静态兜底 `DefaultFormat` 没换。`WidgetItem.SecondaryInfo` 在 `App.Current?.LocalizationService` 为 null 的窗口期（极早期构造/启动异常）走兜底，把**文件系统来源的 `LastModified`**（可为任意年份）以 `{1:yyyy/M/d HH:mm}` token 在 raw 当前文化下求值——ar-SA 格式下渲染 Hijri，且日期超出 Um Al-Qura 范围时抛 `ArgumentOutOfRangeException`（getter 无 catch）。

代码证据：

- `src/DeskBox/Services/LocalizationService.cs:202-205`
  ```csharp
  public static string DefaultFormat(string key, params object[] args)
  {
      return string.Format(CultureInfo.CurrentCulture, DefaultText(key), args);
  }
  ```
- `src/DeskBox/Models/WidgetItem.cs:131` 与 `:173`
  ```csharp
  return LastModified == default
      ? typeText
      : LocalizeFormat("FileInfo.FileModified", typeText, LastModified);
  // LocalizeFormat => TryGetLocalizationService()?.Format(key, args) ?? LocalizationService.DefaultFormat(key, args)
  ```
- 同型兜底还有 `src/DeskBox/Models/OrganizationHistoryEntry.cs:136`、`src/DeskBox/Services/DragDropPermissionService.cs:1182`、`src/DeskBox/ViewModels/TodoItemViewModel.cs:668` 附近。

触发路径：ar-SA 格式机器 + `App.Current` 尚不可用（或其 `LocalizationService` 访问抛异常被 `TryGetLocalizationService` 的 catch 吞掉）+ 一个 LastModified 超范围的文件出现在文件格子/搜索结果里 → getter 抛异常。概率低但与主路径行为不一致是明确的。

建议方向：`DefaultFormat` 改用 `DisplayCulture.GregorianCurrent`（一行改动，与 `Format` 对齐）。

如何验证：单测 `DefaultFormat("FileInfo.FileModified", "x", new DateTime(2099,1,1))` 在 `CurrentCulture=ar-SA` 下断言不抛且含公历年。

### B-3 【P3｜置信度：中】`DisplayCulture` 缓存键只按 `culture.Name`，不区分实例身份/用户覆盖：glance 的应用语言文化与 Windows `CurrentCulture` 同名不同源，先到者的克隆被两边共用

人话摘要：`EnsureGregorian` 的 `ConcurrentDictionary` 键是 `culture.Name`。glance 走 `GetCulture()`（`GlanceWidgetViewModel.cs:941-949`）→ `CultureInfo.GetCultureInfo(name)`（无用户覆盖的标准实例），而 `LocalizationService.Format`/其余 15 个位点走 `CultureInfo.CurrentCulture`（Windows 用户覆盖实例，自定义日期模式如 "dd.MM.yyyy" 时 DateTimeFormat 与标准实例不同）。两个 "ar-SA" 实例共享一个缓存条目：先到者克隆时固化的 DateTimeFormat（含用户覆盖状态）被另一侧永久复用 → 用户在"区域设置"里自定义的日期模式可能在某一侧被静默丢弃。

代码证据：

- `src/DeskBox/Helpers/DisplayCulture.cs:19-27`
  ```csharp
  private static readonly ConcurrentDictionary<string, CultureInfo> s_gregorianByCultureName = new(StringComparer.OrdinalIgnoreCase);
  ...
  return s_gregorianByCultureName.GetOrAdd(culture.Name, _ => CreateGregorianClone(culture));
  ```
- `src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:941-949`（`GetCultureInfo(_localizationService.CurrentCultureName)`，无覆盖）与 `src/DeskBox/Services/LocalizationService.cs:194`（`DisplayCulture.GregorianCurrent` → `CultureInfo.CurrentCulture`，带覆盖）同名相撞。
- 测试盲区佐证：`tests/DeskBox.Tests/DisplayCultureTests.cs:49-68` 的 `GregorianCurrent_FollowsTheCurrentCultureWithAGregorianCalendar` 也只设 `GetCultureInfo("ar-SA")`，从未用用户覆盖实例分叉验证。

触发路径：ar-SA 格式 + 用户自定义了日期模式；先打开 glance（缓存标准实例的克隆）→ 之后 `LocalizationService.Format` 的 token 求值用不到用户自定义模式（或反序）。仅显示风格受影响，无崩溃。

建议方向：缓存键加入区分维度（如 `culture.UseUserOverride` + DateTimeFormat 签名），或 `GregorianCurrent` 与 glance 路径各自持有独立克隆缓存；至少在注释里固化"同名单实例共享是有意的"决策。

如何验证：单测先 `EnsureGregorian(new CultureInfo("ar-SA"))` 再 `EnsureGregorian(new CultureInfo("ar-SA", useUserOverride: true))`（后者改一个 DateTimeFormat 模式），断言拿到的克隆模式符合各自来源。

### B-4 【P3｜置信度：中】`GlanceWidgetViewModel.UpdateDateAndTime` 里 `WeekdayText = now.ToString("dddd", culture)` 未公历化——同一方法内与 `FormatDateText` 处理不一致，且保留了 b96129f6 自述要消除的 Um Al-Qura 超范围异常类

人话摘要：主日期 `DateText` 走 `EnsureGregorianCalendar`（公历化），但同一方法里的星期文本用原始 culture 格式化 "dddd"。星期名与日历无关（同一时刻任何日历的星期几一致），所以正常范围无显示问题；但 ar-SA（Um Al-Qura）下 `dddd` 需经日历求值，系统时钟超出 1900-04-30..2077-11-16 时抛 `ArgumentOutOfRangeException`，且 `ClockTimer_Tick`（`GlanceWidgetViewModel.cs:1002` 附近）是普通 void 计时器回调，无 try/catch → 未处理异常直达 WinUI 线程。这正是 a251103d/b96129f6 提交信息里"UmAlQuraCalendar 的年份范围使这些 ToString 抛异常"要修的同一类问题，漏了这个位点。

代码证据：

- `src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:765-771`
  ```csharp
  private void UpdateDateAndTime()
  {
      CultureInfo culture = GetCulture();
      DateTime now = DateTime.Now;
      TimeText = FormatTimeText(now, _settings.TimeFormat, culture);
      DateText = FormatDateText(now, culture, _settings.ShowYear);   // 内部 EnsureGregorian
      CompactCalendarDateText = FormatCompactCalendarDateText(now, culture);
      WeekdayText = now.ToString("dddd", culture);                   // ← 未公历化
  }
  ```

触发路径：应用语言 = ar-SA（`GetCulture()` 返回 Um Al-Qura 默认文化的 ar-SA）+ 系统时钟被设到 2078+ 或 1900-（或 NTP 异常）→ glance 时钟每分钟 tick 抛异常。概率低（需手动改时钟），但同类位点刚被宣称为已修复。

建议方向：`now.ToString("dddd", DisplayCulture.EnsureGregorian(culture))` 或复用 `FormatDateText` 已算出的公历化 culture。

如何验证：单测 `FormatTimeText`/星期渲染传入 `new DateTime(2099,1,1)` + ar-SA 断言不抛。

### B-5 【P3｜置信度：中高】`TodoWidgetViewModel.ApplyExternalStoreChange` 写模型字段而非 VM 属性：外部 snooze 变更后已打开 todo 窗口的"已暂至 …"状态不刷新（持久化无恙）

人话摘要：DEF-043 合并处理器把 reminder 服务持久化的字段直接写进被包装的模型 `match.Item.SnoozedUntil`，没有走 VM 属性 setter（`match.SnoozedUntil`）。VM 的 `_snoozedUntil` 字段与 `{Binding DueStatusText}`（含"已暂至 {时间}"后缀，`TodoItemViewModel.cs:463-466`）依赖 PropertyChanged 通知，而处理器从不为它们发通知（只在 else 分支发了个无关的 `OnPropertyChanged(nameof(IsInitialized))`，对 `{Binding}` 无刷新作用）。持久化是对的（`SaveAsync` 序列化 `Items.Select(item => item.Item)`，`TodoWidgetViewModel.FilteringAndAppearance.cs:95-97`），纯显示滞留。

代码证据：

- `src/DeskBox/ViewModels/TodoWidgetViewModel.cs:785-790`
  ```csharp
  match.Item.ReminderLastNotifiedAt = changedItem.ReminderLastNotifiedAt;
  match.Item.ReminderDismissedForDueDate = changedItem.ReminderDismissedForDueDate;
  match.Item.SnoozedUntil = changedItem.SnoozedUntil;      // ← 应走 match.SnoozedUntil（internal setter 会发 HasActiveSnooze/DueStatusText 通知）
  match.Item.SnoozeLastNotifiedAt = changedItem.SnoozeLastNotifiedAt;
  match.Item.UpdatedAt = changedItem.UpdatedAt;
  ```
- `src/DeskBox/ViewModels/TodoItemViewModel.cs:279-291`（VM setter 才有 `OnPropertyChanged(nameof(DueStatusText))`）与 `:421`（`HasActiveSnooze` 读的是 `_snoozedUntil` 字段）。
- 绑定：`src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml:400/409` `Text="{Binding DueStatusText}"`。

触发路径：todo 窗口打开 → 到期提醒 toast 上点"暂至 10 分钟"（或 snooze 到点被服务触发并清空）→ 服务 `PublishStoreChanged` → `ApplyExternalStoreChange` 只改模型 → 已打开窗口的 due 状态行仍显示旧"已暂至/无暂至"徽标，直到该条目被重新选中/过滤/编辑等触发其他通知。

建议方向：snooze 相关字段改走 `match.SnoozedUntil = changedItem.SnoozedUntil;`（internal setter 已连带发 `DueStatusText`/`HasActiveSnooze` 通知）。无写回风险：已核实 `SnoozedUntil` 的 VM setter 与 save 路径都读 `_item`，不会用陈旧 `_snoozedUntil` 复活已清空的 snooze。

如何验证：打开 todo 窗口，toast 上 snooze，断言窗口内 DueStatusText 在合并事件后立刻变化（UI 手测或对 `ApplyExternalStoreChange` 后 `DueStatusText` 的单测）。

### B-6 【P3｜置信度：低-中】`NativeAppNotificationService.TryShow` 把 `AppNotificationManager.Default.Setting` 探测放在 try 块之外：平台 COM 激活失败时异常逃逸到不设防调用方

人话摘要：df24c5fe 新增的开关守卫在 try 之前读 `AppNotificationManager.Default.Setting`（`NativeAppNotificationService.cs:200`）。该 WinRT 属性需要 WPN 平台 COM 类；`LogNotificationPlatformEnvironment`（同文件 :158-174）自己就记录了"WpnService 被禁用 → REGDB_E_CLASSNOTREG"是真实环境场景。若 `Register()` 在平台健康时已成功、随后 WPN 被禁用，下一次 `TryShow` 读 `Setting` 可能抛 COMException 并从 `TryShow` 传播出去。多数调用链有兜底（todo 提醒在 `TodoReminderService.CheckCoreAsync:209-217` 有 try/catch），但如 `App.Tray.cs:1334 ShowFolderMappingFailure` 本身位于外层 catch 块中调用，二次异常会逃出整个 handler。

代码证据：

- `src/DeskBox/Services/NativeAppNotificationService.cs:195-207`
  ```csharp
  // Showing while the platform has notifications turned off ...
  AppNotificationSetting setting = AppNotificationManager.Default.Setting;   // ← try 外
  if (setting != AppNotificationSetting.Enabled) { ... return false; }
  try
  {
      var builder = new AppNotificationBuilder() ...
  ```

触发路径：注册成功后运行期间禁用通知平台（服务/组策略切换）→ 任一 TryShow 调用点。无法静态证实 `Setting` 必抛（可能只是返回 Disabled），故置信度低-中；修复成本为零。

建议方向：把探测读进 try 内（catch 已统一处理），或包一层独立 try 返回 false。

如何验证：无直接静态验证；可在 `Setting` 抛异常的环境（禁用 WpnService）冒烟 TryShow 调用点，观察是否走 fallback 而非崩溃。

### B-7 【观察项｜非缺陷，行为确认】snooze 宽限：未送达的 snooze 在 1 分钟宽限后永久静默，`SnoozedUntil` 残留且跨重启不清理

人话摘要：b96129f6(:744-751) 给 snooze 触发加了 `now <= snoozedUntil + MissedReminderGrace(1min)` 上限（f5a01f91 补测试）。语义上与 due 路径一致（due 的宽限从 `dueDate` 起算，`ShouldNotifyDue` :786）。已核实状态机自洽：snooze 分支优先于 due 分支、snooze 时已写 `ReminderDismissedForDueDate = due`，所以过期未发 snooze 之后两条触发器都不可达是既有语义的延续，非新引入矛盾；toast 的 snooze 选项只有 10/30 分钟（`App.xaml.cs:62-63`），与 30s 扫描 + 1min 宽限的配比合理。唯一值得产品确认的场景：用户在 snooze 过期后才重新开启系统通知，该 snooze 永不补发（新行为；旧行为是无限重试、开启后即补发）。若视为可接受，无需改动。

代码证据：`src/DeskBox/Services/TodoReminderService.cs:744-751`、`:21`、`:786`。

---

## 本地化一致性专项结果（程序化）

方法：Python 逐文件解析 12 个 JSON（UTF-8-BOM 容错），比对（a）键集合、（b）重复键、（c）占位符 `{N}` 索引多重集合、（d）带格式 token `{N:fmt}` 的格式串内容。

结果：

1. 键集合：12 个文件完全一致，各 3091 键，无缺失、无多余。
2. 重复键：无。
3. 占位符索引集合：全语言全键一致（翻译重排 `{0}{1}` 顺序不视为差异，且未发现仅顺序差异导致格式串错配的情形）。
4. 格式 token：唯一差异 `FileInfo.FileModified`（en-US/zh 系为 `{0} · {1:yyyy/M/d HH:mm}`，fr-FR/pt-BR 为 `{1:dd/MM/yyyy HH:mm}`，ru-RU 为 `{1:dd.MM.yyyy HH:mm}`）—— 有意的本地化日期次序，经 `LocalizationService.Format → DisplayCulture.GregorianCurrent` 求值后安全（公历、文化数字形状）。
5. 数字 token（`{0:F0}`/`{0:N0}` 类）：`Format` 从 `CurrentCulture` 换为 `GregorianCurrent` 不改变 NumberFormat（clone 仅改 `DateTimeFormat.Calendar`），各文化数字输出与改前逐字节一致 —— 无回归。
6. ar-SA RTL：字符串资源无方向性硬编码问题键（日期 token 均为数字模式）；未发现 `FlowDirection` 相关手写翻转（布局 RTL 由系统处理）。
7. XAML 资源键：全仓 XAML 的 `StaticResource/ThemeResource` 引用（排除 WinUI 系统主题键）与 `App.xaml` + 各文件 `x:Key` 声明集合比对 —— 零缺失。

## 需要主流程复核的条目（我最不确定的）

1. B-3 的运行时真伪：.NET 10 `DateTimeFormatInfo.Calendar` setter 赋值后是否重置模式串/月名。`DisplayCultureTests` 用"手工克隆对照"断言，但手工克隆与被测对象走同一底层路径，区分不出"setter 重置了模式"与"未重置"。若 setter 会按新日历重置 `LongDatePattern`，glance `FormatDateText` 的去星期正则（`GlanceWidgetViewModel.cs:855-868`）处理的可能是被重置的模式而非用户模式。建议在装有自定义区域格式的 ar-SA 虚机上人肉比对 glance 主日期模式。
2. B-6 `AppNotificationManager.Default.Setting` 是否真的会抛（静态无法定论；修复零成本，可直接顺手加固）。
3. `DisplayCulture.CreateGregorianClone` 的 `?? new GregorianCalendar()` fallback（`DisplayCulture.cs:41-43`）：若目标文化的 OptionalCalendars 不含公历且 .NET 的 Calendar setter 校验 OptionalCalendars，赋值会抛 → catch → 退回原文化（非公历）。fa-IR（波斯历默认）在 Windows ICU 数据下是否含公历 optional 未验证。最坏情形是"该文化退回非公历显示"，与修复前的行为一致，不劣化。
4. B-5 的 UI 影响面：我只核对了 `DueStatusText`/`HasActiveSnooze` 两个派生属性；若 todo 详情页还有其他绑定 `SnoozedUntil` 路径的 x:Bind，同样受影响（x:Bind 路径含 `SnoozedUntil` 属性的会在 VM PropertyChanged 时刷新，而这里恰好不发该通知）。

## 已核实稳健的重点区域（留痕，防后续重复劳动）

1. 架构路径无旁路：`ContentWidgetWindowFactory.cs:30` 是全仓唯一 `new ContentWidgetWindow(` 位点；`WidgetRegistry.CreateDefault` 与 `current_architecture.md` 的 kind 清单一致（含 Search/Glance）；`Productivity` 仅存在于 `SettingsService.cs:2745-2753` 的迁移清理与契约测试（`WidgetKindCoverageTests`、`WidgetContentFactoryTests.GetDescriptor_RejectsLegacyProductivityKind`），无活跃创建路径。
2. `ContentWidgetWindow.Closed`（`ContentWidgetWindow.xaml.cs:1342-1381`）解绑/销毁链完整：LanguageChanged/SettingsChanged/AppWindow.Changed/RightTapped/native drop bridge/compact source/search history/`_contentHost.DisposeContent()`/缓存组内容/ResizeGrid 指针事件，均有对应 -= 或 Dispose。
3. 三大事件订阅/退订逐文件配对（LanguageChanged 16 文件成对；SettingsChanged 18 文件成对，多出的 -＝ 为防御性重复退订；TodoStoreChanged 在 App.xaml.cs 成对）。
4. `GlanceWidgetViewModel.Dispose`（:1112-1137）完整退订 store/Localization/Settings 事件并停两只 timer；`WidgetContentAdapterBase`→各 Adapter `Dispose(true)`→VM.Dispose 链路闭合（`GlanceWidgetContentAdapter.cs:134-139`）。
5. `FolderWatcherService` 用 DispatcherQueueTimer 去抖后在 UI 线程回调 `FolderChanged`（`FolderWatcherService.cs:1029-1055`）；`WidgetViewModel.ProcessFolderChangedAsync` 有 `_folderRefreshGate` + 批次/代际双校验 + 全量重载回退（`WidgetViewModel.SortingAndWatchers.cs:341-431`），`ObservableCollection` 变异均落 UI 线程。
6. `MusicWidgetViewModel` 全部 SMTC 事件经 `HasThreadAccess`/`TryEnqueue` marshal，媒体元数据刷新有代际防抖（`MusicWidgetViewModel.cs:687-734`）。
7. `WeatherWidgetViewModel.RefreshAsync` 有请求版本号 + 失败退避 + `_isRefreshing` 合并（`RefreshAndLayout.cs:38-126`）；`SearchPopupViewModel` 三路 CTS + `Interlocked.Exchange` 取消（:20-22, :467-468, :698）。
8. `TodoReminderService` 两阶段协议自洽：未展示则释放会话键重试（:219-230）、持久化有防用户并发编辑守卫（:554-573）、`CheckNowAsync` 的 `_isChecking` 在首个 await 前同步置位（UI 线程序列化）无重入窗。
9. 通知回退链完整：native toast →（df24c5fe 后 Setting≠Enabled 即返回 false）→ 托盘气泡 → false；todo 提醒侧 `_notify` 外层 try/catch 兜底。
10. 更新器 `WaitForParentExit` hoist 覆盖全部重启路径（RestartOnly :66、installer 缺失 :75、catch 兜底），90 秒超时防挂死（`DeskBox.Updater/Program.cs:60-110, :115-131`）。
11. 日历网格 `LocalCalendarPresentationSource` 用 `DateOnly`（公历算术）+ `date.Day.ToString(culture)`（仅数字形状），无日历耦合；glance 跨午夜统一走 `RefreshIfObservedDateChanged` 单通道。
12. 转换器（`SettingsBindingConverters`、Glance/Weather 各 BoolTo* 系列）null/异型输入全部有默认值，ConvertBack 一律 `NotSupportedException` 但未被 TwoWay 绑定使用；Win11-only DWM 属性（圆角/边框色/backdrop）统一经 `WindowsCompatibilityService.IsWindows11OrLater` 门控，Win10 21H2 无裸调用。
