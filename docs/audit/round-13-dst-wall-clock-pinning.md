# 第 13 轮修复 R13-4 — 重复任务 DST 语义：探针定论 + 时区接缝 + 测试钉死（台账 11.A-2 结案修正）

- 级别：P2（台账 11.A-2 的修正版）。状态：已修复（探针 + 测试钉死，生产行为不变）。

## 探针定论（.NET 10，显式 Eastern 时区复现生产语义）

1. **"每次跳变累计漂移 1 小时"不成立**：`AddLocalDays`（`LocalDateTime.AddDays(n)` + `new DateTimeOffset(local)`）对**存在**的本地时刻墙钟完全保留（09:00 EST → 09:00 EDT → 09:00 EDT，偏移正确切换）。第 11 轮子报告的核心主张被证伪。
2. **春季不存在的本地时刻**（02:xx 于 2026-03-08）：`GetUtcOffset` 返回跳变前偏移 → 实际落在 03:30 EDT——**按惯例前移一个 gap**，符合通行约定（java.time 同款）。
3. **秋季二义时刻**（01:30 于 2026-11-01）：取标准偏移（第二次出现）——确定性选择。

即：现状行为已是惯例行为，无需改生产行为；缺的是**钉死**（机器时区无 DST，现有测试根本测不到跳变）。

## 改法

`AddLocalDays` / `CreateMonthlyCandidate` 增加 internal 显式时区重载（`TimeZoneInfo.ConvertTime(value, zone)` + `new DateTimeOffset(local, zone.GetUtcOffset(local))`，与隐式 Local 语义逐点等价），私有包装走 `TimeZoneInfo.Local`。

## 验证

新增 `TodoRecurrenceDstTests`（4 用例）：墙钟跨春季跳变保留、不存在时刻前移一个 gap、二义时刻取标准偏移、月度候选跨 DST 保留锚点墙钟。无生产行为变化，A/B 不适用（钉死性质）。台账 11.A-2 条目据此改写。
