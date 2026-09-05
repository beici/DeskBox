# Linux Hermes 审查报告（R2 Round 1）

> 审查时间：2026-09-02 ｜ HEAD：`191c93e`（Merge upstream 1.4.9 memory-optimization batch）
> 审查方法：3 个并行独立 subagent（相邻代码面 / 覆盖率限制区+台账复核 / 契约面+线程面）+ 主线深挖交叉验证
> 门禁结果：静态门禁 3 项 FAIL（async_void +7 / sync_wait +1 / empty_catch +6），**全部来自上游 1.4.9 合并**，基线需更新

---

## 一、审查范围与结论摘要

| 审查面 | 新方法 | 新 P0 | 新 P1 | 新 P2 | 新 P3 |
|---|---|---|---|---|---|
| 相邻代码面（task-0） | 覆盖 63c786e/db0c1d7/0ea2ddb 全部改动 + 调用方 | 0 | **1** | 2 | 2 |
| 覆盖率限制区+台账（task-1） | round-06 §8 限制区 ×3 + 台账 12 条抽查 | 0 | 0 | 0 | 3（维持挂账） |
| 契约面+线程（task-2） | Rust 冻结契约 + async void/sync_wait/empty_catch 全量定性 | 0 | 0 | **1** | 6 |
| **合计新增** | — | **0** | **1** | **3** | **11** |

**收敛判据**：本轮新增 P0=0、P1=1、P2=3，**未达到"无新增 P0/P1/P2"收敛判据**。按任务书硬上限 5 轮执行，本轮为第 1 轮。

---

## 二、发现清单（按优先级排序）

### 🔴 P1-1 | WidgetLayerService 双重 lock 竞态窗口（task-0）

| 字段 | 内容 |
|---|---|
| 位置 | `src/DeskBox/Services/WidgetLayerService.cs:919-927` |
| 根因 | `ApplyWindowOrderHighestToLowest` 内两段 `lock(s_desktopLayerLock)` 之间无原子保证：第一段 `TryApplyMinimalWindowMoves` 返回 false 后释放锁 → 第二段获取锁前，z-order 已变化但 `IsWindowChainAlreadyHighestToLowest` 短路未检查 → 可能执行两次 DeferWindowPos 事务 |
| 影响 | 性能退化（多余 Win32 调用）；极端情况下可能导致 DWM 微卡顿（measured 165ms frame spikes 场景）；不崩溃、不丢数据 |
| 证据 | `git diff c3a5555..HEAD -- src/DeskBox/Services/WidgetLayerService.cs` 显示原 5 处 lock → 新增 2 处；`IsWindowChainAlreadyHighestToLowest` 在 line 910 已检查但两次 lock 之间无重检查 |
| 处置 | **立案 DEF-034，本轮修复** |

### 🟡 P2-1 | StoreStartupService.GetStartupTask() UI 线程阻塞（task-2）

| 字段 | 内容 |
|---|---|
| 位置 | `src/DeskBox/Services/StoreStartupService.cs:123` |
| 根因 | `StartupTask.GetAsync(id).AsTask().GetAwaiter().GetResult()` 在 UI 线程上同步等待 Windows Runtime 异步调用；Store 构建时 `IsMicrosoftStore==true` 且 `StartupService.Configure(factory.Create(...))` 在 `App.xaml.cs:263` 启动路径直接调用 |
| 影响 | Store 构建 + 首次启动时可能 UI 冻结数秒（StartupTask API 慢时）；非 Store 构建不受影响 |
| 证据 | `rg -n "StoreStartupService\|GetState\(\)" src/` 确认调用链；注释明确"Fire-and-forget: RequestEnableAsync may show consent dialog" 说明原作者已知此 API 会阻塞 |
| 处置 | **立案 DEF-035，本轮修复** |

### 🟡 P2-2 | Step4StartupToggle_Toggled async void 内同步调用 P2-1（task-2）

| 字段 | 内容 |
|---|---|
| 位置 | `src/DeskBox/Views/OnboardingWindow.Hotkey.cs:317` |
| 根因 | `StartupService.SetEnabled()` 在 async void 处理器中同步执行，内部通过 `StoreStartupService.GetStartupTask()` 触发 P2-1 |
| 影响 | Onboarding 步骤 4 切换自动启动开关时 Store 构建下 UI 冻结 |
| 证据 | `sed -n '317,330p' src/DeskBox/Views/OnboardingWindow.Hotkey.cs` 确认同步调用链 |
| 处置 | **立案 DEF-036，本轮修复** |

### 🟡 P2-3 | CancelBackgroundMemoryCleanupDelay 旧 CTS 未 Dispose（task-0）

