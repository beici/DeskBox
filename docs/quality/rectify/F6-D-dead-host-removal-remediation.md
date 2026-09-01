# F6 批次 D 整改报告（DEF-027 死宿主删除 + 架构文档修订）

> 整改日期：2026-09-01 ｜ 基线：`12dbbf2`（批次 C 之后）｜ 分支：`wip/fix-bug` ｜ 方式：Linux 静态迭代，**本批未经编译验证，Windows 侧门禁与随记人工回归见 `pending-windows-gate.md` 与 `F6-D-quickcapture-regression-checklist.md`**。

## 这批做了什么（一句话版）

随记格子曾经有一套专用的宿主窗口（`QuickCaptureWidgetWindow`，13 个文件、约 7,800 行）。自从 B2 配色功能移植到共享 surface（RECT-3）后，这套宿主就再也没被创建过——是纯死代码，但它还带着一批已经过时的契约测试和两份仍写着「双宿主」的架构文档。本批把宿主整体删除，把所有引用它的契约测试迁到现役实现上，把文档改成与代码一致，并产出了一份「删除前随记功能基线」回归清单供 Windows 侧人工验证。

## 根因链（为什么会留下死宿主）

1. **B2 配色移植（RECT-3）**：发现功能挂在零实例化的死窗口上后，实现被移植到生产宿主 `QuickCaptureSurfaceContent` + `ContentWidgetWindow` 菜单入口——但当时的整改范围只做移植，没删旧宿主（台账 BATCH2-F2「旧死窗口实现待独立清理」）。
2. **DEF-009 修复（R6-P1）**：死宿主内 2 处剪贴板写按「随 DEF-027 处置」跳过，进一步固化了「等批次 D」的约定。
3. **R6 立案（ARC-01）**：把「文档-代码结构性漂移 + 死代码」合并立案为 DEF-027，整改=修订文档+删死宿主+清理关联测试。

## 修复内容

### 1. 删除死宿主（13 文件）

`src/DeskBox/Views/QuickCaptureWidgetWindow.{Appearance,Attachments,Detail,Editing,ItemActions,Items,Menus,ResponsiveDetail,SelectionAndDrop,TransientState,WindowInteraction}.cs` + `.xaml` + `.xaml.cs`（7,796 行）。

随删除自然消除：QC-04（详情复制 async void 无保护）、QC-09 的宿主侧残留、DEF-016（失活回落缺门控——生产路径本就有正确门控）、死宿主内 3 处剪贴板写豁免位点（剪贴板写总量 12→8，全部仍配对）。

全仓 grep 复核：生产源码仅剩 `WidgetWindowBase.cs` 的类注释提及（已同步改写）+ 一个测试文件的注释。

### 2. 契约测试修订（18 个文件，全部指向生产面或删除宿主专属用例）

| 测试文件 | 处置 |
|---|---|
| ContentDeletionDirectContractTests | 删除入口契约收敛到生产 surface |
| QuickCaptureSettingsRuntimeContractTests | 格式契约更新为 DEF-011 后语义（编辑保自身格式）；Enter/Ctrl+S 契约去掉死窗口断言 |
| ItemHoverActionContractTests | Pin 徽标契约只留生产 surface |
| QuickCaptureImagePreviewContractTests | 缩略图契约去掉 legacy XAML/Attachments 断言 |
| MarkdownAndSplitterContractTests | 两处死窗口 XAML 断言移除 |
| FrostedActionSurfaceContractTests | 两处死窗口 acrylic/几何断言移除 |
| DpiAdaptiveLayoutContractTests | 标题行契约收敛到 WidgetShell/switcher |
| WidgetForegroundContractTests | 三处：菜单契约单宿主化、Root 语义刷契约单宿主化、`RootGrid.Resources` 资源域断言随宿主删除（生产消费者 markdown/popover/todo 仍锁定） |
| WidgetCoordinatedMoveContractTests | Ctrl 拖拽契约单宿主化 |
| QuickRevealLayerContractTests | ClearTopMost 契约单宿主化 |
| WidgetCompactTrayVisibilityContractTests | 两处 Theory 数据行移除 |
| WidgetMoreMenuPlacementContractTests | 菜单消费方契约单宿主化 |
| WidgetDangerActionStyleTests | 危险操作样式契约单宿主化 |
| WidgetPresentationAndDetachContractTests | 两处 Theory 数据行移除 |
| WidgetVisualActivityContractTests | 三处（挂起时序/恢复时序/代际取消）Theory 行移除 |
| SolidColorBackdropTests | 背景契约单宿主化 |
| AotStage4E2ContractTests | 内联编辑器契约收敛到 Todo（幸存消费者） |
| AotStage5B4B2B1ContractTests | AOT 绑定契约收敛到共享 surface |
| LocalizationResourceContractTests | 异常消息面清单去掉死文件 |
| MultiSelectionKeyboardContractTests | Delete 键契约改指生产 surface（`GetSelectedQuickCaptureItemsInVisibleOrder` 在 :2614 仍存在） |
| SettingsCopyAndHierarchyTests | 预览行数契约单宿主化 |
| QuickCaptureMultiDragTests | 死窗口手动拖放用例删除（共享 surface 等价用例已在） |
| QuickCaptureMaterialRefreshContractTests | LegacyWindow 刷新契约删除（生产面契约保留） |

