# R6-P1 缺陷三角核对（交叉验证参照系）

> **快照时点**：2026-08-30 20:15–20:35 +0800，基线 commit `359aeeb`。
> 三个修复专项 subagent 正并行改码：**20:35 快照时工作树已出现进行时改动**（`WidgetManager.BulkAppearance.cs`、`QuickCaptureSurfaceContent.xaml.cs`、`SearchPopupWindow.xaml.cs`、`QuickCaptureClipboardServiceTests.cs`，详见各节"进行时观察"）。本文所有 file:line 与代码摘录均为**修复前状态**（20:15–20:32 读取），行号对照报告的漂移已逐条标注。交叉验证 subagent 审查修复 diff 时请以本文证据节为"修复前基线"。

三个缺陷的裁决总览：

| 缺陷 | 裁决 | 一句话 |
|---|---|---|
| DEF-008 | **CONFIRMED** | 锚点缺失 / 组宿主不同步 / 落点无钳制三项核心均成立；LAY-02 位置锁与压缩态守卫已在树内修复 |
| DEF-009 | **CONFIRMED** | Surface Ctrl+C 写入无标记成立；SearchPopup 三处移交项全部成立且生产可达（A/B 的回录面比报告表述窄，见 §2.4） |
| DEF-010 | **CONFIRMED** | Task.Run 线程池刷新主题成立；另发现报告未点名的 2 个无防护订阅者（§3.4） |

---

## 1. DEF-008 批量边距应用不刷新位置锚点且无屏幕钳制

来源：S04 LAY-01/LAY-03 + S07 CFG-01（合并立案）。修复专项文件集见 §4。

### 1.1 证据（当前树精确定位）

**E1 · 批量路径只有 SetWindowPos + UpdateConfigFromPhysicalBounds，无锚点捕获**
`src/DeskBox/Services/WidgetManager.BulkAppearance.cs:155-179`（报告行号 :67-84 → 漂移至此；漂移原因：commit `359aeeb` 为"最近格子边距"语义引入了快照/others 参数，前置代码加长）：

```csharp
if (!Win32Helper.SetWindowPos(
        hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
        Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE))
{
    continue;
}

var center = new Windows.Graphics.PointInt32(...);
RectInt32 workArea = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest).WorkArea;
WidgetPositioningService.UpdateConfigFromPhysicalBounds(window.Config, target, workArea);  // :171 仅写 X/Y/W/H
_settingsService.UpdateWidget(window.Config, notifySubscribers: false);                     // :172
...
if (anyChanged) { _settingsService.SaveDebounced(notifySubscribers: false); }               // :179
```

注意 ：82-83 的 doc comment 声称 "anchor/monitor bookkeeping stays consistent" —— 修复前与实际行为不符（UpdateConfigFromPhysicalBounds 不做锚点簿记）。

**E2 · 持久化函数只写 X/Y/W/H，不触碰锚点字段**
`src/DeskBox/Services/WidgetPositioningService.cs:245-257`：

```csharp
private static void UpdateConfigFromPhysicalBoundsCore(...)
{
    double scale = GetDpiScale(workArea, dpiScaleProvider);
    config.BoundsCoordinateVersion = WidgetConfig.CurrentBoundsCoordinateVersion;
    config.X = bounds.X;
    config.Y = bounds.Y;
    config.Width = ToLogicalPixels(bounds.Width, scale);
    config.Height = ToLogicalPixels(bounds.Height, scale);
}
```

**E3 · 恢复路径锚点优先于 X/Y**
`src/DeskBox/Services/WidgetPositioningService.cs:70-90`（`ResolveBoundsCore`；:83-87 锚点覆盖 ：80-81 读出的 X/Y）：

```csharp
int x = (int)Math.Round(config.X);
int y = (int)Math.Round(config.Y);

if (HasValidAnchor(config))
{
    x = ResolveAnchoredX(config, workArea, width, scale);   // 用旧 PositionMarginX/Y
    y = ResolveAnchoredY(config, workArea, height, scale);
}

return EnsureVisible(new RectInt32(x, y, width, height), workArea);   // :89 重启拉回工作区（二次漂移）
```

`HasValidAnchor` 在 :445；可复用的公共锚点捕获 API `CaptureAnchor(config, bounds, workArea)` 在 :181-184（`CaptureAnchorCore` :194-229 写 PositionAnchor/PositionMarginX/Y/PositionMonitorKey/DeviceName/WasPrimary）。

**E4 · 单格路径先捕获锚点再持久化（不对称性对照）**
`src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs:512-549`（`ApplyOwnMarginToSide`；报告行号 :466-475 → 漂移至此）：

```csharp
Win32Helper.SetWindowPos(HWnd, IntPtr.Zero, next.X, next.Y, next.Width, next.Height,
    Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE);          // :539-546
CapturePositionAnchor(next.X, next.Y, next.Width, next.Height);      // :547 ← 批量路径缺这步
UpdateConfigBoundsFromPhysical(next.X, next.Y, next.Width, next.Height, persist: true);  // :548
```

