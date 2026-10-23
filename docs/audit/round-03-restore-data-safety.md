# 第 3 轮 · 恢复路径上的两处数据丢失

范围：备份/恢复的失败路径（`src/DeskBox/Services/DeskBoxDataBackupService.cs`）。
本轮两个缺陷都在“恢复失败”时发生，用户看到的是“数据没了”，而不是“恢复失败”。

## 人话摘要

1. 域级恢复（云端备份的“覆盖式恢复”）在动手之前不确认备份内容是否还在暂存目录里。
   暂存目录被清理过、或上次恢复已被放弃而标记还留着时，它会先把本地待办/速记删掉，
   然后因为“没有东西可拷”直接跳过，最后还弹一条“数据已恢复”。
   ——用户看到的是：本地待办被清空，应用说恢复成功。
2. 全量恢复会先把现有数据整目录搬到 `restore-rollback\<id>\data`，再把备份搬进来。
   如果这两步都失败（或进程在中间被杀），本地数据目录就空了，而唯一的一份数据留在
   `restore-rollback` 里——全仓库没有任何代码读这个目录，下次启动就是“全新安装”的样子。

## 缺陷一：域级“覆盖式恢复”先删后验，删完还报成功

### 问题

`ApplyScopedRestoreCoreAsync`（`DeskBoxDataBackupService.cs:1389-1427`，修复前）的域替换分支：

1. 遍历数据目录，把属于该域的文件 `File.Delete` 掉（待办：`data/widgets/<id>/todo.json`；
   速记：`data/quick-capture/quick-capture.json`）；
2. 然后才 `if (!Directory.Exists(stagedDataDirectory)) { continue; }`；
3. 拷完删标记、删暂存，返回成功。

### 证据

- 代码：`DeskBoxDataBackupService.cs:1392-1408`（删除在前，存在性检查在后）。
- 可达状态（两条，都在现有代码里能走到）：
  - 恢复连续失败 3 次被放弃时，`:1473 TryDeleteFile(标记)` 与 `:1474 TryDeleteDirectory(暂存)`
    是两个独立调用，前者吞异常（`TryDeleteFile` 全吞）。若删标记失败、删暂存成功，
    下次启动就带着“标记在、暂存没了”的状态进入域替换分支。
  - 用户或清理工具删掉 `restore-staging` 目录（该目录没有任何启动清扫）。
- 现有单测只覆盖“两次 Move 之前失败”，域替换的删除阶段没有失败注入测试
  （`tests/DeskBox.Tests/DeskBoxDataBackupServiceTests.cs` 中 `ScopedRestore_RepeatedFailuresKeepOriginalSafetyBackup`
  走的是样式域，不触发删除）。

### 根因

把“删除本地域文件”和“拷入备份域文件”当成一个原子步骤写，但没有先证明后半步的输入存在。
删除是不可逆的，拷贝是可跳过的——顺序错了，失败就变成静默数据丢失。

### 候选方案

| 方案 | 做法 | 优点 | 缺点 | 结论 |
| --- | --- | --- | --- | --- |
| A（采用） | 先把该域的备份文件列出来，为空则整域跳过并记日志；暂存目录整体缺失则抛 `InvalidDataException`，交给既有的“重试 3 次后放弃”预算 | 改动局限在一个分支；不新增状态；失败语义与既有预算一致 | 暂存缺失时仍会重试 3 次才放弃（可接受：每次都不删数据） | 采用 |
| B | 先把备份域文件拷到数据目录的临时子目录，再删旧文件、再改名 | 真正接近原子 | 需要跨目录 rename 语义，且要处理“旧文件与新文件同名”的合并；改动面大 | 否 |
| C | 只加一个“删除前备份到 pre-restore zip”的兜底 | 数据可找回 | 用户仍需手工解压；仍是“先破坏再解释” | 否 |

打分（消除根因 / 回归面 / 新概念 / 可验证性 / 架构一致性 / 改动成本）：A 5/5/5/5/5/5，
B 4/2/2/3/4/1，C 2/5/4/4/3/4。

### 采用方案

`ApplyScopedRestoreCoreAsync` 的域替换分支改为：

1. 列出暂存目录中属于该域的文件（`stagedFiles`）；
2. 暂存目录不存在 → 抛 `InvalidDataException("The staged restore payload is missing; the live data was left untouched.")`；
3. 该域没有任何备份文件 → 记日志并跳过本域（本地数据保持原样）；
4. 只有前两步都通过，才执行删除 + 拷贝。

### 验证方法

新增单测 `ScopedRestore_ReplaceMode_MissingStagingPayloadKeepsLiveData`：造一份待办域备份 →
准备恢复 → 打开覆盖模式 → 删掉暂存目录 → 应用恢复。断言恢复失败、本地 `todo.json` 仍在且内容未变。
修复前该测试红（文件被删且 `Succeeded == true`），修复后绿。

## 缺陷二：全量恢复失败后，用户数据留在无人读取的 `restore-rollback`

### 问题

