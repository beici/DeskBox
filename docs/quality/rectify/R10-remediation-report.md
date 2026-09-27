# R10 整改报告（round-10 闭环批次，2026-09-27）

> 输入：`rounds/round-10/全量代码缺陷审查总报告.md`（DEF-115、DEF-116）。
> 方案：`R10-remediation-plan.md`（独立完善性审查首审 **GO**，5 条建议全部采纳：文件数笔误、行号校正、AOT 契约字面串约束留痕、新增 VM 删除用例、防御姿态）。
> 门禁总览：Debug x64 构建 **0 错误**（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**（async void 249/剪贴板 8 配对/同步等待 147/空 catch 245/契约重放新增 0，全部与基线持平）；x64 全量回归 **4297/4297**（基线 4295 + 新增 2 用例）；新实例 **PID 29740** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-115 ① | `Services/TodoRecurrenceService.cs:86` | 克隆初始化器补 `StorageMode = attachment.StorageMode,`（`Type` 行后）——下一期附件分类修正，备份/重定位/健康扫描回到正确分支 |
| DEF-115 ② | `ViewModels/TodoWidgetViewModel.DetailAndAttachments.cs:389-410` | `DeleteAttachmentAsync` 物理删除前扫描 `Items` 中其余事项对同路径（OrdinalIgnoreCase）的引用，有则跳过 `File.Delete`（条目照常移除）；扫描插于 `await SaveAsync();` 与 `File.Delete(attachment.FilePath);` 之间（AOT 契约 `AotStage5B4B2B2B2ContractTests.cs:139-140` 字面串顺序约束保持） |
| DEF-115 ③ | `tests/…/TodoRecurrenceServiceTests.cs` | 新增 `TryCreateNextOccurrence_PreservesAttachmentStorageModes`：managed+linked 混合附件克隆后 StorageMode 一致、Id 更换、FilePath 共享 |
| DEF-115 ④ | `tests/…/TodoWidgetViewModelTests.cs` | 新增 `DeleteAttachmentAsync_KeepsSharedManagedFileWhileOtherItemReferencesIt`：共享托管文件一侧删除后物理文件保留（`File.Exists` 真），最后引用侧删除后清理（正清路径） |
| DEF-116 ① | `ViewModels/TodoItemViewModel.cs:582` | Collapse 分支去死实参：`Format("Todo.RecurrenceHistory.Collapse")`（`params` 零实参；展示文本零变化） |
| DEF-116 ② | `ViewModels/SearchPopupViewModel.cs:689-696` | 按分支拆分：`IsComplete` 保留 2 实参；`PartialResults` 单独 `string.Format` 只传 `TotalResultCount`（进行中状态展示耗时本无意义；展示文本零变化，12 语言键集不动） |

净变化：4 源文件 + 2 测试文件（+2 用例 → 回归 4297）；无签名/序列化/ABI/依赖变更。

## 2. 门禁证据

1. 停实例（PID 31372）→ 构建 0 错误（22 警告 ≤ 基线）。
2. `static_gate.py --json rectify/r10-static-gate.json` → **PASS**（五项全部与基线持平）。
3. x64 全量回归 → **4297/4297 通过、0 失败**（2m06s）。
4. 新实例 PID 29740 @ 规范 Debug 路径。

## 3. 延后维持（与总报告 §5 一致）

DEF-080 家族新位点（`CloudBackupService.cs:607` 修复首选）、DEF-101 泛化（SafeBroadcast 立项）、THR-06 同族、DEF-078 新位点（GlanceWidgetSettingsSection.OnLoaded）、孤儿键清理、观察项 11 条；可选加固：static_gate 固化「Format 调用点 arity」扫描、O-11 JumpListService COM 一行修（列下次卫生批候选）。