`CapturePositionAnchor` 实现于 `WidgetWindowBase.Bounds.cs:499-533`（压缩态分支 ：508-512；中心点选屏 ：514-520；最终调 `WidgetPositioningService.CaptureAnchor` :531）。窗口自身的 `OnAppWindowChanged` 非拖拽早退不补采锚点：`Bounds.cs:595-618`（早退条件 ：602-609，与报告一致）。

**E5 · 组宿主 `WidgetGroupConfig.X/Y` 不同步**
批量路径循环体内只调 `_settingsService.UpdateWidget`（E1 :172），从不调 `SynchronizeGroupLayoutFromMember`（定义于 `WidgetManager.Groups.cs:1604-1618`：仅当 `group.ActiveMemberId == member.Id` 时 `CaptureGroupLayout` + `ApplyGroupLayoutToMembers` + SaveDebounced）。对照窗口自身 persist 路径：`ContentWidgetWindow.xaml.cs:555-588`，persist 分支 :586 调 `SynchronizeWidgetGroupLayout()`（`WidgetWindowBase.Grouping.cs:283-288` = `SynchronizeGroupLayoutFromMember` + `CaptureCurrentTopologyLayout`）。`WidgetGroupConfig.X/Y` 定义于 `Models/WidgetGroupConfig.cs:28-30`。

**E6 · 落点无工作区钳制（LAY-03；语义已被 359aeeb 重写）**
报告引用的 `ShiftBoundsToMargins`(:516-578) / 旧 `ShiftBoundsToNearestEdge`(:584-627) **已不存在**。当前树（commit `359aeeb`）为"最近格子"语义：

- `ResolveSideBoundary`：`TitleAppearance.cs:604-653` —— 每侧参考边界 = 最近的其他格子边缘（8px 平行重叠容差），无邻居时取工作区边缘。
- `ShiftSideToMargin`：`TitleAppearance.cs:668-700` —— `newX = boundary + margin`（:681）等，**返回值无任何 EnsureVisible/工作区钳制**。
- `ShiftBoundsToNearestEdge(bounds, margin, others, workArea)`：`TitleAppearance.cs:707-749` —— 同样无钳制。
- 消费方：批量 `MoveVisibleWidgets`（`TitleAppearance.cs:484,502` 委托）与单格 `ApplyOwnMarginToSide`（:528-533）均直接 `SetWindowPos(target)`，全程不经过 `EnsureVisible`（该对话框链路仍是全仓唯一不钳制的落点计算，与报告结论一致）。重启后 `ResolveBoundsCore:89` 的 EnsureVisible 把越屏格子拉回 → 二次漂移。

### 1.2 已修复面（防误报）

`WidgetManager.BulkAppearance.cs:131-135` 已有守卫（报告审查期间整改，行号从 :72-74 漂移至此）：

```csharp
if (window.Config.IsPositionLocked ||
    window.IsCompactArrangementActive ||
    window.IsCompactCollapsedState)
{
    continue;
}
```

即 LAY-02（位置锁绕过）与 CFG-02（压缩态污染）**不在本缺陷修复范围**，交叉验证时不要把它们当作未修复项扣分。

### 1.3 触发链（修复前）

1. 边距对话框勾选"应用到所有"（`TitleAppearance.cs:254` CheckBox）→ 保存/预览 → `ApplyMarginsFromDialog` per-side 分支 :484 / uniform 分支 :502 → `MoveVisibleWidgets`（对含源窗口在内的全部可见格子执行）。
2. 每个格子：`SetWindowPos` 移动 → `UpdateConfigFromPhysicalBounds` 只写新 X/Y/W/H → `UpdateWidget(notify:false)` → 循环外一次 `SaveDebounced`。锚点字段保持移动前旧值。
3. 重启 / 显示器拓扑切换 / 窗口重建 → `ResolveBoundsCore` :83-87 `HasValidAnchor` 为真（凡拖拽过或创建定位过的格子均满足）→ 按旧锚点边距重建位置 → **已持久化的新 X/Y 被忽略，批量移动整体回退**；越屏落点先被 :89 EnsureVisible 拉回（二次漂移）。
4. 若被移动的是组宿主（ActiveMember）：`WidgetGroupConfig.X/Y` 与 topology profile 保持旧值 → 组的跨会话布局与成员不一致。

### 1.4 进行时观察（20:35 快照）

`WidgetManager.BulkAppearance.cs` 已插入（位于 :171 UpdateConfigFromPhysicalBounds 之后、:172 UpdateWidget 之前）：`CaptureAnchor(window.Config, target, workArea)` + `SynchronizeGroupLayoutFromMember(window.Config)` + `CaptureCurrentTopologyLayout(window.Config)`，共 +11 行。**该进行时 diff 尚未包含 EnsureVisible 钳制（E6 面）**；最终修复是否补齐以交叉验证时 diff 为准。

### 1.5 交叉验证核对单（DEF-008）

