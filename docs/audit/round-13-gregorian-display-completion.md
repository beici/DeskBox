# 第 13 轮修复 R13-2 — 公历渲染补全（第 12 轮漏网的 11 个显示位点 + 兜底 + 星期）

- 级别：P2（第 13 轮 B-1 + B-2 + B-4）。状态：已修复并验证。

## 问题

b96129f6 声称"render every user-facing date on the Gregorian calendar"，实际又漏 10+1 个用户可见位点：设置页云备份状态行 ×3、远端快照列表标题（显式传 CurrentCulture 也无效——日历仍是默认的）、恢复确认对话框 ×2、快照汇总、反馈卡片、运行诊断"上次生命周期事件"、搜索结果 `DateDisplay`（`FileMetaService.FormatDate`，吃文件系统时间可为 1601/2099，UmAlQura 超范围抛异常）。另有 `LocalizationService.DefaultFormat` 兜底仍用 raw CurrentCulture（`WidgetItem.SecondaryInfo` 早期窗口 + 超范围文件时间在绑定 getter 抛异常）与 `GlanceWidgetViewModel` 的 `WeekdayText`（`dddd` 未公历化，超范围时钟在每分钟 tick 抛）。

## 改法

全部改传 `DisplayCulture.GregorianCurrent`（或 `EnsureGregorian(culture)`）；`DefaultFormat` 与实例 `Format` 对齐。本轮已是该缺陷家族第二次漏网（a251103d 漏 15 处、b96129f6 又漏 11 处）——台账记录"grep 级 ratchet 契约测试"设想待后续轮评估。

## 验证

- 新增 `FileMetaFormatDate_RendersGregorian…` 与 `…DoesNotThrowForFileTimesOutsideTheNonGregorianCalendarRange`。A/B：FileMeta 两行改回无 culture → 前者红（`Expected "24 أكتوبر" Actual "13 جمادى الأولى"`）、后者红（超范围异常）。
- 271/271 绿；构建 0 错误；部署真机 40 步 0 失败。
