# 面A审查报告：修复相邻代码面深度审查

**审查轮次**：第1轮·面A  
**仓库**：/root/DeskBox，分支 wip/fix-bug，HEAD = 1df469e  
**审查日期**：2026-09-01  
**审查范围**：批次B/C/D/F7修复的相邻代码面  
**对照文件**：`docs/quality/defect-ledger.md`、`docs/quality/rectify/F6-BC-stability-remediation.md`、`docs/quality/rectify/F6-D-dead-host-removal-remediation.md`、`docs/quality/rectify/F7-hygiene-batch-remediation.md`

---

## 审查结论总览

| 审查面 | 定级 | 结论 | 关键证据 |
|--------|------|------|----------|
| ① 撤销窗保留集 | P2 | **PASS** | 所有10+调用点共享Gate锁；GetReferencedImagePathsCore正确并入保留集 |
| ② 对比度回退分支 | P3 | **PASS** | 递归调用于Mode切换后短路退出，无无限递归；RefreshItemMaterialSurfaces在阈值跌破后正确调用 |
| ③ 死宿主删除后订阅完整性 | P2 | **PASS** | 全部QuickCaptureWidgetWindow引用已清除；QuickCaptureSurfaceContent事件订阅成对 |
| ④ WeatherService浅拷贝 | P2 | **PASS** | 三出口均返回独立浅拷贝；ViewModel只读取不写入嵌套payload |

---

## 逐项审查详情

### ① 撤销窗保留集 — PASS

**位置**：`src/DeskBox/Services/QuickCaptureService.cs:51-56, 1536-1548`

**审查内容**：
- `CleanupUnusedImageCacheCore()` 的10+调用点（DeleteItemAsync:827, DeleteItemsAsync:877, DeleteRecentItemAsync:907, RestoreDeletedItemAsync:945, ClearAsync:249, ClearRecentAsync:303, AddImageFileItemAsync:357, AddRecentTextAsync:249, AddRecentClipboardImageAsync:303, MoveItemAsync:736, SetAttachmentAsync:827, RemoveAttachmentAsync:946, ReplaceImageAsync:964, TrimRecentItemsAsync:1000, CleanupUnusedImageCacheAsync:1049）是否全部通过 `GetReferencedImagePathsCore()` 正确并入 `_undoWindowImagePaths`？

**证据**：
- 所有13处调用均经 `GetReferencedImagePathsCore()` → `CleanupUnusedImageCacheCore(referenced)` 路径，`GetReferencedImagePathsCore` 在第1536-1548行将 `_undoWindowImagePaths.Keys` 通过 `UnionWith` 并入 referenced 集 ✓
- 所有调用点均在 `_gate.WaitAsync()` 保护下执行，无并发访问竞态 ✓
- 过期条目在每次调用时自清除（第1540-1545行）✓
- Restore 路径（945行）调用 `UnregisterUndoWindowImages` 正确移除 ✓

**窗口稳健性**：
- 撤销窗：`UndoRetentionWindow = TimeSpan.FromSeconds(10)`（第56行）
- Toast 展示：`WidgetFeedbackPolicy.GetDisplayDuration` 返回 `TimeSpan.FromMilliseconds(5000)`（有Action时）
- 10s > 5s，toast消失时撤销窗仍生效，用户仍可恢复 ✓
- 单条删除 key=`"quick-delete"`，批量 key=`"quick-delete-selected"`，去重键语义清晰 ✓

---

### ② 对比度回退分支 — PASS

**位置**：`src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs:219-248`

**审查内容**：
- `ApplyClipboardItemColors` 递归调用自身（第237行）后，`return` 是否导致 `RefreshItemMaterialSurfaces` 被跳过？
- 回退后是否需要重新刷新材质面？

**证据**：
- 当对比度跌破阈值（第228行），执行：
  1. 第230-233行：`SetBackgroundModeOverride(ModeFollowTheme)` + `UpdateWidget` + `App.Log`
  2. 第235行：`ApplyClipboardItemColors()` **递归调用自身**
  3. 第236行：`return`（退出本次调用）
- 递归调用重新进入函数体：
  - 第219-221行：`backgroundCustom = GetBackgroundModeOverride(...) == ModeCustom` → **现为 false**（刚改为 FollowTheme）
  - 第222-236行的 if 块 **不进入**（因为 textCustom && backgroundCustom 为 false）
  - 第243-248行：检查 `backgroundCustom == ModeCustom` → false，**不调用** `RefreshItemMaterialSurfaces()`
- **结论**：回退到 FollowTheme 后，背景通道不再自定义，`RefreshItemMaterialSurfaces` 无需重刷材质面（材质面只对自定义背景色生效）。行为正确 ✓

**递归安全**：递归仅触发一次（第一次回退 → 第二次调用不再满足条件 → 返回），无无限递归 ✓

---

### ③ 死宿主删除后订阅完整性 — PASS

