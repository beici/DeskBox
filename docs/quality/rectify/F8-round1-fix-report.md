# R2 Round 1 审查修复报告（含 Round 2 收敛）

## 审查背景
- 仓库：DeskBox (wip/fix-bug)
- 起点基线：`191c93e`（上游 1.4.9 内存优化批次合并）
- 终态 HEAD：`917bcf9`（全部推送，工作树干净）

---

## 本轮新增缺陷（5 项，全部修复）

### DEF-034 🔴 P1：WidgetLayerService 双重 lock 竞态窗口
**位置**：`src/DeskBox/Services/WidgetLayerService.cs:919-927`
**根因**：两次 `lock(s_desktopLayerLock)` 之间无原子保证。`TryApplyMinimalWindowMoves` 返回 false 后锁被释放，窗口链可能被并发/锁外脉冲改变，`IsWindowChainAlreadyHighestToLowest` 短路失效，执行多余 DeferWindowPos。
**修复**（`663c593`）：两段 lock 之间增加二次短路检查。

### DEF-035 🟡 P2：StoreStartupService.GetStartupTask() UI 线程阻塞
**位置**：`src/DeskBox/Services/StoreStartupService.cs:123`
**根因**：`StartupTask.GetAsync().AsTask().GetAwaiter().GetResult()` 在 UI 线程同步等待 Windows Runtime（仅 Store 构建）。
**修复**（`663c593`）：`_cachedTask` 缓存 + `PrefetchTaskAsync()` 启动异步预取 + `GetCachedOrFreshTask()`。
**审查定论**：`StartupTask.State` 为活属性（每次访问实时查询），缓存语义正确，不影响 Enable/Disable 判断。

### DEF-036 🟡 P2：RefreshStartupToggleFromSystem 同步阻塞 UI
**位置**：`src/DeskBox/Views/OnboardingWindow.Hotkey.cs:298`
**修复**（`cf0fadb`+`4701b39`）：改 `DispatcherQueue.TryEnqueue(Low)` 延迟执行（全限定优先级枚举消除 CS0104）。

### DEF-037 🟡 P2：两处 replaced CTS 未 Dispose
**位置**：`App.xaml.cs` CancelBackgroundMemoryCleanupDelay + ScheduleBackgroundMemoryCleanup（孪生点）
**修复**（`663c593`+`4701b39`）：两处均补 finally Dispose。

### DEF-038 🟡 P2：代际护栏未覆盖「用户拨动 vs 在途刷新」
**位置**：`src/DeskBox/Views/OnboardingWindow.Hotkey.cs:325`
**根因**：Round 1 引入的护栏只拦「新刷新 vs 旧刷新」；用户拨动不递增代际，Store 渠道 Pending 收敛窗口内在途回调仍回弹 IsOn + 覆写 AutoStart。
**修复**（`917bcf9`，一行）：`Step4StartupToggle_Toggled` 入口 `++_startupToggleRefreshGeneration`。

---

## CI 收敛记录

| Commit | 结果 | 备注 |
|---|---|---|
| `663c593` | ❌ | CS0104 前兆 |
| `cf0fadb` | ❌ | 裸 DispatcherQueuePriority 二义性 |
| `3f2504d` | ❌ | 同上（CS0104 根因确认） |
| `4701b39` | ✅ | 全限定消除二义性，run 33561624305 全绿 |
| `bb708e3` | ✅ | 收敛审查 2×P2 加固 + 3×P3 注释，run bb708e3c 全绿 |
| `917bcf9` | ✅ | DEF-038 一行修复，run 917bcf9e 全绿 |

**CS0104 教训**：OnboardingWindow.Hotkey.cs 同时 `using Microsoft.UI.Dispatching` 与 `using Windows.System`，两命名空间均有 `DispatcherQueuePriority`——该文件内必须全限定（仓库既有代码 SettingsWindow.Navigation.cs 同款写法可佐证）。

---

## 静态门禁基线更新（理由：上游 1.4.9 合并）

| 指标 | 旧基线 | 新基线 |
|---|---|---|
| async_void_count | 222 | 229 |
| sync_wait_count | 131 | 132 |
| empty_catch_count | 219 | 225 |

---

## 审查链

1. **Round 1 全量审查**（3 subagent 并行）：面A 相邻代码面（/root/review-findings.md，1×P1+2×P2+2×P3）、面B 覆盖率限制区+台账复核（零新增 P0/P1/P2）、面C 契约+线程面（Rust 十导出零漂移；上游 async void +7 逐条定性合规）。
2. **修复批次独立审查**（deleg_766e0d64）：**GO**，4/4 文件无 P0/P1；提出 2×P2+3×P3 → bb708e3 全部处置。
3. **Round 2 收敛审查**（deleg_8d8b2d7b）：②③④ GO；① NO-GO 指出 DEF-038 → 917bcf9 一行修复闭环。两条 P3（ODE catch 无过滤器折叠深层 ODE 的日志语义 / detail 文案边界）接受挂账。

**误报排除**：面A 的 SimplifyBackdrop P2 复核不成立——`LegacyAccentBackdropActive=true` 蕴含 `UsesLegacyWindowAcrylic=true` 不变量成立，方法自 c3a5555 未被上游改动。

---

## 收敛裁定

Round 2 无新增 P0/P1；唯一 P2（DEF-038）已修复并经 CI 验证 → **R2 判定收敛**（第 2 轮达成，硬上限 5 轮未触发）。

## 遗留

- pending-windows-gate F8-1~4 实机项（CI 已覆盖编译/回归，实机手感待用户验证）
- 两条新 P3 挂账（ODE 日志语义、detail 文案边界）
- 面A 两条 P3（HWND_TOP sentinel 短路、重复 lock 语义）
- PR #1 合并决策权在用户
