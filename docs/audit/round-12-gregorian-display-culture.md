# 第 12 轮修复 R12-1 — 全应用日期统一公历渲染（DisplayCulture）

- 级别：P2（来源：第 12 轮从零审查 A-1 + B-1 合并，两报告独立命中；主流程复核后位点从 11 处扩到 15 处 + 1 个 token 通道）。
- 状态：已修复并验证。

## 问题

第 11 轮只修了 Glance 主日期的回历渲染。其余 15 个直接位点（TodoItemViewModel 的 Created/截止/小睡格式化、TodoReminderService 的 toast 到期文案、TodoClipboardFormatter、QuickCaptureSurfaceContent 两处、SettingsViewModel.AboutAndUpdates 两处、OrganizationHistoryEntry、QuickCaptureItemViewModel、ContentWidgetWindow 的 M/d）以及 `LocalizationService.Format`（`FileInfo.FileModified` 等键的 `{1:yyyy/M/d HH:mm}` token 求值）全部不传 culture——ar-SA 系统区域格式下渲染回历 1448 年，与 Glance/日历格/天气全部矛盾。附带风险：UmAlQuraCalendar 仅支持 1900-04-30~2077-11-16，越界会让这些 `ToString` 抛 `ArgumentOutOfRangeException`（部分在绑定 getter 里）。

## 改法

- 新增共享 `src/DeskBox/Helpers/DisplayCulture.cs`：`EnsureGregorian(culture)`（默认日历已是公历则原样返回；否则克隆并从 `OptionalCalendars` 取第一个公历赋给 `DateTimeFormat.Calendar`，失败回退；按 culture 名缓存克隆）+ `GregorianCurrent`（CurrentCulture 的公历变体）。
- 15 个位点全部改传 `DisplayCulture.GregorianCurrent`；`LocalizationService.Format` 的 `string.Format` 文化改用同一变体（数字/月名/语序不变，仅日历钉死公历）。
- Glance 的第 11 轮私有实现改为委托共享 helper（行为不变）。
- `TodoItemViewModel.FormatDueDateTime` 改 internal 供测试。各文化来源（应用语言 vs 系统区域）保持原状——"口径统一"另行产品决策（台账）。

## 运行时事实（.NET 10，本轮探针实证）

对克隆 culture 赋 `DateTimeFormat.Calendar` 后，`CultureInfo.Calendar` **属性仍报告旧日历**（不反映赋值），但 `DateTimeFormat.Calendar` 与**格式化输出**都正确切换（`1448/5/13 → 2026/10/24`）。断言必须落在 `DateTimeFormat.Calendar` 或输出上，不能落在 `CultureInfo.Calendar`。

## 验证

- 新增 `tests/DeskBox.Tests/DisplayCultureTests.cs`（5 用例：EnsureGregorian 强制/原样/缓存、GregorianCurrent 跟随、FormatDueDateTime 与 LocalizationFormat 的 ar-SA A/B 断言）。
- A/B：`GregorianCurrent` 临时改回原始 CurrentCulture → 3 用例红（`Expected: "2026/10/24 09:30" Actual: "1448/5/13 09:30"` 等）；恢复后 316/316 绿。
- 构建 0 错误（DLL 时间戳证明）；部署真机启动 40 步 0 失败。