| # | 检查点 | 可执行检查动作 |
|---|---|---|
| C1 | **锚点捕获** | 在 `MoveVisibleWidgets` 成功分支（SetWindowPos 成功后、`UpdateWidget` 前）应能看到对每个被移动格子调用锚点捕获——等价于 `WidgetPositioningService.CaptureAnchor(window.Config, target, workArea)` 或窗口侧 `CapturePositionAnchor` 语义；且使用的 `target`/`workArea` 与 :167-171 写 X/Y 所用的完全相同（同一中心点选屏）。抽查 diff 确认没有"只对部分格子捕获"的条件分支（跳过锁定/压缩格子的 continue 在捕获之前，属正确顺序） |
| C2 | **组宿主同步** | 被移动格子属于组且为 ActiveMemberId 时，`WidgetGroupConfig.X/Y`（WidgetGroupConfig.cs:28-30）最终与成员移动后几何一致。允许两种实现：循环内 `SynchronizeGroupLayoutFromMember`，或循环后对受影响组统一同步；但必须覆盖"批量模式把组宿主一起移动"的场景。注意 ：1609-1613 的 ActiveMember 早退是既有语义，不算缺陷 |
| C3 | **拓扑 profile 一致** | 窗口自身 persist 路径的等价语义是 `SynchronizeWidgetGroupLayout()` = `SynchronizeGroupLayoutFromMember` **+** `CaptureCurrentTopologyLayout`（Grouping.cs:283-288）。核对修复是否补了 topology profile 捕获（若只补组 X/Y 而漏 profile，拓扑切换仍回退——降级扣分而非直接判失败） |
| C4 | **落点钳制（LAY-03）** | `MoveVisibleWidgets` 对 transform 返回的 target（或在 `ShiftSideToMargin`/`ShiftBoundsToNearestEdge` 内）套 `WidgetPositioningService.EnsureVisible(target, workArea)`（PositioningService.cs:259）或等价钳制。验证用例：格子宽 ≈ 工作区宽、右侧参考边界为工作区右缘、margin=200 时不再越出工作区。**若修复 diff 完全未触碰钳制面，此项判失败**（LAY-03 与锚点同包立案，缺任一半即未闭环） |
| C5 | **单格路径对称性** | 修复后批量路径持久化序列与单格路径（TitleAppearance.cs:539-548：SetWindowPos → CapturePositionAnchor → UpdateConfigBoundsFromPhysical(persist:true)）语义一致；不改变 `WidgetWindowBase.Bounds.cs` compact 分支与 `OnAppWindowChanged` 早退语义（整改原则 §7.4） |
| C6 | **守卫保留** | :131-135 的 IsPositionLocked / IsCompactArrangementActive / IsCompactCollapsedState 守卫不被删除或绕过；`GetOtherVisibleWidgetRects`/others 最近格子语义不被破坏 |
| C7 | **回归门禁** | 既有 `tests/DeskBox.Tests/WidgetPositioningServiceTests.cs` 全绿；补"批量边距→重启→位置保持"回归用例（S07 §5 回归清单第 ① 项）；建议补"塌缩格子在场时批量边距→展开几何完好"（第 ② 项，已被既有守卫覆盖，验证不回归即可） |
| C8 | **写放大可控** | 循环内逐格子 `SynchronizeGroupLayoutFromMember` 会触发多次 SaveDebounced——确认修复未引入同步 IO（SaveDebounced 本身防抖，纯调用计数增加可接受） |

---

## 2. DEF-009 随记剪贴板写入未标记"自写"

来源：S05 QC-01 + 移交段（SearchPopupWindow 三处）。自写忽略机制：`Services/DeskBoxClipboardWriteScope.cs`（`MarkWrite` :13-33 记录 2 秒窗口快照 ：5；`ShouldIgnoreText` :50-83 支持纯文本与"路径列表型文本"两种匹配）。采集链闸门：`Services/QuickCaptureClipboardService.cs:194`（`ShouldIgnore` 失败 → `AddRecentClipboardItemAsync` 入库）。

### 2.1 核心位点（Surface 选中复制）——证据

`src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs:2584-2625`（`CopySelectedQuickCaptureItemsAsync`；报告行号 :2603-2608 → 写入实际在 :2612-2618）：

```csharp
string text = selectedItems.Count == 1
    ? QuickCaptureClipboardFormatter.FormatSingle(selectedItems[0], _localizationService)   // :2600-2603
    : QuickCaptureClipboardFormatter.FormatBatch(selectedItems, _localizationService);      // :2604-2606
...
var dataPackage = new DataPackage
{
    RequestedOperation = DataPackageOperation.Copy
};
dataPackage.SetText(text);                    // :2616
Clipboard.SetContent(dataPackage);            // :2617 ← 无 MarkWrite / MarkClipboardTextWrittenByDeskBox
Clipboard.Flush();                            // :2618
```

触发链：Surface 列表获得焦点 → Ctrl+C（`ItemsList_KeyDown` :2547-2552，全文件唯一触发点）→ 本方法。单项**文本**也走此未标记路径（单项位图被 :2593-2598 `QuickCaptureClipboardCopyPolicy.ShouldCopyBitmap` 分流到已标记的 `CopyItemWithFeedbackAsync`）。右键菜单"复制"（:2189-2195）走 `CopyItemWithFeedbackAsync`（:2114-2132 → `ViewModel.CopyItemAsync`）——已标记，不受影响。

