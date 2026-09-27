# R15 整改方案（round-15 双路审查闭环批次）

> 输入：`docs/quality/rounds/round-15/全量代码缺陷审查总报告.md`（DEF-130、DEF-131）。
> 原则：最小侵入、语义零变化（除缺陷方向本身）、不触碰 z-order 红线与 12 语言键集。
> 基线：`wip/fix-bug` @ `08bdd98e`，工作树仅含 round-15 文档产出。

## 1. 逐项处置

### W1 ｜ DEF-130 ｜ 两张契约 manifest 的 6 条死条目退役（P3）

- **现状（审查复核更正）**：死条目共 6 条——`ModuleBoundaryContractTests.cs:32`（`ShellDataObjectBuilder.cs = 5`，DEF-127 删除的文件）与 `SettingsSliceOwnershipContractTests.cs:344、:346-349` **五条**（OnboardingWindow.Appearance=10/DesktopOrganization=1/Features=1/Hotkey=15/Storage=5，DEF-128 整删的分部）。**`:345` 的 `OnboardingWindow.Completion.cs = 5` 不是死条目**：该文件尚存（37 行，OnLanguageChanged 活成员），当前树实测 facade 访问为 0，属棘轮合法 slack——**保留不动**。断言只查「实际违规→manifest」方向，死条目使棘轮记忆失真（若执行者误删 :345，测试照样全绿——同类静默漂移，故本清单钉死到行）。
- **修法（删除清单钉死到行，不得越界）**：删除且仅删除 `ModuleBoundaryContractTests.cs:32` 一条 + `SettingsSliceOwnershipContractTests.cs:344、:346、:347、:348、:349` 五条；**不得触碰 :345（Completion.cs=5，合法 slack）/:350（TaskFlow.cs=5）/:351（xaml.cs=4）**。注释：SettingsSlice 侧援引 :352-353 既有 QuickCaptureWidgetWindow 退役先例同款；ModuleBoundary 侧文件内无先例（审查 R2），新写一行注释「ShellDataObjectBuilder went away with the dead drop-delegation path (DEF-127)」。
- **验证**：构建 + 全量回归（两张契约测试自身全绿——棘轮只罚增不罚减，删条目自动通过）。

### W2 ｜ DEF-131 ｜ MergeWidgetsAsync 补双侧切换取消（P3）

- **现状（主流程核验）**：`WidgetManager.Groups.cs:737-927` 全方法无 `_widgetGroupSwitchRequests.Cancel`、不持目标 surface switchGate；全仓其余拓扑变更入口（:1315/:1506/:1727、WidgetManager.cs:2214、Surfaces.cs:219/:229）均先取消再动状态。切换侧结算段（:1156-1237）不设防是既有设计，不动。
- **修法**：在**同组早退校验（:755-760）之后、`preserveRaisedLayer` 计算（:762-764）之前**（即 :760 与 :762 之间，审查 R3 锚点更正）插入：
  ```csharp
  // DEF-131: cancel any in-flight surface member switch on both sides
  // before mutating the topology - this is the one topology change that
  // previously did neither (mirrors RemoveWidgetFromGroupAsync /
  // DissolveWidgetGroupContainingAsync).
  if (sourceGroup is not null)
  {
      _widgetGroupSwitchRequests.Cancel(sourceGroup.SurfaceId);
  }

  if (targetGroup is not null)
  {
      _widgetGroupSwitchRequests.Cancel(targetGroup.SurfaceId);
  }
  ```
  与 :1311-1315 同型（空组条件跳过——standalone 成员无群组 surface 切换请求）；切换侧「取消检查点 + 结算段」既有语义不动。
- **验证**：构建 + 全量回归（群组相关既有用例全绿）+ 静态走查（取消点清单自此全入口覆盖）。

## 2. 不进本批（延后维持，与总报告 §5 一致）

DEF-116 扫描器加固（局部变量键选择形态）、DEF-078 新位点（DataTools 三按钮）、DEF-080 新位点 ×4、观察项 9 条。

## 3. 门禁

1. 停止仓库路径 DeskBox 实例 → Debug x64 构建 0 错误；
2. `python scripts/quality/static_gate.py` PASS（零 async void/同步等待/空 catch 新增）；
3. x64 全量回归全绿（基线 4291/4291）；
4. 启动规范 Debug 实例核验；
5. 台账/TODO 收口（DEF-130/131 → ✅ 已修复；整改批小节遵守「待实施→回填」纪律）。

## 4. 风险与回滚

- 2 个测试文件（删 6 行）+ 1 个源文件（+12 行）；无签名/序列化/ABI/依赖变更。
- 回滚：单 commit revert。
