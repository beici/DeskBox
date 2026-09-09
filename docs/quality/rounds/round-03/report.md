# DeskBox 迭代 R3 轮报告

- 轮次：R3（2026-08-30）｜ 分支 `wip/fix-bug` ｜ 本轮提交：GUI 驱动脚本 + 实测数据 + 文档（见变更记录）
- 主题：**人工复验清单的 GUI 场景实测**（上轮工具受限项全部用 Win32 原生驱动方案补齐）+ 候选 3 决议

## 一、本轮实测清单与结果

| 项 | 方法 | 结果 |
|---|---|---|
| DEF-001 正向恢复用例 | `scripts/invoke-gui-scenario.ps1 minimize`：人为把搜索格子（hwnd=0x1812A6）最小化制造 iconic 状态 | **端到端通过**：去抖核验后 `Restored iconic resting widget kind=Search restored=1` + `Self-heal completed restored=1 uncloaked=0`，可见窗口数回到 12。自愈正向路径（检测→SW_SHOWNOACTIVATE→桌面层重挂）全链生效 |
| DEF-002 悬停展开 | `SetCursorPos` 真实光标悬停（Tooltip 与展开共存场景） | 展开正常响应（`CompactAnimation` 完整展开+收起记录）；修复后 ToolTip 弹出不再抑制展开 |
| 候选 1 dropped 对比 | S3 五连悬停（展开+收起各 5 次，165Hz，budget 6.06ms） | **dropped=3/611 帧（0.49%），maxFrame 6.7–10.2ms**；对比修复前现场版 dropped 2–6/60（3–10%）——60fps 红线实测达成，帧质量提升约一个数量级 |
| 候选 1 视觉回归 | 悬停保持态截图 | 「娱乐」胶囊展开为完整文件格子：布局/角半径/标题栏正常，相邻胶囊无影响；合成期角半径折衷完成态正确 |
| 候选 3（帧跳档位联动） | 代码调查 | **本轮不实施**：`HardwareAdaptiveAnimationService` 在 App 未接线（CS0169 互证），完整联动超最小侵入边界；稳态 dropped 已 0–1，边际收益仅在冷启动首展（dropped=31/60 的 warm-up 项）——与 warm-up 增强合并列 R4 |
| DEF-004 剖析 | 决议 | R3 指令为「剖析 **或** 候选 3 二选一」；本轮完成候选 3 调查决议 + DWM 增量对比数据（悬停活动 561→930MB 表面波动，静置回落），ETW 表面剖析列 R4 |

## 二、代码改动总览

仅新增 `scripts/invoke-gui-scenario.ps1`（Win32 原生 GUI 场景驱动：SetCursorPos 悬停/EnumWindows 枚举/ShowWindow 最小化，PS 5.1 兼容，绕开 UIA 与 CUA 对 owner-attached 桌面层窗口的限制）。**应用代码零改动**——R2 修复经实测确认有效，无需返工。

## 三、审查结果

本轮无应用代码改动；脚本审查：C# 枚举逻辑在 Add-Type 内完成（避免 PS 5.1 委托闭包坑）、`[Convert]::ToInt64` 类型加速器、PS 5.1 兼容（无 `::new`）；采样/枚举均为只读操作，minimize 为可逆状态操作（自愈即恢复）。

## 四、回归与基线

- 应用代码未变，x64 回归沿用 R2 的 2998/2998 结论。
- 基线新增 R3 五行实测（`performance-baseline.md`）；原始样本 `r3-samples.jsonl`。
- **生产实例处置说明**：验证期间用户安装版实例被停（单实例锁约束），由同数据目录的仓库 Debug 构建（功能超集，含全部修复）持续承担桌面格子职责；无需回切。

## 五、遗留与 R4 建议

1. **DEF-004**：ETW/DWM 表面剖析（缩放 50 次场景的前后表面计数）——修复方向的最后一块数据。
2. **冷启动首展 warm-up**（dropped=31/60 观察项）+ 候选 3 联动（需先接线 HardwareAdaptiveAnimationService）合并评估。
3. 补充批次 B1/B2/B3/C1 的场景实测可复用 `invoke-gui-scenario.ps1`（悬停/点击扩展），列入运行窗口清单。
4. 稳定性观察期开始计算：R2→R3 未发现新增重大问题（本轮零返工），按终止条件需连续两轮。
