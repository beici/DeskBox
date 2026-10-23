# 第 5 轮：格子比屏幕还大时只挪位置不缩小，标题栏按钮跑到屏幕外

## 人话摘要

1. 格子的大小是按"逻辑像素"存进布局文件的，程序只保证它的**下限**（不比最小宽度窄），没有上限。
2. 用户在一台大显示器上把格子拉得很宽（比如 4K 屏上 2400 宽），之后换到小屏（笔记本、拔掉外接屏、远程桌面）——启动时程序只把格子的**左上角**挪回屏幕内，宽度原样保留。
3. 结果是格子右边缘伸出屏幕，标题栏右侧的关闭/设置按钮正好落在屏幕外，鼠标点不到；右下角的缩放握把也在屏幕外，拖不回来。
4. 除了手改 `widget-layout.json` 或走设置里的重置，用户没有别的办法把格子救回来。

## 证据

`src/DeskBox/Services/WidgetPositioningService.cs:271-303`（修复前）

```csharp
public static RectInt32 EnsureVisible(RectInt32 bounds, RectInt32 workArea)
{
    bool isWildlyOffscreen = ...;

    if (isWildlyOffscreen)
    {
        return new RectInt32(
            workArea.X + FallbackOffset,
            workArea.Y + FallbackOffset,
            bounds.Width,     // 尺寸原样保留
            bounds.Height);
    }

    int maxX = Math.Max(workArea.X, workArea.X + workArea.Width - bounds.Width);
    int maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - bounds.Height);
    return new RectInt32(
        Math.Clamp(bounds.X, workArea.X, maxX),
        Math.Clamp(bounds.Y, workArea.Y, maxY),
        bounds.Width,          // 尺寸原样保留
        bounds.Height);
}
```

`Math.Max(workArea.X, …)` 这一句是"尺寸超过工作区时把左上角对齐"的兜底，注释（`:281-288`）也承认了：
*"this fallback is only a fixed point … For wider widgets the anchor capture at (x+32, y+32) flips the anchor side"*——
写注释的人已经意识到"比工作区还宽"这种情况存在，但只讨论了锚点回读的稳定性，没有处理"用户够不到按钮"。

尺寸只有下限没有上限：`WidgetPositioningService.cs:508-515` `ResolvePhysicalWidth` 用
`Math.Max(SettingsService.MinWidgetWidth, config.Width)`，`ResolvePhysicalHeight` 同理。
两条调用路径都会走到 `EnsureVisible`：`:111`（`ResolveBoundsCore`，启动/拓扑变化时的主路径）与 `:501`（旧坐标版本路径）。

## 触发路径（真实场景，不需要手改文件）

0. **不需要手改 JSON 的最短路径**：1366×768 笔记本、缩放 150% → 工作区约 911×480 DIP；设置里"默认格子宽度"允许到
   1200（`SettingsService.cs:2375-2389` 的夹取区间是 `[MinWidgetWidth, 1200]`）。此时新建一个格子，
   `InitialFileWidgetPlacementPolicy.CalculateRightAlignedBounds` 会按 1200 DIP × 1.5 算出 1800 物理像素宽，
   `EnsureVisible` 只把 X 夹到 0，窗口就是 1800 宽挂在 1366 宽的屏上——右侧约 434 px（含关闭按钮）永远在屏幕外，
   重启也不回来。
1. 在 3840×2160 的显示器上把某个格子的宽度拖到 2400（逻辑像素）。
2. 拔掉外接屏，只剩 1920×1080 的笔记本屏（工作区约 1920×1040）。
3. 重启 DeskBox 或触发显示拓扑变化 → `ResolveBoundsForCurrentTopology` → `ResolveBoundsCore` →
   `ResolvePhysicalWidth` 得到 2400 → `EnsureVisible` 把 X 夹到 0，宽度仍是 2400 → 右边缘超出 480 px。

标题栏按钮在标题栏右端（`WidgetShell` 布局），缩放握把在右下角，两者都在屏幕外。

## 根因

"可见"只被定义成"位置在屏幕内"，没有被定义成"尺寸放得下"。`EnsureVisible` 这个名字承诺的是可见性，但它只夹了位置。

## 候选方案

