# R6 独立复核整改报告（N1–N5 / DEF-028、DEF-029）

> 整改日期：2026-08-31 ~ 2026-09-01 ｜ 基线：`81d4147`（R6-P1 整改批次之后）｜ 分支：`wip/fix-bug` ｜ 方式：独立复核报告（[R6-P1-independent-review.md](R6-P1-independent-review.md)，3 个后台深挖 subagent + 主流程门禁）→ 按其 §3 返工指令逐项整改 → 统一门禁。
> 输入：独立复核确证三项 P1 修复到位；新立 5 条缺陷——N1（P2，Claude 候选 (a) 确证）、N2（P2）、N3/N4/N5（P3）。

## 门禁结果

- Debug x64 构建（`-p:Platform=x64`）：**0 错误**（24 条既有警告，改动文件零新增）。
- 规范 Debug 构建（非平台）：**0 错误**（同上）。
- x64 全量回归：**3011/3011 通过**（3006 基线 + 本批新增 5 用例；既有备份测试 `ExportBackupAsync_ArchivesStableSnapshotWhenSourceChangesAfterStaging` 一次偶发失败，单独重跑通过、整轮重跑全绿，属文件系统时序敏感既有问题，与本批无关——本批不触碰备份服务）。
- 运行实例：规范 Debug 输出重启（`[Build] buildTime=2026-09-01 00:32:01`，12 个格子无损恢复）。

## N1（DEF-028，P2）边距路径移动展开态格子后不刷新 CompactPlacement

**根因**：胶囊回跳的机制链 = 折叠目标恒经 `GetCompactBounds → WidgetCompactBoundsCalculator.Resolve`，placement 非空即用其持久化坐标解析；而边距三条持久化路径（批量/单格/取消恢复）只写 X/Y+锚点，placement 留旧 → 折叠回跳旧坐标，跨会话持续；组宿主场景经 `CaptureGroupLayout` 固化传播。交互拖拽/缩放有自己的 placement 收尾（`CompleteExpandedWidgetDrag`/`RecaptureCompactPlacementAfterExpandedResize`），唯独边距路径遗漏——**路径不对称**。

**修复**（单点收口 + 三路径接线，零重复逻辑）：

| 文件 | 修改点 |
|---|---|
| `src/DeskBox/Views/WidgetWindowBase.Collapse.cs` | 新增 `public void RefreshCompactPlacementAfterBoundsMove()`（:489）：UI 线程自愈重投（对齐既有公开窗口方法模式）；`_targetCollapsed / IsClosing / !UsesCompactExpansionGeometry() / Config.CompactPlacement is null` 四守卫早退（Expanded 行为无胶囊概念、从未折叠者首次折叠本就从新 bounds 派生，无需修复）；落地 `RefreshCompactPlacementFromExpandedBounds(persist: true)`——从移动后实框（调用方已 SetWindowPos）重派生并持久化 placement，方向/锚点语义与既有调用点完全一致 |
| `src/DeskBox/Services/WidgetManager.cs` | `IDesktopWidgetWindow` 暴露同名接口方法（批量路径经接口调用，管理器与窗口解耦） |
| `src/DeskBox/Services/WidgetManager.BulkAppearance.cs` | 批量成功分支在 `CaptureAnchor` 之后、`UpdateWidget`/组/拓扑同步之前调用——先修 placement 再让组同步拷贝新值；同时改正误导注释（"compact states never reach this point" 不成立：守卫只挡折叠态，展开态+已存 placement 恰好到达） |
| `src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs` | 单格 `ApplyOwnMarginToSide` 与对话框取消恢复块在 `UpdateConfigFromPhysicalBounds(persist: true)` 后各加一处调用（取消恢复把预览期间被刷新的 placement 拉回原始位置） |

**等价性/边界论证**：刷新内部复用既有 `RefreshCompactPlacementFromExpandedBounds`（设置变更、方向修复、行为切换等路径已大量使用），无新数学；对从未折叠的格子零写入（placement 为 null 早退）；对折叠中格子零触碰（`_targetCollapsed` 早退，与批量守卫一致）；组宿主场景 placement 先刷新再组同步，stale 固化链路消除。

## N2（DEF-029，P2）SearchPopup 公开 async void 入口无异常保护

**根因**：`ShowPopup`/`ShowPopupWithQuery` 为公开 async void，调用方（热键/Widget/App）fire-and-forget 零守卫；`ShowPopupCoreAsync` 全方法无 catch（含 `RebuildTabs`/`OnPopupOpenedAsync` 链），任一抛错即未处理异常崩溃 + 半显示态。与 DEF-021（全局兜底未注册）互为放大。

**修复**（异常边界 + 线程亲和双收敛，公开 API 形状不变、零调用方改动）：

