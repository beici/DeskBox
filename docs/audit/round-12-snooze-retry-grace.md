# 第 12 轮修复 R12-5 — snooze 提醒的重试加上宽限上限（修正第 11 轮的过度承诺）

- 级别：P3（来源：第 12 轮维度 A-5 + B-2 独立命中）。
- 状态：已修复并验证。

## 问题

第 11 轮两阶段协议里，due 触发的提醒有 `MissedReminderGrace`（1 分钟）封顶；snooze 触发的（`TryGetReminderTrigger` 的 `now >= snoozedUntil`）没有——通知面长期不可用时每 30 秒空转重试到会话结束，与 fe3103d5 提交说明"bounded by the missed-reminder grace window"不符。

## 改法

snooze 分支加对称上界：`now >= snoozedUntil && now <= snoozedUntil + MissedReminderGrace`。

## 验证

- 新增 `CheckNowAsync_SnoozeReminderStopsRetryingAfterTheGraceWindow`（snoozedUntil = now-5min → 0 候选、0 通知）。
- A/B：临时去掉上界 → 红（`Expected: 0, Actual: 1`）；恢复后 316/316 绿。既有 snooze 用例（SnoozedUntil = now，在宽限内）不受影响。
