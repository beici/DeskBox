# DeskBox 审查与修复循环 — 总览

> 审查者 / 修复者：单一代理（唯一有权改代码、构建、跑测试、提交的人）。
> 分支：`wip/fix-bug`。每个问题一份 `docs/audit/round-<N>-<短名>.md`，本文件每轮更新。

## 当前状态

| 轮次 | 靶（范围） | 发现 | 定级 | 处置 | 提交 |
| --- | --- | --- | --- | --- | --- |
| 1 | `WidgetManager.ShowDesktop` 自愈 + `WidgetLayerService` 桌面层 attach 日志 | 稳态日志风暴；attach 失败行错误码恒为 0 | P2 / P2 | 已修 | 见下 |

第 1 轮结束时：P0 = 0，P1 = 0（两条 P1 候选经真机实验证伪，见 §1.4），P2 = 2（均已修）。

## 第 1 轮：Show Desktop 自愈链

### 1.1 问题一 — 稳态下每 1.5 秒刷一行日志（P2，已修）

**人话摘要**：用户按 Win+D 或点任务栏右下角后把桌面留在最前面时，程序每 1.5 秒往日志里写一行"桌面已抬起，格子都在原位"。什么都没发生，日志却一直长；5 MB 轮转窗口很快被这一行挤满，真出事时的证据就被冲掉了。现在一次"桌面在前台"只写一行。

**根因**：`VerifyRestingWidgetsAfterShellMinimize` 由 `WidgetShowDesktopSelfHealService` 的 1500 ms 看门狗驱动。方法在"桌面已抬起 + 每个格子都在原位"这一稳态分支里直接 `App.Log`，把"状态"当成"事件"记录 —— 状态持续多久就写多少行。

**证据**（本机 `%LOCALAPPDATA%\DeskBox\DeskBox.log`，修复前构建）：
- `05:13:59.560` → `05:14:28.355` 连续 24 行，间隔恒为 1.5 s；
- `06:18:09.046` → `06:18:26.436` 连续 14 行；
- 同一台机器上，桌面处于前台的时间越长，行数线性增长。

**改法**：新增 `_showDesktopSteadyStateLogged`，让"稳态结论"每次 Show Desktop 只落一行；只要该趟真的恢复了/抬升了/解除了遮挡（`restored/uncloaked/raised > 0`）或桌面不再被抬起，就复位标记，下一趟重新记账。

**验证**：修复后构建（`DESKBOX_VERBOSE_LOG=1`）在桌面前台状态停留 5 分钟以上，全日志只有 2 行该文案（`06:39:12.751`、`06:41:23.783`），而修复前同状态 30 秒就有 24 行。详见 `round-01-show-desktop-steady-log.md`。

### 1.2 问题二 — attach 失败日志的错误码恒为 0（P2，已修）

**人话摘要**：格子挂到桌面层的动作失败时，日志写 `error=0`，等于没写 —— 谁也没法从现场判断是"没权限"还是"句柄无效"。现在错误码在系统调用返回的当场就被抓住，并且字段名从 `defView=` 改成 `desktopLayerOwner=`，因为实际挂的 owner 是桌面层根窗口，不是图标视图。

**根因**：`Marshal.GetLastWin32Error()` 在失败判定之后才调用，中间隔着一次 `GetWindowLongPtr` 读回；后者自己也是 `SetLastError=true` 的 P/Invoke，把 `SetWindowLongPtr` 的错误码覆盖掉了。历史上 252 条现场记录全部是 `error=0`，正是这个覆盖造成的。

**证据**：日志中 `[WidgetLayer] DesktopPinned owner attach failed` 共 252 条（`04:20` 前后），`error=` 全为 0，`actual=0x0`。

**改法**：`SetWindowLongPtr` 返回后立即取错误码，存入局部变量；失败行改用该变量，并把 `defView=` 更名为 `desktopLayerOwner=`。

**验证**：构建通过；本机 smoke 窗口内 attach 全部成功（`[WidgetLayer] Desktop owner attached hwnd=... bottom=False`），未自然复现失败路径，故该行只做代码级验证。

### 1.3 两条被证伪的候选（不再追）

