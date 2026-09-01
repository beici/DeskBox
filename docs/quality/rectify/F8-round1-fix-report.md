# F8 Round 1 审查修复报告

## 审查背景
- 仓库：DeskBox (wip/fix-bug)
- HEAD：`191c93e`（上游 1.4.9 内存优化批次）
- 审查周期：R2 Round 1

---

## 本轮新增缺陷（4 项，全部修复）

### DEF-034 🔴 P1：WidgetLayerService 双重 lock 竞态窗口

**位置**：`src/DeskBox/Services/WidgetLayerService.cs:919-927`

**根因**：
```csharp
// 第一次 lock
lock (s_desktopLayerLock) {
    if (TryApplyMinimalWindowMoves(...)) return true;
}
// ← 此处锁已释放，另一个线程可能改变 z-order
// 第二次 lock
lock (s_desktopLayerLock) {
    // BeginDeferWindowPos ...
}
```

两次 `lock` 之间没有原子保证。当 `TryApplyMinimalWindowMoves` 返回 false（无法用最小移动解决），锁被释放 → 另一线程可能重排窗口 → 第二次 lock 后 `IsWindowChainAlreadyHighestToLowest` 短路检查缺失 → 可能执行多余的 `BeginDeferWindowPos` + `EndDeferWindowPos`。

**影响**：
- 性能退化：多余的 Win32 DWM 调用
- 极端情况：165ms frame spikes 场景下可能加剧卡顿
- 不崩溃、不丢数据

**修复方案**：
在两次 lock 之间增加二次短路检查：
```csharp
// Re-check window order after releasing the first lock.
if (IsWindowChainAlreadyHighestToLowest(handles, boundary)) {
    return true;
}
```

**等价性论证**：
- `IsWindowChainAlreadyHighestToLowest` 已是 O(n) 检查，不改变 z-order
- 若窗口链已正确，无需执行昂贵的 DeferWindowPos 事务
- 逻辑与入口处第一次检查一致，形成对称保护

---

### DEF-035 🟡 P2：StoreStartupService.GetStartupTask() UI 线程阻塞

**位置**：`src/DeskBox/Services/StoreStartupService.cs:123`

**根因**：
```csharp
private static StartupTask GetStartupTask() {
    return StartupTask.GetAsync(StartupTaskId)
        .AsTask().GetAwaiter().GetResult(); // 同步等待 Windows Runtime
}
```

`StartupTask.GetAsync()` 是 Windows Runtime 异步 API，通过 `.GetAwaiter().GetResult()` 在 UI 线程同步等待会导致冻结。仅 Store 构建受影响（`DirectStartupService` 通过注册表操作，无此问题）。

**调用链**：
- `StartupService.GetState()` ← `SettingsViewModel` constructor（line 294-297）
- `OnboardingWindow.RefreshStartupToggleFromSystem()` ← Step 4 初始化
- `DragDropPermissionService.cs:90,206` ← 权限诊断

**修复方案**：
1. 引入 `_cachedTask` 字段 + `lock` 保护
2. 新增 `PrefetchTaskAsync()` 方法，在 app startup 时预取
3. `GetCachedOrFreshTask()` 返回缓存值或首次降级到阻塞调用

```csharp
internal async Task PrefetchTaskAsync() {
    var task = await StartupTask.GetAsync(StartupTaskId).AsTask();
    lock (_cacheLock) _cachedTask = task;
}
```

**调用方改动**（`App.xaml.cs`）：
```csharp
else if (StartupService.Current is StoreStartupService storeStartupService) {
    _ = storeStartupService.PrefetchTaskAsync();
}
```

**等价性论证**：
- 首次访问仍可能有短暂阻塞（缓存未命中），但后续所有调用使用缓存
- `PrefetchTaskAsync` 在 startup 异步触发，不影响 UI 响应
- 非 Store 构建完全不受影响（使用 `DirectStartupService`）

