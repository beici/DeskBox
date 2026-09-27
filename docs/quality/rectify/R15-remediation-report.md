# R15 整改报告（round-15 闭环批次，2026-09-27）

> 输入：`rounds/round-15/全量代码缺陷审查总报告.md`（DEF-130、DEF-131）。
> 方案：`R15-remediation-plan.md`（独立完善性审查**首审不通过**——R1：W1 现状把仍存在的 `Completion.cs=5` 合法松弛误列进死条目；按 R1~R3 修订后复审 **GO**）。
> 门禁总览：Debug x64 构建 **0 错误**；`static_gate.py` **PASS**（五项与基线持平：async void 247 / 剪贴板 8 配对 / 同步等待 147 / 空 catch 241 / 契约重放新增 0）；x64 全量回归 **4291/4291**；新实例 **PID 25320** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-130 | `tests/…/ModuleBoundaryContractTests.cs:32` | 删除死条目 `ShellDataObjectBuilder.cs = 5`，新写注释「ShellDataObjectBuilder went away with the dead drop-delegation path (DEF-127)」（该文件无既有先例） |
| DEF-130 | `tests/…/SettingsSliceOwnershipContractTests.cs:344-351` | 删除 **5 条**死条目（Appearance=10/DesktopOrganization=1/Features=1/Hotkey=15/Storage=5，文件已随 DEF-128 整删），附先例注释；**`:345` 的 `Completion.cs = 5` 按审查指令保留**（文件尚存 37 行、facade 访问 0、合法 slack；首审曾误列，复审钉死到行）；TaskFlow=5/xaml.cs=4 活上界保留 |
| DEF-131 | `Services/WidgetManager.Groups.cs`（MergeWidgetsAsync，同组早退校验后、preserveRaisedLayer 前） | 对 `sourceGroup`/`targetGroup` 两侧 surface 各补 `_widgetGroupSwitchRequests.Cancel(surfaceId)`（空组跳过——standalone 成员无群组切换请求；与 RemoveWidgetFromGroupAsync/DissolveWidgetContainingAsync :1315/:1506 同型）；全仓拓扑变更入口的取消防线自此无缺口 |

净变化：2 测试文件（−7+5 行）+ 1 源文件（+14 行）；无签名/序列化/ABI/依赖变更。

## 2. 门禁证据

1. 停实例 → 构建 0 错误。
2. x64 全量回归 → **4291/4291 通过、0 失败**（2m46s）。
3. `static_gate.py --json rectify/r15-static-gate.json` → **PASS**（五项与基线持平）。
4. 新实例 PID 25320 @ 规范 Debug 路径。

## 3. 延后维持（与总报告 §5 一致）

DEF-116 扫描器加固（局部变量键选择形态）、DEF-078 新位点（DataTools 三按钮 + TaskFlow ×2）、DEF-080 新位点 ×4、观察项 9 条（O-30~O-33 + B 面 5 条）。
