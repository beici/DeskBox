# 第 1 轮 · 显示桌面自愈巡检把「稳态」当成「事件」写日志

- 严重度：P2（不影响功能，影响唯一诊断通道 + 每秒级磁盘写入）
- 区域：显示桌面 / 桌面层 z 序（提交 `5e0449aa` 引入）
- 状态：已修复（提交见 SUMMARY.md）

## 人话摘要

显示桌面（Win+D）状态下，DeskBox 每 1.5 秒做一次自愈巡检，巡检发现"格子都在正确位置、不需要动"时，会往日志写一行
`[ShowDesktop] Desktop raised, widgets already in place`。问题是这一行在**状态没变的每一次巡检**都会写：
用户按 Win+D 后离开电脑、或者只是把桌面留在前台，日志就以约 40 行/分钟的速度增长，大约 26 小时就能把 5 MB 的轮转窗口填满，
把真正有用的自愈/失败记录冲掉。改成"进入这个状态时记一次，离开后允许再记一次"。

## 用户可感知的后果

1. 日志是 DeskBox 唯一的诊断通道（`%LOCALAPPDATA%\DeskBox\DeskBox.log`，5 MB 轮转）。桌面留在前台期间日志被稳态行淹没，用户报问题时的历史记录被挤掉。
2. 每 1.5 秒一次字符串格式化和文件追加（含 flush），是持续的后台写盘。

## 复现（真机，可重复）

1. 启动 DeskBox（High 实例），确保 `keepWidgetsVisibleOnShowDesktop=true`（默认）。
2. 用 `E:\DeskBox-dev\zprobe6\zprobe.exe seq 5000` 注入一次 Win+D 往返，或在桌面空白处点一下让桌面成为前台窗口，然后**不要碰其它窗口**。
3. `Select-String -Path $env:LOCALAPPDATA\DeskBox\DeskBox.log -Pattern 'Desktop raised, widgets already in place'`。

实测（2026-10-23）：

```
[05:14:05.681] [ShowDesktop] Desktop raised, widgets already in place reason=watchdog
[05:14:07.187] ...
[06:18:09.829] ...
[06:18:26.436] ...   ← 共 10 条 / 14 秒，间隔 1.506s ± 0.01s
```

同一次采样里，`zprobe.exe sim` 打印每个可见格子的 `walkSteps=446..458`，即每次巡检要做
13 × ~450 ≈ **5850 次 `GetWindow`** 的全量 z 序比较（这是巡检的成本，见「未采纳的观察」）。

## 根因

`src/DeskBox/Services/WidgetManager.ShowDesktop.cs:242-246`

```csharp
else if (desktopRaised)
{
    App.Log($"[ShowDesktop] Desktop raised, widgets already in place reason={reason}");
}
```

这个分支在"没有做任何修复动作"时执行，而 `VerifyRestingWidgetsAfterShellMinimize` 由
`WidgetShowDesktopSelfHealService` 的 1500 ms 周期定时器驱动（`src/DeskBox/Services/WidgetShowDesktopSelfHealService.cs:31`）。
于是"稳态"被当成"事件"逐 tick 记录：日志语义是"发生过一次巡检结论"，实际语义是"当前状态"。

## 候选方案与打分

判据：消除根因 / 回归面 / 新概念依赖 / 可验证性 / 架构一致性 / 改动面

| 方案 | 说明 | 打分 |
| --- | --- | --- |
| A. 降级为 `App.LogVerbose` | 一行改动 | 消除根因 2（只是把噪声藏起来，`DESKBOX_VERBOSE_LOG` 一开照样刷）；回归面 5；可验证性 4；**否决** |
| B. 边沿触发 | 只在"进入稳态"时记一次，做过修复动作或离开稳态时复位 | 消除根因 5（语义从"状态"变回"事件"）；回归面 5（仅日志）；新概念 5（一个 bool 字段）；可验证性 5（数日志条数）；架构一致性 5；**采用** |
| C. 删掉这一行 | 信息量与 verbose 分支重复 | 消除根因 5；但丢掉"巡检确实跑过、结论是无需修复"的唯一线索，用户报"格子没了"时无法确认巡检是否在工作；**否决** |

## 采用的改法

`ShowDesktop.cs`：新增 `_showDesktopSteadyStateLogged`，进入稳态只在首次记一条；
`restored/uncloaked/raised > 0`（真的做了事）或 `!desktopRaised`（离开稳态）时复位。

## 影响面 / 回归风险

- 只改日志分支，不改任何 z 序调用与状态机；`_showDesktopRaisedWidgets` 的语义不变。
- 风险：若有人依赖"每 tick 一条"来判断巡检存活 —— 仓库内没有这种消费者（`grep 'Desktop raised, widgets already in place'` 只命中源码）。

## 验证

- 单测：`--filter "FullyQualifiedName~StartupResilienceContractTests|FullyQualifiedName~WidgetShowDesktop"` 与全量通过数对比（见 SUMMARY.md 的基线）。
- 真机：重建 → 部署到 `E:\DeskBox-dev\app` → 启动 → `zprobe seq` 注入 Win+D 往返 → 桌面留前台 30 秒 → 该行应为 1 条（修复前 ≥ 15 条）。

## 未采纳的观察（记录，不修）

1. **巡检成本**：稳态下每 tick 13 次全量 z 序遍历（~5850 次 `GetWindow`），实测整机空闲 CPU 0.78% 单核。可用"一次遍历算出所有格子是否被桌面根覆盖"降到 ~450 次，但那是把最敏感的 z 序判定重写成批量版本，回归面远大于 0.4% 的收益；**记录为已接受的取舍**。
2. **`IsDesktopLayerRaised()` 的语义**：它等价于"桌面根之上没有外来普通窗口"，在"用户把所有窗口都最小化"时同样为 true，与"桌面真的被抬起"不可区分（z 序本身无法区分）。当前它只用于两个日志分支与归位判定，误报无功能后果，**不修**。
3. **不能把"是否被桌面覆盖"的判定改成先看 `IsDesktopLayerRaised()` 再逐窗检查**：本机存在 `NVIDIA GeForce Overlay`（`CEF-OSC-WIDGET`）压在桌面根之上，会让聚合判定返回 false，从而漏掉真正被桌面盖住的格子 —— 这正是 `5e0449aa` 要修的 bug。逐窗比较必须保留。