全量恢复（`DeskBoxDataBackupService.cs:1275-1313`）的顺序是：

```
Directory.Move(数据目录 → restore-rollback/<guid>/data)   // :1287，在 try 之外
try   { Directory.Move(暂存目录/data → 数据目录) }          // :1292
catch { if (!数据目录存在 && rollback 存在) Move(rollback → 数据目录); throw; }  // :1296-1299
```

若第二次 Move 失败、且回滚 Move 也失败（`F11`），则：数据目录不存在、用户数据只在
`restore-rollback/<guid>/data`、外层 catch 调 `DeletePendingRestoreCore()` 删掉标记与暂存
（`:1322`），`finally` 因“数据目录不存在”不删 rollback（`:1327-1329`）——数据留在磁盘上，
但**没有任何入口能找回**：

- 全仓库对 `restore-rollback` 只有 3 处代码引用：创建（`:1282`）、成功时删除（`:1306`）、
  finally 删除（`:1331`）。没有读取方。
- `GetLatestRecoverySnapshotAsync`（`:2553-2592`）只扫 `automatic` 目录，不扫 `backups/pre-restore`。
- 启动期唯一读恢复状态的地方是 `ApplyPendingRestoreAsync`（`App.xaml.cs:1143`），而标记已被删，
  它在 `:1232` 直接返回“无待处理恢复”。

### 证据

- 上述行号均为当前 checkout 实测（`Select-String` + 逐段阅读）。
- 失败分支枚举（子代理独立复核，与我的阅读一致）：F10（第二次 Move 失败但回滚成功）时数据已还原
  却仍报失败；F11（回滚也失败）时数据滞留 rollback；F12（成功后删标记失败）下次启动误报失败。
- 用户提示文案与实际不符：`Settings.DataBackup.RestoreApplyFailedBody` =
  “Current data was not replaced and this restore task was canceled.”（`en-US.json:1174`）——
  在 F11 场景里数据**确实**被移走了。

### 根因

回滚目录是一个“只写不读”的中间态。代码假设“失败就回滚成功”，没有为“回滚也失败”准备恢复通道。

### 候选方案

| 方案 | 做法 | 优点 | 缺点 | 结论 |
| --- | --- | --- | --- | --- |
| A（采用） | 启动应用恢复前先做一次“滞留数据回收”：`restore-rollback/*/data` 存在且数据目录缺失/为空时，把最近的一份搬回数据目录并删掉该 rollback 目录 | 改动小、无新概念、只补一条读取通道；不碰恢复主流程 | 只在数据目录缺失/为空时生效（正是需要它的场景） | 采用 |
| B | 保留标记（不删）+ 下次启动重试整次恢复 | 语义连贯 | 需要给全量恢复也加“尝试次数”预算；且 F11 后标记与暂存都已删，多数场景救不回来 | 否 |
| C | 失败时把 rollback 目录重命名成 `recovered-<时间>` 并在设置页列出 | 用户可见 | 要新增 UI 入口与文案（12 语言）；本轮不引入新界面 | 否 |

打分：A 5/5/5/5/4/5，B 3/3/3/3/4/2，C 3/4/1/2/2/1。

### 采用方案

新增 `TryRecoverStrandedRollback()`（`DeskBoxDataBackupService.cs`，`ApplyPendingRestoreAsync`
进入 `try` 后第一件事调用）：

1. `restore-rollback` 不存在 → 返回；
2. 数据目录存在且非空 → 返回（绝不覆盖正在使用的数据）；
3. 取 `restore-rollback/*/data` 中最近修改的一份，搬到数据目录，删掉该 rollback 子目录，记日志；
4. IO/权限异常 → 只记日志，不影响启动。

### 验证方法

- `PendingRestore_RecoversDataLeftBehindByAnInterruptedFullRestore`：造 `restore-rollback/<id>/data/settings.json`、
  数据目录不存在 → 调用 `ApplyPendingRestoreAsync()` → 断言 `data/settings.json` 内容为滞留的那份、
  rollback 子目录已清掉。修复前该测试红（数据目录仍为空）。
- `PendingRestore_LeavesLiveDataAloneWhenRollbackCopyExists`：数据目录已有 `settings.json` 且
  rollback 也有一份 → 断言本地内容不变、rollback 未被消费（保护性测试，修复前后都绿）。

## 风险与遗留

- 回收只认“数据目录缺失或为空”。若某次失败后应用已经用默认数据重建了数据目录（本次未观察到，
  因为恢复在服务初始化之前执行），回收会放弃——此时数据仍在 `restore-rollback` 里，只是仍需手工找回。
- `restore-staging` 与 `restore-rollback` 都没有启动清扫（孤儿目录会一直留着）。本轮不引入清扫，
  避免与“重试中”的状态竞争。
- 恢复失败的用户文案（`RestoreApplyFailedBody`）在 F10/F11 场景仍不准确（说“当前数据未被替换”）。
  改文案要动 12 个语言文件，留待后续轮次一并处理。
