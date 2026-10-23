# 第 12 轮（从零）全量代码缺陷审查 · 总报告

- 基线：`wip/fix-bug` @ `9cfd9611`（第 11 轮修复提交之后）。
- 方式：延续"从零重审"纪律——两个 SubAgent 禁读 `docs/audit/` 独立审查，本轮重点攻击第 11 轮新改动（`fe3103d5` 提醒两阶段、`a251103d` Glance 公历）+ 全量扫描。主流程逐条复核代码证据。
- 原始报告：`round-12-scout-a.md`（9 条）、`round-12-scout-b.md`（6 条 + 本地化专项）。

## 复核结论（主流程裁决）

1. **两份报告独立确认第 11 轮两笔修复主体稳健**（两阶段提醒协议的选择/守卫/失败回收/DisposeAsync 交互、EnsureGregorianCalendar 本体），各自留痕防重复劳动。交叉验证通过。
2. **两报告共同发现第 11 轮修复的残余缺口**，归并为 4 条独立问题（见 P2/P3 清单）。
3. **位点扩充**：日历家族缺陷经主流程全仓 grep 复核，实为 **15 个直接位点 + 1 个格式化 token 通道**（`LocalizationService.Format` 用 CurrentCulture 求值 `{1:yyyy/M/d HH:mm}`），比两份子报告合计（11 处）多出 3 处：`OrganizationHistoryEntry.cs:90`（`MM-dd HH:mm`）、`QuickCaptureItemViewModel.cs:395`（`MM-dd HH:mm:ss`）、`ContentWidgetWindow.xaml.cs:500`（`M/d`）。
4. **A-3 补充事实**：`Program.cs:60-67` 的"App 可执行文件不存在"分支**有** `WaitForParentExit`，仅"安装包不存在"分支（`:70-75`）漏等——不对称性坐实了这是遗漏而非设计。
5. **A-2 前提保留**：`Show()` 在"应用通知被系统关闭"时是否静默（不抛）未实测；但无论哪种行为，先查 `AppNotificationManager.Setting` 都是正确且无害的守卫（文档化的判断途径），修复不依赖该前提成立。

## 风险等级分布（去重合并后）

| 等级 | 数量 | 条目 |
| --- | --- | --- |
| P0 | 0 | — |
| P1 | 0 | — |
| P2 | 3 | R12-1（日历家族，A-1+B-1 合并）、R12-2（A-2 原生 toast 乐观返回）、R12-3（A-3 更新器竞态） |
| P3 | 10 | R12-4（A-4 中止搁浅会话键）、R12-5（A-5+B-2 snooze 重试无上限）、R12-6（B-3 Refresh 清键重放）、R12-7（A-6 WebDAV 二次解码）、R12-8（A-7 rollback 目录累积）、R12-9（A-8 zip 健壮性三缺口）、R12-10（A-9 DST 明天 9 点近似）、R12-11（B-4 Glance CurrentImage 滞后）、R12-12（B-5 戳记注释不符）、R12-13（B-6 legacy 迁移竞态） |

## 问题总览（按优先级）

### P2

| # | 位置 | 问题 | 置信度 |
| --- | --- | --- | --- |
| R12-1 | TodoItemViewModel:113,674-675,700-701；TodoReminderService:699-700；TodoClipboardFormatter:122-123；QuickCaptureSurfaceContent:1265,1304；SettingsViewModel.AboutAndUpdates:573,576；OrganizationHistoryEntry:90；QuickCaptureItemViewModel:395；ContentWidgetWindow:500；LocalizationService.Format（FileInfo.FileModified 的 `{1:yyyy/M/d HH:mm}` token） | 第 11 轮公历修复只覆盖 Glance 主日期。全仓 15 处显式日期格式化不传 culture，跟随系统 CurrentCulture 的默认日历——ar-SA 系统区域下 Todo 截止日期/提醒 toast/剪贴板/速记详情/设置页/文件 tooltip 全部渲染回历（1448 年）。附带：UmAlQura 仅支持 1900-04-30~2077-11-16，越界时钟/时间戳会让这些 `ToString` 抛 `ArgumentOutOfRangeException`（部分在绑定 getter 里） | 高（机制确凿；两报告独立命中） |
| R12-2 | NativeAppNotificationService.cs:287-288 | `AppNotificationManager.Default.Show(...)` 返回值被丢弃、`Setting` 从不检查：应用通知被用户/组策略关闭时 toast 被静默吞掉，`TryShow` 仍返回 true → 第 11 轮两阶段协议据此消费提醒且连托盘兜底都不尝试 | 中高（前提需实测，守卫无害） |
| R12-3 | Updater/Program.cs:70-75 | "安装包缺失"路径不等父进程退出就重启应用：与单实例互斥体竞态，可留下整机无 DeskBox 的结局（DEF-113 要防的正是这个）；对照 `:60-67` 同类路径有等待 | 中（窗口秒级，撞锁概率高） |