**对照正确实现**：`src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.Operations.cs:380-414`（`WriteItemToClipboardOnceAsync`）——文本分支 :387-388、图像分支 :401-403（`MarkWrite(hasImage:true, paths:[item.ImagePath])`）、兜底分支 :407-409 均先 `DeskBoxClipboardWriteScope.MarkWrite(...)` 再 `Clipboard.SetContent`（:412）。服务侧包装 `QuickCaptureService.MarkClipboardTextWrittenByDeskBox`（`QuickCaptureService.cs:1465-1468`）。

### 2.2 死宿主位点（随 ARC-01 消除，不在本修复范围）

- `Views/QuickCaptureWidgetWindow.SelectionAndDrop.cs:343-352`（`SetClipboardText`，报告 :343 → 当前树复核仍在，:350-351 SetContent+Flush 无标记）。
- `Views/QuickCaptureWidgetWindow.Items.cs:475-478`（OCR 文本复制，报告 :476 → 当前树 :477-478 无标记）。
- 13 个死宿主文件仍在编译（ARC-01/DEF-027 待清理）。对照：死宿主 `Attachments.cs:53`（`MarkClipboardTextWrittenByDeskBox`）与 ：219（`MarkWrite(hasImage:true, paths:...)`）是已标记位点。

### 2.3 移交核查：SearchPopupWindow 三处逐一编号定性

`src/DeskBox/Views/SearchPopupWindow.xaml.cs`，全文件仅此三处剪贴板写入（:4110-4171 的 `SetDragPayloadAsync` 是拖拽载荷，不写剪贴板）。三处均为 DeskBox 主动写入、生产可达（搜索弹窗为生产窗口）、修复前均无任何标记。

| 编号 | 方法 / 写入行 | 触发点 | 载荷形态 | 修复前标记 | 回录可达性（细化） |
|---|---|---|---|---|---|
| SP-1 | `CopySelectedItemsAsync` :3589-3616，写入 ：3604-3605（SetContent+Flush） | Ctrl+C :1837、Ctrl+X :1845、多选右键菜单 复制/剪切 ：3556/:3564、:3679/:3684 | `SetDragPayloadAsync(data, paths)` :3603 → 路径存在时 `SetStorageItems`（:4163-4166）；仅当全部路径解析失败时 `SetText(路径列表)`（:4167-4169） | 无 | **部分可达**：(a) 文本回退分支 → 路径列表文本被采集入库；(b) 载荷含**图像文件**（png/jpg/…）→ 读取器 StorageItems→图像分支（`IQuickCaptureClipboardReader.cs:55-70`）回录为图像记录（需图像记录开关开启）；(c) 普通文件/文件夹 → 无 Text 格式 → 读取器返回 null → "ignored:empty-or-unsupported"，**不产生文本记录** |
| SP-2 | `CopyFileSystemItemAsync` :3816-3839，写入 ：3827-2828（SetContent+Flush） | 右键菜单 剪切 ：3746 / 复制 ：3754 | 同 SP-1 单路径形态（:4110-4134：SetStorageItems 或回退 `SetText(path)` :4133） | 无 | 同 SP-1：文本回退分支与图像文件可达；普通文件/文件夹不可达 |
| SP-3 | `CopyPathToClipboard` :4050-4068，写入 ：4059-4061（仅 SetContent，**无 Flush**） | 右键菜单"复制路径" ：3776 | 纯文本 `SetText(item.DetailPath)` | 无 | **完全可达**：文本直接被采集为一条随记记录（只要开启随记剪贴板记录）；另因缺 Flush，内容存活依赖进程存活，弹窗场景下复制后立即退出应用可能丢内容（次要卫生点，不属于本缺陷判定项） |

三处共同点：只影响"随记剪贴板记录"开启（`QuickCaptureClipboardEnabled`，默认关）时的用户；不影响剪贴板内容本身。严重度低于 Surface 位点（Surface 是随记核心高频路径），但属同型遗漏，S05 移交定性正确。

### 2.4 全文件扫描：报告未点名的剪贴板写入位点

全仓生产代码 `Clipboard.SetContent` 位点清点（`rg "Clipboard.SetContent" src`，修复前状态）：

