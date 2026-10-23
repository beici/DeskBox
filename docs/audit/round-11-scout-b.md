# 第 11 轮（从零）审查 · 维度 B（UI / ViewModel / 架构与本地化面）

- 审查日期：2026-10-24；分支 `wip/fix-bug`（HEAD `7fcede2c`）。
- 审查方式：**纯静态**。只读源码与 `docs/architecture/current_architecture.md`、`docs/architecture/[重要勿删]widget_zorder_lifecycle.md`；未构建、未运行测试、未启动应用、未修改任何源码（唯一写入即本报告文件）。历史审查结论（docs/audit/ 其余文件）一律未读取。
- 扫描范围：`src/DeskBox/Views/`（41 个 .cs + XAML）、`src/DeskBox/ViewModels/`（全部 60 个文件，精读 Weather×3 / Glance / SearchPopup / Music×4 / Todo×6 / QuickCapture×4 / WidgetViewModel 关键 partial）、`src/DeskBox/Controls/`（含 `WidgetContents/`、WidgetShell / WidgetShellContentHost）、`src/DeskBox/Helpers/`（UI 相关 helper）、`src/DeskBox/Strings/` 12 个语言 JSON、`Services/` 中架构路径与 z-order 相关文件（WidgetRegistry / WidgetContentFactory / WidgetManager.* / WidgetLayerService / LocalizationService / GlanceImageService / LocalCalendarPresentationSource / WindowsCompatibilityService / ExplorerQuickAccessHelper / ShellClipboardHelper）、`tests/DeskBox.Tests/ModuleBoundaryContractTests.cs`（只读契约注释）。
- 方法：程序化对比（本地化键/占位符矩阵、Format 调用参数数 vs 占位符数、XAML StaticResource 引用 vs 定义）+ 模式扫描（`+=/-=` 配对、`TryEnqueue`、`async void`、`Task.Run`、`DateTime.Now`、`ToString("…")`、`Thread.Sleep`/`GC.Collect`/`.Result`、静态字段/静态 timer、`Begin/EndInteraction` 配对、死代码）+ 命中处精读确认。
- 命中统计：问题 9 条（P2×2、P3×7；无 P0/P1）。本地化 12 文件键集合与占位符**完全一致**（3091 键 × 12）。本仓库已经过 10 轮修复，浅层缺陷基本清空，本轮命中的是区域设置边缘行为、生命周期竞态窗口与卫生类问题。

> 总体判断：核心链路（内容窗口路径、交互深度配对、z-order 组恢复、undo/tombstone、QuickCapture 增量同步、天气刷新状态机）防御性极强，未发现数据丢失/崩溃级缺陷。剩余风险集中在**特定语言环境（ar-SA）**与**少数 Dispose/async-void 边缘路径**。

---

## 问题清单

### B-1 【P2｜置信度：高（机制）／中（是否有意）】ar-SA 语言下 Glance 主日期按 .NET 默认 UmAlQura（伊斯兰历）渲染，与全应用其余公历日期矛盾

- 文件:行：
  - `src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:764-771`（`UpdateDateAndTime`：`DateText = FormatDateText(now, culture, _settings.ShowYear);`）
  - `src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:853-874`（`FormatDateText` 用 `culture.DateTimeFormat.LongDatePattern` 与 `date.ToString("M", culture)`）
  - `src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:927-937`（`GetCulture()` → `CultureInfo.GetCultureInfo(_localizationService.CurrentCultureName)`）
- 人话摘要：.NET 中 `ar-SA` 文化的默认 Calendar 是 **UmAlQuraCalendar（Hijri）**（官方文档与社区均确认，见文末参考）。因此阿语用户 Glance 小组件的大日期文本被格式化为**伊斯兰历日期**（如 "١٤ رمضان ١٤٤٨"），而：
  1. 应用其余所有日期都显式用公历固定格式（`TodoItemViewModel.cs:671-683` `"yyyy/M/d HH:mm"`、`QuickCaptureSurfaceContent.xaml.cs:1263-1265` 同、`WeatherWidgetViewModel.DataProcessing.cs:672` `"HH:mm"`）；
  2. Glance 自带的"传统历法"功能把 UmAlQura 当作**用户显式选择**的模式（`GlanceTraditionalCalendarService.cs:296-306`：`GlanceTraditionalCalendarMode.UmAlQura => (CalendarIdentifiers.UmAlQura, "ar-SA")`）——阿语用户不选传统历，主日期却已是 Hijri；选了则主/附日期重复；
  3. 同一小组件内的日历网格是公历（`LocalCalendarPresentationSource.cs:15-36` 以 `DateOnly` 公历算 `date.Day`），主日期（Hijri）与网格（公历）互相矛盾。
