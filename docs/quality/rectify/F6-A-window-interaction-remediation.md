# F6 批次 A 整改报告（DEF-014 / DEF-015 / DEF-017 · 窗口交互安全网）

> 整改日期：2026-09-01 ｜ 基线：`4bc81af`（独立复核整改批次之后）｜ 分支：`wip/fix-bug` ｜ 方式：Linux 静态迭代（无 WinUI3 编译环境），**本批未经编译验证，Windows 侧门禁见 `pending-windows-gate.md`**。
> DEF-016（QuickCapture 失活回落门控）位于死宿主 `QuickCaptureWidgetWindow*` 内，按任务约定随 DEF-027 整体退役，**本批跳过不修**。

## 这批修了什么（一句话版）

| 编号 | 问题 | 通俗解释 | 修法 |
|---|---|---|---|
| DEF-014 | 交互深度泄漏看门狗缺失 | 格子的「正在交互」计数如果因为某个 bug 没有清零，托盘唤起后的自动回落会被永久卡死——文档里承诺的「10 秒强制清零保险丝」在源码里根本不存在 | 把保险丝造出来：交互开始时装上一个 10 秒定时器，到期发现「计数还大于 0 且 DeskBox 没有任何前台窗口」就强制清零并恢复 |
| DEF-015 | Content 宿主激活失败无日志 | 按 F7/托盘唤起格子时，Windows 可能拒绝把窗口带到前台（前台锁、管理员权限窗口挡着），失败本来是完全无声的，出了问题没法排查 | 补上失败日志（返回值检查），照抄 QuickCapture 宿主的成熟写法 |
| DEF-017 | 显示桌面自愈 hook 注册失败无检测无重试 | 「显示桌面后格子不显示」的自动修复依赖两个系统事件钩子；如果钩子注册失败（系统资源紧张等），整个自愈就静默失效，日志里只有一行不起眼的 `0x0` | 注册失败立刻记警告日志 + 每 5 秒自动重试，直到两个钩子都注册成功；「已启动」的判定从「第一个钩子成功」改为「两个都成功」 |

## 逐项根因与修复

### DEF-014（WIN-01，P2）交互深度泄漏看门狗

**为什么会出问题**：`WidgetSessionManager` 用一个计数器（`_interactionDepth`）记录「有多少个交互在进行」（拖拽、重命名、菜单、弹层各占一层，可嵌套）。每次开始交互 `Begin` +1，结束 `End` -1。只要计数 > 0，`TryRestoreRaisedWidgetsAfterInteraction`（`WidgetManager.ZOrder.cs:516`）每 tick 都直接跳过——格子永远不回落。而 [重要勿删] 手册 §8 明确记载有一个保险丝：`RunInteractionLeakWatchdog`（深度>0 且 DeskBox 无前台持续 10 秒 → 强制清零）+ `ForceResetInteractions`，但这两个在源码里从未存在（全仓 grep 为零），属于**文档承诺的安全网缺失**。

**修复**（三个文件，各司其职）：

| 文件 | 改动 |
|---|---|
| `src/DeskBox/Services/WidgetSessionManager.cs` | 新增 `ForceResetInteractions(string reason)`：把计数清零、状态回退到「泄漏前记录的状态」（`_stateBeforeInteraction`，与 `EndInteraction` 的回落语义一致），带日志。正常配对的 Begin/End 完全不受影响；计数本来是 0 且状态不是 InteractionActive 时是纯空操作 |
| `src/DeskBox/Services/WidgetManager.ZOrder.cs` | 新增看门狗：`StartInteractionLeakWatchdog`（10 秒 DispatcherQueueTimer，模式对齐同文件 `_trayLayerRestoreTimer`：先 `Tick -=` 再 `Tick +=` 防重复订阅）；`InteractionLeakWatchdog_Tick` 检查「深度仍 > 0」+「前台窗口存在且不是 DeskBox 的」→ `ForceResetInteractions` + 停表 + 记日志（带排障手册指定的 `[TrayBatch] Interaction watchdog` 标记）+ 恢复回落 + 排队空闲层级整理；`StopInteractionLeakWatchdog` 在深度归零时停表退订 |
| `src/DeskBox/Services/WidgetManager.cs` | `BeginWidgetInteraction` 在深度 0→1 转变时装表；`EndWidgetInteraction` 在深度归零时停表（放在既有 Restore 之前，互不影响） |

