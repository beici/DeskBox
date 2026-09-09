# R4 Round 4 收敛审查报告（linux-hermes）

## 审查背景
- 基线：HEAD=4c16e05（R3 修复+docs，CI 绿）
- 本轮目标：验证 R3 后是否达到收敛判据（一整轮全量审查无新增 P0/P1/P2，仅 P3）
- 审查方式：单 subagent（FileSurfaceContent 大面，deleg_96eac299，66 工具调用）+ 主线亲审（其余子系统）

## 主线亲审结果（零新缺陷，全部 GO）

| 审查面 | 结论 | 关键证据 |
|---|---|---|
| BoundedStaOperationRunner | GO | STA 线程 CoInitializeEx/CoUninitialize 配对（finally）；取消不中止 native 调用、slot 归还不越权（worker 归属正确）；异常全走 TCS 无逃逸；admission/workers 双层有界 |
| FileService.OpenItem 门控 | GO | item 快照先行（Path/TargetPath/IsShortcut 值拷贝）杜绝后台线程 XAML 通知；静态有界 runner(2并发/6排队/2s超时)；Busy 语义明确；try/catch/finally + trace 全覆盖 |
| FileOpenTrace | GO | opt-in（PerformanceLogger.IsEnabled 门控）；不记录文件名/路径；InvariantCulture 格式化 |
| FileMetaService | GO | LoadIconBytes 内部吞异常返 null（无故障 Task 驻留）；UI 解码 tcs 包裹兜底；LRU 64 上界 + 只淘汰已完成任务；SHGetFileInfo + DestroyIcon finally 配对；FillStats 全 catch |
| ShortcutHelper（上游 +118） | GO | 负缓存改进：失败条目 2s 退避重试（IsRecentMetadataFailure）替代旧全量 Clear；LRU 512 上界；线程锁正确 |
| IconBitmapQuality | GO | 纯数学启发式（padded-icon 检测），保守阈值，无状态 |
| MemoryCleanupPolicy | GO | retry delay 指数钳 0-30 + long cast 防溢出；阶段 flags 门控（HasFlag 跳过已完成）无双重调度；CleanupNever 哨兵正确 |
| PerformanceLogger | GO | s_windowCounts 键有界（window kinds）+ Math.Max(0,...) 防负；Measure/Mark 为日志直写无缓冲堆积；开关经环境变量+设置双门控 |
| 占位符 arity 崩溃面 | PASS | 全库 Format 调用点参数量 vs 占位符数匹配率 100%（169 条 +1 假阳性为计数器含 key 自身，修正后零失配）；{F0} 定点格式合法 |
| RTL 镜像 | 观察项 | ar-SA 有翻译但 UI 无 FlowDirection 处理——上游长期设计决策非 1.4.9 回归，记录不立案 |

## Subagent 审查结果（FileSurfaceContent 面）：GO
五项重点核查全部通过（事件对称性/async void 兜底/监听器/StackPopover 生命周期/虚拟化复用竞态），详见 deleg_96eac299 报告。3 条 P3 记录：

| 编号 | 内容 | 置信度 | 处置 |
|---|---|---|---|
| DEF-040 | StackSurface PropertyChanged 订阅生命周期缝隙（容器销毁路径 Unloaded 不保证触发；EVT-02 同族新实例，ItemVisuals.cs:973-1002） | 低-中 | 挂账 P3（加固即可，不阻断） |
| DEF-041 | _stackProjectionTransitionPending 无 Reuse/Dispose 复位点（SelectionAndMenus.cs:1335-1368；同一调度循环内自愈） | 中（影响极低） | 挂账 P3 |
| DEF-042 | HandleNativeFileDropAsync 前置段位于 try 之外（xaml.cs:2949-3004；全局兜底 EVT-03 已覆盖） | 低 | 挂账 P3 |

## 收敛判定
**R4 达成收敛**：一整轮全量审查（主线 9 面 + subagent 文件格子大面）无新增 P0/P1/P2，仅 3 条 P3 挂账。

F8 R2 审查循环（4 轮）总结：
- R1：DEF-034~037（1×P1 + 3×P2）→ 修复，CI 绿
- R2（收敛审查）：DEF-038（P2，护栏漏洞）→ 修复，CI 绿
- R3：DEF-039（P2，三国非法日期格式）→ 修复 + 契约测试锁死，CI 绿
- R4：**无新增 P0/P1/P2** → 收敛 ✅（硬上限 5 轮内完成，用了 4 轮）

## 交付物
- [x] 本报告（rectify/F8-round4-review.md）
- [x] defect-ledger.md 增补 DEF-040~042（挂账 P3）
- [x] 全量任务TODO清单.md 第 18 次核验行
- [x] pending-windows-gate.md 无新增项（P3 均为代码级挂账，无需实机验证门）