- 触发路径：设置 → 语言 → العربية → Glance 小组件显示日期/农历标题区域。星期文本（`dddd`）与网格不受影响（星期名与公历日号）。
- 用户可感知后果：阿语环境日期显示为用户未要求的伊斯兰历，且与应用内其他位置、Windows 任务栏（Win10+ 阿语默认公历显示）不一致。
- 建议方向：`FormatDateText`/`FormatCompactCalendarDateText` 使用克隆的 culture 并强制 `DateTimeFormat.Calendar = new GregorianCalendar()`（保留阿语月名与数字），把 Hijri 交给传统历法选项。
- 如何验证（不由本审查执行）：语言切 ar-SA，对比 Glance 主日期与任务栏时钟；或单元测试断言 `FormatDateText(new DateTime(2026,10,24), GetCultureInfo("ar-SA"), true)` 的月名/年份数字为公历。

### B-2 【P2｜置信度：中】全仓库没有任何 RTL / FlowDirection 处理，ar-SA 作为一等发布语言整体按 LTR 渲染

- 文件:行：全仓库证据——`grep -r FlowDirection src/DeskBox --include=*.cs --include=*.xaml` 命中 **0** 处（Views/Controls/XAML 全无）；ar-SA 却是 12 个完整发布语言之一（`Strings/ar-SA.json` 3091 键）。
- 人话摘要：Windows App SDK 的 RTL 支持靠 `FlowDirection="RightToLeft"`（通常在语言切换时对根元素设置）。本应用从未设置，阿语用户看到的是左对齐、不镜像的界面：标题栏图标在左、列表进度条方向、返回箭头方向、菜单勾选位都按 LTR 布局。阿拉伯语文本本身可正确 shaping 显示，但整体版式对 RTL 读者是"反的"。
- 触发路径：语言切 ar-SA 后打开任意窗口/小组件。
- 用户可感知后果：RTL 用户可用性显著退化（属于"特定语言环境退化"分级）；若产品上明确不支持 RTL，应在文档/设置页声明而非静默。
- 建议方向：在语言为 `ar` 时对窗口根元素设置 `FlowDirection=RightToLeft`（可先只做设置窗口与主 shell 试点）；或在发布说明中明确 ar-SA 仅做翻译不做镜像。
- 如何验证：切 ar-SA 观察设置窗口与 Todo 列表的布局方向。

### B-3 【P3｜置信度：高】`TodoItemViewModel.MarkerGlyph` 三元表达式两个分支完全相同（疑似复制粘贴残留）

- 文件:行：`src/DeskBox/ViewModels/TodoItemViewModel.cs:388`
  ```csharp
  public string MarkerGlyph => HasRedMarker ? "\uE915" : "\uE915";
  ```
- 人话摘要：红标记与非红标记返回同一 Segoe MDL2 glyph（\uE915 = Error/标记类）。条件永远不影响结果，属死条件；从相邻代码看（`MarkerGlyph` 紧邻 `HasRedMarker`，同文件 360-388 行其他 Glyph 属性均按状态区分），非红分支本应给别的 glyph。
- 触发路径：Todo 列表/详情显示彩色标记行时取该属性绑定（无视觉 bug，因两分支相同；问题在意图丢失与误导后来者）。
- 用户可感知后果：当前无；若本意是红/其他标记视觉区分，则该区分从未生效。
- 建议方向：确认设计意图——要么删掉三元，要么补上正确的非红 glyph。
- 如何验证：查 Todo 标记 UI 设计稿/上游对应提交。

### B-4 【P3｜置信度：高（路径存在）／触发窗口小】Glance VM Dispose 与已排队回调竞态：Dispose 后读 `_lifetimeCts.Token` 抛 ObjectDisposedException（被全局兜底吞掉）

