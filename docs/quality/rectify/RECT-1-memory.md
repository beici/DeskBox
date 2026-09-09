# RECT-1 整改版：内存占用过高问题

- 归属：复测未达标项整改批次 ｜ 关联缺陷：DEF-004（P1）｜ 验证方式：代码层面审查 + 自动化回归

## 一、复测结论 vs 证据对照

**复测称「内存占用异常偏高未根治，多次编辑后依然居高不下」——该结论在开发构建上与实测数据不符，判定为复测二进制陈旧 + 部分真实残留缺陷的混合。**

- 复测要求提到「C# 后端 + Vue3 前端的架构组合」：**本仓库不存在任何 Vue/前端框架代码**（全仓库零 .vue/package.json/前端构建配置，技术栈为 WinUI 3/XAML + C# + Rust 原生层，子代理 B 双向 grep 核验）。整改按实际技术栈执行。
- R4 已实测：S2 布局编辑 50 次分段采样——DeskBox Private +5MB 后平台化、DWM Private +10MB 后三次采样持平、GDI 恒定 91。「编辑会话无界泄漏」被数据排除。
- 若复测跑的是 `D:\DeskBox` 安装版（1.4.8.0，构建于 2026-08-29 19:14，早于全部五轮工作），则「编辑后内存居高不下」正是 1.4.8 的原始表现（实测 Private 760MB）——与本批全部「未找到/未生效」结论同源。

## 二、根因分析（本轮新增发现）

审计子代理发现一个**真实的会话内回收封锁缺陷**：

`FileSurfaceContent.StackPopover.cs:1347` 的 `StackPopoverCloseButton_Click` 调用 `BeginWidgetInteraction("surface-stack-popover-close-button")` 租用一个交互深度单位，但全仓库无任何路径归还——`HideStackPopoverForReuse`（关闭后的公共复用路径）仅把弹层窗口停泊屏外。后果：点击堆叠弹层关闭按钮一次，`WidgetSessionManager._interactionDepth` 永久 ≥1，`MemoryCleanupPolicy.IsVisibleIdleCandidate`/`CanTrimWorkingSet` 因 `IsWidgetInteractionActive` 恒假而**封锁全部空闲内存回收**（可见态 GC、WS trim、隐藏态资源释放、空闲 Z 归一），直到托盘隐藏/显示强制归零才自愈。

这正是「多次编辑/操作格子后内存居高不下、回收不生效」的一个确定性成因：**不是泄漏，是回收通道被交互深度泄漏焊死**。

## 三、修复方案与代码修改说明（已实施）

| 文件 | 改动 |
|---|---|
| `FileSurfaceContent.StackPopover.cs` | 新增字段 `_stackPopoverCloseButtonInteractionLeased`；`StackPopoverCloseButton_Click` 在 Begin 后置位；`HideStackPopoverForReuse` 开头**恰好一次**归还（置位才 End，防止外点关闭路径超量归还） |

另：DEF-004 长周期观测机制已落地（`record-longperiod-sample.ps1` 自动发现实例采样 + 每 6 小时定时任务，样本写入 `r5-longperiod-samples.jsonl`），数据积累后按增长形态定论（线性→表面剖析修复；平台化→归因历史版本关闭观测）。

## 四、代码审查结论

- 配对纪律：置位/归还以布尔租约守卫，天然幂等；Hide 早退分支（无 host）不归还——此时弹层从未被 Begin 过（host 缺失意味着点击路径也不会走到 Begin），无失衡。✅
- 线程：Begin/End 均在 UI 线程（点击与隐藏回调）。✅
- 回归：x64 2998/2998 通过。✅

## 五、验证方案

1. 点击堆叠弹层关闭按钮 → `WidgetSessionManager` 深度归零（日志/诊断）；其后 10 分钟空闲触发 VisibleIdle 回收（日志 `Visible idle cleanup`）。
2. 长周期观测样本 3–7 天后按 `performance-baseline.md` 定论标准闭环。
