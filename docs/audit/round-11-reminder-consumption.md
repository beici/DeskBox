# 第 11 轮修复 A-1 — 待办提醒"先落盘消费、后显示"导致提醒永久/整会话静默

- 级别：P2（置信度高）。来源：第 11 轮从零审查维度 A（A-1），主流程已复核全部证据。
- 状态：**已修复并验证**（方案 A 落地；验证记录见文末）。

## 问题（根因）

`TodoReminderService.CollectWidgetCandidatesAsync`（`src/DeskBox/Services/TodoReminderService.cs:455-498`）在 `store.MutateAsync` 回调里**先持久化**消费标记：

- `:476` `_sessionNotifiedKeys.TryAdd(reminderKey, 0)`（会话去重键）；
- `:483-489` `SnoozeLastNotifiedAt=now; SnoozedUntil=null`（小睡路径）或 `ReminderLastNotifiedAt=now; ReminderDismissedForDueDate=DueDate`（到期路径，永久闸门，见 `ShouldNotifyDue` `:679-683`）。

之后才在 `CheckCoreAsync` `:201` 调 `_notify(...)` 真正显示。显示端存在三个静默落空点（`App.xaml.cs:2884-2924`）：

1. `TryEnqueue` 返回 false（关机/队列停摆）——无人检查返回值；
2. 原生 toast 失败且 `_trayIcon is null`——直接 `return`，无日志无补偿；
3. 落盘成功与显示执行之间进程退出。

后果：**该到期日的提醒永久静默**（`ReminderDismissedForDueDate == DueDate` 跨重启生效），小睡计划也被清空。另一变体：`MutateAsync` 内 `SaveUnsafeAsync` 抛出（瞬时 IO）→ `:476` 已占用的会话键不清除 → **本会话**内该提醒不再尝试。

## 候选方案

| 方案 | 内容 | 评估 |
| --- | --- | --- |
| A（采用） | **显示确认后才消费**：选择阶段只读（gate 内 `LoadAsync` 快照 + 占会话键，不落盘）；`_notify` 改为 `Func<TodoReminderNotification, Task<bool>>` 返回"确实展示成功"；仅成功后按候选落盘标记（带并发编辑守卫）；显示失败/抛异常则释放本轮会话键，下个 tick（30 s）重试 | 根治两类丢失；代价：改 1 个服务 + App 显示方法签名 + 5 个构造点；显示成功但落盘失败时下次启动可能重复提醒一次（被 `MissedReminderGrace` 1 分钟窗口限制到极小）——重复远轻于丢失 |
| B | 保留先落盘，显示失败后回调撤销落盘标记 | 需要新的"撤销"通道跨 App→Service，比 A 多一次磁盘往返与更复杂的状态机；撤销本身也可能失败 |
| C | 只补日志 + 保存失败时清会话键 | 浅修：永久丢失场景（显示端落空）原样保留 |

## 改法（方案 A）

### `TodoReminderService`

1. `_notify` 类型 `Action<TodoReminderNotification>` → `Func<TodoReminderNotification, Task<bool>>`（两个 ctor 同步改）。
2. `TodoReminderCandidate` 私有 record 扩展字段：`ReminderKey`、`TriggerKind`、`DueDateAtSelection`、`SnoozedUntilAtSelection`。
3. `CollectWidgetCandidatesAsync` 改为只读选择：`await store.LoadAsync()`（gate 内一致快照，公共 API `TodoWidgetStore.LoadAsync:139`）→ 复查 `_disposed || !ShouldBeRunning()`（保持既有 `CheckInProgress_DoesNotNotifyAfterDisableOrDisposal` 语义：工厂回调里 Dispose/禁用后不得产出候选）→ 按 `TryGetReminderTrigger` 选择 + 占会话键。**不写任何字段。**
4. `CheckCoreAsync`：候选非空后 `bool shown`：
   - `try { shown = await _notify(BuildNotification(candidates)).ConfigureAwait(false); } catch (Exception ex) { 记日志; shown = false; }`
   - `shown == false` → 逐候选 `_sessionNotifiedKeys.TryRemove(candidate.ReminderKey)` + 记一行日志（下个 tick 自动重试，直至 `MissedReminderGrace` 窗口关闭，与既有"错过不补"设计衔接）。
   - `shown == true` → `await PersistDisplayedRemindersAsync(candidates, now)`。
5. 新增 `PersistDisplayedRemindersAsync`：逐 widget `store.MutateAsync`，带**并发用户编辑守卫**：
   - 到期类：item 按 Id 找到 && `!IsCompleted && !IsDeleted` && `DueDate == DueDateAtSelection` && `ReminderDismissedForDueDate != DueDate` → 写两个标记字段；
   - 小睡类：item 找到 && `!IsCompleted && !IsDeleted` && `SnoozedUntil == SnoozedUntilAtSelection` → 写 `SnoozeLastNotifiedAt`、清 `SnoozedUntil`；
   - 守卫不满足 → 跳过（用户并发编辑/通知按钮动作获胜——激活路由走 store 直写，本守卫天然化解"toast 上点稍后提醒/完成"与本落盘的竞态）；
   - 每个**实际落盘**的 item 触发一次 `PublishStoreChanged`（旧代码每 widget 只广播 lastChangedItem，多条目时漏合并——顺带修正）；
   - 单 widget 异常逐个捕获记日志（会话键保留 → 本会话不重复弹；跨启动的重复面见上）。