- 文件:行：
  - 订阅与入队：`src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:90-95`（ctor 订阅 `Store.Changed` / `LanguageChanged` / `SettingsChanged`）、`:1034-1049`（`Store_Changed` 非 UI 线程时 `_dispatcherQueue.TryEnqueue(() => _ = ReloadSettingsAsync())`）、`:1051-1066`（`LocalizationService_LanguageChanged` → `TryEnqueue(... _ = UpdateCalendarAsync(); ...)`）
  - 释放：`:1112-1133`（`Dispose` 依次 `_lifetimeCts.Cancel(); _lifetimeCts.Dispose();`，无回调排空）
  - 触达点：`:884-887`（`GetMonthAsync(..., _lifetimeCts.Token)`）、`:555-557`（`GetAvailableImagesAsync(_settings, _lifetimeCts.Token)`）——`ReloadSettingsAsync` 入口**无 `_isDisposed` 复查**；`UpdateCalendarAsync` 只 catch `OperationCanceledException`（`:922-924`）。
- 人话摘要：`Store_Changed`/`LanguageChanged` 的处理器检查 `_isDisposed` 后入队，但入队回调真正执行时 Dispose 可能已经发生并 Dispose 了 CTS；回调里访问 `_lifetimeCts.Token`（已释放的 CancellationTokenSource.Token）会抛 `ObjectDisposedException`。UI 线程 lambda 抛出进入 `App.OnUnhandledException`（`App.xaml.cs:5861-5875`，`e.Handled = true`）——进程不崩，但留下 "Unhandled exception" 日志、回调中途夭折；fire-and-forget 任务一侧变 `UnobservedTaskException`（`App.xaml.cs:5887-5891` 兜底）。
- 触发路径：Glance 小组件关闭（Dispose）与一次 store 写盘完成 / 语言切换在毫秒级窗口内交错。低频但真实存在（语言切换必然触发 `LanguageChanged` 广播到所有 Glance 实例）。
- 用户可感知后果：几乎无（日志噪音）；属"Dispose 后对象仍可被排队回调触达"的生命周期卫生问题，同类模式若被复制到无全局兜底的路径会变成崩溃。
- 建议方向：`ReloadSettingsAsync`/`UpdateCalendarAsync` 入口补 `_isDisposed` 短路；或 Dispose 时捕获 token 副本供后续使用；或 catch 增加 `ObjectDisposedException`。
- 如何验证：在 Dispose 与 Store_Changed 之间加延迟的测试钩子，单元测试断言不抛。

### B-5 【P3｜置信度：高】`WidgetManager.QueueRequestedLayerRestoreCheck` 死代码；`RequestRestoreRaisedWidgetsToDesktopLayer` 语义与名称不符（仅记日志不调度）

- 文件:行：`src/DeskBox/Services/WidgetManager.ZOrder.cs:1148-1158`（`QueueRequestedLayerRestoreCheck` 定义，全仓库 0 调用者）；`:1130-1146`（`RequestRestoreRaisedWidgetsToDesktopLayer` 在 raised 态仅 `App.LogVerbose("...held reason=... until=next-toggle")` 后 `return true`，不调度任何检查）。调用方：`ContentWidgetWindow.Commands.cs:1295`、`:1325`、`ContentWidgetWindow.WindowInteraction.cs:407`、`WidgetWindowBase.Interaction.cs:829`——它们拿到 `true` 后跳过本地 `RestoreDesktopLayerFromManager()`，完全依赖恢复监视器。
- 人话摘要：这与 `[重要勿删]widget_zorder_lifecycle.md` 坑 #9 的记载一致（"RequestRestore… 只记日志不调度任何检查；QueueRequestedLayerRestoreCheck 定义了但无人调用"）——**文档已把它标为已知状态**，回落确实永远走监视器（设计意图）。但死方法留在代码里 + "Request…Restore" 这个名字会持续误导后来者以为"请求了就会被处理"。
- 用户可感知后果：无（行为符合设计）。
- 建议方向：删除 `QueueRequestedLayerRestoreCheck`；将 `RequestRestoreRaisedWidgetsToDesktopLayer` 改名（如 `NoteRestoreHeldForMonitor`）或补注释引用文档坑 #9。
- 如何验证：grep 全仓库确认 0 调用者后直接删。

