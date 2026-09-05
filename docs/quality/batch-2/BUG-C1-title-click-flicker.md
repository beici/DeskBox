# BUG-C1 点击格子标题区域时其他格子闪烁修复

- 所属：长期迭代补充批次 ｜ 分类：专项 Bug ｜ 验证方式：代码层面审查 + 自动化回归（按批次约束，无 GUI 运行测试）

## 一、问题现象说明

- **复现条件**：桌面存在 ≥2 个格子；鼠标左键单击任意一个格子的标题栏区域。
- **现象**：未被点击的其他格子出现短暂闪烁（视觉抖动一下）。
- **影响范围**：全部格子类型（ContentWidgetWindow 宿主与 QuickCapture 宿主共用同一标题点击入口）；高频交互路径。
- **风险等级**：中（不影响功能，但违反「点击交互过程中所有格子视觉表现稳定」要求，生产观感差）。

## 二、代码修改模块与核心逻辑说明

**根因**（源码定位）：标题点击链路为
`TitleBarGrid_PointerPressed`（`src/DeskBox/Views/ContentWidgetWindow.WindowInteraction.cs:179`；QuickCapture 平行实现 `QuickCaptureWidgetWindow.WindowInteraction.cs:51`）
→ `WidgetManager.ActivateAllVisibleWidgetsFromTitle`（`src/DeskBox/Services/WidgetManager.ZOrder.cs:404`，意图是整组格子临时浮到应用窗口之上，保证标题弹出菜单可见）
→ `WidgetLayerService.BringGroupTemporarilyToFront`（`src/DeskBox/Services/WidgetLayerService.cs:534`）。

原实现把「浮起技巧」（先 `SetWindowPos(HWND_TOPMOST)` 再立即 `HWND_NOTOPMOST`，使窗口停在普通层级带顶部）应用到**包括未点击格子在内的全部可见格子**：每个未点击格子每次都要经历两次 DWM z-order 带间迁移（normal band → topmost band → normal band）。带迁移触发 DWM 对该窗口的层级重合成，与 Acrylic/Mica 背板叠加即表现为肉眼可见的「闪一下」。`docs/architecture/[重要勿删]widget_zorder_lifecycle.md` §2 明确该技巧只服务于「需要浮起的那个窗口」。

**修复**（最小侵入）：新增专用方法 `WidgetLayerService.BringTitleActivatedGroupToFront`，仅被点击的格子保留瞬态置顶技巧（激活 + 前台语义不变）；其余格子通过**单次 `BeginDeferWindowPos` 合批**直接插入到刚浮起的激活窗口之后（同带内 z-order 平移，无带迁移），组相对顺序与原实现一致。`ActivateAllVisibleWidgetsFromTitle` 改调新方法；托盘唤起路径（`BringGroupTemporarilyToFront` 的另一调用方）不受影响。

## 三、关键代码实现

```csharp
// WidgetLayerService.cs（节选）
IntPtr activeHandle = ...;
// 仅被点击的格子走瞬态置顶技巧
DetachFromDesktopIconLayerIfNeeded(activeHandle);
Win32Helper.SetWindowTopMost(activeHandle);
Win32Helper.ClearWindowTopMost(activeHandle);
Win32Helper.BringWindowToFront(activeHandle);
Win32Helper.SetForegroundWindow(activeHandle);

// 其余格子：单次合批，直接排到激活窗口之后（同带内平移）
IntPtr deferred = Win32Helper.BeginDeferWindowPos(peers.Count);
IntPtr insertAfter = activeHandle;
foreach (IntPtr handle in peers)
{
    deferred = Win32Helper.DeferWindowPos(deferred, handle, insertAfter, 0,0,0,0, flags);
    insertAfter = handle;
}
Win32Helper.EndDeferWindowPos(deferred);   // 失败兜底：逐个 SetWindowPos（同样无 TOPMOST 往返）
```

回落路径（`RestoreTemporarilyRaisedWidgetsToDesktopLayer` → `RestoreGroupPreservingForeground`）本就是单次组操作，未改动。

## 四、兼容性与风险评估

- **组连续性**：peers 紧随 active 之后按原相对顺序排列，组语义与原实现一致；flyout 可见性目标不变。
- **四种象限**（2 宿主 × 动态/桌面固定层模式）：DesktopPinned 模式在 `ActivateAllVisibleWidgetsFromTitle` 入口与 `BringTitleActivatedGroupToFront` 入口双重短路（与原方法一致），行为零变化。
- **回落**：`TrackTemporarilyRaisedWidgets` 记录与 2300ms fallback restore 链路完全未动。
- **风险**：低。peers 失去「瞬时 TOPMOST」这一冗余保护后，若存在第三方窗口恰好在 active 与 peers 之间…… peers 插在 active 之后且 active 在普通带顶，插入后组整体位于普通带顶部，该场景不存在。

## 五、代码审查要点与逻辑验证结论

- 资源管理：无新增句柄/计时器；DeferWindowPos 失败路径有逐窗兜底。✅
- 异常安全：纯 Win32 布尔调用，无异常路径新增。✅
- 线程安全：仅 UI 线程调用（标题按压事件），与原实现一致。✅
- 窗口交互：未点击格子从「2 次带迁移」降为「1 次同带平移」；`SWP_NOACTIVATE` 保持不抢焦点；回落链路未动。✅
- 性能：DWM 重合成次数从 2N-1 次带迁移降为 1 次（active）+ 1 次合批平移；点击路径更快。✅
- 一致性：新方法与 `ApplyWindowOrderHighestToLowest` 的 DeferWindowPos+兜底风格一致；日志沿用 `[ZOrder]` 前缀（`Title group raised batch/fallback`）。✅
- 回归：x64 全量 2998/2998 通过（含 WidgetZOrderRestoreContractTests 层级契约）。
