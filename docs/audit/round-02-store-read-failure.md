# 第 2 轮 · 存储读失败被当成文件损坏

## 人话摘要

DeskBox 的每个数据文件（设置、组件布局、待办、快捷记录……）都由同一个"读档器"加载。这个读档器只要**读文件时出错**，就断定"文件坏了"：把文件改名成 `.corrupt-*` 收走，然后用旧备份覆盖回去，或者直接回默认。

问题是"读不到"和"内容是坏的"是两件事。杀毒软件、Windows 搜索索引器、同步盘、另一个进程随手打开一下文件，都会让读取瞬间失败——这时文件其实是完好的、而且往往比备份**更新**。结果：一次瞬时占用，用户的设置被回滚到上一次保存，改动丢了，界面不提示。

## 问题

`ResilientJsonStore.LoadWithResultAsync` 用 `catch (Exception ex)` 包住"读文件 + 反序列化"两步，任何异常都走同一条路：记日志 → `QuarantineCorruptFile`（把主文件改名搬走）→ 回退到 `.bak` → `RestorePrimaryAsync`（用备份内容**覆盖**主文件）。

## 证据

代码（`src/DeskBox/Services/ResilientJsonStore.cs`）：

```csharp
// :57-68
try
{
    return new ResilientJsonLoadResult<T>(
        deserialize(await File.ReadAllTextAsync(storePath)),
        ResilientJsonLoadSource.Primary);
}
catch (Exception ex)          // ← 读失败与内容损坏共用一条路径
{
    primaryFailed = true;
    App.Log($"[{logName}] Primary store is invalid: {ex}");
    QuarantineCorruptFile(storePath, logName);
}
```

- `File.ReadAllTextAsync` 在共享冲突时抛 `IOException`（0x80070020），在 ACL/只读目录下抛 `UnauthorizedAccessException`；两者都被上面的 `catch` 收走。
- 回退分支 `:84-90` 接着 `RestorePrimaryAsync(storePath, backupJson)`，即**用旧备份覆盖主文件**——用户最新那次保存被抹掉。
- `QuarantineCorruptFile`（`:370-383`）把主文件改名成 `.corrupt-<时间戳>-<guid>`，只保留最近 2 份（`:391`），第 3 次起旧份被删。
- 同一文件里**只有异常回退路径**做了落盘（`:351-354` `FileOptions.WriteThrough` + `Flush(flushToDisk: true)`），正常读写路径没有——说明"读失败=损坏"这个判定从来没被当成失败路径对待。

现场证据：真实数据目录里目前**没有**任何 `.corrupt-*` 文件（说明本机尚未触发），但存在两个 2026-09-08 遗留的孤儿临时文件 `data\music\settings.json.<guid>.tmp`，说明"写到一半被打断"的窗口确实会真实发生。

## 根因

把"这次没读到"与"这份内容不可用"混为一谈。判定损坏的正确依据只能是**解析结果**，不能是 IO 结果。

## 候选方案

| 方案 | 消除根因 | 回归面 | 新概念 | 可验证性 | 架构一致 | 代价 |
|---|---|---|---|---|---|---|
| A. 读失败时重试几次，仍失败则**不动文件**、只读备份不覆盖主文件、不隔离 | 是 | 小（仅加载路径） | 无 | 单测可造共享冲突 | 与"备份只读恢复"的既有意图一致 | 读路径多两次 `Task.Delay` |
| B. 只把 `JsonException` 当损坏，其它异常一律上抛 | 部分 | 中（调用方会崩在启动路径） | 无 | 单测可验证 | 违背"启动不能崩" | 用户会看到启动失败 |
| C. 保持现状，只把 `.corrupt-*` 的保留份数从 2 提到 10 | 否（仍会覆盖主文件） | 无 | 无 | 难 | 是 | 治标不治本 |

选 **A**。

## 采用方案

`ResilientJsonStore.LoadWithResultAsync`：

1. 读与解析分成两步。读失败（`IOException` / `UnauthorizedAccessException`）重试 3 次（40ms、80ms 退避），仍失败则：**不隔离、不覆盖**，记一条 `could not be read (left in place)` 日志，走备份/默认回退，且回退分支在"主文件未读到"时跳过 `RestorePrimaryAsync`。
2. 解析失败（`JsonException` 等）保持原行为：隔离 + 从备份恢复。
3. 备份文件的读取同样区分两类失败，读不到 `.bak` 时不再把它改名收走。

不变量：**"没读到"永远不改变磁盘上的用户数据。**

## 验证方法

- 新增单测 `LoadAsync_UnreadablePrimary_KeepsFileAndDoesNotQuarantine`：主文件内容为 `{"value":"newer"}`、`.bak` 为 `{"value":"older"}`，用 `FileShare.None` 占住主文件后加载，断言：
  - 返回值来自备份（`Backup`），
  - 主文件内容仍是 `newer`（没有被备份覆盖），
  - 没有产生 `.corrupt-*`。
- 现有 15 个 `ResilientJsonStoreTests` 全部保持通过（尤其 `SaveAsync_BackupCanRecoverCorruptPrimary` 仍要求"内容损坏时隔离"）。
- 全量单测差值：与基线（总计 5808 / 通过 4405）相比，通过数只增不减。

## 风险

- 读失败时界面仍会回到旧备份或默认值（这是不可避免的），但用户的数据文件完好，下次成功读取即恢复，且 `.corrupt-*` 不再出现误导性证据。
- 重试引入最多约 120ms 的加载延迟，仅在共享冲突时发生。
- 若某调用方依赖"读失败也会被隔离"，行为会变；仓库内没有这样的调用方（`LoadWithResultAsync` 的 15 处调用都只看 `Source`/`Value`）。
