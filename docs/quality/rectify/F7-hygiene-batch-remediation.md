# F7 卫生批次报告（P3×45 按域顺手修 + 五项死代码 + CFG-08）

> 整改日期：2026-09-01 ｜ 基线：`ad8febe`（批次 D 之后）｜ 分支：`wip/fix-bug` ｜ 提交：`4682a02` ｜ 方式：Linux 静态迭代，未经编译验证（Windows 侧门禁见 `pending-windows-gate.md`）。

## 本批处置总览

P3×45 条中，**大量条目已在前序批次被顺手修复或消除**（台账维护规则允许：修复时对照全表不重复立案）。本批实际动手项与逐条归类如下。

## 死代码删除（本批实际执行）

| 项 | 处置 | 说明 |
|---|---|---|
| ARC-06 `WidgetSurfaceSnapshotCache` | **删除**（含测试） | R1 观察项复核成立：全仓仅测试引用，无生产调用方 |
| ANI-02 硬件自适应动画链路 | **删除**（约 1,000 行） | `AdaptiveTrayAnimationController`（607 行）+ `SmartAnimationAdapter`（80 行）+ `HardwareAdaptiveAnimationService`（291 行）+ `WidgetWindowBase` ctor 的静态引导块（S02 已定案：与 DEF-003 无关联、内含接线即失效缺陷）；CS0169 观察项随之消除 |
| CFG-07 `NormalizeGlobal` 死代码 | **不删（复核为已接线）** | `FileWidgetFolderOpenBehaviorNames.cs:34` 与 `SettingsService.cs:2124` 生产在用——S07 立案时点早于接线提交，当前树不成立 |
| QC-08 缩略图键 + 死代码 | **死代码部分随 DEF-027 消除** | 键部分并入 CFG-08 同型补键逻辑（见下） |
| EVT-04 `RegistrationChanged` 死事件 | **不删（复核为已有订阅者）** | `SettingsWindow.xaml.cs:200/294` 订阅/退订成对（构造/Closed 生命周期），处理器保留并加注释锚点——S06 立案时点早于该接线 |
| QC-04 / QC-09（死宿主内） | **随 DEF-027 消除** | 见批次 D 报告 |

## CFG-08 补键（本批实际执行）

`Common.Close` / `Common.More` 补入全部 12 个 `Strings/*.json`（2561 键 × 12，门禁验证占位符一致）。修复两个可见缺陷：对比度拒绝对话框关闭按钮曾渲染为字面量 `Common.Close`；Markdown 工具栏「更多」按钮 tooltip 非英文语言回退英文。

## OnboardingWindow 字段观察项

`_desktopOrganizationCompleted`（CS0414）复核：字段由 `OrganizationCompleted/Undone` 事件维护、当前无读取方，但语义是「桌面整理步骤完成状态」的锚点，footer 状态扩展（`UpdateFooterState`）是它的预期消费者。处置：保留 + 注释声明意图（删除会丢掉事件语义，且属活跃功能面）。

## 其余 P3 条目归类（前序批次已覆盖 / 挂账）

| 域 | 条目 | 归类 |
|---|---|---|
| S1 内存 | MEM-01 托盘旧 Icon、MEM-02 SoftwareBitmap | **挂账**（需 Windows 侧运行时验证Dispose 时序，静态改写风险大于收益；列入 pending-windows-gate 观察项） |
| S2 动画 | ANI-03 每帧委托分配、ANI-04 反模式、ANI-05 转场中断、ANI-06 不复位 | ANI-01 同域复核已在批次 C 定案（20Hz 计时器为死代码删除）；其余为低频动画卫生，**挂账**随下次动画专项 |
| S3 窗口 | WIN-05/06 | **批次 D 完成**（文档校正） |
| S3 窗口 | WIN-07 搜索弹窗前台失败无降级 | **已被 N2（DEF-029）覆盖**（ShowPopupSafelyAsync 全管线保护） |
| S3 窗口 | WIN-08 拓扑协调器 async void、WIN-09 raised 双写 | **挂账**（WIN-08 属既有防护模式内，WIN-09 为行为等价双写） |
| S4 布局 | LAY-05/06/07/08 | LAY-07 相邻面已在 N1（DEF-028）修复中覆盖组 X/Y 同步；其余 **挂账**（启发式/级联 DPI/预览逐键为行为取舍类，需产品决策） |
| S5 随记 | QC-06 读取链容错、QC-07 自写忽略窗、QC-10 Markdown 递归、QC-11 预览清理、QC-12 附件孤儿、QC-13 超长丢弃、QC-14 图像持锁、QC-15 平行漂移 | QC-15 的宿主漂移面**随 DEF-027 消除**；QC-10（StackOverflow 面）**挂账列高优先观察**；其余 **挂账**（各自需独立小批，避免与本批混改） |
| S6 事件 | EVT-02 TodoItem 残留订阅、EVT-03≡EXC-03 | EVT-03 全局兜底**已被 DEF-021 修复**（批次 C）；EVT-02 **挂账** |
| S7 持久化 | CFG-03/04/05/09 | **挂账**（原子写改造需独立小批 + 数据迁移兼容论证，不属「顺手修」） |
| S8 线程 | THR-06/07 | THR 类大头（DEF-010/022/023/024/025）**批次 A/C 已修**；剩余两项 **挂账**（剪贴板服务状态域/StoreStartup IPC 各需独立论证） |
| S9 异常 | EXC-04/05/06 | EXC-03 部分**已被 DEF-021 覆盖**；EXC-04 的两处改进点、EXC-05/06 **挂账** |
| S10 架构 | ARC-02 文档漏记、ARC-03 文档领先、ARC-04 DI 未接线、ARC-05 重复实例化 | **挂账**（ARC-04 接线涉及启动顺序，ARC-05 需生命周期论证；文档类两项并入下次文档轮） |

**挂账条目已全部保留在台账**（状态不变），收敛式深度审查的去重基线即本表。

## Linux 静态门禁

| 检查 | 结果 |
|---|---|
| 12 语言键一致 | PASS（2559→**2561**×12，CFG-08 两键） |
| async void | 222 = 基线 |
| 剪贴板写配对 | 8 写全配对 |
| 同步等待/空 catch/反射 | 131/219/6，基线已对齐（死代码删除效应） |
| 契约断言重放 | 5181 命中 / 42 失联（基线内），新增 0 |

## 回滚方式

`git revert 4682a02` 单提交整体还原；死代码三个服务文件可独立恢复（无交叉引用）。