| 位点 | 标记状态 | 定性 |
|---|---|---|
| `QuickCaptureSurfaceContent.xaml.cs:2617` | **无** | 本缺陷核心位点（§2.1） |
| `SearchPopupWindow.xaml.cs:3604 / 3827 / 4061` | **无** | 移交三处（§2.3） |
| `FileSurfaceContent.xaml.cs:3745`（Shell 剪贴板失败回退分支） | **无**（有 `package.Properties["DeskBoxSourceWidgetId"/"DeskBoxSourcePaths"]` 自有标记，但读取器不读 Properties） | 报告未点名；主路径 `ShellClipboardHelper.TrySetFileDropList` 在前，回退分支罕见；载荷以 StorageItems 为主、仅 `GetStorageItemsAsync` 失败才 SetText。回录面同 SP-1 细化逻辑。**低危观察项**，建议随本批补标记或记录为后续卫生项（不阻塞 DEF-009 验收） |
| `FileSurfaceContent.SelectionAndMenus.cs:1282`（复制路径为文本） | 已标记（:1277-1281 `MarkWrite(text, paths)`） | 正确实现范例 |
| `TodoWidgetContent.ClipboardSelection.cs:107` | 已标记（:104） | 正确 |
| `QuickCaptureWidgetViewModel.Operations.cs:412` | 已标记 | 正确（对照实现） |
| `QuickCaptureWidgetWindow.Attachments.cs:51 / 219`（死宿主） | 已标记 | 正确但属死代码 |
| `QuickCaptureWidgetWindow.SelectionAndDrop.cs:350 / Items.cs:477`（死宿主） | 无 | §2.2，随 ARC-01 消除 |

QuickCaptureSurfaceContent.xaml.cs 全文件扫描结论：**报告点名的 :2612-2618 是该文件唯一剪贴板写入位点**（其余 DataPackage 出现于拖放处理器 ：2416-2465 / :2673-2965，不写剪贴板；文件内无 OCR 复制路径）。

### 2.5 进行时观察（20:35 快照）

- `QuickCaptureSurfaceContent.xaml.cs`：:2616 SetText 后已插入 `DeskBoxClipboardWriteScope.MarkWrite(text: text);`（+1 行）。
- `SearchPopupWindow.xaml.cs`：三处均已插入 MarkWrite（SP-1/SP-2 带 `text` + `paths` 双参数，SP-3 仅 text），+7 行。
- `tests/DeskBox.Tests/QuickCaptureClipboardServiceTests.cs` +35 行。

### 2.6 交叉验证核对单（DEF-009）

| # | 检查点 | 可执行检查动作 |
|---|---|---|
| C1 | **Surface 位点已标记** | `CopySelectedQuickCaptureItemsAsync`（修复前 ：2612-2618）的写入前/写入紧邻处出现 `MarkWrite(text: text)`（或收敛为调用已标记的 `ViewModel.CopyItemAsync`）。**必须覆盖单项文本分支**（FormatSingle，:2600-2603）——只标记多项分支或只改位图分流都算未闭环 |
| C2 | **SearchPopup 三处全标记** | SP-1/SP-2/SP-3 每处 SetContent 前有 MarkWrite；SP-1/SP-2 建议带 `paths` 参数（路径列表型文本依赖 `ShouldIgnoreText` :74-82 的路径匹配才能被忽略；20:35 进行时 diff 已带，核对保留）。逐一核对，漏一处即未闭环 |
| C3 | **标记时序正确** | MarkWrite 与 SetContent 同步紧邻（同一同步段内，不得隔 await——若隔 await，2 秒窗口内 ContentChanged 可能先于标记到达。SP-1/SP-2 的 SetDragPayloadAsync await 在 MarkWrite **之前**属正确顺序） |
| C4 | **死宿主不扩散** | 不要求修复 SelectionAndDrop.cs:343 / Items.cs:476（随 ARC-01 消除）；但修复 diff 不得新增对死宿主的引用 |
| C5 | **既有标记路径不回归** | `Operations.cs`、`Attachments.cs:51/219`、`TodoWidgetContent.ClipboardSelection.cs:104`、`FileSurfaceContent.SelectionAndMenus.cs:1277-1281` 的既有标记调用保持原样 |
| C6 | **回归门禁** | `tests/DeskBox.Tests/QuickCaptureClipboardServiceTests.cs` 全绿（20:35 已见 +35 行用例）；行为验收：开启随记剪贴板记录后，Surface Ctrl+C、SearchPopup 复制/剪切/复制路径均不产生新随记记录 |
| C7 | **（加分项）FileSurface 回退分支** | `FileSurfaceContent.xaml.cs:3745` 回退分支补标记或显式记录豁免理由（报告未点名，不作为否决项） |

---

## 3. DEF-010 启动路径线程池线程刷新主题

来源：S08 THR-01。

### 3.1 证据（当前树精确定位）

**E1 · 启动把整个 RefreshAppearance 丢进线程池**
`src/DeskBox/App.xaml.cs:940-948`：

```csharp
// Parallel: theme refresh only. Clipboard event subscription must stay on the UI thread.
var themeTask = Task.Run(() => themeService.RefreshAppearance());   // :941
RefreshQuickCaptureClipboardService();

// Parallel: independent UI setup
CreateTrayIcon();                                                   // :945 ← UI 线程并行跑，内部 TrackWindow(_trayWindow)
InitializeLifecycleRecoveryWatcher();

await themeTask;                                                    // :948
```

**E2 · RefreshAppearance 本体：遍历无锁集合 + 跨线程 WinUI 读取 + 裸广播**
`src/DeskBox/Services/ThemeService.cs:198-211`：

