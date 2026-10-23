# 第 11 轮（从零）全量代码缺陷审查 · 总报告

- 基线：`wip/fix-bug` @ `7fcede2c`（2026-10-24）。
- 方式：应用户要求**不信任此前任何审查结论，从零开始**。两个 SubAgent 独立工作（维度 A：服务/数据/原生互操作；维度 B：UI/ViewModel/架构/本地化），均被禁止读取 `docs/audit/` 历史文档，只信当前代码。主流程对两份报告逐条复核了代码证据（file:line + 引文全部核对原文）。
- 原始报告：`round-11-scout-a.md`（16 条）、`round-11-scout-b.md`（9 条 + 本地化专项）。
- 纪律：纯静态审查，未构建、未运行测试、未启动应用、未修改任何源码。

## 复核结论（主流程裁决）

1. **25 条问题全部核实成立**（引用行与代码原文一致，触发路径经主流程独立推演确认）。
2. **定级调整 1 条**：A-3（Everything Dispose 信号量竞态）P2 → P3。窗口仅存在于应用关闭时 Dispose 与最后一次搜索刷新并发；后果是单次 `ObjectDisposedException` 逃逸（全局兜底可捕获），非数据损害。
3. **主张修正 1 条**：A-8（WeatherCacheStore 回写清缓存）的触发面收窄——`ResilientJsonStore.LoadAsync` 在主文件瞬时读失败时会以只读方式改用 `.bak`，因此**清空另一半缓存需要主文件与备份同时读失败或同时缺失**，比子报告描述的更窄；且缓存可再生、`.unreadable-*` 侧车保全字节。维持 P3。
4. **事实补充 1 条**：B-5（`QueueRequestedLayerRestoreCheck` 死代码）——该方法同时是 `WidgetRaiseBandContractTests` 的切片锚点（`SliceMethod` 的结束边界字符串），删除需同步改测试，不是纯死代码。
5. **设计确认 1 条**：D-1（Rust `panic = "abort"`）——`native/README.md:35` 明文规定 "Panic policy: abort; panic must never cross the C ABI"，属有意契约，非缺陷，销案。
6. **交叉验证**：两份报告各自独立标注的"已核实稳健区域"高度重合（ResilientJsonStore 提交协议、备份/恢复事务、模块边界契约、本地化 12×3091 键全等、Widget 创建路径与 z-order 约束），与此前 10 轮修复方向一致——从零重审未推翻任何既有修复，浅层缺陷面基本清空。

## 风险等级分布

| 等级 | 数量 | 条目 |
| --- | --- | --- |
| P0（数据永久丢失/崩溃/安全） | 0 | — |
| P1（用户可感知的数据或功能损害，常见路径） | 0 | — |
| P2（核心功能语义缺陷/特定环境显著退化/低概率功能损害） | 4 | A-1、A-2、B-1、B-2 |
| P3（卫生/诊断/边缘路径） | 21 | A-3…A-16、B-3…B-9 |

## 问题总览（去重合并后，按优先级排序）

### P2

| # | 级别 | 位置 | 问题 | 置信度 |
| --- | --- | --- | --- | --- |
| A-1 | P2 | `TodoReminderService.cs:455-498,201` + `App.xaml.cs:2884-2924` | 待办提醒**先落盘消费标记、后显示**：显示失败（原生 toast 不可用且无托盘、`TryEnqueue` 失败、进程死亡）→ `ReminderDismissedForDueDate=DueDate` 已持久化 → 该提醒永久静默；保存失败 → 会话去重键已占用 → 本会话不再提醒 | 高 |
| B-1 | P2 | `GlanceWidgetViewModel.cs:853-874,927-937` | ar-SA 语言下 Glance 主日期用 .NET 默认 UmAlQura（回历）渲染，与同组件公历日历网格、应用其余日期、传统历法功能语义三方矛盾 | 高 |
| A-2 | P2 | `TodoRecurrenceService.cs:185-197,235-250,257-261` | 重复任务跨 DST 漂移 1 小时（`AddLocalDays` 本地加天后重包 `DateTimeOffset`）；月度候选落在 DST 不存在的本地时刻被静默平移 | 中 |
| B-2 | P2 | 全仓库（`FlowDirection` 0 命中） | ar-SA 为一等发布语言但整体无 RTL/FlowDirection 处理，界面按 LTR 渲染 | 中（需产品决策） |

### P3（21 条）