**为什么不会误伤正常使用**：真实交互（拖格子、开菜单、弹窗）期间前台必然是 DeskBox 自己的窗口，看门狗 tick 到了也只是「再等 10 秒」；只有「计数卡住 + 用户已经离开 DeskBox 超过 10 秒」这个组合才可能触发——这正是手册定义的泄漏判据，不误伤。看门狗只在 UI 线程（dispatcher Tick）运行，与既有会话状态机的线程模型一致。

### DEF-015（WIN-02，P2）Content 宿主激活失败诊断

**为什么会出问题**：`ContentWidgetWindow.ActivateRaisedFromTrayBatch` 直接丢弃了 `SetForegroundWindow` 的返回值。QuickCapture 宿主（`QuickCaptureWidgetWindow.xaml.cs:520-524`）对同一调用有完整的失败检查和日志——两个宿主不对齐，[重要勿删] 手册坑 #3 指定的「复盘先看这条」诊断锚点在主力宿主上失效。

**修复**：`ContentWidgetWindow.xaml.cs` `ActivateRaisedFromTrayBatch` 内补返回值检查，失败记 `[ZOrder] Content ActivateRaisedFromTrayBatch: SetForegroundWindow FAILED hwnd=0x... (raised-state release will rely on click detection)`——格式、信息量与 QuickCapture 宿主的既有日志同构（坑 #3 类型）。纯诊断增强，不改变任何行为。

### DEF-017（WIN-04，P2）显示桌面自愈 hook 注册失败检测 + 重试

**为什么会出问题**：`WidgetShowDesktopSelfHealService.StartCore` 把两枚 `SetWinEventHook` 的返回值直接赋字段，返回 `IntPtr.Zero`（失败）时只有信息级日志里的 `0x0`，无失败字样、无重试。更隐蔽的是：幂等守卫「`_minimizeHook != IntPtr.Zero` 即已启动」意味着 minimize 成功而 foreground 失败时，补注册会被永远挡住——半个自愈链路静默失效，而这正是 DEF-001 唯一的回归面。

**修复**（单文件 `WidgetShowDesktopSelfHealService.cs`）：

| 改动点 | 内容 |
|---|---|
| 幂等守卫 | 新增 `IsFullyRegistered`（两个钩子都非零才算已启动），`Start()`/`StartCore()` 统一用它判定；`StartCore` 改为「哪个钩子缺就补哪个」，天然支持增量补注册 |
| 失败可见 | 任一钩子注册失败即记 `App.Log`（非 Verbose）：`[ShowDesktop] Self-heal hook registration FAILED minimizeHook=0x.. foregroundHook=0x..; will retry`；双钩子齐了才打既有的 `watcher started` 日志并停掉重试定时器 |
| 自动重试 | 新增 5 秒间隔 `DispatcherQueueTimer`（`HookRetryDelay` 常量），失败即启动，`HookRetryTimer_Tick` 自停表后重跑 `StartCore`（内部先查 `IsFullyRegistered`，幂等）；全部在 dispatcher 线程执行，与 WinEvent 回调线程模型一致 |
| 资源对称 | `Dispose` 中重试定时器 Stop + Tick 退订 + 置 null，与 debounce 定时器的既有清理并列 |

## 测试

新增 `tests/DeskBox.Tests/WindowInteractionSafetyNetContractTests.cs`（4 用例，x64 自动发现；WidgetManager/宿主窗口无法无头实例化，按仓库既有模式用源码契约锁定接线）：

