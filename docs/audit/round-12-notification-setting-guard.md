# 第 12 轮修复 R12-2 — 原生 toast 前置检查通知开关（消除"乐观显示成功"）

- 级别：P2（来源：第 12 轮维度 A-2；置信度中高——"Show 在通知被关时静默"未实测，但守卫无害且为文档化判断途径）。
- 状态：已修复（代码级验证；平台前提留待真机观测）。

## 问题

`NativeAppNotificationService.TryShow` 里 `AppNotificationManager.Default.Show(...)` 的返回值被丢弃、`Setting` 从不检查。应用通知被用户/组策略关闭时 toast 被静默吞掉而 `TryShow` 仍返回 true——第 11 轮两阶段提醒协议据此消费提醒（落盘 `ReminderDismissedForDueDate`），且因返回 true 连托盘兜底都不尝试。这正是第 11 轮想消灭的"提醒被无声消耗"的残余入口。

## 改法

`TryShow` 在 `Register()` 成功后、构建通知前探测 `AppNotificationManager.Default.Setting`；非 `Enabled` 即记日志并返回 false，让调用方回落自有显示面（提醒路径 → 托盘气泡 → 都不可用则进入第 11 轮的重试协议）。

## 验证

- 代码级：`Setting` API 在本仓库 WASDK 2.5.1 已有使用先例（AotTodoNotificationLifecycleSmoke:130）。
- 316/316 定向套件绿（含 AotStage5B4C3AContractTests 通知边界契约——smoke 文件仅回调的约束不受影响）。
- 平台前提（Show 静默 vs 抛异常）如需实证：系统设置关掉 DeskBox 通知后观察日志出现 `Native app notifications unavailable (setting=...)` 且托盘气泡兜底。
