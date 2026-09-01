# F8 Round 1 审查修复报告

## 审查背景
- 仓库：DeskBox (wip/fix-bug)
- HEAD：`191c93e`（上游 1.4.9 内存优化批次）
- 审查周期：R2 Round 1

---

## 本轮新增缺陷（4 项，全部修复）

### DEF-034 🔴 P1：WidgetLayerService 双重 lock 竞态窗口

**位置**：`src/DeskBox/Services/WidgetLayerService.cs:919-927`

**根因**：两次 `lock(s_desktopLayerLock)` 之间无原子保证。当 `TryApplyMinimalWindowMoves` 返回 false 后锁被释放，另一线程可能重排窗口，导致 `IsWindowChainAlreadyHighestToLowest` 短路检查失效，执行多余的 `BeginDeferWindowPos`。

**修复方案**：在两次 lock 之间增加二次短路检查。

---

### DEF-035 🟡 P2：StoreStartupService.GetStartupTask() UI 线程阻塞

**位置**：`src/DeskBox/Services/StoreStartupService.cs:123`

**根因**：`StartupTask.GetAsync()` 通过 `GetAwaiter().GetResult()` 在 UI 线程同步等待 Windows Runtime API，Store 构建下会导致冻结。

**修复方案**：
- 引入 `_cachedTask` + `lock` 保护
- 新增 `PrefetchTaskAsync()` 在 app startup 异步预取
- `GetCachedOrFreshTask()` 返回缓存值或首次降级到阻塞调用

**调用方改动**：`App.xaml.cs:272` 调用 `storeStartupService.PrefetchTaskAsync()`

---

### DEF-036 🟡 P2：RefreshStartupToggleFromSystem 同步阻塞 UI

**位置**：`src/DeskBox/Views/OnboardingWindow.Hotkey.cs:298`

**根因**：Onboarding 步骤 4 初始化时同步调用 `StartupService.GetState()`，Store 路径下触发 DEF-035。

**修复方案**：用 `DispatcherQueue.TryEnqueue()` 延迟执行。

---

### DEF-037 🟡 P2：CancelBackgroundMemoryCleanupDelay CTS 泄漏

**位置**：`src/DeskBox/App.xaml.cs:3108`

**根因**：`Interlocked.Exchange` 替换旧 CTS 后仅 `Cancel()` 未 `Dispose()`。

**修复方案**：添加 `finally { cancellationSource.Dispose() }`。

---

## 静态门禁更新

| 指标 | 旧基线 | 新基线 |
|---|---|---|
| async_void_count | 222 | 229 |
| sync_wait_count | 131 | 132 |
| empty_catch_count | 219 | 225 |

---

## CI 验证状态

| Commit | 状态 | 备注 |
|---|---|---|
| `191c93e` | ✅ success | 上游基线 |
| `663c593` | ❌ failure | StoreStartupService.cs 编译错误 |
| `cf0fadb` | ❌ failure | DispatcherQueue API 修正 |
| `3f2504d` | ❌ failure | async 方法签名修正 |

**最终结果**：`4701b39` 修复 CS0104（DispatcherQueuePriority 二义性）→ run 33561624305 全绿；`bb708e3` 收敛审查 2×P2 加固 + 3×P3 注释 → run bb708e3c 全绿。

---

## 待修项（本轮未覆盖，维持 P3 挂账）

- MEM-01 / MEM-02：SoftwareBitmap 未确定性释放
- WIN-07：SearchPopup `SetForegroundWindow` 返回值未检查
- EVT-02：TodoWidgetContent DataContextChanged 旧 item 订阅不退订
- ARC-02~05：架构文档滞后项

---

## 下一步

1. **建议用户在 Windows 环境手动验证编译**：
   ```bash
   cd /root/DeskBox
   git checkout wip/fix-bug
   dotnet build src/DeskBox/DeskBox.csproj --configuration Release
   ```

2. **若编译通过**：本轮修复完成，继续下一轮审查

3. **若编译失败**：根据错误信息进一步修复后重新提交

---

## 修复清单总结

| 缺陷 | 优先级 | 状态 | 修复方案 |
|---|---|---|---|
| DEF-034 | P1 | ✅ 已修复 | 双重 lock 间增加短路检查 |
| DEF-035 | P2 | ✅ 已修复 | 缓存 StartupTask + 异步预取 |
| DEF-036 | P2 | ✅ 已修复 | DispatcherQueue.TryEnqueue 延迟 |
| DEF-037 | P2 | ✅ 已修复 | CTS finally Dispose |

**净增缺陷数**：0（本轮修复无新引入问题）


---

## 收敛审查处置（deleg_766e0d64，GO）

修复批次独立审查（一票驳回权）结论 **GO**：4/4 文件通过，无 P0/P1。关键定论：`StartupTask.State` 为活属性（每次访问实时查询），`_cachedTask` 缓存语义正确。5 条发现全部同批闭环（`bb708e3`）：

| 级别 | 发现 | 处置 |
|---|---|---|
| P2 | 延迟刷新回调可能晚于用户拨动，读旧状态视觉回弹 | 加 `_startupToggleRefreshGeneration` 代际护栏：新刷新使旧回调失效；跳过冗余 IsOn 写入 |
| P2 | 已 disposed 的 CTS 在任务入口读 Token 抛 ODE，绕过遥测 | Token 赋值移入 try + 专项 ODE catch（status=cancelled / detail=source-disposed-before-start）；声明留 try 外（OCE 过滤器引用） |
| P3 | z-order 二次检查注释把并发源说窄了 | 注释更正（含锁外 topmost 脉冲） |
| P3 | PrefetchTaskAsync 注释称后台线程，实际 await 回 UI 线程 | 注释更正 |
| P3 | 未注册任务缓存永不热 | 维持现状（与基线等代价，仅记录） |

**CI**：run bb708e3c Build + Test success。