| 用例 | 锁定 |
|---|---|
| `Watchdog_ForceReset_ExistsOnSessionManager` | 会话管理器公开强制清零方法在位 |
| `Watchdog_BeginEndInteraction_PairWithWatchdogLifecycle` | Begin 装 / End 停的接线位置 + 10 秒判据 + 指定日志标记 + 强制清零经会话管理器执行 |
| `SelfHealHook_PartialRegistration_FailsClosedAndRetries` | 幂等守卫要求双钩子、失败日志非静默、重试定时器存在、Dispose 对称清理 |
| `ContentHost_ActivationFailure_IsLoggedLikeQuickCaptureHost` | Content 宿主返回值检查 + 失败日志锚点在位 |

`tests/DeskBox.Tests/WidgetSessionManagerTests.cs` 追加 3 个行为用例：`ForceReset_FromDesktopInteraction_ReturnsToPreInteractionState`（含重置后杂散 End 不产生负深度）、`ForceReset_FromRaisedSession_RestoresRaisedState`、`ForceReset_WithoutLeak_IsANoOp`。

## Linux 静态门禁结果（scripts/quality/static_gate.py，本批新增固化脚本）

| 检查 | 结果 |
|---|---|
| 12 语言键/占位符一致性 | PASS（2555 键 × 12 语言，本批零新增键） |
| async void 计数 | 263 = 基线 263，零新增 |
| 剪贴板写配对 | 12 写全部配对（本批不涉及剪贴板） |
| 同步等待/空 catch/反射 | 134/223/7，全部等于基线，零新增 |
| 契约断言重放 | 5191 命中 / 43 失联（失联均为基线内已知 XAML/安装器类条目，新增 0） |

实现者自审清单：UI 线程亲和（Tick 均在 dispatcher 线程；跨线程恢复路径走既有 `HasThreadAccess` 自愈重投）✓ ｜ 无新增 async void ✓ ｜ 事件订阅/退订对称（两枚新定时器均成对退订）✓ ｜ Nullable 守卫（`_hookRetryTimer?.`、`_interactionLeakWatchdogTimer is not null`）✓ ｜ 无锁/集合并发面变化（全部收敛 dispatcher 线程）✓ ｜ AOT 反射零新增 ✓ ｜ 12 语言键零新增 ✓ ｜ 不触碰 z-order 生命周期既有约定（看门狗只在手册定义的泄漏判据下触发，正常路径逐行为等价）✓

## 等价性论证

- DEF-014：正常路径（Begin/End 严格配对）下看门狗「装上→10 秒后 tick→发现 DeskBox 在前台→重新等」循环，不产生任何状态变化与日志（除 Verbose 外无输出）；泄漏路径才触发清零——这正是文档承诺但缺失的行为，属**恢复性新增**而非行为变更。`ForceResetInteractions` 的状态回退语义与既有 `EndInteraction` 的回落目标（`_stateBeforeInteraction`）完全一致。
- DEF-015：仅增加返回值读取与条件日志，调用序列（HoldTemporaryTopMost → Activate → SetForegroundWindow → Focus → OnActivated）逐项不变。
- DEF-017：双钩子注册成功路径的行为与旧实现完全一致（同日志文本、同 flags、同注册线程）；变化仅在失败分支（旧：静默；新：警告 + 重试）与半注册场景（旧：永久卡半态；新：补注册到全量）。

## 回滚方式

按文件单项回滚即可：DEF-014 = `WidgetSessionManager.cs` + `WidgetManager.cs`（Begin/End 段）+ `WidgetManager.ZOrder.cs`（看门狗方法块）+ `WidgetSessionManagerTests.cs`（3 用例）+ 契约测试前 2 用例；DEF-015 = `ContentWidgetWindow.xaml.cs` 单 hunk + 契约测试第 4 用例；DEF-017 = `WidgetShowDesktopSelfHealService.cs` + 契约测试第 3 用例。`git checkout 4bc81af -- <files>` 可整体还原。
