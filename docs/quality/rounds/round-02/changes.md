# DeskBox R2 轮代码变更记录（可追溯 / 可回滚）

> 回滚：整轮 revert 对应提交；单项按下表文件 revert。
> 提交链：`e2a6aa2`（R2 中间态：DEF-001 事件修复）→ 本轮收尾提交（DEF-003 候选 1 + 文档）。

## e2a6aa2（R2 中间态，已含在先前提交）

| 文件 | 改动 |
|---|---|
| `src/DeskBox/Helpers/Win32Helper.cs` | 事件常量修正（`EVENT_SYSTEM_MINIMIZESTART=0x0016`、`MINIMIZEEND=0x0017`、新增 `EVENT_SYSTEM_FOREGROUND=0x0003`） |
| `src/DeskBox/Services/WidgetShowDesktopSelfHealService.cs` | 双钩子注册（minimize 0x0016-17 + foreground 0x0003）；`HasThreadAccess` 门控 + `TryEnqueue(StartCore)` 强制 dispatcher 线程注册；Dispose 双钩子注销 |
| `src/DeskBox/Services/WidgetManager.ShowDesktop.cs` | 五道闸提前 return 全部补 `Self-heal skipped reason=` 日志 |
| `scripts/measure-quality-baseline.ps1`（新增） | PS 5.1 兼容采样器（WS/Private/Handles/Threads/GDI/UserObjects/CPU + DWM 同步采样，JSONL 输出） |

## 本轮收尾提交（DEF-003 候选 1）

| 文件 | 函数/位置 | 改动 |
|---|---|---|
| `src/DeskBox/Controls/WidgetShell.xaml.cs` | `SetCompactTransitionProgress` | 删除 `compositionOwnsWin10Visuals` 的 `!IsWindows11OrLater` 门控，改名 `compositionOwnsFadeVisuals = _isCompactCompositionTransitionActive`：全部版本合成期跳过逐帧 DP 写（约 12 次/帧 → 0） |
| 同上 | `StartCompactCompositionTransition` | 删除主淡入动画组与 full-bleed Opacity 动画的两处 `!IsWindows11OrLater` 包裹——Win11 与 Win10 一致走 compositor 时钟；注释记录动机（Win11 实测逐帧 DP walk 占主导） |
| `tests/DeskBox.Tests/Windows10WidgetMotionContractTests.cs` | `Win10CompactVisuals_UseCompositionWithoutReplacingRealBoundsMotion` | 契约更新：钉住 `compositionOwnsFadeVisuals` 判定与 `if (!compositionOwnsFadeVisuals)` 跳过分支（「合成器接管透明度 + UI 线程只做真实边界运动」从 Win10 扩展为全版本） |

## 验证记录

- Debug 构建（含候选 1）：0 错误。
- x64 回归：2998/2998（候选 1 后、契约更新后各一轮）。
- DEF-001 目标机实测：双钩子注册 + Win+D 核验 + 真实最小化核验，三条 verbose 日志留痕（05:23:49 / 05:24:30 / 05:25:02）。
- 基线：`performance-baseline.md` 新增 R2 两行真实数据 + 结论三条；原始样本 `.artifacts/quality-baseline/r2-samples.jsonl`。
