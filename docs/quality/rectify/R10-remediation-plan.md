# R10 整改方案（round-10 双路审查闭环批次）

> 输入：`docs/quality/rounds/round-10/全量代码缺陷审查总报告.md`（DEF-115、DEF-116）。
> 原则：最小侵入、零架构变更、展示文本零变化、不触碰 12 语言键集与 z-order 红线。
> 基线：`wip/fix-bug` @ `e3ad11ad`，工作树仅含 round-10 文档产出。

## 1. 逐项处置

### W1-1 ｜ DEF-115 ｜ 循环 Todo 附件克隆漏 StorageMode + 托管物理文件跨事项共享无删除协调（P3）

- **现状（主流程核验）**：`TodoRecurrenceService.cs:79-88` 克隆初始化器缺 `StorageMode`（`TodoAttachment.cs:18` 默认 `LinkedStorageMode`）→ 下一期托管附件被误分类；`TodoWidgetViewModel.DetailAndAttachments.cs:389-399` 删除附件时对 `IsManagedCopy` 无条件 `File.Delete`，而循环克隆与源事项共享同一物理路径（新 Id 使去重失效）→ 一侧删除另一侧悬空。规范克隆 `CloneTodoItem`（FilteringAndAppearance.cs:730）正确复制该字段，证明属遗漏。托管目录按事项分目录（`GetManagedAttachmentDirectory` → `<attachments>/<itemId>/`），事项级删除无整目录清理，唯一危险面是附件级删除。
- **修法**：
  1. 克隆初始化器补一行 `StorageMode = attachment.StorageMode,`（`Type` 行后）——分类修正，使备份快照/重定位（TodoWidgetStore.cs:655 仅改写 IsManagedCopy 路径前缀）/健康扫描（DeskBoxAttachmentHealthService.cs:179）全部回到正确分支；
  2. `DeleteAttachmentAsync` 的物理删除前加同 store 引用扫描：遍历 `Items`（`ObservableCollection<TodoItemViewModel>`，TodoWidgetViewModel.cs:113）中除当前事项外的所有包装项的 `Item.Attachments`，存在 OrdinalIgnoreCase 同路径引用则**跳过物理删除**（条目本身照常移除）——两侧无论从哪边删都不再制造悬空，孤儿面收敛为「所有引用方均已删除」的正常清理路径。扫描为 best-effort：Items 与 store 由 DEF-043 家族的合并链路保持镜像。**实现约束（完善性审查留痕）**：`AotStage5B4B2B2B2ContractTests.cs:139-140` 以 `IndexOf` 断言 `await SaveAsync();` 先于 `File.Delete(attachment.FilePath);` 两个字面串——扫描代码必须插于两者之间，且不得改名/移除这两个字面串或调换其顺序；
  3. 契约用例钉死：`TodoRecurrenceServiceTests.cs` 新增用例——带 managed + linked 混合附件的源事项经 `TryCreateNextOccurrence` 后，克隆侧逐附件 `StorageMode` 与源一致、`Id` 已更换、`FilePath` 沿用共享（分类与身份语义双钉）；
  4. VM 用例钉死删除协调（完善性审查建议 4）：`TodoWidgetViewModelTests.cs` 既有 `DeleteAttachmentAsync` fixture（:1115-1146，临时目录 store）扩展——预置两个事项共享同路径 managed 附件（临时目录落物理文件），从一侧删除后断言条目已移除且 `File.Exists(path)` 仍真；单引用侧再删后文件被清理（正清路径）。
- **验证**：新增用例 + 全量回归全绿。
- **不做**（留痕）：派生时复制物理文件（每期一份副本的存储增长取舍，触发条件：用户报告共享语义不合预期）；跨 widget 引用扫描（循环克隆不跨 widget，无此触发面）。

### W2-1 ｜ DEF-116 ｜ 两处 Format 实参/占位符失配（P3）

- **现状（主流程核验）**：①`TodoItemViewModel.cs:582` `Format("Todo.RecurrenceHistory.Collapse", HiddenRecurringHistoryCount)`，键值「Hide history」/「收起历史」无占位符（实参被静默吞）；②`SearchPopupViewModel.cs:689-694`（方法 GetSearchStatusText :664-695）单一 `string.Format` 以 2 实参服务 `Search.Status.Results`（{0}+{1:F0}，匹配）与 `Search.Status.PartialResults`（仅 {0}，第二实参被吞）。
- **修法**：①Collapse 分支改 `Format("Todo.RecurrenceHistory.Collapse")`（`params` 兼容零实参；展示文本零变化）；②按分支拆分——`IsComplete` 分支保留 2 实参，`PartialResults` 分支单独 `string.Format` 只传 `TotalResultCount`（进行中状态展示耗时本无意义，展示文本零变化）。**不触碰 12 语言键集**（不加 {1} 占位符方案留痕不采）。
- **验证**：全量回归全绿；两处 UI 文案在回归中无快照断言冲突（grep 确认无测试钉住这两处格式化输出）。
- **可选加固（留痕不进本批）**：把「Format 调用点实参 ↔ 键占位符 arity」扫描固化进 `static_gate.py`（B 代理方法已验证），随下次门禁改造批次落地。

## 2. 不进本批（延后维持，与总报告 §5 一致）

DEF-080 家族新位点（`CloudBackupService.cs:607` 为修复首选位点）、DEF-101 泛化（SafeBroadcast 立项）、THR-06 同族、DEF-078 新位点（GlanceWidgetSettingsSection.OnLoaded）、孤儿键清理、观察项 11 条（O-11 JumpListService COM 一行修列入下次卫生批候选）。

## 3. 门禁

1. 停止仓库路径 DeskBox 实例 → Debug x64 构建 0 错误；
2. `python scripts/quality/static_gate.py` PASS（本批零 async void/同步等待/空 catch 新增）；
3. x64 全量回归全绿（基线 4295/4295 + 本批新增用例）；
4. 启动规范 Debug 实例核验；
5. 台账/TODO 收口（DEF-115/116 → ✅ 已修复）。

## 4. 风险与回滚

- 4 文件改动（TodoRecurrenceService.cs、TodoWidgetViewModel.DetailAndAttachments.cs、TodoItemViewModel.cs、SearchPopupViewModel.cs）+ 2 测试文件；每处 ≤12 行。
- 行为变化面：仅「删除被共享的托管附件时保留物理文件」与「循环克隆附件分类正确」两处，均为缺陷方向；无签名/序列化/ABI/依赖变更。
- 回滚：单 commit revert。