### B-6 【P3｜置信度：高（逃逸路径存在）／触发概率低】Onboarding 两个 Toggle 的 async void 处理器只有 try/finally 没有兜底 catch

- 文件:行：`src/DeskBox/Views/OnboardingWindow.TaskFlow.cs:110-145`（`TaskStep3QuickAccessToggle_Toggled`）、`:147-180`（`TaskStep3DesktopShortcutToggle_Toggled`）——`try { … await …; await ShowTaskStorageEntryErrorAsync(...); } finally { toggle.IsEnabled = true; }`，无 `catch`。
- 异常源：
  - `ExplorerQuickAccessHelper.RunShellStaAsync`（`Helpers/ExplorerQuickAccessHelper.cs:227-255`）在 STA 线程动作抛出时 `completion.TrySetException(ex)` **重抛给 await 方**（内层 `TryInvokeFolderVerbCSharp` 等 catch 全部异常，所以只有非常规故障才会走到这里）；
  - `ShowTaskStorageEntryErrorAsync` 内 `await dialog.ShowAsync()`（`OnboardingWindow.TaskFlow.cs:216`）——用户在操作期间关闭 Onboarding 窗口时 `ShowAsync` 可抛（XamlRoot 失效）。
- 人话摘要：异常逃逸 async void → `App.OnUnhandledException`（`e.Handled=true`）→ 进程存活，但该 toggle 操作**静默中断**：回滚 UI（`SetTaskStorageToggleState(toggle, !requestedState)`）不会执行，toggle 停在与真实状态相反的位置，且 finally 已把 IsEnabled 恢复。同文件其他 async void（如 `GlanceWidgetContent` 的 FEXC-05 模式）都有兜底 catch，这两个漏了。
- 触发路径：Onboarding 第 3 步拨动"固定到快速访问/桌面快捷方式"开关 → 底层 Shell 调用抛出非预期异常，或操作过程中窗口被关闭。
- 用户可感知后果：首次安装引导中开关状态与系统真实状态脱节（低频）。
- 建议方向：套用同仓库 FEXC-05 模式（catch + 日志 + 回滚 toggle 状态）。
- 如何验证：代码走查即可；或临时让 `RunShellStaAsync` 抛出验证回滚路径。

### B-7 【P3｜置信度：高（事实）／中（是否算缺陷）】Todo 到期/草稿日期短格式硬编码 "月/日" 顺序，与同屏其他日期顺序不一致

- 文件:行：`src/DeskBox/ViewModels/TodoItemViewModel.cs:515-518`（`MetadataDueDateText`：`$"{local.Month}/{local.Day}"` 与 `$"{local.Month}/{local.Day} {local.Hour:00}:{local.Minute:00}"`）；`src/DeskBox/ViewModels/TodoWidgetViewModel.cs:184-187`（`DraftDueDateText` 同样 `$"{local.Month}/{local.Day} ..."`）。
- 人话摘要：de-DE / fr-FR / ru-RU / pt-BR 等惯用"日.月"的区域里，这个"月/日"词序别扭（美式习惯）。同时同文件 `CreatedText`（`TodoItemViewModel.cs:113`）用 "yyyy/M/d"（年.月.日），同一详情页出现两种月日顺序。应用整体日期风格是固定 ISO 式（刻意不本地化），这两处单独采用了美式短格式。
- 触发路径：Todo 项设到期日（非今天/明天）后查看列表元数据 / 输入区草稿徽标。
- 用户可感知后果：轻微（词序不习惯、风格不一致）。
- 建议方向：统一为全应用的 `yyyy/M/d` 或本地化的 `d/M` 模式（`culture.DateTimeFormat.ShortDatePattern`）。
- 如何验证：语言切 de-DE 后对比。

### B-8 【P3｜置信度：高】QuickCapture 图片搜索额外匹配硬编码英文单词 "Image"

- 文件:行：`src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.ItemSync.cs:282-287`
  ```csharp
  if (item.Type == QuickCaptureItemType.Image)
  {
      return matchesMetadata ||
             "Image".Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
             _localizationService.T("QuickCapture.ImageItem").Contains(...);
  }
  ```
