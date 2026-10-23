# 第 13 轮（从零）全量代码缺陷审查 · 总报告

- 基线：`wip/fix-bug` @ `9f261cbb`（第 12 轮修复提交之后）。
- 方式：延续"从零重审"纪律——两个 SubAgent 禁读 `docs/audit/`，重点攻击第 12 轮 5 个新提交再全量扫描；主流程逐条复核代码证据。
- 原始报告：`round-13-scout-a.md`（A：P2×3 + P3×9）、`round-13-scout-b.md`（B：P2×1 + P3×5 + 观察项 1）。

## 复核结论（主流程裁决）

1. **两报告再次独立确认第 11/12 轮修复主体稳健**（两阶段提醒协议、DisplayCulture 15 个已接位点、通知守卫、更新器等待），各自留痕。
2. **两报告各自发现第 12 轮修复的漏网**：B-1 证明 b96129f6 "render every user-facing date" 声明又漏了 **10 个显示位点**（设置页云备份/数据备份/反馈/运行诊断/搜索结果日期）；A-1/A-2 证明协议/文件名家族（应钉 InvariantCulture 的领域）有**两个带功能性后果的位点**（Wikimedia 分类名 → 精选类目静默空；云快照名 → 解析回环失败）。
3. **主张修正（11.A-2 DST 漂移）**：静态推演显示 `AddLocalDays` 的 `LocalDateTime.AddDays(n)` + `new DateTimeOffset(local)` 对**存在**的本地时刻墙钟是被保留的（"每次跳变累计漂移"不成立）；缺陷面收窄为**跳变窗口内的不存在/歧义本地时刻**（春季 02:xx 被静默平移、秋季 01:xx 二义）。本轮以运行时探针定论后修复（见 §13.4）。
4. **A-4/B-7 归并为产品决策项**：snooze 宽限 × 通知面长期不可用 = "从未显示过的 snooze 永久静默、`SnoozedUntil` 永久滞留"。三个代码事实属实（小睡时提前消费 due、snooze 分支无回落、无托盘必 false），但"宽限关闭后是否释放 `SnoozedUntil`"是语义取舍（旧行为的无限重试本身已被判定为缺陷），记台账待产品裁决，本轮不擅自改。
5. A-8/A-9/A-10/A-12 与台账既有条目重复（11.A-15/11.A-6/11.A-14/11.A-11），A-3 大部分与 11.A-4 重复；B-3/B-5 为新 P3 入台账。

## 风险等级分布（去重合并后）

| 等级 | 数量 | 条目 |
| --- | --- | --- |
| P0 / P1 | 0 / 0 | — |
| P2 | 4 | R13-1（A-1+A-2：协议标识符未钉 InvariantCulture，带功能性后果）、R13-2（B-1：显示日期公历化漏网 11 处）、R13-3（R12-4+R12-6 合并：会话键生命周期两缺口）、R13-4（11.A-2 修正版：DST 跳变窗口时刻映射） |
| P3（新） | 8 | R13-5（A-5+B-6：Setting 探测在 try 外）、R13-6（B-2：DefaultFormat 未换公历文化）、R13-7（B-4：WeekdayText 未公历化）、R13-8（B-3：DisplayCulture 缓存键不区分用户覆盖）、R13-9（B-5：ApplyExternalStoreChange 写模型不写 VM → snooze 状态文本滞留）、R13-10（A-6：更新器等待前移的 90s/PID 复用新权衡）、R13-11（A-7：缓存克隆共享可变实例）、R13-12（A-11：CitySearch 去重键 CurrentCulture 数值格式） |

## 问题总览（按优先级）

### P2