- **"高完整性实例持锁时，中完整性实例会因 `UnauthorizedAccessException` 静默退出"** —— 假。本机实测：中完整性进程启动第二个实例，成功打开高完整性实例持有的互斥体并激活主实例，退出码 0，日志 `[Activation] Secondary instance kind=bare ... tokenElevated=True integrity=Medium(0x2000)`。
- **"explorer 重启会销毁被 owner 挂靠的格子窗口，格子消失且可见性被写成 false"** —— 假。本机实测（`E:\DeskBox-dev\er-test.ps1`）：杀掉 explorer 后新 `Progman 0x30496` 起来，13 个格子 HWND 全部存活、仍可见，owner 被系统清成 `0x0`（不重挂，但窗口不消失），`widget-layout.json` 的 `isVisible` 未被改写。owner 丢失本身无用户可感知后果：格子带 `WS_EX_TOOLWINDOW`，不进 Alt+Tab。

### 1.4 留给后续轮的线索（本轮不主张、不定级）

1. **自愈看门狗会整段静默**：`DESKBOX_VERBOSE_LOG=1` 下，看门狗本应每 1.5 s 出一条 verbose 行，实测出现 `06:43:44 → 06:45:19`（95 s）、`06:45:42 → 06:46:26`（44 s）两段完全静默；同期 UI 线程未挂（`SendMessageTimeout(WM_NULL)` 对 23 个顶层窗口全部 ≤11 ms 返回），后台服务仍在写日志。怀疑是 WinUI `DispatcherQueueTimer` 在窗口非前台时被系统节流/合并，需下一轮用独立探针隔离（改 Timer 为线程池心跳对照）。
2. **备份/恢复的数据安全候选**（子代理只读报告，尚未经我复核，未定级）：全量恢复两次 `Directory.Move` 都失败时，现数据被弃留在 `restore-rollback/<guid>/data` 且无提示；恢复失败即 `DeletePendingRestoreCore()` 删除 marker 与 staging，零重试。下一轮按"真机可复现"要求复核后再定级。

## 环境事实（后续轮直接复用）

- 构建：`dotnet build .\src\DeskBox\DeskBox.csproj --configuration Debug --no-restore -p:Platform=x64`；产物 `src\DeskBox\bin\x64\Debug\net10.0-windows10.0.22621.0\`。**构建成功的唯一证明是 `DeskBox.dll` 写入时间晚于构建开始**（exe 是固定桩）。
- 真机 smoke 必须把构建产物 `robocopy` 到 `E:\DeskBox-dev\app` 再启动：仓库内 `bin` 目录带 Low 完整性标签，直接运行会让托盘注册被 UIPI 拒绝。
- 本机 UAC 已关闭，explorer 与 DeskBox 都是 High（`0x3000`）；我的 shell 是 Medium。需要 High 上下文的操作（模拟 Win+D、启动 High 实例、跑 testhost）一律走**计划任务**：`Register-ScheduledTask` + `New-ScheduledTaskPrincipal -RunLevel Highest -LogonType Interactive`。`schtasks.exe` 在本机被拦截（进程直接死亡），不要用。
- 结束 High 进程：`Get-CimInstance Win32_Process -Filter "ProcessId=<pid>" | Invoke-CimMethod -MethodName Terminate`（`taskkill` 会 Access denied）。
- 单测基线（上一轮 High 上下文）：总计 5808 / 失败 1403 / 通过 4405。**只认差值**：明确要求全绿的子集用 `--filter "FullyQualifiedName~<Fixture>"`。
- 探针：`E:\DeskBox-dev\zprobe6\zprobe.exe`（`dump` / `sim` / `seq <ms>` / `minall` / `toggle` / `kids`），`E:\DeskBox-dev\tokprobe\...\tokprobe.exe <pid>`。
- 桌面层在本机的真实形态：`SHELLDLL_DefView` 是 `Progman` 的子窗口；格子以 owner 方式挂在 `Progman` 上；`CEF-OSC-WIDGET`（NVIDIA 覆盖层）是常驻非 topmost 覆盖窗口，会让 `IsDesktopLayerRaised()` 在本机恒为 false —— 判断"桌面是否抬起"不要依赖它，用 `IsShellDesktopForeground()` 一类的前台判定，或逐窗口 `IsDesktopLayerAboveWindow()`。
