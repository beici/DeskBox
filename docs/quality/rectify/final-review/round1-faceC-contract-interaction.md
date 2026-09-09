# 收敛式深度审查 第1轮·面 C：Rust 线程边界 + 看门狗/异步握手/启动失败通知 新缺陷清单

> 分支：wip/fix-bug · HEAD=1df469e · 审查范围：4bc81af..HEAD（F6 批次 A/B/C/D + 独立复核整改）
> 作者：Agnes（面 C） · 2026-09-01

---

## 审查口径

对照 `docs/quality/defect-ledger.md` 与 `rectify/` 报告去重，本面聚焦四项契约：
1. **Rust 冻结契约** — native/ 目录零改动、十导出与 ABI/capability mask 零漂移。
2. **看门狗 + 既有 200ms/50ms 计时器交互** — WidgetManager.ZOrder.cs 的 `_interactionLeakWatchdogTimer` / `_trayLayerRestoreTimer` / `_trayMouseSamplerTimer` 三定时器并行时段的时序安全性、ForceReset 后与正常 End 路径的重入安全性。
3. **DEF-022 异步握手与钩子线程** — ReservedHotkeyHookService.TryStartAsync / DesktopDoubleClickActivationService.TryStartAsync 在同一次超时周期内可能产生两个钩子线程共存的边界。
4. **DEF-020 ShowStartupFailureNotification fallback** — LocalizationService 未初始化时的兜底行为（相邻面）。

---

## 逐项 PASS/FAIL

| # | 审查项 | 结论 | 证据 |
|---|--------|------|------|
| C-1 | Rust FFI 十导出冻结契约 | **PASS** | `git diff 4bc81af..HEAD --stat`：native/ 文件数=0，行数变化=0；`native/deskbox-native/src/lib.rs:1078-1402` 十导出签名未变；改变的文件中无 DllImport/LibraryImport 新增；全仓 34 个 FFI 调用站点均属于既有模块，未随本轮引入新的 native 依赖。 |
| C-2 | 看门狗 Timer 生命周期与 guard 正确性 | **PASS** | `WidgetManager.ZOrder.cs:533-548`（Start/StopInteractionLeakWatchdog）、`:761-812`（Start/StopTrayLayerRestoreMonitor / Start/StopTrayMouseSampler）三组各自独立、互不干扰；每个 Stop 都先 Stop() 再 -= Tick；每个 Start 都 Stop() → 更新 Interval → 重新 += Tick → Start()。`_interactionLeakWatchdogTimer` 仅在 depth 0→1 时装表，depth 归零或 ForceReset 时立刻停表。 |
| C-3 | ForceReset 后与 End 正常路径的重入安全 | **PASS** | `WidgetManager.ZOrder.cs:566-573`：ForceReset 后调用 `RestoreTemporarilyRaisedWidgetsToDesktopLayer` → `QueueIdleWidgetZOrderNormalization`；前者被 `_sessionManager.IsInteractionActive` 守卫（ForceReset 已将 depth 清零，若 End 后 IsInteractionActive=false 则走非 busy 分支直接回落），若 depth 仍>0（未结束）则 deferred；后者生成新 generation，generation mismatch 会丢弃陈旧 normalize。时序上是串行于 UI 线程的，不可能发生真正的重入。 |
| C-4 | 200ms restore monitor 与 watchdog 的 tick 竞争 | **PASS** | `WidgetManager.ZOrder.cs:853-870`（restore monitor）与 `:550-574`（watchdog）均在 UI dispatcher 线程上同步执行；任一 tick 内都不派发异步工作；watchdog 触发时调用 `ForceResetInteractions` 会把 depth 清零，使 restore monitor 后续 tick 不再跳过（`IsWidgetInteractionActive` 为 false 时继续走正常 restore 路径）。 |
| C-5 | DEF-022 钩子线程共存窗口 | **PASS** | `ReservedHotkeyHookService.cs:182`（TryStartAsync 入口）与 `:103`（TryStart 入口）均立即调用 `Stop()`，Stop 内部 `lock(_sync)` 递增 `_lifecycleGeneration`；新线程在 `HookThreadMain:358-364` 与 `:389-398` 各检查 `generation != _lifecycleGeneration`，若检测到已过期则 `TrySetResult(true)` 并 return，不会安装 hook。`DesktopDoubleClickActivationService.cs:232-250` 同构。因此任意时刻最多只有一个 hook 线程处于安装态，不存在"两个钩子线程共存超过一个超时周期"的可能。 |
| C-6 | DEF-022 启动/恢复路径经 SafeFireAndForget | **PASS** | `App.xaml.cs:972-975`（启动期 DesktopDoubleClickActivationService.RefreshRegistrationAsync）与 `:1352-1370`（生命周期恢复）均经 `SafeFireAndForget`，内部 await RefreshRegistrationAsync 无 await 之外抛错风险；全局兜底 `TaskScheduler.UnobservedTaskException` + `AppDomain.UnhandledException` 在 `RegisterGlobalExceptionBackstops()`（构造期注册）提供最终诊断保障。 |
| C-7 | DEF-020 ShowStartupFailureNotification fallback 路径 | **PASS（含一处观察项）** | `App.xaml.cs:1123-1156`：title/phase 使用 `LocalizationService?.T(...) ?? hardcoded` 保底（硬编码英文 `"DeskBox startup incomplete"` / phase 名），body 在 localization null 时走 `"Startup failed at \"...\". See log for details."` 硬编码兜底；整个通知逻辑包在 `try { ... } catch (Exception notifyEx) { Log(...) }` 内，永不向上抛。时序：`LocalizationService` 在 line:938 赋值，早于 `_nativeNotificationService` 的 line:1027 初始化，晚于 `_trayIcon` 的 line:953 创建 —— 三种失败阶段下均有合适的 fallback 对象。 |
| — | DEF-029 (N2) 遗留 | **已修复，不在本轮** | 独立复核整改批次 `4bc81af` 已修正：`SearchPopupWindow.xaml.cs:176-244`，入口改经 `DispatchShowPopupAsync` + `ShowPopupSafelyAsync` try/catch。台账已标注已修复。 |