```csharp
public void ApplyToAllWindows()
{
    foreach (var window in _trackedWindows)      // :200 无锁 List 枚举
    {
        ApplyToWindow(window);
    }
}

public void RefreshAppearance()
{
    ApplyToAllWindows();                         // :208
    AppearanceChanged?.Invoke();                 // :209 后台线程裸广播
    App.ScheduleLightMemoryCleanup();            // :210（该函数线程安全：Interlocked + TryEnqueue，App.xaml.cs:3516-3526）
}
```

`ApplyToWindow`（:157-193）：`window.Content is not FrameworkElement rootElement` 读取在 ：159 —— **发生在 HasThreadAccess 防护（:164-168）之前**，这正是跨线程读取 WinUI 对象（RPC_E_WRONG_THREAD 面）的精确暴露点；防护之后的 `rootElement.RequestedTheme`/`AccentResourceScope.Apply`（:176-177）在后台线程会走 TryEnqueue 重投，不直接崩。

**E3 · _trackedWindows 无锁**
`ThemeService.cs:17` `private readonly List<Window> _trackedWindows = new();`。读写点全量（见 §3.3）：Contains :132 / Add :138 / Remove :151 / 枚举 ：200，无任何锁或并发容器。

**E4 · 托盘订阅者无投递保护**
`src/DeskBox/App.Tray.cs:203` `ThemeService.AppearanceChanged += UpdateTrayIconAppearance;`（位于 `CreateTrayIcon` 流程内，即 ：945 启动并行段执行的代码）；处理体 ：849-857：

```csharp
private void UpdateTrayIconAppearance()
{
    if (_trayIcon is null) { return; }
    string style = SettingsService.Settings.TrayIconStyle ?? "System";
    _trayIcon.Icon = AppBranding.CreateTrayIcon(style, IsDarkThemeActive());   // :855 H.NotifyIcon 句柄操作，无 TryEnqueue/HasThreadAccess
}
```

### 3.2 RefreshAppearance 全部调用方及其线程语境

| # | 调用方 | 位置 | 线程语境 | 判定 |
|---|---|---|---|---|
| 1 | OnLaunched 启动并行段 | `App.xaml.cs:941` | **线程池（Task.Run）** | **缺陷本体** |
| 2 | 系统色变更 debounce Tick | `ThemeService.cs:38`（timer 于 ：29-44 经 `App.UiDispatcherQueue.TryEnqueue` 在 UI 线程创建） | UI 线程 | 安全 |
| 3 | `SetTheme` | `ThemeService.cs:93` | UI 线程（设置命令） | 安全 |
| 4 | `SetAccentMode` | `ThemeService.cs:106` | UI 线程 | 安全 |
| 5 | `SetCustomAccentColor` | `ThemeService.cs:124` | UI 线程 | 安全 |
| 6 | 设置保存应用 | `ViewModels/SettingsViewModel.PreferenceCommands.cs:76` | UI 线程（命令处理器） | 安全 |
| 7 | Onboarding 材料单选 | `Views/OnboardingWindow.Appearance.cs:162` | UI 线程（Checked 事件） | 安全 |
| 8 | 搜索命令 toggle-theme → `ToggleTheme` | `App.xaml.cs:4137`（case :4107-4109；命令注册 `SearchEngineService.cs:719`） | UI 线程 | 安全 |

结论：全仓唯一非 UI 线程调用点即启动路径 :941，与 S08 "每次启动必现（确定性路径）" 定性一致。

### 3.3 _trackedWindows 全部读写点与 TrackWindow 调用方

读写点（均在 ThemeService.cs）：声明 ：17；`TrackWindow` Contains :132 + Add :138；`OnTrackedWindowClosed` Remove :151（window.Closed 事件，UI 线程）；`ApplyToAllWindows` 枚举 ：200（线程归属取决于调用方）。

TrackWindow 调用方（全部 UI 线程语境）：`App.DesktopOrganization.cs:16`、`App.Tray.cs:171`（CreateTrayIcon 内，**与 ：941 线程池刷新并发**——Add 与枚举竞态的具体窗口）、`App.xaml.cs:2732`（Onboarding）、`ReleaseNotesWindow.xaml.cs:74`、`SettingsWindow.xaml.cs:194`、`WidgetManager.cs:2205`（widget 创建，晚于 `await themeTask`）。

启动期并发时序：`Task.Run`（:941）先启动 → UI 线程 `CreateTrayIcon`（:945）→ 其中 ：171 `TrackWindow(_trayWindow)` 做 Contains+Add —— 若后台枚举恰在此交错，`List<T>` 并发 Add/枚举可抛 `InvalidOperationException` 或脏读。这是 S08 无锁集合竞态结论的具体落点，当前树复核成立。

### 3.4 AppearanceChanged 全部订阅者及其线程防护现状（共 7 个）