### P3（10 条，入台账）

| # | 位置 | 问题 |
| --- | --- | --- |
| R12-4 | TodoReminderService.cs:192-199 | 多 widget 收集中途禁用/退役/读盘抛错时，已占用的会话键不回收（显示失败路径有回收，收集中止路径没有） |
| R12-5 | TodoReminderService.cs:743-747 | snooze 触发的提醒重试无宽限上限（due 分支有 1 分钟 `MissedReminderGrace`，snooze 分支没有）——通知面长期不可用时每 30 秒空转到会话结束；与 fe3103d5 提交说明"bounded by the missed-reminder grace window"不符 |
| R12-6 | TodoReminderService.cs:100,125-133 | 持久化失败保留的会话键会被 `Refresh()`/`Start()` 的全量 Clear 清掉：不经重启的开关循环也会重放已显示过的提醒 |
| R12-7 | WebDavBackupTransport.cs:285-286 | PROPFIND 条目名二次 `UnescapeDataString`：含字面 `%XX` 的远端名被错误寻址（验证降级为主，理论上有删错文件窗口） |
| R12-8 | DeskBoxDataBackupService.cs:1296-1315 | 全量恢复成功路径在删 rollback 前崩溃 → 整份数据大小的 `restore-rollback/<guid>` 永久累积，无清理路径 |
| R12-9 | ArchiveZipService.cs | `./` 根条目解压误报越界；解压无文件数/字节预算；压缩直写最终路径非原子、失败留残档 |
| R12-10 | TodoNotificationActivationRouter.cs:258-268 | snooze"明天 9 点"在 DST 切换日的无效本地时刻取近似偏移（误差 ≤1h，极少数时区） |
| R12-11 | GlanceWidgetViewModel.cs:648-659 | 在线刷新命中同 Id 只回写 `_currentIndex` 不更新 `CurrentImage`：元数据滞后一拍；Content-Type 变化时可能留断图 |
| R12-12 | WidgetShellContentHost.cs:272-287 | `ApplyContentAppearance` 注释承诺的防重入与实现不符（行为幂等良性，注释误导） |
| R12-13 | GlanceWidgetStore.cs:65-83,179-208 | 多 Glance 格子并发迁移共享 legacy 文件无跨实例互斥，归属不确定 |

### 本地化与架构专项

- 12 文件 × 3091 键与占位符**全等**（连续第 2 轮程序化验证）；XAML 资源引用零缺失；`{1:yyyy/M/d HH:mm}` 等 token 的 fr/pt/ru 本地日期序为有意设计（但其求值文化是 R12-1 的载体）。
- Widget 创建路径、z-order 文档不变量、模块边界棘轮契约：全部符合，无漂移。

## 整改建议与本轮排期

- **本轮修复**：
  1. **R12-1**：`EnsureGregorianCalendar` 提升为共享 helper（`DeskBox.Helpers.DisplayCulture`），15 个位点 + `LocalizationService.Format` 全部改走公历变体（保留各文化月名/数字形状/语序）；顺带消除 UmAlQura 年份范围异常面。
  2. **R12-3**：updater 把 `WaitForParentExit` 提到安装包存在性检查之前（对齐"App 不存在"分支）。
  3. **R12-2**：`TryShow` 增加 `AppNotificationManager.Setting` 预检 + `Show` 结果状态检查，失败回落托盘路径。
  4. **R12-5**：snooze 触发加对称宽限 `snoozedUntil + MissedReminderGrace`（修正第 11 轮提交说明的过度承诺）。
- **入台账待后续轮**：R12-4、R12-6（会话键生命周期边缘，可与某轮 reminder 改动合并）；R12-7~R12-13。
- **需产品决策**：日期显示"跟随应用语言 vs 系统区域"的策略口径（本轮只统一日历、不改文化来源）。
