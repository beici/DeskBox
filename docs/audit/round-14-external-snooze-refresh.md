# 第 14 轮修复 B-2 — 外部 snooze 变更后打开的 Todo 格子实时刷新（台账 R13-9 销案）

- 级别：P2（第 14 轮 B-2；台账 R13-9 证的深挖版）。状态：已修复并验证。

## 问题

`TodoWidgetViewModel.ApplyExternalStoreChange`（DEF-043 合并）把提醒服务持久化的 snooze 字段直接写进**模型**（`match.Item.SnoozedUntil = ...`），而绑定（`DueStatusText`/`HasActiveSnooze`）读的是 **VM 阴影字段**（`_snoozedUntil`）。两个方向都错：提醒消费 snooze（清空）后徽标**永不消失**；用户在 toast 上点"稍后提醒"后徽标**不出现**。`listChanged == false` 时发的 `IsInitialized` 通知全仓库零绑定，是安慰性通知。`CompletedAt` 阴影陈旧还影响已完成排序。持久化无恙（序列化走模型），缺陷纯在显示层。

## 改法

`snooze/完成/更新`三类有 VM setter 的字段改走 setter（内部镜像回模型并发派生属性通知）；纯簿记字段（`ReminderLastNotifiedAt`/`ReminderDismissedForDueDate`/`SnoozeLastNotifiedAt`）无 VM 投影，仍写模型。

## 验证

新增 `ApplyExternalStoreChange_RefreshesSnoozeStateForOpenWidgets`（外部消费后 `HasActiveSnooze` true→false）。A/B：合并改回模型直写 → 红（`Expected: False, Actual: True`）。325/325 绿。