| # | 级别 | 位置 | 问题 |
| --- | --- | --- | --- |
| A-3 | P3 | `EverythingSearchService.cs:637-666` | Dispose 时释放并 Dispose 信号量，排队等待者收到 `ObjectDisposedException`（catch 清单不含） |
| A-4 | P3 | `DeskBoxDataBackupService.cs:276,332,393,2489,3115`、`CloudBackupService.cs:648`、`AttachmentStorageService.cs:45`、`DeskBoxDragData.cs:252,523`、`QuickCaptureService.cs:2211`、`DeskBoxDiagnosticsBundleService.cs:431`、`ResilientJsonStore.cs:236,527`、`GlanceImageService.cs:421` | 约 14 处文件名/远端名日期格式化未传 InvariantCulture；ar-SA/th-TH 等区域生成回历/佛历年份文件名（本地快照名失去字典序；云快照名 `ParseSnapshotTimestamp` 解析失败退化为 LastModified 排序） |
| A-5 | P3 | `DeskBoxDataBackupService.cs:3235-3237` | `.unreadable-*` 侧车未被备份过滤排除，全量备份携带取证残留 |
| A-6 | P3 | `SyncOutboxStore.cs:126-136` | 出队只删主文件，`.bak` 侧车永久残留（含已发送载荷） |
| A-7 | P3 | `QuickCaptureService.cs:128-137,561-579,700-713` | 附件导入在服务 gate 外，落盘失败留孤儿文件（后续 GC 兜底，暂态） |
| A-8 | P3 | `WeatherCacheStore.cs:148-188` | `UpdateAsync` 读失败得默认态后整体回写，清掉另一半缓存（触发需主备同败；缓存可再生） |
| A-9 | P3 | `DeskBoxDataBackupService.cs:2456-2538` | `HasUserDocuments` 不认 quick-capture.json/music-store 等文档；搁浅回滚恢复可能 park 掉只含这些文档的数据目录（需 settings+layout 双缺失，极低概率；字节保留在 parked 目录） |
| A-10 | P3 | `AppUpdateService.cs:332`、`AppRelaunchService.cs:50`、`Updater/Program.cs:498` | `Process.Start` 返回句柄未释放 |
| A-11 | P3 | `ArchiveZipService.cs:81-147,200-243` | 压缩丢嵌套空目录；输出直写最终路径非原子；解压中断留残目录 |
| A-12 | P3 | `DesktopOrganizationTransaction.cs:108-120` | `RecordCompleted` 用 `First(...)`，路径规范化差异会让整批当失败（数据保留不丢，仅本轮不动） |
| A-13 | P3 | `CloudBackupService.cs:508-536` | 下载失败留截断 zip 无清理（上游校验会拒绝，仅垃圾） |
| A-14 | P3 | `QuickCaptureStore.cs:293` | 标签去重用 `CurrentCultureIgnoreCase`（tr-TR 折叠异常 + 性能） |
| A-15 | P3 | `MusicSessionService.cs:77-95` | `InitializeAsync` check-then-act 双初始化竞态（当前调用点均在 UI 线程，难触发） |
| A-16 | P3 | `DesktopOrganizationRecoveryStore.cs:61-62`、`DesktopOrganizationHistoryStore.cs:109` | 每个移动项完成同步阻塞 STA 写盘一次，大整理逐项卡 UI |
| B-3 | P3 | `TodoItemViewModel.cs:388` | `MarkerGlyph` 三元两分支相同（"\uE915"），意图丢失（需设计决策） |
| B-4 | P3 | `GlanceWidgetViewModel.cs:1034-1066,1112-1133` | Dispose 与已排队回调竞态：回调读已释放的 `_lifetimeCts.Token` 抛 ODE（全局兜底吞掉，日志噪音） |
| B-5 | P3 | `WidgetManager.ZOrder.cs:1148-1158` | 死方法 + 误导性名称；同时是契约测试切片锚点，删除需同步改测试 |
| B-6 | P3 | `OnboardingWindow.TaskFlow.cs:110-180` | 两个 Toggle 的 async void 只有 try/finally 无 catch，异常时回滚 UI 不执行 |
| B-7 | P3 | `TodoItemViewModel.cs:515-518`、`TodoWidgetViewModel.cs:184-187` | 到期/草稿日期硬编码 "月/日" 顺序，与同屏 "yyyy/M/d" 风格不一致 |
| B-8 | P3 | `QuickCaptureWidgetViewModel.ItemSync.cs:282-287` | 图片搜索额外硬编码英文 "Image"，违反本地化约定且行为不对称 |
| B-9 | P3 | `GlanceWidgetViewModel.cs:645-660` | 在线刷新 `Apply()`：`File.Exists` 过滤后为空时不清 `CurrentImage`/`StatusText`（极边缘） |

### 本地化专项（维度 B 程序化对比）

12 个语言文件 × 3091 键：键集合、占位符集合**全等**；全部 `Format` 调用点实参与占位符匹配；XAML StaticResource 无缺失；无 UI 输出硬编码中文。**本地化无缺陷**。

## 整改建议与本轮排期

- **本轮修复**（方案见 `round-11-reminder-consumption.md`、`round-11-glance-hijri-date.md`）：
  1. **A-1**：根治"先落盘后显示"——选择与消费落盘拆为两阶段，显示确认后才持久化消费标记，失败补偿会话键，支持并发用户编辑守卫。
  2. **B-1**：Glance 主日期强制公历（克隆 culture 换 `GregorianCalendar`），回历保留给传统历法选项。
- **下轮优先**：A-2（重复任务 DST 漂移，纯静态可测）；A-4（文件名 InvariantCulture 批量整改，与第 9/10 轮同缺陷类）。
- **需产品决策**：B-2（RTL 支持 vs 声明不支持）；B-3（MarkerGlyph 非红分支意图）。
- **P3 卫生批**：A-5、A-6、A-10、A-13、B-8、B-9、B-6、B-4、A-14 适合后续按"小改动+单测"分批消化；A-7、A-8、A-9、A-11、A-12、A-15、A-16、A-3、B-5、B-7 留台账观察或随相关子系统改动顺带处理。