| # | 位置 | 问题 | 置信度 |
| --- | --- | --- | --- |
| R13-1 | `GlanceImageService.cs:421`（Wikimedia 分类名）、`CloudBackupService.cs:648`（远端快照名）；同族 11 处文件名/目录名/日志位点 | 发向远端/供机器回读的**协议标识符**用 CurrentCulture 渲染年份：ar-SA 等非公历默认格式机器上 Wikimedia "精选"分类名拼出回历 1448 → API 返回空、该类目静默无图；云快照名内嵌回历年 → `ParseSnapshotTimestamp`（Invariant）解析必然失败 → 排序/时间戳退化、跨设备混排命名破坏保留策略次序 | 高（两报告独立命中 + 主流程 grep 复核 13 插值位点 + 2 个 F2 键） |
| R13-2 | `BackupSettingsViewModel.SettingsSurface.cs:534,540,547,590`、`SettingsWindow.CloudBackup.cs:397`、`SettingsWindow.Maintenance.cs:251,438`、`SettingsWindow.Feedback.cs:472`、`SettingsViewModel.RuntimeDiagnostics.cs:46`、`FileMetaService.cs:436-437` | 第 12 轮"全量公历"又漏 10+1 个用户可见位点：设置页云备份状态行/快照标题/恢复确认/反馈卡片/运行诊断/搜索结果 `DateDisplay` 仍走文化默认日历——ar-SA 下与刚修好的 Todo/Glance 同屏双历法；`FileMetaService` 路径吃文件系统时间（可为 1601/2099），UmAlQura 超范围抛异常（有外层 catch，仅该条目统计缺失） | 高 |
| R13-3 | `TodoReminderService.cs:192-199`（中止搁浅键）+ `:100,125-133`（Clear 重放） | 会话键生命周期两缺口：多 widget 收集中途禁用/退役/读盘抛错时已占用的键不回收（显示失败路径有回收）；持久化失败特意保留的键会被 Start/Refresh 的全量 Clear 清掉——不经重启的开关循环重放已显示的提醒 | 高 |
| R13-4 | `TodoRecurrenceService.cs:185-197,235-250,257-261` | DST 语义（经本轮探针修正）：对存在的本地时刻 `AddLocalDays` 墙钟保留（原"累计漂移"主张不成立）；真缺陷是跳变窗口——春季不存在的 02:xx 被 `new DateTimeOffset(local)` 静默平移、秋季 01:xx 二义取值未定义；月度候选同型 | 中高（探针定论后修复） |

### P3（新 8 条）

| # | 位置 | 问题 |
| --- | --- | --- |
| R13-5 | `NativeAppNotificationService.cs:200` | `Setting` 探测在 try 外：WPN 平台半死时 COM 异常逃逸（TryShow 原本"永不抛"）；todo 提醒侧 TCS 无界等待链放大（修法零成本） |
| R13-6 | `LocalizationService.cs:204` | `DefaultFormat` 兜底仍用 raw CurrentCulture：`WidgetItem.SecondaryInfo` 早期窗口 + 超范围文件时间可抛 |
| R13-7 | `GlanceWidgetViewModel.cs:771` | `WeekdayText` 的 `dddd` 未公历化：超范围时钟在每分钟 tick 抛异常（与 B-4 同修） |
| R13-8 | `DisplayCulture.cs:19-27` | 缓存键只按 Name：应用语言文化（无用户覆盖）与 CurrentCulture（带覆盖）同名相撞，先到者的克隆被两边共用，用户自定义日期模式可能被静默丢弃 |
| R13-9 | `TodoWidgetViewModel.cs:785-790` | `ApplyExternalStoreChange` 直接写模型字段不走 VM setter：外部 snooze 后已开窗口的"已暂至…"状态文本不刷新（持久化无恙） |
| R13-10 | `Updater/Program.cs:70-82` | 等待前移的新权衡：installer 缺失也要等父进程（最长 90s）；PID 复用时等错进程。可在等待前校验进程名 |
| R13-11 | `DisplayCulture.cs` | 缓存克隆是共享可变实例：当前全只读安全，需注释固化"视为只读" |
| R13-12 | `CitySearchService.cs:205,226` | 去重键 `F2` 数值格式随文化（逗号小数点区域键形不同）：当前同次运行一致故良性，改 Invariant 绝后患 |

### 本地化与架构专项

- 12×3091 键、占位符、格式 token 程序化全等（连续第 3 轮）；数字 token 在 `GregorianCurrent` 下与改前逐字节一致（无回归）；XAML 资源键零缺失。
- 架构路径、z-order 不变量、事件配对、Win10 兼容门控：全部符合。

## 整改建议与本轮排期

- **本轮修复**（方案复核后实施）：
  1. **R13-1**：协议/文件名家族一次性钉 InvariantCulture（13 插值位点 + 2 个 F2 键）。
  2. **R13-2 + R13-6 + R13-7**：显示侧公历化补全（11 处 + DefaultFormat + WeekdayText）。
  3. **R13-5**：`Setting` 探测移入 try。
  4. **R13-3**：会话键生命周期（中止回收 + shown-without-persist 独立集合防 Clear 重放）。
  5. **R13-4**：DST 探针定论后按实际缺陷面修复（跳变窗口时刻显式映射）。
- **入台账**：R13-8/9/10/11/12、A-4/B-7 产品决策项、其余重复项维持原编号。
