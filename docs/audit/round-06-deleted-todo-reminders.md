# 第 6 轮：已删除的待办仍在“提醒”的世界里

## 人话摘要

- 待办的“删除”是软删除：条目标成 `IsDeleted = true` 留在文件里，界面不再显示它。
- 但提醒扫描只认 `IsCompleted`，不认 `IsDeleted`——所以**删掉一条有截止时间的待办，到点照样弹提醒**。
- 更糟的是提醒卡片上的“完成”按钮：它对这条“已经不存在”的条目执行“完成并生成下一次”，而生成克隆时只复制字段、不复制 `IsDeleted`，于是**你删掉的循环待办会重新出现在列表里**。
- 修复：提醒判定与“完成”操作都把 `IsDeleted` 当作 `IsCompleted` 一样对待（不存在的东西不该被提醒，也不该被“完成”）。

## 问题

软删除（tombstone）在提醒链路上被当成“仍然存在”：

1. `src/DeskBox/Services/TodoReminderService.cs:642`（`TryGetReminderTrigger`）与 `:674`（`ShouldNotifyDue`）只排除 `IsCompleted`；
2. `src/DeskBox/Services/TodoReminderService.cs:343`（`CompleteAsync`）同样只排除 `IsCompleted`，随后 `:360` `TodoRecurrenceService.TryCreateNextOccurrence` 生成并插入一个新条目。

用户可感知的后果：

- 删掉一条带截止时间的待办（或“清除已完成”）后，到点仍收到该待办的提醒；点进去看列表里没有这条待办。
- 若该待办是循环待办，在提醒卡片上点“完成”，删除的待办会以“下一次”的身份复活（新 `Id`、`IsDeleted = false`），用户会以为删除失败了。

## 证据

删除路径确实是软删除，且保留截止时间与提醒偏移：

```
src/DeskBox/ViewModels/TodoWidgetViewModel.EditingAndUndo.cs:289
    item.Item.IsDeleted = true;
```

模型上两者并存（`src/DeskBox/Models/TodoItem.cs:79` `public bool IsDeleted { get; set; }`，`DueDate`/`ReminderOffsetMinutes` 不受影响），且 tombstone 会随文档一起保存（`src/DeskBox/ViewModels/TodoWidgetViewModel.FilteringAndAppearance.cs:95-98` 把 `Items` 与持久化的 tombstone 一起写回）。

提醒判定只排除“已完成”：

```
src/DeskBox/Services/TodoReminderService.cs:642-645
        if (item.IsCompleted || item.DueDate is null)
        {
            return false;
        }

src/DeskBox/Services/TodoReminderService.cs:674-677
        if (item.IsCompleted || item.DueDate is not { } dueDate)
        {
            return false;
        }
```

“完成”动作同样只排除“已完成”，然后照常生成下一次：

```
src/DeskBox/Services/TodoReminderService.cs:343-346
                if (item.IsCompleted)
                {
                    return false;
                }

src/DeskBox/Services/TodoReminderService.cs:360-366
                if (TodoRecurrenceService.TryCreateNextOccurrence(item, now, out TodoItem? nextItem) &&
                    nextItem is not null)
                {
                    item.GeneratedNextItemId = nextItem.Id;
                    current.Items.Insert(Math.Clamp(itemIndex + 1, 0, current.Items.Count), nextItem);
```

克隆按字段逐个赋值、不复制 `IsDeleted`（`src/DeskBox/Services/TodoRecurrenceService.cs:59-63` 一带的字段列表），所以新生条目是“活的”。

对照：同一个仓库里其它地方都把 `IsDeleted` 当成“不存在”（例如 `src/DeskBox/Services/QuickCaptureService.cs:199` `if (item is null || item.IsDeleted)`、`:835` `.Where(item => !item.IsDeleted && item.IsPinned)`），说明这不是“软删除语义没定义”，而是提醒链路漏了这一条。

触发条件不需要特殊环境：默认设置（`TodoReminderEnabled = true`、默认提前 5 分钟）下删除任意一条带截止时间的待办，等它到点即可。

## 根因

“条目是否存在”这件事在提醒链路里没有统一表达。`IsCompleted` 被当作“不用提醒”，但 `IsDeleted` 在别处是“不存在”，在提醒链路里却完全不参与判定——判定层与数据层对同一份数据的“存在性”理解不一致。