6. `CheckNowAsync` 返回值语义不变（本轮选择的候选数）。

### `App.xaml.cs`

7. `ShowTodoReminderNotification` → `ShowTodoReminderNotificationAsync` 返回 `Task<bool>`：
   - UI 线程直行同步核心；非 UI 线程 `TaskCompletionSource<bool>(RunContinuationsAsynchronously)` + `TryEnqueue`，`TryEnqueue` 失败 → 返回 false + 记日志；
   - 核心：原生 toast 成功 → true；托盘成功 → true；`_trayIcon is null` → **false + 记日志**（原来静默 return）；托盘抛异常 → false（已有日志）。
8. `CreateTodoReminderService`（`App.xaml.cs:2408/2417`）两处装配点换新方法组。

### 构造点同步（编译器枚举）

9. `App.AotTodoNotificationActivationSmoke.cs:216`、`App.AotTodoRecurrenceReminderSmoke.cs:200`、`tests/TodoReminderServiceTests.cs:21,565`、`tests/TodoNotificationActivationRouterTests.cs:333`、`tests/SearchResultActionServiceTests.cs:89`。

## 方案复核（第 2 步：遗漏/逻辑/可行性）

- **死锁面**：`_notify` 跨线程取 UI 结果改为异步等待；`DisposeAsync` 只是 `await Task.WhenAll(_initialCheck, _activeCheck)`，调用方均为 await/释放线程，无 `.Result` 阻塞点（实施时逐一确认 App 退役路径与 smoke 驱动方式）。TCS 用 `RunContinuationsAsynchronously`，不在 UI 线程内联跑续体。
- **重入**：`_isChecking` 防重入已存在；检查总时长变长（等 UI marshal）无碍。
- **toast 按钮竞态**：激活路由（完成/稍后）直写 store；选择→落盘之间的并发编辑被守卫拦下，用户动作优先。
- **disabled/disposed 时序**：与既有用例 `CheckInProgress_DoesNotNotifyAfterDisableOrDisposal` 的注入点（store 工厂回调）对齐：选择在 LoadAsync 之后、产出候选之前复查。
- **空候选/守卫全跳过**：`MutateAsync` 返回 false 不落盘；无副作用。
- **A/B 区分力**：测试接缝为 `notify` 注入（返回 false/true）+ 真实文件 store，能同时证明旧红新绿。
- 遗留已知项不动：`MissedReminderGrace` 1 分钟（台账既有，产品决策）。

## 验证（已全部通过）

- [x] **新增 3 条用例**（`tests/DeskBox.Tests/TodoReminderServiceTests.cs`）：
  - `CheckNowAsync_DoesNotPersistMarksWhenNotificationIsNotShown`：notify 返回 false → 落盘无任何标记，同窗口第二次 CheckNow 重新选择并再次通知（notifications.Count=2）；
  - `CheckNowAsync_DoesNotStampMarksWhenUserCompletesItemWhileNotificationShows`：notify 回调内完成条目 → 守卫生效，`ReminderLastNotifiedAt/ReminderDismissedForDueDate` 均为 null；
  - `CheckNowAsync_KeepsUserResnoozeWhenSnoozeNotificationShows`：notify 回调内重新小睡 → 新 `SnoozedUntil` 不被清空。
- [x] **A/B 红灯**：把接线临时改回"先消费后显示"（`PersistDisplayedRemindersAsync` 提前 + 忽略 `_notify` 结果）后，前两条用例红——`Assert.Null() Failure: ... has a value`（标记被落盘）与 `Expected: 1, Actual: 0`（第二次扫描不重试）；第三条按预期仍绿（新顺序守卫契约，旧顺序用户写后到同样获胜）。恢复后全绿。
- [x] **定向套件**：`TodoReminderServiceTests` + `TodoNotificationActivationRouterTests` + `SearchResultActionTests` + `GlanceCalendarAndImageServiceTests` + `GlanceWidgetPhotoControlsTests` + `CompactPresentationRefreshTests` + `AotStage5B4C3AContractTests` + `AotStage5B4B2C1ContractTests` + `TodoWidgetStoreGateTests` + `TodoRecurrenceServiceTests` = **176/176 通过**（0 失败 0 跳过）。`AotStage5B4C3AContractTests.NotificationBoundary`（smoke 仅回调、runner 日志文案）不受影响。
- [x] **构建**：`dotnet build src/DeskBox/DeskBox.csproj -c Debug -p:Platform=x64` 0 错误；`DeskBox.dll` 写入时间（02:45:26）晚于构建开始（02:44:06）。
- [x] **真机端到端**（`E:\DeskBox-dev\app` 部署新构建，注入不可见 Todo widget + 70 s 后到期条目，现场已备份/还原）：
  - 启动流水 40 步 0 失败（首轮）、39 步 0 失败（注入轮与还原轮）；
  - 到期条目在 02:59:14.867 经 tray fallback 显示（原生 toast 不可用时的既有回退链），日志 `[TodoReminder] Tray notification fallback shown count=1`；
  - 注入条目的 `reminderLastNotifiedAt = 2026-10-24T02:59:14.860046+08:00`、`reminderDismissedForDueDate == dueDate`——**消费标记与显示同一 tick、且在显示确认之后落盘**；
  - 还原现场后重启，流水 39 步 0 失败，运行实例 `E:\DeskBox-dev\app\DeskBox.exe`（pid 7256）。