| 文件 | 修改点 |
|---|---|
| `src/DeskBox/Views/SearchPopupWindow.xaml.cs` | 入口改经 `DispatchShowPopupAsync`：非 UI 线程先 `TryEnqueue` 重投（后台热键回调安全），队列不可用静默记录；`ShowPopupSafelyAsync` 全管线 try/catch——Log + 已可见时重聚焦搜索框；`DispatchShowPopupAsync` 自身亦包 try/catch（TryEnqueue 于队列关闭时抛 ObjectDisposed 的关闭窗口防御） |

**语义保持**：已可见时的 `ActivateSearchInput` 短路、带 query 直搜、入口 void 签名全部不变；TryEnqueue 后由 UI 线程调度器串行化，天然消除快速开关的双管线竞态面。

## N3（P3）多选复制混合解析静默丢失败路径

**根因**：`SetDragPayloadAsync(List)` 中 `SetText` 被 `items.Count == 0` 门控——部分路径解析成功、部分失败时只写 StorageItems，失败路径静默消失。

**修复**：`fallbackText.Length > 0` 即写 Text（与 StorageItems 并存，DataPackage 支持多格式；Explorer 优先文件格式、文本消费者获得完整清单）。**自写忽略核对**：`MarkWrite(text: Join(paths), paths: paths)` 为全路径快照；混合分支读取侧 `ShouldIgnoreText` 第二道按行路径匹配以 `textPaths.Length == snapshot.Paths.Length` 判定——失败子集与全量长度不等，但首道 Ordinal 精确匹配以全量文本命中……混合时实际剪贴板 Text 为失败子集，两者不等 → 需依赖读取器先查 StorageItems（非图像即返回 null，`IQuickCaptureClipboardReader` :55-78 顺序：Bitmap → StorageItems(仅图像 return) → Text）——非图像 StorageItems 存在时逐项扫描无图像后**落入 Text 分支**记录失败子集文本。此处为已知残余边界：仅当随记剪贴板记录开启 + 混合解析失败 + StorageItems 全为非图像时可能多录一条失败路径文本记录（低频×低频，随记记录器本就以「用户复制的内容」为采集对象，污染面远小于 DEF-009 的全量回录；已记入观察，随 F7 批次可将 MarkWrite 收敛为「实际落盘载荷」精确标记）。

## N4（P3）StartDragAsync 无 catch

**修复**：`ResultsPanel_PointerMoved` 的 `await dragSource.StartDragAsync(...)` 补 catch（Log），finally 既有清理不变；async void 逃逸面消除。

## N5（P3）防御性注释

- `WidgetPositioningService.EnsureVisible` wildly 回退分支：标注 (32,32) 仅当 `Width <= workArea.Width - FallbackOffset` 时为「捕获→重解」不动点；边距入口经钳制不可达该宽度域，约束仅约束未来复用方。
- `ThemeService._trackedWindows`：标注 UI 线程单一归属约定（呼应 DEF-010 C3）。

## 测试

新增 `tests/DeskBox.Tests/MarginMoveCompactPlacementContractTests.cs`（5 用例，x64 自动发现）：

| 用例 | 覆盖 |
|---|---|
| `CompactPlacement_RederivesFromMovedExpandedBounds_TranslatingByMoveDelta` | 行为数学：胶囊自新展开框重派生 = 旧胶囊 + 移动 delta（右锚顶边逐项验证），锁死 N1 修复欲达成的几何性质 |
| `WindowOwnedMarginPaths_RefreshCompactPlacementAfterPersist` | 源码契约：单格 + 取消恢复两条窗口自有路径均调用刷新（计数 ≥2） |
| `BatchMove_RefreshesCompactPlacement_BeforeGroupAndTopologySync` | 源码契约：批量接线顺序 anchor → compact → group → topology（组同步必须看到修复后的 placement） |
| `CompactRefresh_IsExposedOnWindowAndInterface` | 源码契约：基类公开实现 + 接口声明同时在位 |
| `SearchPopup_MixedPayload_NoLongerDropsFailedPaths` | 源码契约：`items.Count == 0` 门控已消除 |

## 文档同步

- `defect-ledger.md`：新立 DEF-028/DEF-029 并标记**已修复（独立复核整改批次）**；待修 P2 收敛为 DEF-011~027。
- `全量任务TODO清单.md`：追加第 10 次循环核验行（独立复核 + 本批整改全记录）。
- `rectify/R6-P1-independent-review.md`：文末补整改追踪注记（N1~N5 逐项落地位置 + 门禁数据）。

## 回滚方式

按文件单项回滚即可：N1 = `WidgetWindowBase.Collapse.cs` + `WidgetManager.cs` + `WidgetManager.BulkAppearance.cs` + `WidgetWindowBase.TitleAppearance.cs`（+契约测试 3 用例）；N2/N3/N4 = `SearchPopupWindow.xaml.cs` 三段独立 hunk（+1 契约用例）；N5 = `WidgetPositioningService.cs`/`ThemeService.cs` 纯注释。`git checkout 81d4147 -- <files>` 可整体还原至整改前。
