# DEF-003 胶囊展开/收起动画逐帧开销，达不到稳定 60fps

- 优先级：P1 ｜ 状态：部分修复（每帧分配已消除；Win11 合成器动画列为 R2 候选）｜ 修复轮次：R1（部分）

## 一、问题现象

- **复现步骤**：胶囊【鼠标悬停自动展开】模式下反复悬停展开/移出收起；或托盘批量唤起（最多 4 个胶囊并发 morph）。肉眼可见卡顿、掉帧，达不到稳定 60fps。
- **触发条件**：展开/收起过渡期间；Acrylic/Mica 背景叠加、多胶囊并发时加重。
- **影响范围**：全部 Smart 胶囊动画及托盘批量动画的帧预算。
- **风险等级**：中高（生产红线明确要求 ≥60fps）。

## 二、根因分析（源码级，已经主流程复核子代理结论）

动画机制为**逐帧 `SetWindowPos` 改窗口物理边界**（非 Composition 隐式动画）：帧时钟 `WidgetCompactAnimationCoordinator.StartFrameClock`（Win11 用 `CompositionTarget.Rendering`，Win10 用 DwmFlush 对齐），逐帧回调 `CollapseAnimationRendering`（`src/DeskBox/Views/WidgetWindowBase.Collapse.cs:3052` 起）每帧执行：

1. `MoveWindowWithoutPersisting(bounds, suppressRedraw: true)` —— 每帧 HWND resize（XAML island 重排 + swapchain resize + DWM 重合成，天然最贵），已经通过 `TryQueueBoundsMove` + `FlushPendingBoundsMoves` 用 `BeginDeferWindowPos` 合批，并有内容布局冻结（`FreezeTransitionContentLayout`）与 `SWP_NOREDRAW` 缓解。
2. `WidgetShellControl.SetCompactTransitionProgress`（`src/DeskBox/Controls/WidgetShell.xaml.cs:1796-1885`）—— **Win11 上每帧写约 10+ 个依赖属性**（8×Opacity + 3×Translation + full-bleed Opacity/Scale），引发每帧 XAML render walk。Win10 反而走 compositor 关键帧动画（`StartCompactCompositionTransition`），但该路径被 `!WindowsCompatibilityService.IsWindows11OrLater` 显式排除在 Win11 之外（Shell.xaml.cs:1900）。
3. 每帧小分配：`FlushPendingBoundsMoves` 中 `PendingBoundsMoves.Values.ToArray()`（`WidgetCompactAnimationCoordinator.cs:380`）每帧产生列表 + 枚举器垃圾，叠加 GC 压力。

帧率采样与目标定义：`WidgetCompactAnimationFrameTracker` 按帧间隔 > budget×1.5 记 drop；自适应降级 `WidgetCompactFrameSkipPolicy`（60fps 档位）在 8 tick 窗口 ≥6 次 overrun 时降档（session 粘性）——降档只是降频，不解决单帧成本。

## 三、优化/修复思路

**本轮已实施（零风险项）**：
- `FlushPendingBoundsMoves` 改用复用缓冲（`Clear + AddRange` 到静态 List），消除每帧堆分配；`TryCommitBatch` 本就接受 `IReadOnlyList`，语义不变，唯一调用点在 `OnRendering` finally，无重入风险。

**R2 候选（按收益/风险排序，本轮不做）**：
1. 【收益最大】Win11 启用 compositor 透明度/平移动画：移除 Shell.xaml.cs:1900 与 1926-1935 的 `IsWindows11OrLater` 排除条件，`SetCompactTransitionProgress` 在合成器接管期间早退。每帧 DP 写从 ~12 → 0。风险：Win11 视觉回归（角半径、full-bleed 缩放需逐项回归）。
2. 【低风险】`s_compactSessionFrameSkipLevel` 初始档位与 `HardwareAdaptiveAnimationService.CurrentLevel` 联动，低端设备直接从 60fps 档起步，避免观察期抖动。
3. 【观察】确认 `App.LogVerbose` 在帧路径仅在降级时触发（已核实早退，无 action 项）。

**备选方案评估**：改为纯 Composition 隐式动画驱动整体形变（放弃 HWND 逐帧缩放）需要重做点击测试边界与子窗口跟随逻辑，属架构级改动，违反「不大刀阔斧重构」迭代原则，明确不做。

## 四、拟修改代码模块与功能说明（本轮已实施部分）

| 文件 | 改动 |
|---|---|
| `src/DeskBox/Services/WidgetCompactAnimationCoordinator.cs` | 新增静态复用缓冲 `PendingBoundsMovesBuffer`；`FlushPendingBoundsMoves` 以 `Clear+AddRange` 替代 `Values.ToArray()`；配套 `moves.Count` |

## 五、风险评估（本轮改动）

- 复用缓冲与原 `ToArray` 快照语义一致（struct 值拷贝）；唯一调用点已核实无重入，后续新增调用点需遵守「不得在回调内嵌套触发 flush」约束（已在代码注释标注原因）。
- 无行为变化；批次提交、fallback、性能日志计数（`count=`）保持原值。

## 六、验证方案

1. **自动化**：x64 全量回归 2998/2998 通过。
2. **场景复现（目标机，R2 前置）**：运行 `scripts/measure-scenario-memory.ps1` / `measure-deskbox-memory.ps1` 建立动画场景基线；用 `PerformanceLogger` 的 `CompactAnimation`/`CompactBoundsBatch` 指标对比实施候选 1 前后的 dropped 帧数与 maxFrameMs。
3. **性能红线**：目标 Win11 机器单胶囊展开 `CompactAnimation` dropped=0；4 胶囊并发 morph 无 ≥3 连续 drop。
