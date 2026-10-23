# 第 4 轮：收纳文件夹不可达时，格子被静默销毁、文件变孤儿

## 人话摘要

1. 收纳格子会把文件整理进一个"管理文件夹"（本地默认在用户数据目录，也可以指向 U 盘、网络盘）。
2. 这个文件夹**暂时打不开**的时候——U 盘拔了、网络盘掉线、被安全软件锁住——用户选择"把文件移回桌面"或"删除收纳文件夹"，程序看到"文件夹不存在"，就当成"没有东西要处理"，**照样把格子删掉**。
3. 用户看到的是：格子没了，文件也没回到桌面。文件其实还躺在那个暂时连不上的位置，而格子已经不在，程序再也没有入口去处理它们。
4. 代码自己的注释写着"不能静默地把用户文件留在孤儿目录里"，但只在网络盘那一个分支上兑现了这句话。

## 证据

`src/DeskBox/Services/WidgetManager.Storage.cs:554-558`（修复前）

```csharp
string folderPath = Path.GetFullPath(config.MappedFolderPath!);
if (!Directory.Exists(folderPath))
{
    return;
}
```

`Directory.Exists` 对三种情况一律返回 `false`：目录真的不存在、卷不可达（盘符掉线/未插）、访问被拒绝。随后
`WidgetManager.RemoveWidgetAsync` 继续走到 `_settingsService.RemoveWidgetImmediate(widgetId)`
（`src/DeskBox/Services/WidgetManager.cs:2106`），格子被永久移除。

同一批代码里的注释正好描述了这种后果：

- `src/DeskBox/Services/WidgetManager.cs:2053-2058`：*"a failed move-back or recycle throws here and leaves the widget fully intact, so the close flow can surface the error and the user can retry"*
- `src/DeskBox/Services/WidgetManager.Storage.cs:579-581`：*"A silent 'keep the folder, remove the widget anyway' strands the user's files in an orphaned directory with no feedback."*

第二个触发点在移动过程中掉线：`WidgetManager.Storage.cs:608-617` 把"移动后文件夹不存在"当成"全部移动完成"，直接返回成功。

## 根因

把"看不见"当成"没有"。可判定的信息（卷是否可达、路径是否只是被拒绝）在调用点被 `Directory.Exists` 抹平了。

## 候选方案

| 方案 | 消除根因 | 回归面 | 新概念依赖 | 可验证性 | 架构一致性 | 合计 |
| --- | --- | --- | --- | --- | --- | --- |
| A. 三态判定（存在 / 确实不存在 / 暂时不可达），不可达时抛错保留格子 | 5 | 5 | 4 | 5 | 5 | **24** |
| B. 不可达时弹确认框让用户选"仍然删除" | 4 | 3 | 3 | 3 | 3 | 16 |
| C. 不可达时保持现状但只写日志 | 2 | 5 | 5 | 2 | 4 | 18 |
| D. 删除前先枚举一次目录内容做"探测" | 3 | 3 | 2 | 3 | 3 | 14 |

采用 **A**。判据：`FileSystemInfo.Attributes` 能区分三种情况——目录存在则成功；不存在抛
`DirectoryNotFoundException`；存在但被拒绝抛 `UnauthorizedAccessException`。对
`DirectoryNotFoundException` 再叠加"卷是否可达"：卷可达说明文件夹确实被删了（可以继续移除格子），卷不可达说明
文件可能还在（必须保留格子）。

## 改法

`src/DeskBox/Services/WidgetManager.Storage.cs`

- 新增 `private static bool IsManagedFolderUnavailable(string folderPath)`，按顺序判定：
  1. `Path.GetPathRoot` 得到的卷根不存在（未插的 U 盘、断开的映射盘）→ 不可达；
  2. 读 `Attributes`：`FileNotFoundException`/`DirectoryNotFoundException` → 交给下一步；`IOException`/
     `UnauthorizedAccessException`/`ArgumentException`/`NotSupportedException` → 不可达；
  3. **`Attributes` 返回 `(FileAttributes)(-1)`** → 不可达（实测发现，见下）；
  4. `Directory.GetParent(folderPath)` 也读不到 → 不可达（父目录都不在，说明是卷/挂载点问题而不是"文件夹被删"）；
  5. 其余情况 → 文件夹确实不存在。