### 3. 架构文档修订（一次性消除 WIN-01/05/06 + 双宿主矛盾）

| 文件 | 修订 |
|---|---|
| `current_architecture.md` | 4 处：provider 路由（QuickCapture → ContentWidgetWindow）、Current windows 清单、QuickCapture 专节（改述共享 surface 路径）、动画/布局节的 users 列表 |
| `[重要勿删]widget_zorder_lifecycle.md` | 6 处：§1 宿主表改单宿主（附演进注记）；§3.3-3 `EnsureRaisedFromTrayTopMost` 的 `_isAtDesktopLayer` 短路失效校正（**WIN-05**：现行实现 `!Visible` 早退，已可见格子每次 BringToFront+Hold，多次带迁移为台账有意保留）；§3.3-8 与 §4 的「两个宿主类」表述；坑 #5「四象限（2 宿主×2 模式）」改「两层级模式」；§8-D 可观测行（标注 F6-A 已在 Content 宿主补齐）、§8-D 看门狗行（标注 F6-A 以 `StartInteractionLeakWatchdog` 落地——**WIN-01 文档-代码矛盾闭环**）；**WIN-06** 的 §8 记录行同步现状 |

核心 z-order 约定（瞬态置顶、组恢复定界、peer-only 空闲整理、HWND_BOTTOM 限制、鼠标高位采样、抑制窗/代际）**零触碰**——本批只动文档的宿主数量描述与已失效细节。

### 4. 随记全功能人工回归清单

`rectify/F6-D-quickcapture-regression-checklist.md`：7 大类 30 项（记录与显示/详情编辑/删除与撤销/自写忽略/配色/拖拽与附件/宿主窗口），含 DEF-011/012/013/023 的验证点。Windows 侧构建后执行，通过后本删除方可视为完全闭环。

## Linux 静态门禁

| 检查 | 结果 |
|---|---|
| 12 语言键一致 | PASS（2559×12 不变） |
| async void | **222（基线 263→222，-41 均为死宿主内的 async void 事件处理器）** |
| 剪贴板写配对 | SetContent 12→8（死宿主 3 豁免+1 生产重复计数修正），全部配对 |
| 同步等待 | 131 = 批次 C 后基线 |
| 空 catch | 221（-2，死宿主） |
| 反射 | 6（-1，死宿主） |
| 契约断言重放 | 失联 43→42（随死代码消除），新增 0，PASS |

基线文件已随死代码消除同步刷新（`scripts/quality/static-baseline.json`），后续批次以新基线对比。

## 等价性论证

- **运行时行为零变化**：死宿主零实例化（RECT-3 审计 A 95% 置信度 + 本次删除前全仓引用复核：生产源码零引用），删除不改变任何可达路径。
- **契约测试语义保持**：每条迁移动都先在生产面找到等价锚点（如 `GetSelectedQuickCaptureItemsInVisibleOrder` → surface :2614；`WidgetForegroundMenuBuilder.Create` → ContentWidgetWindow.Commands）才改写；找不到等价锚点的宿主专属用例整体删除而非弱化。
- **文档修订不触碰核心约定**：[重要勿删] 的机制性章节（§2 瞬态置顶、§4 回落信号、坑 #1-#4/#6-#7）原文未动。

## 回滚方式

`git revert <batch-D-commit>` 整体还原（文件删除+测试修订+文档修订同批）；或 `git checkout 12dbbf2 -- src/DeskBox/Views/QuickCaptureWidgetWindow*` 恢复宿主文件后按需恢复测试文件。**注意**：回滚前必须确认 Windows 侧回归清单未发现生产面行为漂移（漂移才需要回滚，死宿主本身不需要）。