- 人话摘要：图片类型的搜索除了本地化词（`QuickCapture.ImageItem`，各语言各有翻译）外，还硬编码匹配英文 "Image"。结果：zh-CN 用户搜 "image"（英文）能命中图片项，但搜 "Bild"（de）/ "画像"（ja 中该键的译词以外）时行为不对称——其他类型（文本/URL/标签）都没有英文别名通道。硬编码 UI 语言词也违反"所有 UI 文案走 Strings"的仓库约定。
- 触发路径：QuickCapture 搜索框输入 Image（任意语言环境）。
- 用户可感知后果：极轻微（搜索命中范围不一致 + 规范违反）。
- 建议方向：删掉 `"Image".Contains(...)` 分支（本地化键已覆盖），或把英文别名也放进 12 个语言文件。
- 如何验证：语言切 zh-CN，搜索 "image" 与 "图片" 对比命中。

### B-9 【P3｜置信度：高（代码事实）／触发条件极边缘】Glance 在线刷新 `Apply()`：File.Exists 过滤后为空时不清 `CurrentImage`/`StatusText`，且刷新命中旧图时不换新实例

- 文件:行：`src/DeskBox/ViewModels/GlanceWidgetViewModel.cs:645-660`（`RefreshOnlineInBackgroundAsync` 的 `Apply()`：`_images = refreshed.Where(image => File.Exists(image.LocalPath)).ToArray();` 之后——若过滤结果为空，`existingIndex=-1`、`else if (_images.Count > 0)` 不成立 → `CurrentImage` 保留旧实例、`_currentIndex` 过期，随后 `StatusText = string.Empty; IsLoading = false;` 照常执行，**清掉了本应提示的 fallback 状态**。对照同文件 `ReloadImagesAsync` `:560-568` 在 count==0 时正确置 `CurrentImage = null` 并显示 `Glance.Status.OnlineFallback`）。
- 人话摘要：只有当网络刷新返回了非空列表、但每个文件在返回与应用之间全部失效（如磁盘/杀毒瞬时删除）才会走到；此时界面保留一张可能已不存在的背景且无任何状态提示，直到下一次手动/定时轮换自愈（`AdvanceImage` 空 表 分支会重新拉取）。
- 附带观察（非缺陷）：`existingIndex >= 0` 分支不重赋 `CurrentImage` 是刻意避免闪烁；由于 `GlanceImageService` 的下载路径由稳定 Id 决定（`GlanceImageService.cs:544` `$"{candidate.Id}{extension}"`），新旧实例等价——除非服务器对同 Id 改变 Content-Type 导致扩展名变化（.jpg→.webp），届时旧实例的路径会被 `TrimAndSaveCatalogAsync`（`:586-593`）删除，显示断链。同样极边缘。
- 建议方向：`Apply()` 内补 count==0 分支对齐 `ReloadImagesAsync` 行为。
- 如何验证：单测构造 refreshed 全部路径不存在的场景断言 CurrentImage/StatusText。

---

## 本地化一致性专项结果

方法：Python 脚本提取 12 个 JSON 的扁平键集合与每键 `{0}/{1}…` 占位符集合，逐文件与 en-US 对比。

### 键集合差异矩阵

| 文件 | 键数 | 缺失 | 多余 |
|---|---|---|---|
| en-US（基准） | 3091 | — | — |
| zh-CN | 3091 | 0 | 0 |
| zh-TW | 3091 | 0 | 0 |
| ja-JP | 3091 | 0 | 0 |
| de-DE | 3091 | 0 | 0 |
| fr-FR | 3091 | 0 | 0 |
| es-ES | 3091 | 0 | 0 |
| pt-BR | 3091 | 0 | 0 |
| ru-RU | 3091 | 0 | 0 |
| ar-SA | 3091 | 0 | 0 |
| hi-IN | 3091 | 0 | 0 |
| bn-BD | 3091 | 0 | 0 |

### 占位符差异矩阵