| 方案 | 消除根因 | 回归面 | 新概念依赖 | 可验证性 | 架构一致性 | 合计 |
| --- | --- | --- | --- | --- | --- | --- |
| A. 在 `EnsureVisible` 里把宽高夹到工作区（单一收口，两条调用路径都覆盖） | 5 | 5 | 5 | 5 | 5 | **25** |
| B. 只在 `ResolveBoundsCore` 里夹（旧坐标版本路径漏掉） | 4 | 5 | 5 | 4 | 4 | 22 |
| C. 在窗口 `Show`/`Resize` 时夹（晚于布局计算，握把位置已经错） | 3 | 4 | 4 | 3 | 3 | 17 |
| D. 只加"重置尺寸"菜单项 | 2 | 5 | 3 | 3 | 3 | 16 |

采用 **A**。`EnsureVisible` 已经是位置收口的唯一入口，把尺寸也收在这里，语义与名字一致，且不需要新概念。

## 改法

`src/DeskBox/Services/WidgetPositioningService.cs` `EnsureVisible`：

- 入口先算 `int width = Math.Min(bounds.Width, workArea.Width);`、`height` 同理，后续的越界判断与返回值都用
  `width`/`height`。
- 注释里明确写下**已知代价**：启动归一化路径会把解析出的 bounds 写回配置，所以这里夹掉的尺寸会被持久化（见"风险与遗留"第一条）。

## 验证方法

- 新增单测 `WidgetPositioningServiceTests.EnsureVisible_ShrinksBoundsThatExceedTheWorkArea`：
  - 工作区 `1920×1040`，输入 `(0, 0, 2400, 1600)` → 期望 `(0, 0, 1920, 1040)`；旧代码返回 `(0, 0, 2400, 1600)`，必红。
  - 放得下的 `(100, 100, 800, 600)` 尺寸必须原样保留。
- 既有测试必须全绿：`EnsureVisible_ClampsOversizedMarginTargetsBackIntoTheWorkArea`（断言 1500 宽在 1600 工作区内不变）、
  `WidgetBatchMarginPositioningContractTests`（边距对话框的收口契约）、`WidgetPositioningServiceTests` 全部。
- A/B：先在旧代码上跑新测试确认红，再跑修复后确认绿。

## 风险与遗留

### 已知代价：夹出来的尺寸会被写回配置（P2，本轮不修，需产品决策）

- 事实链：`WidgetManager.cs:3109` `ResolveBoundsForCurrentTopology(config)` → `:3126`
  `UpdateConfigFromPhysicalBounds(config, safeBounds, selectedWorkArea)` → `:3128-3140` 判定"宽/高变了" →
  `:3147` `_settingsService.UpdateWidget(...)`。这条归一化在**每次**格子窗口创建时都会跑（`WidgetManager.cs:2889`）。
- 后果：3000 px 宽的格子在小屏上启动一次，配置里就变成 1920；回到 4K 屏时它不会回到 3000，用户得重新拖一次。
  同样的单向收缩在本轮之前就存在于拓扑映射路径（`WidgetTopologyLayoutService.cs:1282-1289` 夹到目标屏工作区、
  `:1553` `config.Width = layout.Width`），所以"换屏后尺寸变小"不是本轮引入的，但本轮让它扩展到"每次在小屏上启动都收缩"。
- 为什么先这样：另一种做法是**显示尺寸与存储尺寸分离**（配置永远保存用户选的尺寸，只在解析/应用窗口大小时夹取），
  这需要改 `UpdateConfigFromPhysicalBounds` 的语义、拖动/缩放提交路径（`WidgetManager.ScreenHome.cs:33-99`）、
  拓扑映射，以及"设置界面显示的宽度"与实际窗口宽度的一致性。属于跨子系统的语义改动，不该塞进一个 P1 修复里。
- 建议的产品决策：是否接受"在小屏上打开一次就永久变窄"，还是投入上面那条根因修复。当前选择是"够得到按钮"优先于"记住尺寸"。
- 次要：设置界面显示的宽度与窗口实际宽度可能短暂不一致（配置仍是 2400、窗口 1920），用户一旦拖动/调整就写回实际值。
- 工作区极小（例如 0 宽）时 `Math.Min` 会把尺寸压到 0，与既有 `Math.Max(workArea.X, …)` 兜底同样属于"异常拓扑"范畴，
  未新增处理。
- 格子内部的布局在极窄宽度下的表现（最小宽度以下的挤压）不在本轮范围：`SettingsService.MinWidgetWidth` 仍保证下限。
- 缩放握把路径（`WidgetWindowBase.Interaction.cs:584-644` `ResolveInteractiveResizeBounds`）同样只有下限没有上限；
  但鼠标拖拽受屏幕边界限制，实际拖不出比屏幕更宽的窗口，故不构成独立缺陷（记录备查）。
