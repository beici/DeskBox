# 第 7 轮：读不出来的旧数据文件被下一次保存覆盖

## 人话摘要

设置、待办、格子布局这些数据文件，某次打开时读不出来（被杀软、搜索索引器、同步盘或另一个进程
占住一瞬间），应用会退回用上一份备份继续跑——这一步第 2 轮已经修好了，主文件不会被立刻覆盖。
但**下一次保存**会把新数据直接写下去，那份"读不出来的旧文件"被挤进 `.bak`，再下一次保存就
彻底没了。用户看到的是：某次改动之后，之前那次没读到的改动永远消失了。

修完之后：这份读不出来的文件会在被覆盖之前改名留存（`<文件名>.unreadable-<UTC 时间戳>`），
每个 store 最多留 3 份，随时可以手工找回。

## 问题

`src/DeskBox/Services/ResilientJsonStore.cs`：

- `SaveCoreAsync`（`:315-327`）：`if (File.Exists(storePath)) await ReplaceOrFallbackAsync(tempPath, storePath, GetBackupPath(storePath), ...)`，否则 `File.Move(tempPath, storePath)`。
- `ReplaceOrFallbackAsync`（`:346-350`）走 `File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors)`（默认实现 `:212-219`）——**主文件的字节被轮转进 `.bak`**。
- 加载路径（`:155-163`）在第 2 轮已改成"主文件读不出来时不覆盖它"，但那只保证了**这一次加载**不丢字节。

于是序列是：读失败 → 用 `.bak` 的内容跑 → 用户改动 → 保存 1（旧主文件进 `.bak`，`.bak` 里更早的好数据被删）→ 保存 2（`.bak` 变成保存 1 的内容，**旧主文件的字节彻底消失**）。

## 证据

1. 代码：`ResilientJsonStore.cs:315-327`（保存分支）、`:212-219`（`File.Replace` 的三个路径参数）、`:155-163`（第 2 轮修的加载分支）。
2. 第 2 轮的单测 `LoadAsync_LockedPrimary_KeepsFileAndDoesNotQuarantine` 只断言"加载后主文件不变"，没有任何测试跨到"保存之后"。
3. 反例单测（本轮新增，先在旧代码上跑红）：`SaveAsync_AfterUnreadablePrimary_KeepsTheOldBytesInsteadOfRotatingThemAway`——锁定主文件首字节 → 加载退回 `.bak` → 释放锁 → 连续保存两次 → 旧代码下磁盘上已经没有任何文件包含 `newer`。

## 根因

"主文件这次读不出来"这个事实只活在 `LoadWithResultAsync` 的一次调用里（`ResilientJsonStore` 是无状态的静态工具类），
保存路径无从得知，于是按最普通的覆盖语义处理，把用户最新的一份字节挤成了滚动 `.bak`。

## 候选方案

| 方案 | 消除根因 | 回归面 | 新概念/依赖 | 可验证性 | 架构一致性 | 总分 |
| --- | --- | --- | --- | --- | --- | --- |
| A 记住"上次读失败"，保存前把主文件改名留存 | 是（字节一定留住） | 小（只多一个文件） | 一个静态字典 + 一种新文件后缀 | 高（单测可断言文件内容） | 高（和第 2 轮的隔离/留存语义一致） | 30 |
| B 读失败后整进程进入只读模式 | 是 | 大（用户改不了东西） | 需要 UI 状态与提示 | 中 | 低（要新增全局状态机） | 24 |
| C 每次保存都留一份历史副本 | 是 | 中（磁盘增长） | 需要清理策略 | 高 | 低（和 `.bak` 语义重复） | 20 |
| D 把 `.bak` 保留多代 | 部分（多代之后照样丢） | 小 | 需要代际命名与迁移 | 中 | 中 | 16 |

采用 **A**：改动最小、字节一定留住、和第 2 轮"隔离而不删除"的处置方式一致。

## 改法

`src/DeskBox/Services/ResilientJsonStore.cs`：

- 新增 `s_unreadablePrimaries`（`ConcurrentDictionary<string, byte>`，按路径登记"上次加载读不出来"）与 `MaxUnreadableAsides = 3`。
- 加载路径的瞬时读失败分支（`:72-81`）除了原有的"留在原地 + 记日志"，再登记该路径。
- `SaveCoreAsync` 开头（`:299-301`）调用 `PreserveUnreadablePrimary(storePath)`：若该路径被登记过且文件仍在，就 `File.Move` 成 `<storePath>.unreadable-<UTC yyyyMMdd-HHmmss-fff>`，记一行日志，并 `PruneUnreadableAsides` 只留最近 3 份。
- 改名失败只记日志、不阻断保存（保住这次的新改动优先）。
- 后续提交看到 `File.Exists(storePath)` 为假，走 `File.Move(tempPath, storePath)`，`.bak` 里的旧好数据保持不动。

## 验证方法

- 新增单测 `tests/DeskBox.Tests/ResilientJsonStoreTests.cs:SaveAsync_AfterUnreadablePrimary_KeepsTheOldBytesInsteadOfRotatingThemAway`：
  主文件 `{"value":"newer"}`、`.bak` `{"value":"older"}`，锁定主文件首字节后加载（断言 `Source == Backup`），
  释放锁后连续保存两次，断言 `*.unreadable-*` 恰好一份且内容含 `newer`、主文件是第二次保存的内容。
- A/B：同一批 19 个 `ResilientJsonStoreTests` 用例，修复前 1 红 18 绿，修复后 19/19 绿。
- 真机：本机数据目录不存在"读不出来的主文件"状态，无法自然触发；用单测覆盖，并在
  `docs/audit/SUMMARY.md` 环境事实里记录这一限制（不伪造真机证据）。

## 风险与遗留

- 用户数据目录会新增 `*.unreadable-*` 文件（每个 store 最多 3 份）。它们不被任何加载路径读取，只作为人工恢复线索。
- 依赖"文件仍可改名"。若 ACL 连改名都拒绝（极少见），只记日志，保存照常进行——那种情况下旧字节仍然只保到下一次保存为止，属于已知残余风险。
- 只处理"主文件读失败"。主文件**内容解析失败**的路径早已由 `QuarantineCorruptFile` 改名隔离，字节本来就保住了，不需要重复处理。
