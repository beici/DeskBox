# DeskBox 迭代 R2 轮报告

- 轮次：R2（2026-08-30，含补充批次插队后恢复）
- 分支：`wip/fix-bug` ｜ 提交链：`97060a3`(R1) → `e2a6aa2`(R2 中间态修复) → `227102f`/`f203f23`/`596e0fc`(补充批次) → 本轮收尾提交

## 一、本轮输入与修复清单

| 来源 | 问题 | 处理 | 状态 |
|---|---|---|---|
| R1 遗留 | DEF-001 自愈链路实测 | 目标机 GUI 场景验证 | ✅ 链路三要素实测通过（见三） |
| R1 遗留 | DEF-001 自愈代码缺陷 | 独立监听器对照实验定位：`EVENT_SYSTEM_MINIMIZESTART=0x0016`（原误用 0x0002=ALERT）、补 FOREGROUND 事件源、dispatcher 线程注册 | ✅ 提交 e2a6aa2 |
| R1 遗留 | DEF-004 关联定位 | 目标机采样（`scripts/measure-quality-baseline.ps1`） | ✅ DWM 强关联坐实，GDI 排除（基线表已落盘） |
| R1 遗留 | DEF-003 帧成本 | 候选 1：Win11 启用合成器透明度动画（删除两处版本排除条件） | ✅ 代码完成 + 契约更新 + 回归全绿 |
| R1 遗留 | DEF-002 实测 | GUI 悬停自动化受工具限制（CUA 无法作用于 owner-attached 窗口 + UIA 树为空） | ⏸ 降级：契约测试+回归为证，GUI 复验列入人工清单 |
| 长期扫描 | 隐藏缺陷 | 补充批次插队消耗本轮子代理额度；常规扫描并入 R3 | ⏸ |

## 二、代码改动总览（本轮新增，不含补充批次）

1. **`Win32Helper` / `WidgetShowDesktopSelfHealService` / `WidgetManager.ShowDesktop.cs`**（e2a6aa2）：事件常量修正 + 双钩子（MINIMIZESTART/END 0x0016-17 + FOREGROUND 0x0003）+ dispatcher 线程注册 + skip 原因日志。
2. **`WidgetShell.xaml.cs`**（本轮）：候选 1——`StartCompactCompositionTransition` 的 Win11 排除条件与 `SetCompactTransitionProgress` 的 `compositionOwnsWin10Visuals` 版本门控删除（改名 `compositionOwnsFadeVisuals`）：全部 OS 版本合成期跳过逐帧 DP 写（每帧约 12 次 DP 写 → 0），UI 线程仅保留 HWND 边界运动；角半径/full-bleed 折衷与 Win10 既有行为一致；`CompleteCompactTransition→SetCollapsed` 终值收口与 `CancelCompactTransition` 全量重置均未改动。
3. **`Windows10WidgetMotionContractTests`**：源码形状契约从「Win10 专属合成器」更新为「全版本合成器接管 + UI 线程只做真实边界运动」，钉住 `compositionOwnsFadeVisuals` 判定。
4. **`scripts/measure-quality-baseline.ps1`**：PS 5.1 兼容的 WS/Private/Handles/GDI/DWM 采样器。

## 三、审查结果（7 维）

| 维度 | 结论 |
|---|---|
| 资源管理 | 双钩子 Dispose 注销；委托由服务字段保活；无新增计时器。✅ |
| 异常安全 | 事件回调仅去抖操作；核验全链 try/catch；`StartCompactCompositionTransition` 原有 catch+降级路径保留（合成器启动失败自动回退逐帧 DP 写）。✅ |
| 线程安全 | 钩子注册经 `HasThreadAccess` 门控强制 dispatcher 线程；核验在 UI 线程。✅ |
| 窗口交互 | 核验仅触碰「本应用可见 + 非托盘 cloak + iconic/cloak 实证」窗口；合成器动画不改变窗口交互路径。✅ |
| 性能影响 | 事件驱动无轮询；悬停展开每帧 DP 写 ~12→0（本轮最大帧收益）；核验幂等。✅ |
| 兼容性 | Win10 行为零变化（原本就走合成器路径）；Win10 兼容底线不受影响；DesktopPinned 模式双重短路。✅ |
| 逻辑一致性 | 复用 `StartCompactOpacityAnimation`/`StopCompactCompositionTransitionAnimations`/`CompleteCompactTransition` 既有生命周期；契约测试同步钉住。✅ |

## 四、回归测试与场景验证

- **x64 自动化回归：2998/2998 全部通过**（两轮：候选 1 后一次；契约更新后一次）。
- **DEF-001 实测（目标机，verbose 日志）**：
  1. 双钩子注册成功（`minimizeHook=0x100F030F foregroundHook=0x155C03C3`）。
  2. Win+D → 700ms 去抖 → `Self-heal verified, nothing to restore reason=minimize-storm`（幂等正常路径）。
  3. 真实窗口最小化 → 第二次核验记录（MINIMIZESTART 事件源独立确认）。
  4. 「恢复受损格子」正向用例需 attach 失败等瞬态条件，无法在不破坏桌面前提下人工制造——`[ShowDesktop] Restored iconic resting widget` 日志标记已内置，留生产观察。
- **DEF-002 实测**：GUI 悬停自动化受工具限制（见一）；ToolTip 豁免修复由契约测试 + 回归背书，人工复验清单：悬停至 ToolTip 出现保持不动 → 应按时展开。
- **性能基线**：首行真实数据已落盘（`performance-baseline.md`）——S0 安装版参考（Private 760MB/GDI 200/DWM Priv 5.88GB）、S1 R1 代码静置（Private 318MB/DWM Priv 561MB）、1.4.8 动画掉帧参考（dropped 2–6/60 @165Hz）。

## 五、遗留与 R3 建议

1. **DEF-004 修复**：关联已坐实（长运行 × DWM 表面高水位）。修复需表面级剖析数据（哪类表面、多大、何时分配）——建议 R3 引 ETW/DWM 剖析或在交互缩放路径接入帧跳降档（DEF-003 候选 3，低风险）后对比 DWM 增量。本轮不做无据猜测性改动（保守原则）。
2. **DEF-003 候选 3**（帧跳档位与硬件档位联动）与**候选 2**（指针新进入语义）列入 R3。
3. **人工复验清单**：DEF-002 悬停 ToolTip 场景；DEF-001 正向恢复用例（生产观察 `[ShowDesktop] Restored` 日志）；候选 1 的 Win11 展开动画视觉回归（角半径在合成期保持起始值的折衷需目检）。
4. 补充批次 GUI 实测（B1/B2/B3/C1）与长期主线 GUI 实测合并为一个运行窗口清单，见全量任务 TODO 清单。
