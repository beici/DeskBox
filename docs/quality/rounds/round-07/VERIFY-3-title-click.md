# VERIFY-3 验证版：点击格子标题栏闪烁根因再定位与修复

- 归属：遗留问题清单批次（项③）｜ 关联：DEF-006 ｜ 验证方式：代码层面审查 + 自动化回归；运行时人工复验由用户执行

## 一、根因再定位（为什么修复后仍见闪烁）

R2 修复消除了 peers 的 **TOPMOST 往返**（R4 像素 diff 已证 changed=0——但采样区域为「工具」胶囊与「办公文档」的**局部小区域**）。用户在本轮人工测试中仍见「所有格子一起闪烁」，复检后定位到残余机制：

**标题点击仍会抬升整组格子**（`ActivateAllVisibleWidgetsFromTitle` 把全部可见 peers 经 `DeferWindowPos` 批量插入被点击格子之后）。虽然不再有 TOPMOST 往返，但**任何 z 序移动都会改变丙烯酸背景窗口"身后"的内容**——peers 背后的墙纸/应用被交换，丙烯酸重新采样即整窗视觉抖动。R4 的局部采样区域恰好落在未发生相对内容变化的区域，掩盖了这一残余。结论：**丙烯酸格子上，任何非必要的 z 序移动本身即闪烁源**；唯一结构性解法是 peers 完全不动。

## 二、最小侵入修复（已实施）

`ActivateAllVisibleWidgetsFromTitle`（`WidgetManager.ZOrder.cs`）：标题点击**只抬升被点击的格子**——`handles` 收敛为 `[activeHwnd]`，peers 不再进入 `TrackTemporarilyRaisedWidgets`/`BringTitleActivatedGroupToFront`/2300ms 兜底恢复链路，即**零 SetWindowPos、零 owner 变更、零重合成**。

行为语义：点击标题 = 被点击格子浮到最前（其标题弹出菜单由该窗口拥有，可见性不受影响）；其余格子保持原位原 z（视觉绝对稳定）。整组唤起仍可经 F7/托盘（`BringGroupTemporarilyToFront` 托盘语义保留，属功能本体）。2300ms 兜底与 release 恢复链路仅作用于 active 一个窗口。

## 三、代码验证要点结论

- **点击传播**：`ShouldOpenTitleBarFlyout` 排除按钮/文本框后仍走激活链路；peers 无 hit-test 介入。✅
- **重绘范围**：peers 无任何 SetWindowPos——重绘严格限定在被点击格子自身（其激活/浮起是用户直接交互的对象）。✅
- **交互不破坏**：选中（PointerPressed→激活）、展开/收起（collapse 链路）、拖拽（BeginWindowDragCore）均在 active 窗口本地，不涉及 peers；恢复（title-released）仅恢复 active。✅
- **快速连续点击**：每次点击 active 都是同一窗口，`TrackTemporarilyRaisedWidgets` 幂等登记；兜底 generation 机制防旧回调恢复。✅
- **展开/收起状态**：胶囊态标题点击走 `QuickCaptureShell` 抬升路径（同函数），行为一致。✅
- 回归：x64 2998/2998（含 `WidgetZOrderRestoreContractTests` 层级契约与 `Windows10WidgetMotionContractTests`）。

## 四、人工复验清单

1. 多格子 + 打开应用窗口场景：快速连续点击不同格子标题 → 其余格子视觉零抖动；被点击格子浮起并弹菜单。
2. 展开态/胶囊态两种格子分别点击标题 → 各自正常浮起。
3. 拖拽标题移动格子 → 移动流畅，释放后回落正常（title-released 恢复链路未变）。
