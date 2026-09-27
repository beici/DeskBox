# R9 整改方案（round-09 双路审查闭环批次）

> 输入：`docs/quality/rounds/round-09/全量代码缺陷审查总报告.md`（DEF-110~114 + DEF-078 新位点）。
> 原则：最小侵入、零架构变更、不触碰 z-order 核心约定与 [重要勿删] 手册；审查（静态）与修复（构建+回归门禁）分离。
> 基线：`wip/fix-bug` @ `097b04a3`，工作树仅含 round-09 文档产出。

## 1. 逐项处置

### W1-1 ｜ DEF-110 ｜ TodoReminderService 提醒去重集合跨线程无同步（P3）

- **现状**：`_sessionNotifiedKeys` 为无锁 `HashSet<string>`（`TodoReminderService.cs:26`）；`Clear` 三处在 UI 线程（`:91,119,133`），`Add` 在 `store.MutateAsync` 回调（`:439`）——`TodoWidgetStore.MutateAsync` 内部 `await _gate.WaitAsync().ConfigureAwait(false)`（`TodoWidgetStore.cs:177-180`），回调运行于线程池延续。
- **修法**：字段类型改为 `ConcurrentDictionary<string, byte>`（`new(StringComparer.Ordinal)`，构造重载兼容）；`Add(reminderKey)` → `TryAdd(reminderKey, 0)`；三处 `Clear()` 不变（`ConcurrentDictionary.Clear()` 语义等价）；文件头补 `using System.Collections.Concurrent;`（与 `TodoWidgetStore.cs:1` 惯例一致）；字段处加一行注释说明双线程归属约束。提醒判定/通知语义零变化。
- **验证**：现有 TodoReminder 相关回归用例全绿；无新增用例必要（竞态窗口毫秒级，行为用例无法确定性复现；类型替换后并发安全性由集合契约保证）。

### W1-2 ｜ DEF-113 ｜ 更新器安装成功路径重启失败无信号（P3）

- **现状**：`DeskBox.Updater/Program.cs:58-61` 成功路径 `_ = RestartApp(options.AppPath);` 丢弃布尔；失败路径有 `RestartAfterIncompleteUpdate(options, GetIncompleteUpdateOutcome(exitCode))`，手动渠道 `:45` 检查布尔。
- **修法**：改为 `if (!RestartApp(options.AppPath)) { RestartAfterIncompleteUpdate(options, "restart-failed"); }`，附 `// DEF-113:` 注释。`restart-failed` 为自由字符串 outcome：主程序 `ShowUpdateInstallResultDialogAsync`（`SettingsWindow.StorageAndUpdates.cs:568-583`）对非 `cancelled`/`path-mismatch` 的取值一律落「安装失败」标题+正文+手动下载兜底，无需新增本地化键。`RestartAfterIncompleteUpdate` 内部再检 AppPath 存在后二次尝试（携带 outcome），双失败则留更新器日志——与既有失败路径语义完全对称。**残余语义说明（审查建议 2）**：若二次尝试成功，主程序仍会弹「安装失败」对话框——与既有失败路径一致的保守行为（宁可误报也不漏报），执行者与用户均不应据弹窗断定安装本身失败。
- **验证**：无更新器专项自动化测试（进程级工具）；以构建 + 源码走查 + `GetIncompleteUpdateOutcome` 契约不受影响（仅新增独立字符串，不改既有三个取值）为门禁。

### W1-3 ｜ DEF-078 家族新位点 ｜ 随记详情打开入口 async void 纪律收口（P3，并入 DEF-078）

> **审查修订（完善性审查 NO-GO 项 1，已按指令改写）**：本项定性由「磁盘失败静默无反应」更正为「async void 纪律对齐 + 防御纵深收口」。

- **现状（更正后）**：`QuickCaptureSurfaceContent.xaml.cs:1221` `ItemsList_ItemClick`（async void，await 位于 `:1234`）与 `:2298` 上下文菜单 Edit 的 `async (_, _)` lambda，两处调用 `OpenDetailAfterSavingAsync(item)` 均无外层守卫。**磁盘写失败路径并非静默**：`SaveDetailAsync` 自带 `catch (Exception)`（`:1566-1575`，`App.Log` + `RaiseFeedback("quick-detail-save-error")` + 返回 false，commit `618ccba5` 起）。真实残余逃逸面：①`OpenDetail` 同步 UI 段（`IsDetailSelected`/`ApplyResponsiveLayout`/`RefreshDetailPresentation`）异常；②`FlushPendingDetailSaveAsync` → `SaveDetailAsync` 中位于 try 之前的 `_detailSaveGate.WaitAsync()` 异常（如 Dispose 竞态 ODE）。异常直穿 async void → 全局兜底仅记日志、无操作级反馈。
- **修法**：两处调用点的 `await OpenDetailAfterSavingAsync(item);` 各包 try/catch，catch 内 `App.Log($"[WidgetSurface] Quick Capture detail open failed id={WidgetId}: {ex}")` + `RaiseFeedback(T("Common.OperationFailedRetry"), WidgetFeedbackSeverity.Error, "quick-detail-open-error")` —— 逐字对齐 `Root_Drop`（:2876-2883）既有模式；`Common.OperationFailedRetry` 为既有键（12 语言齐全，审查已逐一 grep 实证）。其余逻辑（修饰键短路、`flyout.Hide()`、`BeginDetailEditing`、`await Task.CompletedTask;`）不动。
- **验证**：QuickCapture 面回归用例全绿（`R8AbWave12RemediationTests` 对该文件有源码断言但不落在点击 handler 区域，审查核验无需同步更新）。