---

## 新立案缺陷清单

**本轮无新缺陷可立案。** 四项审查轴全部 PASS，未见跨层交互泄漏、Rust 漂移、异步握手竞态或 fallback 路径缺失。

### 保留观察项（非立案级，P3 卫生范畴，供下一轮 R7 抽检时回溯）

| 编号 | 位置 | 现象 | 建议 |
|------|------|------|------|
| OBS-C-1 | `src/DeskBox/Views/SearchPopupWindow.xaml.cs:176/185` | 尽管已修 N2，`ShowPopup` / `ShowPopupWithQuery` 仍是 `public async void` 公开入口，调用方（热键/Widget/App）通过 fire-and-forget 触发；虽然管道内部 try/catch 完善，但 async void 本质仍不可 await、不可组合。 | 下一轮考虑改为 `internal Task ShowPopupAsync()` + 保留 2 个 async void 转发壳，或全链路 async。当前 P3，不影响生产行为。 |
| OBS-C-2 | `src/DeskBox/App.xaml.cs:1130-1134` | `ShowStartupFailureNotification` 在 LocalizationService 已初始化但 NativeNotificationService 未初始化时，toast 通道静默失效，fallback 到 tray balloon。此时用户收到的是 tray 气泡而非 toast，体验降级但不影响可见性。 | 可考虑在 notification 前先 Log info 而非静默，便于问题诊断。当前为已知的 design trade-off，P3。 |

---

## 验证记录

| 验证项 | 结果 |
|--------|------|
| `git diff 4bc81af..HEAD -- native/` 空集 | ✅ |
| `rg "DllImport|LibraryImport" src/DeskBox/Services/*.cs` 无新增符号 | ✅ |
| `rg "async void" src/DeskBox` 中 `SearchPopup.ShowPopup*` 已被 `DispatchShowPopupAsync` 包裹 | ✅ |
| `rg "public async void" src/DeskBox` 共 5 处，均为事件 handler 或受控入口 | ✅ |
| `WidgetSessionManagerTests` 与 `WindowInteractionSafetyNetContractTests` 已通过 | ✅ |
| `GlobalHotkeySafetyContractTests.LifecycleRecovery_ReRegistersGlobalHotkeyAfterExternalSessionChanges` 通过 | ✅ |

---

## 结论

**面 C 审查完成，本轮零新增缺陷。** Rust FFI 冻结、看门狗 + 计时器交织、异步握手并发边界、启动失败通知 fallback 均通过检验。建议收敛至总评审汇总输出。