对每个键比较 `{0..n}` 占位符的**集合**（数量与编号）：12 个文件、3091 键全部一致，差异 **0**。（顺带验证了顺序问题不存在风险：全部键的占位符按编号连续出现，无 `{1}` 先于 `{0}` 的重排翻译。）

### Format 调用点核对

程序化扫描全部 `.Format("Key", args…)` 调用点的实参数 vs 占位符数：唯一两个多行命中的假阳性（`SettingsWindow.CloudBackup.cs:395`、`SettingsWindow.Maintenance.cs:249`，各 5 实参/5 占位符）人工核对正确。**无数不匹配**（即不存在运行时 FormatException 风险点）。`string.Format(CultureInfo.CurrentCulture, …)` 的区域性数字渲染（ar-SA 阿拉伯数字）为刻意设计。

### 其他本地化观察

- 语言显示名全部用本名（endonym，en-US.json: Language.English="English"、Language.Japanese="日本語"…），一致。
- `WeatherCodeMapper.DescriptionToWmoCode`（`Helpers/WeatherCodeMapper.cs:130-201`）只识别中文+英文天气描述，其他语言经 `MsnDescriptionOrIconToWmoCode`（`:208-234`）的 MSN icon 编号兜底，不会空图标——设计闭环，不列为问题。
- 硬编码中文扫描：仅 AOT smoke 测试文件（`App.AotTodoNotification*Smoke.cs`，条件编译）与 WeatherCodeMapper 的**输入解析**侧，无 UI 输出硬编码中文。

---

## 需要主流程复核的条目（按优先级）

1. **B-1（ar-SA UmAlQura 日期）**：机制已由官方文档确认，但"是否产品有意让阿语显示 Hijri"需要产品决策；若有意，B-1 降级为文档事项，但主日期与日历网格（公历）不一致仍需统一。
2. **B-2（无 RTL）**：需确认是否为"暂不支持 RTL"的有意取舍；若是，建议在设置页/发布说明声明。
3. **B-4（Glance Dispose 竞态）**：路径存在性由代码得出，真实触发频率需运行观察（日志搜 `ObjectDisposedException` + `GlanceWidgetViewModel`）。
4. **B-6（Onboarding async void 逃逸）**：内层 Try* 已吞常规异常，实际逃逸概率低；是否值得修复取决于对首次安装体验的容忍度。
5. **B-3（MarkerGlyph 死三元）**：需查设计意图确认是缺陷还是残留。

## 核实过认为稳健的重点区域（留痕，防重复劳动）