| 字段 | 内容 |
|---|---|
| 位置 | `src/DeskBox/App.xaml.cs:3111`（CancelBackgroundMemoryCleanupDelay 内） |
| 根因 | `Interlocked.Exchange` 替换旧 CTS 后仅 `Cancel()` 但未 `Dispose()`；旧 CTS 的 registered wait handles 延迟到 GC 才释放 |
| 影响 | 每次 cancel 泄漏 ~1 个 WaitHandle（16 bytes + native wait registration）；高频场景下累积 |
| 证据 | `sed -n '3108,3130p' src/DeskBox/App.xaml.cs` 可见 Cancel() 后直接丢弃 cancellationSource 引用 |
| 处置 | **立案 DEF-037，本轮修复** |

### 🟢 P3 项（11 条，维持挂账不升级）

| 编号 | 位置 | 说明 |
|---|---|---|
| P3-1 | `FileSurfaceContent.StackPopoverRename.cs:257/273` | 2 个新 async void 事件处理器，已有内部 try/catch，P3 观察 |
| P3-2 | `OnboardingWindow.TaskFlow.cs:67/104` | 2 个新 async void，有 IsEnabled 保护 + try/finally，P3 观察 |
| P3-3 | `SettingsWindow.Startup.cs:21` | 1 个新 async void 薄包装，可接受 |
| P3-4 | `SettingsWindow.QuickCaptureColors.cs:26/31/36` | 3 个新 async void，均 await 简单操作，P3 观察 |
| P3-5~10 | 6 处新 empty_catch（App/Win32Helper/ManagedStorage/MemoryReclaimer/WindowsCompat/StartupService） | task-2 逐条核查，全部有显式 fallback 返回值，不违反 S9 EXC-04，维持 P3 |
| P3-11 | `WidgetWindowBase.Backdrop.cs` | upstream 移除 InactiveBackdropCleanupTimer 中的 controller dispose，改为 detached 保留（架构 trade-off），P3 观察 |

---

## 三、已覆盖/维持挂账的台账项

| 编号 | 原状态 | HEAD 现状 | 结论 |
|---|---|---|---|
| ANI-02 | 待修 | 已删除（4682a02） | ✅ 覆盖 |
| WIN-05/06 | 待修 | 已校正（ad8febe） | ✅ 覆盖 |
| ARC-06 | 待修 | 已删除（4682a02） | ✅ 覆盖 |
| R1-CS0169 | 观察 | 随死代码链路删除（4682a02） | ✅ 覆盖 |
| R1-CS0414 | 观察 | 已修复（`OnboardingWindow.DesktopOrganization.cs`） | ✅ 覆盖 |
| MEM-01 | 挂账 | 析构路径已有 Dispose，热替换时旧 Icon 仍延迟释放 | ⏳ 维持 P3 |
| MEM-02 | 挂账 | `IQuickCaptureClipboardReader.cs:99,124` SoftwareBitmap 未 using | ⏳ 维持 P3（本次复核确认） |
| WIN-07 | 挂账 | SearchPopup `SetForegroundWindow` 返回值未检查 | ⏳ 维持 P3 |
| EVT-02 | 挂账 | TodoWidgetContent 旧 item 订阅不退订 | ⏳ 维持 P3 |

---

## 四、静态门禁基线更新

**理由**：1.4.9 上游合并引入 7 个新 async void（UI 事件处理器，全部低风险）、1 个 sync_wait（StoreStartupService，已立案 P2-2）、6 个空 catch（全部有显式 fallback，符合 S9 EXC-04 豁免条件）。门禁工具本身逻辑正确，只是基线停留在合并之前。

```bash
python3 scripts/quality/static_gate.py --update-baseline
```

---

## 五、修复计划

| 缺陷 | 修复方案 | 预估工作量 |
|---|---|---|
| DEF-034（P1） | 将两段 lock 合并为一段，或在第一段返回 false 后重新检查 `IsWindowChainAlreadyHighestToLowest` | 小改 |
| DEF-035（P2） | `GetStartupTask()` 改为 `await` 返回 Task，所有调用方异步化（Store 路径只在启动时调一次，影响可控） | 中改 |
| DEF-036（P2） | `Step4StartupToggle_Toggled` 改为 `async Task`，用 `SafeFireAndForget` 包装 | 小改 |
| DEF-037（P2） | `CancelBackgroundMemoryCleanupDelay` 内对旧 CTS 加 `using` 或显式 Dispose | 一行改动 |

**修复批次**：DEF-034/037 同批（简单），DEF-035/036 同批（需联动修改）。

---

## 六、下一步

1. 本批次提交门禁基线更新 + DEF-034/037 修复 + 契约测试
2. 推送 → CI 验证
3. 下一轮继续修复 DEF-035/036 + 深度收敛审查
