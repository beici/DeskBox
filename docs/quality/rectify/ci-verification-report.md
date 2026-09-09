# CI 验证收尾报告（F6/F7 迭代轮 · GitHub Actions）

> 日期：2026-09-01 ｜ 结论：**CI 全绿（Build + 3015 用例全过）** ｜ 本文档补全 `pending-windows-gate.md` 中「编译 + 测试」两项的验证状态

## 验证结果

| 项 | 状态 | 证据 |
|---|---|---|
| Restore（锁定模式） | ✅ success | NuGet lock 文件有效，无新依赖引入（红线⑤复核通过） |
| Release x64 Build | ✅ success | WinUI3 XAML 编译 + C# 编译全部通过 |
| x64 全量测试 | ✅ success | **3015 / 3015 通过**（含本轮新增约 30 个契约用例） |
| 运行环境 | windows-latest，.NET 10.0.x | run: <https://github.com/beici/DeskBox/actions/runs/33477323326> |

## CI 过程抓到并修复的问题（4 轮收敛）

1. **CS0050 ×3**（`e8108d2`）：`HotkeyApplyResult` 是 `internal` 却被 `public` async 方法返回——DEF-022 subagent 半成品余毒，独立审查（语义层）抓不到编译级可访问性问题。修复：改 `public`。
2. **CS1739 / CS0023 / CS0136**（`3a3fc17`）：DEF-022/023 的参数名错拼（`hasStorageItems` → `HasStorageItems`）、调用方未适配 `bool → HotkeyApplyResult` 返回类型变更、同名局部变量作用域冲突。
3. **CS0103**（`a693a1d`）：批次 D 死宿主删除后 `FrostedActionSurfaceContractTests` 残留对已删 `quickCaptureWindow` 变量的引用。
4. **IndexOutOfRangeException（真 bug！）**（`a2adb5f`）：DEF-026 胶囊截断逻辑 `FitPrimarySizes` 在极小工作区把 `sizes` 数组缩到 `fitCount`，但 `ArrangeHorizontalSingleRow` / `ArrangeVerticalSingleColumn` 仍按 `items.Count` 迭代 → 运行时数组越界崩溃。修复：循环上界改为 `widths.Length` / `heights.Length`（被截断的尾部胶囊不摆放，正确回退到自由放置，与 DEF-026 设计语义一致）。**这是本轮唯一一个会崩到用户桌面的缺陷，恰好是 Linux 静态审查（含独立审查、三面深挖）都抓不到、只有真实执行才能暴露的类别。**
5. **8 个测试锚点失配**（`a2adb5f`）：CRLF checkout 使 `\n` 字面锚点失配（改为 `\r?\n` 正则）；三处测试自身写错（Dispose 锚点未限定作用域、合法新建路径被 DoesNotContain 误杀、方法定义行被计入调用数）。

## 剩余待验证项（Windows 侧人工）

编译与自动化测试已闭环。`pending-windows-gate.md` 中的以下项目仍需实机确认（属「体验/行为」性质，CI 无法覆盖）：

- 随记全功能人工回归（`F6-D-quickcapture-regression-checklist.md` 30 项）
- 看门狗 / 激活失败诊断 / 启动失败通知的实机表现
- 拖拽延迟供给（DEF-023）在真实 Shell drop target 的手感
- `%LOCALAPPDATA%\DeskBox\DeskBox.log` 的 `[Build] buildTime` 身份核实

## 复盘要点

- 「无编译环境」红线下，静态审查能覆盖语法结构、接线模式、契约锚点，但**编译级错误（CS 类型）与执行级错误（越界/竞态）必须靠真实构建+测试**。本轮 4 轮 CI 迭代的 4 类问题全部属于该盲区，印证了任务书「Windows 侧门禁由用户执行」设计的必要性。
- 建议：后续迭代保持「推送即触发 CI」的门禁节奏（`ci.yml` 已补 `workflow_dispatch` 手动触发器，分支 gate 可随时手动跑）。