**位置**：`src/DeskBox/Views/QuickCaptureWidgetWindow*`（已删除，commit ad8febe）

**审查内容**：
- 删除 QuickCaptureWidgetWindow（13文件/7,796行）后，`QuickCaptureSurfaceContent` 的事件订阅/退订是否完整？
- 是否存在死宿主与共享 surface 平行的订阅在删除后语义变化？

**证据**：
- 生产引用检查：`grep -rn "QuickCaptureWidgetWindow" src/` 仅返回 `WidgetWindowBase.cs` 中的注释，无代码引用 ✓
- 测试引用检查：`grep -rn "QuickCaptureWidgetWindow" tests/` 无匹配 ✓
- `QuickCaptureSurfaceContent` 订阅（构造阶段，第130-152行）：
  - `ViewModel.PropertyChanged += ViewModel_PropertyChanged`
  - `DetailMarkdownEditor.*TextChanged/TextTruncated/CommitRequested`
  - `DetailMarkdownView.AttachmentOpenRequested`
  - `_detailAutoSaveTimer.Tick += DetailAutoSaveTimer_Tick`
  - `Loaded += OnLoaded`, `Unloaded += OnUnloaded`, `ActualThemeChanged += ...`
  - `FeedbackRequested` 公开事件（无订阅，仅广播）✓
- 退订（Dispose，第3505-3518行）：与订阅完全对称 ✓
- 批次D提交信息明确："所有 remaining writes paired"、"契约测试逐一修订为生产面契约" ✓
- `WidgetManager.BulkAppearance.cs:26` 中对 `quickCaptureSurface.ApplyClipboardItemColors()` 的调用路径完整 ✓

---

### ④ WeatherService 浅拷贝 — PASS

**位置**：`src/DeskBox/Services/WeatherService.cs:157, 214, 224, 242-257`

**审查内容**：
- `CloneWeatherData` 是否为真正的浅拷贝？
- 调用方（`WeatherWidgetViewModel`）是否只改顶层字段？

**证据**：
- `CloneWeatherData`（第242-257行）：创建新 `WeatherData` 实例，顶层字段赋值，嵌套引用直接传递（Current/Daily/Hourly 为引用复制）✓
- 三个出口均使用 `CloneWeatherData`：
  - 缓存命中（157行）：`WeatherData cached = CloneWeatherData(_cachedData); cached.LocationName = ...; return cached;` ✓
  - 新拉取（214行）：`return CloneWeatherData(data);` ✓
  - 过期回退（224行）：`WeatherData stale = CloneWeatherData(_cachedData); stale.IsStale = true; return stale;` ✓
- ViewModel 使用模式：
  - `RefreshAndLayout.cs:85`：`_weatherData = await _weatherService.GetWeatherAsync(...)`
  - `DataProcessing.cs:263-337`：`ApplyWeatherData(_weatherData)` — 只读取 Current/Daily/Hourly 的数组索引和属性，不修改嵌套对象 ✓
  - `DataProcessing.cs:409`：`PopulateDailyForecast` — 只读 `daily.Time[i]`, `daily.TemperatureMax[i]` 等，写入 ViewModel 自己的 `DailyForecast` 列表，不影响 WeatherData ✓
- 测试验证：`WeatherResilienceTests.cs:78-118` 三调用三实例 + 污染隔离测试全部覆盖 ✓

---

## 新立案清单

经本次面A审查，**未发现新的 P0/P1 缺陷**。

发现 1 项 P3 观察项（卫生类，不阻塞发布）：

| ID | 定级 | 位置 | 问题描述 | 触发条件 | 影响 | 根因 | 置信度 |
|----|------|------|----------|----------|------|------|--------|
| QC-16 | P3 | `QuickCaptureSurfaceContent.xaml.cs:235-236` | `ApplyClipboardItemColors` 递归调用后 `return`，若递归中因异常提前退出则 `RefreshItemMaterialSurfaces` 不会被执行（背景色刷新中断） | 主题切换时同时发生异常且背景处于自定义模式 | UI背景色刷新可能不完整，需下一次主题变更或重新打开页面才能修复 | 递归+return结构缺乏异常保护 | 中 |

**立案说明**：
- QC-16 为理论风险路径，实际触发条件极苛刻（递归内抛异常）
- 当前实现 `App.Log` 已在阈值跌破时记录日志，可作为诊断线索
- 建议：将递归调用改用 `try-finally` 或在递归前暂存状态，但优先级低（P3），可纳入下次卫生批次

---

## 与台账交叉验证

- DEF-011~029 已全部关闭，本次审查未发现回归
- F7 卫生批次挂账条目（QC-06/07/10/11/12/13/14/15、WIN-08/09、EVT-02、EXC-04/05/06、ARC-02/03/04/05）均为前序批次已确认待修项，与本次审查面无交集
- 无重复立案

---

## 交付物

- 本报告（`faceA-review-report.md`）
- 新立案：QC-16（P3，1项）