1. **本地化文件完整性**：12×3091 键 + 占位符程序化全等；Format 调用点全部匹配；XAML `StaticResource/ThemeResource` 未解析键全部为 WinUI 内建主题资源（AccentButtonStyle、SolidBackgroundFillColor* 等），无 DeskBox 资源缺失。
2. **架构路径**：`WidgetRegistry`（7 可创建 + Tags/SystemMonitor 占位）→ `WidgetContentFactory`（provider 注册）→ `ContentWidgetWindowFactory` → `ContentWidgetWindow`（File/QuickCapture/Todo/Music/Weather/Search/Glance 全走统一宿主）→ `WidgetManager`（FeatureWidgetHandler/WidgetWindowProvider 注册表）与 `current_architecture.md` 完全一致；`WidgetKind.Productivity` 仅存在于枚举定义（`src/DeskBox.Abstractions/Models/WidgetConfig.cs:278`）与 SettingsService 的迁移清理（`SettingsService.cs:2745-2753`），无活跃创建路径。
3. **事件配对**：`AppWindow.Changed`（ContentWidgetWindow.xaml.cs:1325/1351）、`XamlRoot.Changed`（WidgetWindowBase.Bounds.cs:572/579、DesktopOrganizationPreviewCard.xaml.cs:201-217）、Todo/Weather/Search 内容控件对 App 级事件（TodoWidgetContent.xaml.cs:184-212、WeatherWidgetContent.xaml.cs:94-114、SearchWidgetContent.xaml.cs:61-100）、Glance/Music VM 的服务事件（GlanceWidgetViewModel.cs:90-95/1120-1129、MusicWidgetViewModel.cs:666-682/607-611）——全部先 `-=` 再 `+=` 或对称退订。
4. **交互深度配对（文档坑 #4）**：`BeginWidgetInteraction/EndWidgetInteraction` 各调用点（含 ContentWidgetWindow.Commands.cs:1317-1356 flyout 的 ShowAt 失败回滚、StackPopover 两个 Begin 的两个 End、标题重命名 commit/cancel/异常三路均走 `CompleteTitleRename`）配对完整；看门狗（WidgetManager.ZOrder.cs:1171-1189）在位。
5. **z-order 约束（文档坑 #2/#6）**：`NormalizeIdleWidgetZOrder` 确为 peer-only（WidgetManager.ZOrder.cs:892-898 注释与实现一致，不调 MoveToDesktopBottom）；`MoveToDesktopBottom` 调用点均在桌面回落/ShowDesktop/落床语义路径。
6. **静态 hover 恢复 timer**（WidgetWindowBase.Collapse.cs:66/1649-1901）：WeakReference 目标 + 剪枝 + 空时停表退订，无跨窗口泄漏。
7. **WidgetShellContentHost**（628 行全文精读）：版本护栏、转换失败回滚（CommitPreparedContent catch 恢复 outgoing）、Dispose-once（ConditionalWeakTable）、pending 初始化的取消路径全部闭合。
8. **Weather 刷新状态机**（RefreshAndLayout.cs）：`_refreshRequestVersion` 代际 + pending 合并 + backoff；ISO 时间戳 InvariantCulture 解析（近三提交已修）；`ResolveTodayIndex/ClassifyDayLabel` 按日期而非数组位置。
9. **Todo undo/tombstone**：`SaveAsync`（FilteringAndAppearance.cs:78-109）以 liveIds 过滤 tombstone，`UndoLastActionAsync` 恢复 Items 不会造成同 Id 双写；软删项不再提醒（近期提交）。
10. **QuickCapture `SyncVisibleItems`**（ItemSync.cs:81-166）：重建阈值 + 增量 Move/Insert 的索引簿记正确。
11. **SearchPopupViewModel**（1518 行全文精读）：搜索代际、LoadMore 重入闸（`Interlocked.Exchange`）、可见行元数据任务表（ReferenceEqualityComparer + ContinueWith 清理）、provider 刷新 1s 防抖均正确；`EnrichVisibleResultAsync` 覆盖 ObjectDisposedException。搜索输入 35ms 防抖在 SearchEngineService 上游。
12. **Win10 兼容层**：`WindowsCompatibilityService` 集中门控 Mica/圆角/DWM 属性（OsBuild<22000 回退），WinUI 控件内的 backdrop 使用均经该层；未发现无守卫的 Win11-only 调用。
13. **卫生扫描**：UI 层无 `.Result/.Wait()/GetAwaiter().GetResult()`；无 `GC.Collect`；`Thread.Sleep` 仅 ShellClipboardHelper 开剪贴板重试（5×5ms 上限 25ms）；`int/double TryParse` 无文化敏感用法（均解析内部生成的整数串）。
14. **IValueConverter**（SettingsBindingConverters、Glance/Weather 内嵌、ColorToSharedBrushConverter）：null/异型输入均回退 UnsetValue/默认值，ConvertBack 抛 NotSupportedException 或返回 UnsetValue，符合规范。
15. **模块边界**：`ModuleBoundaryContractTests` 的 21 条源码扫描 ratchet（P/Invoke 硬零、Todo/QuickCapture 设置直写禁令、文件变更域白名单 DeskBox.FileSafety / DeskBox.Core.Persistence / DeskBox.Platform）与当前架构文档描述一致，未见越界样本（静态走查）。

---

### 参考来源（B-1 机制核实）

- [Working with calendars – .NET (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/standard/datetime/working-with-calendars)——ar-SA 默认 Calendar 为 UmAlQuraCalendar；`DateTime.ToString(ar-SA culture)` 输出 Hijri；强制公历需克隆 culture 并设 `DateTimeFormat.Calendar = new GregorianCalendar()`。
- [How can I parse an Arabic Umm Al-Qura date string into a .NET DateTime object (Stack Overflow)](https://stackoverflow.com/questions/32861526/how-can-i-parse-an-arabic-umm-al-qura-date-string-into-a-net-datetime-object)