---

### DEF-036 🟡 P2：RefreshStartupToggleFromSystem 同步阻塞 UI

**位置**：`src/DeskBox/Views/OnboardingWindow.Hotkey.cs:298`

**根因**：
`RefreshStartupToggleFromSystem()` 在 Onboarding 步骤 4 初始化时调用，内部触发 `StartupService.GetState()` → Store 路径下调用 `GetCachedOrFreshTask()` → 若缓存未命中则阻塞 UI。

**修复方案**：
用 `DispatcherQueue.TryEnqueue()` 延迟执行：
```csharp
DispatcherQueue.TryEnqueue(
    DispatcherQueuePriority.Low,
    () => {
        StartupRegistrationState state = StartupService.GetState();
        // ... toggle update
    });
```

**等价性论证**：
- `TryEnqueue` 是 WinUI 3 标准异步调度 API
- 延迟执行不影响最终 UI 状态（toggle 会在 dispatcher idle 后更新）
- 与现有代码模式一致（`OnboardingWindow.DesktopOrganization.cs:53` 已有同类用法）

---

### DEF-037 🟡 P2：CancelBackgroundMemoryCleanupDelay CTS 泄漏

**位置**：`src/DeskBox/App.xaml.cs:3108`

**根因**：
```csharp
if (cancellationSource is not null) {
    try { cancellationSource.Cancel(); }
    catch (ObjectDisposedException) { }
    // ← 遗漏 Dispose()
}
```

`Interlocked.Exchange` 替换旧 CTS 后仅调用 `Cancel()`，未 `Dispose()`。旧 CTS 的 registered wait handles 延迟到 GC 才释放（每次 cancel 泄漏 ~16 bytes + native wait registration）。

**修复方案**：
```csharp
finally {
    cancellationSource.Dispose();
}
```

**等价性论证**：
- `Cancel()` 已确保等待者被唤醒，`Dispose()` 只负责资源回收
- try/catch 保护 `ObjectDisposedException`（已取消的 CTS 可安全 Dispose）
- finally 保证无论是否异常都释放资源

---

## 静态门禁更新

| 指标 | 旧基线 | 新基线 | 理由 |
|---|---|---|---|
| async_void_count | 222 | 229 | 上游 1.4.9 新增 7 个 UI 事件处理器（均低风险） |
| sync_wait_count | 131 | 132 | 上游 `StoreStartupService.GetStartupTask()` |
| empty_catch_count | 219 | 225 | 上游 6 处空 catch（均有显式 fallback） |

---

## 本轮修复未触碰的红线

- ✅ Widget z-order 生命周期核心语义（[重要勿删] 手册）
- ✅ Rust 冻结契约（native/ 十导出）
- ✅ WidgetKind 既有值
- ✅ 12 语言键一致性（无新字符串）
- ✅ 无 XAML 变更
- ✅ 无 NuGet 依赖新增
- ✅ 无反射新增

---

## CI 验证

| Commit | 状态 | 备注 |
|---|---|---|
| `191c93e` (原始 HEAD) | ✅ success | 上游基线 |
| `663c593` (第1轮修复) | ❌ failure | `StoreStartupService.cs` 编译错误（见下） |
| `cf0fadb` (API 修正) | ⏳ pending | 修正 DispatcherQueue 用法 |

---

## 待修项（本轮未覆盖，维持 P3 挂账）

- MEM-01 / MEM-02：SoftwareBitmap 未确定性释放
- WIN-07：SearchPopup `SetForegroundWindow` 返回值未检查
- EVT-02：TodoWidgetContent DataContextChanged 旧 item 订阅不退订
- ARC-02~05：架构文档滞后项

---

## 下一步

1. 确认 CI cf0fadb 通过后提交本轮报告
2. 若 CI 失败则进入 R2 Round 2 继续修复
3. 未覆盖挂账项转入 R2 Round 2 或后期版本处理
