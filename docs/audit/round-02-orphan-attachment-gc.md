# 第 2 轮 · 缺陷 B：读取失败时把“读不到”当成“没有”，顺带删光附件

- 轮次：第 2 轮（数据安全方向）
- 等级：P1
- 类型：真缺陷（不可逆数据丢失，潜在未在本机触发）
- 影响面：快捷收集（QuickCapture）的附件目录

## 人话摘要

快捷收集把待办和附件放在同一个数据文件里。某次启动时这个文件因为被杀毒软件/同步盘/备份工具短暂占用而读不出来，程序没有区分“读失败”和“文件就是空的”，直接把内存当成空清单，然后在随后任何一次自动清理里，把所有“清单里没有的”附件目录整目录递归删除。用户的图片、文档副本就此永久消失，界面上只表现为“条目不见了”。

## 问题

`QuickCaptureService.EnsureLoadedCoreAsync()`（`src/DeskBox/Services/QuickCaptureService.cs:1513`，改动前）用

```csharp
_data = await _store.LoadAsync();
```

接收加载结果。而 `QuickCaptureStore.LoadAsync()`（`src/DeskBox/Services/QuickCaptureStore.cs:136-153`，改动前）内部调用的是 `ResilientJsonStore.LoadAsync(...)`——这个便捷重载**丢弃了 `ResilientJsonLoadSource`**，于是“文件读失败后回退到默认空文档”与“文件确实不存在/确实为空”在调用方看来完全一样。

紧接着，附件 GC `CleanupOrphanedAttachmentDirectoriesCore()`（同文件 `:1868-1896`）以 `GetReferencedAttachmentItemIdsCore()` 作为“谁还活着”的唯一依据，对 `attachments/` 下所有不在集合里的目录执行 `TryDeleteDirectory`（递归）。空集合 + 递归删除 = 全删。

这条 GC 有 13 个调用点（`:261, :315, :369, :790, :919, :978, :1011, :1070, :1095, :1115, :1136, :1185, :1312`），其中包括每次增删改后的自动清理，以及用户可见的“清理图片缓存”按钮（`CleanupUnusedImageCacheAsync()`，`:1179-1191`）。也就是说：读失败之后，用户几乎不需要做任何事，第一次编辑就会触发全删。

## 证据

1. 加载结果被丢弃（改动前）：`QuickCaptureStore.cs:136-153` 只 `return await ResilientJsonStore.LoadAsync(...)`。
2. 失败路径确实返回“默认值”而不是抛异常：`ResilientJsonStore.LoadWithResultAsync`（`:44-120`）在文件不可读/内容损坏时 `return new ResilientJsonLoadResult<T>(createDefault(), ResilientJsonLoadSource.DefaultAfterFailure)`。
3. GC 的删除是不可逆的：`:1885 TryDeleteDirectory(itemDirectory)`，`Directory.Delete(..., recursive: true)`，不走回收站。
4. 对照：设置服务遇到同样的加载失败时**会**记录恢复状态并提示用户（`SettingsService.cs:789-801`），快捷收集没有等价处理。
5. 本机现场（`%LOCALAPPDATA%\DeskBox`）没有 `attachments/` 下被误删的痕迹——因为本机从未发生过读失败。这是“潜在缺陷”，不是已发生事故。

## 根因

“读不到”被建模成“读到空文档”。只要加载失败以默认值形式下沉到业务层，所有以“内存中的清单”为真值的破坏性操作都会把用户数据当成垃圾。

## 候选方案与打分

判据：①消除根因 ②回归面 ③新概念/依赖 ④可验证性 ⑤架构一致性 ⑥实施风险（1 差 – 5 好）。

| 方案 | ① | ② | ③ | ④ | ⑤ | ⑥ | 合计 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A. 把加载来源暴露到 store，服务记录“数据可信”状态，不可信时跳过破坏性 GC；写入成功后恢复可信 | 4 | 5 | 4 | 5 | 5 | 5 | **28** |
| B. 加载失败即抛异常，服务进入只读模式，拒绝一切变更（根因修复） | 5 | 2 | 3 | 4 | 4 | 2 | 20 |
| C. GC 前重新读一遍文件做“影子校验” | 2 | 4 | 3 | 3 | 3 | 3 | 18 |
| D. 给附件目录加“墓碑/回收站”式软删除 | 3 | 2 | 2 | 3 | 2 | 2 | 14 |

方案 B 是真正的根因修复，但它改变了产品行为：用户会看到一个“数据暂时不可用”的界面状态，需要产品决策（提示文案、12 语言、是否允许强制覆盖）。按仓库约定，这类需要产品决策的改动不在本轮静默实施，记入遗留。

## 采用方案（A）

1. `QuickCaptureStore` 新增 `internal ResilientJsonLoadSource LastLoadSource { get; private set; }`，`LoadAsync()` 改用 `LoadWithResultAsync` 并记录来源。
2. `QuickCaptureService` 新增 `private bool _dataTrusted;`：
   - 加载后 `_dataTrusted = LastLoadSource is Primary or DefaultMissing`（`DefaultMissing` = 全新安装，没有附件可删，可信）；
   - `SaveCoreAsync` 成功后置 `true`（磁盘与内存已一致，GC 重新有权威依据）。
3. `CleanupOrphanedAttachmentDirectoriesCore` 开头：`_dataTrusted == false` 时记一条日志并直接返回。

范围刻意收窄：只拦**附件目录的递归删除**（不可逆）。图片缓存/缩略图文件不拦——它们是派生文件，可重建，且拦截会让“清理缓存”按钮在故障状态下失效。

## 验证方法

- 新增单测 `CleanupUnusedImageCacheAsync_KeepsAttachmentsWhenStoreCouldNotBeRead`（`tests/DeskBox.Tests/QuickCaptureServiceTests.cs`）：写坏 `quick-capture.json` → 建 `attachments/existing-item/managed.bin` → 调 `CleanupUnusedImageCacheAsync()` → 断言目录与文件仍在。
- 既有 `CleanupUnusedImageCacheAsync_RemovesOrphanedAttachmentDirectories`、`DeleteItemAsync_KeepsAttachmentDirectoryAliveDuringUndoWindow` 必须继续通过（正常路径的 GC 不受影响）。
- A/B：先在未修改的源码上跑该测试，必须**失败**（证明测试真的覆盖了缺陷）；再在修改后跑，必须通过。

## 风险与遗留

- 采纳方案 A 的代价：故障状态下孤儿附件不再被回收（只占磁盘，不影响正确性），且用户看到的仍是空清单——**误导性仍在，只是不再毁数据**。
- 遗留（需要产品决策）：方案 B 的“只读/降级模式”——加载失败时把“数据暂不可用”如实告诉用户，并禁止一切写入，避免用户对着空清单继续录入后被覆盖。同族问题还有 `SettingsService`（只提示不回滚）与 `TodoWidgetStore`。
- 同类破坏性清理还有两处待评估：`PruneAutomaticSnapshots`（按 mtime 裁剪自动快照，无“刚写入这份”豁免）、scoped 云恢复的“先删后拷”。
