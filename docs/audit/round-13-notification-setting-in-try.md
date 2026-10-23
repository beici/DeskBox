# 第 13 轮修复 R13-5 — 通知 Setting 探测移入 try（恢复 TryShow 永不抛契约）

- 级别：P3（第 13 轮 A-5 + B-6 独立命中）。状态：已修复（代码级验证）。

## 问题

df24c5fe 新增的 `AppNotificationManager.Default.Setting` 探测放在 try 之外：这是 WinRT COM 属性读取，WPN 平台半死（服务被禁用/恢复中）时可能抛异常逃逸——TryShow 原本全函数体在 try 内、任何失败都返回 false。todo 提醒侧的逃逸路径会放大为 TCS 永不完成 → `DisposeAsync` 挂起 → 关闭序列 `todo-reminders` 步骤超时后中止后续全部关闭步骤。

## 改法

探测移入既有 try（catch 统一返回 false），注释说明该读取与其他平台调用一样必须留在 try 内。

## 验证

代码级（结构恢复"永不抛"契约）；271/271 绿（含 AotStage5B4C3AContractTests 通知边界契约）。