| # | 订阅者 | 订阅位置 | 防护现状 | 评估 |
|---|---|---|---|---|
| 1 | `App.Tray.cs UpdateTrayIconAppearance` | `App.Tray.cs:203` | **无**（:849-857 直接 `_trayIcon.Icon = ...`） | 报告点名，启动期唯一在 `await themeTask` 完成前订阅的订阅者 → 后台广播实际可达 |
| 2 | `WidgetManager.ApplyAppearancePreview` | `WidgetManager.cs:438`（ctor :961 处 new，**晚于 await themeTask**） | **无**（:668-687 遍历 `GetLoadedDesktopWindows` 调 XAML 外观刷新） | **报告未点名**。因订阅晚于启动刷新，启动期不可达；但若修复选"保留后台广播"方案（S08 方案 b 只重投 RefreshAppearance 本体则广播仍在后台），此订阅者暴露 |
| 3 | `SettingsViewModel.OnAppearanceChanged → RefreshAccentPreview` | `SettingsViewModel.cs:430`（:464-469，写 `AccentPreviewBrush.Color` + PropertyChanged） | **无** | **报告未点名**。SettingsWindow 打开期间才存在；日常刷新均 UI 线程（表 3.2 #3-8），仅"后台广播"方案下暴露 |
| 4 | `SettingsWindow.OnAppearanceChanged` | `SettingsWindow.xaml.cs:195` | **有**（:330-342 HasThreadAccess → TryEnqueue） | 安全 |
| 5 | `SearchPopupWindow.OnThemeServiceAppearanceChanged` | `SearchPopupWindow.xaml.cs:151` | **有**（:474-482） | 安全 |
| 6 | `TodoWidgetContent.OnThemeAppearanceChanged` | `TodoWidgetContent.xaml.cs:167` | **有**（:203-209） | 安全 |
| 7 | `WeatherWidgetContent.OnThemeAppearanceChanged` | `WeatherWidgetContent.xaml.cs:93` | **有**（:111-118） | 安全 |

（S08 报告"Weather/Todo 有防护、托盘无防护"的定性正确；本核对补充 #2/#3 两个无防护订阅者，供修复方案选择时评估覆盖面。）

### 3.5 触发链（修复前）

每次启动：OnLaunched :941 把 `RefreshAppearance` 整体投入线程池 → (a) :200 在后台线程枚举 `_trackedWindows`，与 ：945 CreateTrayIcon 的 TrackWindow Add 并发（List 竞态）；(b) `ApplyToWindow` :159 后台线程读 `window.Content`（RPC_E_WRONG_THREAD 面；此时 _trayWindow 可能尚未 Track，但风险不依赖具体内容）；(c) :209 后台广播 → 托盘订阅者 :855 跨线程改 H.NotifyIcon 句柄。三者均无需用户操作，启动即触发。

### 3.6 进行时观察（20:35 快照）

`App.xaml.cs` / `ThemeService.cs` / `App.Tray.cs` 在 20:35 快照**尚无改动**（DEF-010 修复专项进行中，未落盘或未保存）。

### 3.7 交叉验证核对单（DEF-010）

| # | 检查点 | 可执行检查动作 |
|---|---|---|
| C1 | **启动路径不再后台触碰 WinUI** | `App.xaml.cs:941` 不再出现裸 `Task.Run(() => themeService.RefreshAppearance())`。方案 a：Task.Run 只包离线工作（如读系统色值），RefreshAppearance 留 UI 线程；方案 b：RefreshAppearance 入口 `HasThreadAccess` 判定 + `DispatcherQueue.TryEnqueue` 重投。核对所选方案下 ：948 `await themeTask` 语义仍成立（若改重投，需保证仍可等待/不改变启动并行收益） |
| C2 | **window.Content 读取受线程保护** | 修复后 `ApplyToWindow` 的 `window.Content` 读取（:159）不发生在非 UI 线程；若选方案 b，重投判定不得依赖先读 Content（现有 ：164-168 防护在 ：159 之后的顺序问题必须被消除，不能只是外层再包一层而对 :159 保持裸读） |
| C3 | **_trackedWindows 竞态消除** | 仅当修复保留"后台线程可能触达 ApplyToAllWindows"时需检查：增删（:132/:138/:151）与枚举（:200）加了锁或换并发容器。若选方案 a（全部调用方回到 UI 线程），本项可豁免，但核对时需确认修复文档/注释明示线程归属约定 |
| C4 | **托盘订阅者防护** | `UpdateTrayIconAppearance`（App.Tray.cs:849-857）补 TryEnqueue 投递，或所选方案保证 AppearanceChanged 只在 UI 线程触发（方案 a 下自动满足；方案 b 下必须显式补，因重投只保护 RefreshAppearance 的 XAML 段不保护广播段——核对 diff 中广播语句是否也进了 UI 线程） |
| C5 | **其余无防护订阅者安全性** | `WidgetManager.ApplyAppearancePreview`（WidgetManager.cs:668-687）与 `SettingsViewModel.RefreshAccentPreview`（SettingsViewModel.cs:464-469）在所选方案下不再可能于后台线程被调用（方案 a 下自动满足；方案 b 下需一并处理或论证不可达） |
| C6 | **不回归** | 系统强调色/深浅色切换链（OnColorValuesChanged :29-44 → debounce → RefreshAppearance）保持即时生效；启动日志无新增串行化退化说明（该并行段原意是启动提速，修复不得把整段启动改成串行） |
| C7 | **回归门禁** | 自动化回归全绿；行为验收：连续启动多次无 RPC_E_WRONG_THREAD / InvalidOperationException / 渲染异常；托盘图标在启动后随主题即时正确 |