## 候选方案与打分

| 方案 | 消除根因 | 回归面 | 新概念/依赖 | 可验证性 | 架构一致性 | 实施成本 | 合计 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **A. 判定层把 `IsDeleted` 与 `IsCompleted` 同等对待**（`TryGetReminderTrigger`、`ShouldNotifyDue`、`CompleteAsync` 三处） | 5 | 5 | 5 | 5 | 5 | 5 | **30** |
| B. 删除时清空 `DueDate`/`ReminderOffsetMinutes` | 3 | 2 | 4 | 4 | 2 | 3 | 18 |
| C. 只在扫描循环里过滤 `IsDeleted`（`TodoReminderService.cs:468`） | 3 | 4 | 5 | 4 | 4 | 5 | 25 |
| D. 提醒链路改成读“存活视图”（新增查询层） | 4 | 3 | 2 | 4 | 3 | 2 | 18 |

- B 会把删除变成“改写用户数据”：撤销删除后提醒设置就丢了；而且 tombstone 参与备份/同步比较（`src/DeskBox/Services/DeskBoxDataBackupService.cs:1676-1680`），改字段会改变同步语义。
- C 只挡住“弹提醒”，`CompleteAsync` 仍能从一张已弹出的卡片复活条目——而“卡片弹出后用户才删除”正是最现实的复活路径，所以 C 不算根因修复。
- D 为两个调用点引入一层查询抽象，属于过度工程。

## 采用方案

A：在判定层统一“存在性”：

1. `TryGetReminderTrigger`：`if (item.IsCompleted || item.IsDeleted || item.DueDate is null) return false;`
2. `ShouldNotifyDue`：`if (item.IsCompleted || item.IsDeleted || item.DueDate is not { } dueDate) return false;`
3. `CompleteAsync`：`if (item.IsCompleted || item.IsDeleted) return false;`

不新增抽象、不改变存储格式、不动撤销与同步语义。

## 验证方法

1. **单测（有区分力的红灯）**：在 `tests/DeskBox.Tests/TodoReminderServiceTests.cs` 增加
   - `CheckNowAsync_DoesNotNotifyForDeletedItem`：种一条 `IsDeleted = true`、`DueDate = now + 4min` 的条目，`CheckNowAsync` 必须返回 0、通知列表必须为空、`ReminderDismissedForDueDate` 必须仍为 `null`；
   - `CompleteAsync_DoesNotResurrectDeletedRecurringItem`：种一条 `IsDeleted = true` 且带循环规则的条目，`CompleteAsync` 必须返回 `false`，且存储里仍是 1 条、仍是 `IsDeleted = true`（不得插入新条目）。
   - 修复前这两条必须红（前者红在“通知了 1 条”，后者红在“返回 true 且条目变成 2 条”）。
2. **真机 smoke**：停掉应用 → 备份 `data/widgets/<id>/todo.json` → 注入一条 `IsDeleted = true`、`DueDate = 现在 - 30s` 的条目 → 启动应用 → 等一个扫描周期（启动延迟 8s + 30s 周期）→ 检查该条目是否被写入 `ReminderDismissedForDueDate`。修复前会被写入（提醒已发出），修复后必须仍为 `null`。
3. **回归**：既有的 `TodoReminderServiceTests`（含“到点提醒一次”“截止时间变更后再提醒”“禁用后不提醒”等）全部保持通过。

## 风险与遗留

- 提醒卡片若在删除**之前**已经弹出，用户点“完成”现在会得到“失败”的结果（`DispositionMutationFailed`）而不是复活条目——这是期望行为，但卡片上不会解释“这条待办已被删除”。要解释就得给路由加一个“目标已不存在”的结果种类并配 12 语言文案，属于下一层的产品决策，本轮不做。
- 本轮不处理“错过超过 1 分钟的提醒永远不响”（`TodoReminderService.cs:20` `MissedReminderGrace = TimeSpan.FromMinutes(1)`，`ShouldNotifyDue` `:691`）：那是**另一个**判定问题（该不该补发），需要产品决策（补发窗口多长），留给后续轮。
