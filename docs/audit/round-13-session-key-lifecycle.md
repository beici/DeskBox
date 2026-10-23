# 第 13 轮修复 R13-3 — 提醒会话键生命周期两缺口（R12-4 + R12-6）

- 级别：P2（台账 R12-4 + R12-6）。状态：已修复并验证。

## 问题

1. **中止搁浅（R12-4）**：多 widget 收集中途禁用/退役/读盘抛错时，已占用候选的会话键不回收（显示失败路径有回收、收集中止路径没有）→ 本会话内这些条目不再提醒。
2. **Clear 重放（R12-6）**："已显示但持久化失败"特意保留的会话键会被 `Start()`/`Refresh()` 的全量 Clear 清掉 → 不经重启的开关循环重放已显示过的提醒。

## 改法

- `CheckCoreAsync` 重构为收集段 try/catch：两条提前返回路径与异常路径统一 `ReleaseCandidateKeys(candidates)`（已持久化的条目由持久化标记保护，释放会话键不会造成重复）。
- 新增 `_shownWithoutPersistKeys` 集合：persist 失败时记录该组候选；选择阶段先查该集合（toast 已上屏、标记不在盘上，本服务实例存续期内绝不重放）；三处 Clear 收口为 `ResetSessionClaimedKeys()`（只清 `_sessionNotifiedKeys`，shown 集合刻意保留）。

## 验证

- 新增 `CheckNowAsync_ReleasesSessionKeysWhenTheScanAbortsMidway`（双 widget、第二 widget 收集时禁用 → 0 通知；重启用后同一条目重新提醒）与 `CheckNowAsync_DoesNotReshownWhenPersistFailsAndTheClaimKeysReset`（FileShare.Read 锁住 store 使 persist 失败 → 断言标记未落盘；模拟开关循环的键重置 → 不重放）。
- A/B：释放改 no-op → 前者红（`Expected: 1, Actual: 0`）；shown 集合检查短路 → 后者红（`Expected: 0, Actual: 1`）。恢复后 271/271 绿。
- 测试方法学：注入用"工厂按 widgetId 单次触发禁用"必须加一次性标志，否则第二次扫描再次中止，断言会被自己的注入逻辑骗过（本轮实际踩到，已修正）。