- `ApplyWidgetRemovalActionAsync` 与 `MoveManagedFolderContentsToDesktopAsync` 的 `!Directory.Exists` 分支改为：
  不可达 → `throw new InvalidOperationException(LocalizeOrKey("Widget.Error.ManagedFolderUnavailable"))`（格子保留、关闭流程可提示），
  确实不存在 → 保持原来的 `return`。
- 新增资源键 `Widget.Error.ManagedFolderUnavailable`，十二种语言同步补齐（每份 3091 个键，键集与占位符一致）。

### 实测发现：`Attributes` 会返回 -1 而不是抛异常

第一版守卫只写了 `catch`，结果单测在"不存在的盘符"上**没有抛异常**，守卫判定为"文件夹确实不存在"，测试红在
`Assert.ThrowsAny() Failure: No exception was thrown`。在 .NET 10 上，对不可达卷的路径读
`FileSystemInfo.Attributes` 返回 `(FileAttributes)(-1)`（全位为 1）而**不抛异常**，因此必须显式判定 `-1`。
这一点与 `Directory.Exists` 的"静默 false"是同一类陷阱：API 用哨兵值表达失败。

## 验证方法

- 单测（新增，A/B 可对照）：
  - `WidgetManagerStorageCleanupTests.RemoveWidgetAsync_UnreachableManagedFolder_KeepsWidgetAndConfig`：把格子的
    `MappedFolderPath` 指到一个不存在的盘符、并让收纳根也在该盘符下，调用
    `RemoveWidgetAsync(..., MoveManagedFolderContentsToDesktop)`，期望抛 `InvalidOperationException` 且格子仍在设置里。
    旧代码下该测试必红（`No exception was thrown`，格子被删）——已实测。
  - `WidgetManagerStorageCleanupTests.RemoveWidgetAsync_MissingManagedFolder_RemovesConfigWithoutCreatingFolder`（既有）
    必须继续通过：卷可达、文件夹确实被删时仍然静默移除格子。
- A/B 实测（本机，High 完整性计划任务，`--filter FullyQualifiedName~WidgetManagerStorageCleanupTests`）：
  - 修复前：`RemoveWidgetAsync_UnreachableManagedFolder_KeepsWidgetAndConfig` **FAIL**（`No exception was thrown`）。
  - 修复后：两个测试类 41 个用例全绿。

## 被证伪的候选：无回收站卷上的"静默永久删除"

上一轮的只读侦察报告提出：`WidgetManager` 删除收纳文件夹时固定 `recycleManagedFolderDeletes: true`、且以
`ownerHandle = IntPtr.Zero` 无主调用 Shell，而 `FOF_ALLOWUNDO` 在"没有回收站的卷"上会静默降级为永久删除，
调用方却看到"成功"。我按这个前提写好了修复（新增 `HasRecycleBin` 谓词 + 守卫），随后做真机实验，**前提不成立**：

- 造一个 64 MB exFAT 的 VHD（`E:\DeskBox-dev\vhd\norecycle.vhdx`），挂载为 `Q:`，确认 `Q:\$Recycle.Bin` 不存在。
- 用 Shell 的回收语义删一个文件（`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., SendToRecycleBin)`）：
  调用成功，**Windows 当场创建了 `Q:\$RECYCLE.BIN`**，文件进入回收站，没有永久删除。
- 再用同样方式删一个目录：同样自动创建回收站、目录进回收站。
- 也就是说"卷上没有 `$Recycle.Bin` 目录"并不等于"回收会降级为永久删除"——回收站是按需创建的。

结论：**撤回**这个修复（`FileService.cs` 的守卫扩展、`HasRecycleBin`/`CanRecycleWithoutOwner`、对应单测）。保留的
是仓库原有的、有据可依的网络盘守卫 `IsNetworkVolumePath`。留下的未验证窗口（作为后续线索，不修）：卷为只读、
回收站被组策略禁用、文件夹超过回收站配额时，Shell 是否降级为永久删除——这需要专门的实验环境才能确认。

## 风险与遗留

- 不可达判定依赖 `FileSystemInfo.Attributes` 与父目录探测：网络盘超时可能阻塞数秒，与原先 `Directory.Exists` 的
  阻塞特性一致，未引入新的等待模型。
- 判定为"确实不存在"时仍会静默移除格子（保持原有行为）：如果用户是先拔盘再删格子，卷不可达会被判为不可达而保留
  格子，符合预期；只有"卷可达且文件夹被删"才会静默移除。
- 抛出的异常由关闭流程转成 `Widget.Error.ManagedFolderUnavailable` 提示，格子保留；用户可重试或先恢复卷再删。