---

## 4. 三缺陷文件集不相交性（并行修复安全性）

| 缺陷 | 主修复文件（预期） | 次级可能触碰 |
|---|---|---|
| DEF-008 | `Services/WidgetManager.BulkAppearance.cs` | `Views/WidgetWindowBase.TitleAppearance.cs`（钳制面）、`Services/WidgetManager.Groups.cs`、`Services/WidgetPositioningService.cs`（均只读参照）、`tests/.../WidgetPositioningServiceTests.cs` 等 |
| DEF-009 | `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs`、`Views/SearchPopupWindow.xaml.cs` | `Services/QuickCaptureService.cs`、`Services/DeskBoxClipboardWriteScope.cs`（只读参照）、`tests/.../QuickCaptureClipboardServiceTests.cs` |
| DEF-010 | `App.xaml.cs`、`Services/ThemeService.cs`、`App.Tray.cs` | tests（如有） |

**结论：三者主文件集互不相交，无共享文件；次级集合亦无交集**（唯一共享面是同一个测试项目 `tests/DeskBox.Tests`，但预期新增/修改不同测试文件，无合并冲突风险）。20:35 快照实证：进行时改动为 BulkAppearance.cs（008）、QuickCaptureSurfaceContent.xaml.cs + SearchPopupWindow.xaml.cs + QuickCaptureClipboardServiceTests.cs（009），三者互不重叠；DEF-010 文件集尚未出现改动。并行修复安全。

## 5. 与报告不符的意外发现汇总

1. **行号漂移**：DEF-008 全部行号因 commit `359aeeb`（最近格子边距语义）漂移——`MoveVisibleWidgets` :67-84→:155-179（写入点 ：171）、`ApplyOwnMarginDelta` :466-475→`ApplyOwnMarginToSide` :512-549、`ShiftBoundsToMargins`/旧 `ShiftBoundsToNearestEdge` 已被 `ResolveSideBoundary`(:604-653)/`ShiftSideToMargin`(:668-700)/新 `ShiftBoundsToNearestEdge`(:707-749) 取代；transform 委托从 (config, bounds) 双参变为 (config, bounds, others) 三参。**DEF-009 核心写入点 :2603-2608→:2612-2618**（前置代码变化）。DEF-010 行号与报告完全一致。
2. **LAY-03 语义变形**：越屏风险面从"工作区边缘 + 大边距"变为"其他格子边缘参考 + 大边距"——钳制缺失依旧成立（修复判定不豁免），但交叉验证设计越屏用例时应基于 ResolveSideBoundary 的新语义。
3. **SearchPopup SP-1/SP-2 的回录面比报告"每次必现"窄**：StorageItems 主分支对普通文件/文件夹不产生记录（读取器返回 null）；实际文本回录仅发生在路径解析失败的文本回退分支，图像文件复制则回录为图像记录。SP-3（复制路径）才是无条件文本回录。修复仍应三处全标记（防御一致 + paths 匹配），但交叉验证做行为验收时不应以"复制任意文件必产生记录"为预期。
4. **SP-3 额外缺 `Clipboard.Flush()`**（:4059-4061）：与 SP-1/SP-2 不同，复制结果依赖进程存活。次要卫生点，不作为 DEF-009 否决项。
5. **报告未点名的全仓未标记位点**：`FileSurfaceContent.xaml.cs:3745`（Shell 剪贴板失败回退分支，低危，自有 Properties 标记不被读取器识别）。
6. **报告未点名的无防护 AppearanceChanged 订阅者 ×2**：`WidgetManager.ApplyAppearancePreview`（WidgetManager.cs:438/668-687）与 `SettingsViewModel.RefreshAccentPreview`（SettingsViewModel.cs:430/464-469）。启动期实际可达的只有托盘订阅者（其余订阅晚于 `await themeTask` 或窗口未开）；修复方案 b（保留后台广播）下两者均需纳入，方案 a 下自动安全。
7. **`RefreshAppearance` 尾部 `App.ScheduleLightMemoryCleanup()`（ThemeService.cs:210）线程安全**（App.xaml.cs:3516-3526，Interlocked + TryEnqueue），修复时无须处理，不要误列为风险。
8. **`MoveVisibleWidgets` doc comment（BulkAppearance.cs:82-83）在修复前与行为不符**（声称 anchor/monitor bookkeeping 一致）；修复后若注释保留则变为准确，交叉验证可顺带确认。
9. **20:35 进行时 diff 中 DEF-008 已含 `CaptureCurrentTopologyLayout`**（超出报告建议面，方向正确对应核对单 C3），但**尚未见 EnsureVisible 钳制**（核对单 C4）——最终判定以交叉验证时点 diff 为准。