### W2-1 ｜ DEF-111 ｜ LocalizationService.cs 文件头四重 BOM（P3）

- **现状**：`head -c 12` = `EF BB BF ×4` 后才是 `using`（od 字节级实证）；全仓源文件 BOM 惯例混合（`FileSurfaceContent.*.cs`、`MusicWidgetContent.xaml.cs` 等带单 BOM，多数文件无 BOM）。
- **修法**：去除前 3 个多余 BOM，保留 1 个——与仓库带 BOM 文件一致，兼容 MSBuild/Roslyn 与字节级工具链。**零内容变更**（仅删 9 个字节）。
- **验证**：修后 `head -c 6` = `EF BB BF 75 73 69`（单 BOM + `usi`）；构建通过；12 语言 parity 校验不受影响（该文件为服务代码非 JSON）。

### W2-2 ｜ DEF-112 ｜ SearchEngineService 两个死方法（P3）

- **现状**：`GetRecentNotesAsync`（:400-440）、`GetUpcomingTodosAsync`（:742-791）全仓仅声明处命中（grep 实证，src+tests）；内部 `new QuickCaptureStore()` / `new TodoWidgetStore(widget.Id)` 直连 store。
- **修法**：整段删除两方法。**连带核查结论**：`TruncateText(text, maxLength)`（:848）仍被活代码 `:580` 使用，不删；本地化键 `Search.Todo.Due` 仍被活代码 `:639` 使用，不删（键集 2896 不变、12 语言 parity 不动）。`SearchRecommendationItem.QuickCaptureItemId/TodoWidgetId/TodoItemId` 为模型属性，保留（不在本批扩scope）。
- **验证**：构建 0 错误 + 全量回归；grep 复验两方法名零残留。

### W3-1 ｜ DEF-114 ｜ 云备份「运行中跳过」测试时间窗弱断言（P3）

- **现状**：`CloudBackupTransportTests.cs:297-307`（Fact 声明起于 :281）以 `Task.Delay(50)` + `Assert.Single(transport.Files)` 断言无第二次运行；`CloudBackupService.RunScheduledIfDueAsync` 的契约是 `_gate.WaitAsync(0)` **跳过而非排队**（CloudBackupService.cs:122-128）——若回归改为排队，第二次运行在门释放后启动，50ms 内未完成上传则假通过（漏报方向）。
- **修法**：把断言对象从「上传完成数」改为「第二次上传**启动**信号」：`UploadHook` 内 `Interlocked.Increment` 计数，第二次进入时 `secondUploadStarted.TrySetResult()`；释放并 `await first` 后以 `await Assert.ThrowsAsync<TimeoutException>(() => secondUploadStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(250)))` 断言「有界窗口内无第二次启动」，再保留 `Assert.Single(transport.Files)`。启动是 hook 首语句，与上传时长无关——错误排队会在门释放后立即触发信号，窗口只需覆盖线程调度；比完成数时间窗在断言意义上强一个量级（残余为任意负向断言共有的调度延迟边界，与仓库既有 `ElevatedFileLauncherTests` 200ms 负向窗同水位）。测试净增 ≤200ms。
- **验证**：该用例 + 全量回归全绿；连续 3 次重跑该用例稳定（防新引入时序敏感）。

## 2. 不进本批（延后维持，与总报告 §6 一致）

- DEF-070 残余维护路径锁外写点、DEF-101 广播隔离泛化（待 SafeBroadcast 立项）、DEF-096 批量边距语义（需 GUI 对照）、443 孤儿键清理（需先排除反射/动态拼接面）、EXC-06 DCL volatile 族泛化、观察项 18 条。

## 3. 门禁

1. 停止仓库路径下运行中的 DeskBox.exe → Debug x64 构建 0 错误；
2. `dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64` 全量回归全绿（基线 4295/4295）；
3. `python scripts/quality/static_gate.py` 留档 PASS（审查核验本批不触 async void/同步等待/剪贴板/契约重放基线，运行属零成本收口；审查建议 3）；
4. 启动规范 Debug 实例并核验 PID 与路径；
5. 台账/TODO 清单收口更新（DEF-110~114 → ✅ 已修复，DEF-078 记部分修复；I4/I5 状态更新）。

## 4. 风险与回滚

- 全部改动 ≤6 文件、每处 ≤15 行；无签名/序列化/ABI 面变更；`packages.lock.json` 无涉。
- 回滚方式：单 commit 粒度 revert；无数据迁移、无行为语义变化（除更新器新增 outcome 字符串与随记详情打开失败反馈——均为纯增量提示面）。
