# DEF-002 胶囊悬停自动展开偶现无响应（需点击桌面空白后恢复）

- 优先级：P1 ｜ 状态：已修复（代码 + 契约测试更新，回归通过）｜ 修复轮次：R1

## 一、问题现象

- **复现步骤**：格子设置为【鼠标悬停自动展开】（Smart 胶囊）模式 → 鼠标悬停在折叠胶囊上 → 偶现不展开；此后反复悬停均无响应，直到用户点击一次桌面空白处，再悬停即恢复。
- **触发条件**：胶囊模式 + 悬停展开；悬停期间胶囊主体上的 ToolTip（「展开」提示）弹出并保持。
- **影响范围**：所有 Smart 胶囊格子（两种宿主共用同一状态机）。
- **风险等级**：高（高频交互路径的「功能假死」体验）。

## 二、根因分析（源码级，已经主流程复核子代理结论）

判定函数 `WidgetCompactInteractionPolicy.CanHoverExpand`（`src/DeskBox/Services/WidgetCompactInteractionPolicy.cs:58-76`）要求 `!snapshot.HasActiveInteraction`，而：

1. `HasBlockingSurface` 的实现 `WidgetWindowBase.HasBlockingFlyoutOpen()`（`src/DeskBox/Views/WidgetWindowBase.cs`）把 **XamlRoot 内任意打开的 Popup** 一律计为阻塞面（旧实现：`VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count > 0`）。
2. 胶囊主体恰好挂了 ToolTip：`ApplyCompactTooltips()`（`src/DeskBox/Views/WidgetWindowBase.Collapse.cs:1663` 起）对 `CompactBodyElement` 设置 `ToolTipService.SetToolTip(..., "Widget.Compact.Expand")`。悬停胶囊 → ToolTip 以 Popup 形式弹出并保持（指针静止时 ToolTip 不关闭）→ `HasBlockingSurface=true`。
3. 悬停展开计时器 `TryScheduleCompactHoverExpansion`（`Collapse.cs:2098`）在 fire 时刻若 `CanHoverExpand=false` 直接丢弃；120ms 恢复探针虽会重试，但只要指针静止在胶囊上，ToolTip 就一直开着，探针每次都被同一阻塞面挡死 → **指针不动就永不展开**。
4. 用户把指针移到桌面空白并点击：指针物理离开胶囊 → 原生光标复核探针（`SynchronizeCompactHoverFromNativeCursor`）复位指针状态、ToolTip 随之关闭 → 恢复。**「点一下就恢复」的实质是「指针离开 + 阻塞面消失」，点击本身并非恢复条件。**

结论：ToolTip（非交互预览）被误判为交互阻塞面，与悬停目标元素是同一元素，形成「悬停 → 提示弹出 → 展开被抑制」的自锁环。

## 三、优化/修复思路

**选定方案**：`HasBlockingFlyoutOpen()` 逐个检查打开的 Popup，**豁免 ToolTip 弹出窗口**（`popup.Child is ToolTip` 或其父为 ToolTip 的包装），其余 Popup（菜单、flyout 等）维持阻塞语义不变。ToolTip 本身不可交互，永不构成「正在交互」的合法理由。

**备选方案（未采用，留待后续轮）**：
- 在计时器丢弃路径改为「重排重试」：治标，阻塞面仍在时反复空转。
- 让 shell 上报真实「新进入」语义以修复 suppression×stale-pointer 链（子代理候选 B）：改动面大，列入 R2 观察。

## 四、拟修改代码模块与功能说明（已实施）

| 文件 | 改动 |
|---|---|
| `src/DeskBox/Views/WidgetWindowBase.cs` | `HasBlockingFlyoutOpen()` 重写为逐 Popup 判定 + 新增 `IsToolTipPopup()` 私有判定 |
| `tests/DeskBox.Tests/WidgetCompactTrayVisibilityContractTests.cs` | 契约测试从「必须有 `.Count > 0`」改为钉住新语义：仍使用 `GetOpenPopupsForXamlRoot`、必须存在 `IsToolTipPopup(popup)` 豁免、非 ToolTip Popup 必须 `return true` 阻塞 |

## 五、风险评估

- **可能引入**：ToolTip 显示期间展开胶囊——ToolTip 会在内容布局变化时自动重定位/关闭，视觉影响为瞬时提示框，可接受；若实测发现干扰，可将豁免收紧为「展开动作触发前先关闭 ToolTip」。
- **不改变**：菜单/flyout/弹层的 light-dismiss 与阻塞语义；`InteractionDepth`、拖拽、缩放等其它阻塞条件。
- **契约兼容**：源码形状契约同步更新，防回归语义保留（豁免被钉住，未来误删会被测试拦截）。

## 六、验证方案

1. **自动化**：x64 全量回归 2998/2998 通过（含更新后的契约测试）。
2. **场景复现（目标机）**：
   - 悬停胶囊至 ToolTip 出现并保持不动 → 应在配置延迟后正常展开（修复前此处卡死）。
   - 悬停期间右键打开菜单 → 不得展开（阻塞语义保留）。
   - 拖拽/缩放/编辑标题期间悬停 → 不得展开。
   - 展开后移出胶囊 → 按配置自动收起，再悬停可再展开。
3. **性能红线**：判定为 O(打开 Popup 数)（通常 ≤1），仅在交互决策路径调用，无帧路径影响。
