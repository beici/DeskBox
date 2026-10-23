# 第 11 轮修复 B-1 — ar-SA 语言下 Glance 主日期按默认回历渲染

- 级别：P2（置信度高）。来源：第 11 轮从零审查维度 B（B-1），主流程已复核。
- 状态：**已修复并验证**。

## 问题（根因）

`GlanceWidgetViewModel.GetCulture()`（`:927-937`）返回**应用语言**对应 culture；.NET 中 `ar-SA` 的默认 `Calendar` 是 `UmAlQuraCalendar`（回历）。`FormatDateText`（`:853-874`）用该 culture 的 `"M"` 标准模式与 `LongDatePattern` 渲染主日期（`DateText`，`:769`）→ 阿语用户的主日期显示为**回历年月日**（如 1448 年），而：

1. 同组件的日历网格按公历 `DateOnly` 计算（`LocalCalendarPresentationSource.cs:15-36`）；
2. 应用其余日期面（Todo/QuickCapture/天气等）均为公历数字；
3. Glance 自带"传统历法"功能把 UmAlQura 当**用户显式选择**的附加模式（`GlanceTraditionalCalendarService.cs:296-306`）——主日期已回历后，选择传统历法会主/附日期重复。

`FormatCompactCalendarDateText`（`date.Day.ToString(culture)`，仅数字形状）、`WeekdayText`（`dddd`，星期与历法无关）、`FormatTimeText`（时间模式，与历法无关）不受影响，不动。

## 候选方案

| 方案 | 内容 | 评估 |
| --- | --- | --- |
| A（采用） | `FormatDateText` 入口：culture 默认日历非公历时，克隆 culture 并把 `DateTimeFormat.Calendar` 设为其 `OptionalCalendars` 中第一个公历 | 保留阿语月名/数字形状与 `LongDatePattern` 语序，仅换历法；最小侵入；回历仍可通过"传统历法"选项显式获得 |
| B | 全应用语言注册时改用 `CultureAndRegionInfoBuilder` 重建 culture | 过重，影响面不可控 |
| C | 主日期改用固定公历格式串 | 丢失各语言自然的日期语序与月名本地化 |

注意实现细节：`DateTimeFormat.Calendar` 赋值只接受该 culture `OptionalCalendars` 集合内的日历（ar-SA 含多个 `GregorianCalendar` 变体），必须从 `OptionalCalendars` 里挑第一个 `GregorianCalendar`，否则 `ArgumentException`；再兜一层 try/catch 失败回退原 culture（渲染不中断）。

## 改法

1. `GlanceWidgetViewModel` 新增私有静态 `EnsureGregorianCalendar(CultureInfo)`：
   - 默认日历已是 `GregorianCalendar` → 原样返回（zh-CN 等全部语言零开销短路）；
   - 否则克隆 + 从 `OptionalCalendars.FirstOrDefault(c => c is GregorianCalendar)` 赋值（无则 `new GregorianCalendar()`），try/catch 回退。
2. `FormatDateText` 开头 `culture = EnsureGregorianCalendar(culture)`（"M" 与 LongDatePattern 两个分支同时受益）。
3. 无新增字符串、无 XAML 改动、无其他调用方（`internal static`，仅本 VM 与测试使用）。

## 方案复核

- ar-SA 的 `OptionalCalendars` 含 `GregorianCalendar`（Windows/ICU 数据均如此），赋值合法；
- 克隆 culture 是标准做法（MSDN「Working with calendars」），不改全局；
- 传统历法附加行独立服务，不受影响；修复后 ar-SA 主日期=公历、附加行=可选回历，语义与其它语言一致；
- 十二语言其余语言默认公历，路径短路，零行为变化。

## 验证（已全部通过）

- [x] **新增 2 条用例**（`tests/DeskBox.Tests/GlanceCalendarAndImageServiceTests.cs`）：
  - `FormatDateText_UsesGregorianCalendarForNonGregorianDefaultCulture`：ar-SA + 2026-10-24，includeYear 两种取值都与"强制公历克隆"渲染一致；
  - `FormatDateText_KeepsGregorianCulturesUnchanged`：zh-CN → `"2026年10月24日"`、en-US → `"October 24"`（默认公历语言零行为变化）。
- [x] **A/B 红灯**：注释掉 `EnsureGregorianCalendar` 接线后第一条用例红，断言全文直接呈现缺陷本体：
  `Expected: "24 أكتوبر"  Actual: "13 جمادى الأولى"`（公历 10 月 24 日 vs 回历 1448 年 6 月 13 日）。恢复后绿。
- [x] **回归**：176/176 定向套件通过（含 Glance 相关 4 套与 AOT 契约 2 套）；构建 0 错误、DLL 时间戳证明；新构建部署真机启动 40/39 步 0 失败。
