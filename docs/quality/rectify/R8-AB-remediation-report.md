# R8-AB 整改报告（A/B 双轮审查修复批次）

> 输入：修复方案 `docs/quality/rectify/R8-AB-remediation-plan.md`（经独立完善性审查 NO-GO→按指令修订→条件 GO）。
> 执行日期：2026-09-25 ｜ 代码基线：`wip/fix-bug`（工作树未提交改动，见「交付状态」节）。
> 执行方式：4 个修复波次（W1-1/W1-2/W2-1/W2-2），波内 2 组并行（受并发上限约束），文件集互斥；每波次后构建＋x64 全量回归门禁。

---

## 1. 结果总览

- **门禁**：构建 0 错误；x64 全量回归 **4285/4285 通过、0 失败**（整改前基线 4226，净增 59 用例）；12 语言键齐（2906→2895，随死链删除 −11 键，12 文件 parity 验证通过）；新实例已从规范 Debug 路径启动并核验（PID 4652，`src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe`）。
- **修复**：方案内【修】项全部落地——P1×2、P2×18、P3×26；另将 B 轮多项观察项升级修复（FMEM-01/02、FANI-03/04、FEXC-04/05、FEVT-02/04/05/06、FTHR-03~09、FQC-05~10/13/14、FARC-01/02/05）。
- **延后**：9 项（§4，均有理由与触发条件）；**不修**：0。
- **门禁迭代记录**（诚实留痕）：Gate-1 经 3 轮（第 1 轮发现风暴用例自锁挂起＋7 处源码契约表述冲突；第 2 轮余 2 处钩子契约；第 3 轮全绿）；Gate-2 经 3 轮（W2 引入 1 处语法错误＋5 处冻结清单/契约连带；第 3 轮全绿）。全部迭代均为主流程直接核码修复。
- **交付后核验加跑（2026-09-25）**：独立核验轮发现 `PerformanceLoggerTests.ThumbnailEstimatedBytes_ConcurrentWideWritesAndReads_DoNotTear` 偶发失败——读取者可观察到**写入前初始值**（测试设计竞态，非生产撕裂：Interlocked 背书正确）。修法：派发任务前播种已接受模式，撕裂判别力不变；该类 5 连跑全绿＋全量复跑 **4285/4285** 确认稳定。

## 2. 修复明细

### W1-1① P1 双条＋存储门控（5 条）

| 条目 | 落点 | 要点 |
|---|---|---|
| FCFG-01（P1） | `QuickCaptureService.AddExternalLinkedFileItemAsync`（新增，走服务 `_gate`＋缓存＋SaveCore）＋ `SearchResultActionService` 改调＋App 构造点注入 | 旁路写根因消除：搜索「保存到随记」不再绕过服务缓存 |
| FCFG-02（P1） | `TodoWidgetStore.MutateAsync` 原子变更＋`TodoReminderService.NotifyExternalStoreChanged`（复用 PublishStoreChanged 隔离/投递）＋注入改 `Func<TodoReminderService?>` 访问器（适配按需创建/释放生命周期）＋外部新增以 `insertedItem` 发布（匹配 `ApplyExternalStoreChange` 合并语义） | 丢更新根因消除：外部附加经中继链合并进常驻 VM |
| DEF-102 | `QuickCaptureStore` 补 per-path 门控（镜像 TodoWidgetStore `s_pathGates`） | — |
| FMEM-01 | 两 store 门字典有界化（>64 清理未持有非主根条目；主存储路径永不清理；`internal TrimPathGates/IsProtectedMainStorePath` 测试缝隙） | — |
| FQC-13 | 随记 GC 纳入孤儿 `attachments/{itemId}/` 清理（有界＋日志＋`_undoWindowItemIds` 10s 保护镜像 DEF-012；Linked 附件不落盘已核实） | — |

### W1-1② 持久化组（8 条）

DEF-086（settings.json 新架构档案只读守卫：`_loadedDiskSchemaVersion`＋SaveToFileOnlyAsync 拒写段，镜像 layout 段形式）；FEVT-01（AppearancePreviewChanged 改 `NotifyAppearancePreviewChangedSafely` 逐 handler 隔离）；FEXC-08（catch 放宽＋日志）；FCFG-04（PasswordVault Retrieve→Remove→Add，internal IVault 接缝）；FCFG-05（MusicSettingsStore 写链化 `_writeChain`）；FCFG-06/FEXC-09（SearchHistoryService 迁移 ResilientJsonStore）；FCFG-07（`DeserializeSettingsDocument`：原始 JSON 缺 `schemaVersion` 属性按 0 参与迁移，Migration_0_To_1 复活）；FCFG-08（Migration_9_To_10 经 `WaitForPendingPersist` 落盘完成再推进检查点）。

### W1-2③ 窗口/钩子/随记 UI（13 条）

DEF-087（三热键服务 `RefreshRegistrationAsync`＋恢复链 `OnLifecycleRecoveryRequestedAsync` 三连 await＋Onboarding/Settings 录制钩子切异步）；FTHR-01（`UpdateGlobalHotkeySettings` 锁内三字段 API＋GlobalHotkeyService 调用点切换——回滚路径随之获得持久化语义，属有意修正）；FEXC-01（SafeFireAndForget＋handler 全体 try/catch＋`ShowTransientStatus` 失败反馈）；FEXC-02/DEF-097（两处剪贴板/打开路径 try/catch＋反馈；MarkWrite 移至实际写入后）；FQC-01（删除链改 per-item 归属 `(Id, IsRecent)`，旧 bool 签名删除）；FQC-02（附件新建校验前置，成功即收尾，取消/重试不再孤儿/重复）；FQC-04（ActualThemeChanged 补 `ApplyClipboardItemColors`＋`TryGetElementThemeColor` 元素主题解析）；DEF-098（配色基线主题化：`ResolveFollowThemeTextColor/BackgroundColor(isDarkTheme)` 下沉共享，SettingsWindow 按 ActualTheme 解析）；FEXC-03（Todo SaveAsync try/catch＋`SaveFailed` 事件→既有反馈通道）；FARC-03 硬化（WindowInteraction/SearchPopup 两处 null 一次性日志＋AOT 注释）；DEF-094（capture-lost 补 `wasEngaged` 门）；FEXC-07（KeyboardHookProc 拆 wrapper＋Core，顶层吞并＋封顶日志＋恒回 CallNextHookEx）。

### W1-2④ 布局/动画/备份/更新（14 条）

DEF-085（边距对话框取消恢复：快照展开/胶囊双态，折叠后不套回展开矩形、走快照胶囊 placement 或保守 no-op）；DEF-095（`ReferenceEquals` 改字段值比较，分支语义经 `!IsCompactBoundsStateActive` 修正为「展开主体→通用刷新；胶囊主体→显式捕获」）；FLAY-01（排列重排为文档语义：尺寸优先→spacing=0→等比压尺寸→尾截断，不变量保持）；FLAY-03（初始补位级联偏移后再钳制）；DEF-089/FANI-01（混合策略：随启动变化的模板改 `CreateScalar/CreateVector3` 每次新建，常量形状模板 `GetScalar(template, seed)` 首建种子化——依据注释记录）；FANI-03（FrameBudget 手写循环零分配）；FANI-04（帧回调异常即注销＋一次性日志）；FARC-03 硬化（WidgetShell 处 null 日志）；FWIN-01（cloak 判定 `>0`＋纯函数 `IsDwmCloakStateCloaked`＋Win32Helper 掩码注释更正）；FARC-05（ARM64 选择器非变异浅拷贝，缓存保原始）；DEF-104（重复 manifest 条目→带 key 的 InvalidDataException）；DEF-105（WebDAV href `Uri.TryCreate` 预检跳过）；DEF-106（`"id": null`→带归因 key 的可读 InvalidDataException）；DEF-090 注释更正（Collapse.cs:3422）。

### W2-1⑤ 死代码删除（12 条）

DEF-092（两死方法）、DEF-099（WidgetRemoved）、DEF-100（SearchRequested 链）、FWIN-05（WithAlpha）、FWIN-06（BringAllVisibleWidgetsToFront＋测试同步）、FQC-05（保存到文件格子链九方法＋切片属性＋设置键＋5 键×12）、FQC-06（图片导出链＋1 键×12）、FQC-07（遗留输入管线死簇＋XAML 段）、FQC-08（Recent 状态簇＋5 键×12）、FQC-09（ActivationHelper 整文件）、FQC-10（配色编辑器七参重载）、FEVT-04（IFeatureLifecycleEvents 删除——核实 roadmap 注释锚定不成立）。每项删除前零调用重验；强制连带点（SettingsViewModel.FeatureOptions 重置行、ItemSync OnPropertyChanged、恢复态 record 收窄）逐条记录。

### W2-1⑥ 异常/线程卫生（13 条）

DEF-088（watchdog 显式生命周期＋try/finally 兜底）、FMEM-02（托盘旧 Icon 先 Dispose——已核 H.NotifyIcon 幂等安全）、FEXC-04（通知激活 SafeFireAndForget）、FEXC-05/FEVT-06（Glance 两 async void 薄壳化）、FTHR-03（`_featureToken` 快照消除 CTS 竞态）、FTHR-04（DisplayTiming 静态锁）、FTHR-05（FolderWatcher ticks/Interlocked/volatile＋原子置换）、FTHR-06（7 个公共计数器 Interlocked 背书属性）、FTHR-07（SaveChecked Task.Run 中转）、FTHR-08/DEF-103（volatile＋超时日志）、FTHR-09（钩子族 volatile；`:423-424` 经核实已在锁内无需改）。

### W2-2⑦+⑧ 文档/注释/契约（6 条）

DEF-091（切片注释改地板除语义）、DEF-093（[重要勿删] 三处事实勘误：50/200 双档、预留钩子现状化、QuickReveal 第三层级补记——核心约定零触碰，均标「R8-AB 勘误」）、DEF-107/FARC-04（sync 契约文档状态化＋`ShouldIncludeInBackup:3042` 备份排除显式化）、FARC-01（search-core 文档/README 存档标注）、FARC-02（current_architecture.md 补 Search/Glance 全清单）、FQC-14（撤销常量契约测试：保护窗 10s 严格大于 toast 5s，反射读值＋源码形状双钉）。

## 3. 测试增量

新增/调整约 30 个用例，重点：`SearchResultActionServiceTests`（P1 双链端到端：外部写入→宿主保存不丢）、`QuickCaptureStoreGateTests`/`StorePathGateBoundingTests`（门控串行＋有界化风暴）、`QuickCaptureServiceTests`（attachments 回收/撤销窗保护/混合归属删除）、`SettingsServiceTests`（高版本档案拒存）、`PasswordVaultCredentialStoreTests`（凭据更新）、`R8AbWave12RemediationTests`（主题化/归属链/捕获丢失门源码契约）、`WidgetManagerShowDesktopCloakTests`（完整 DWM 掩码）、`WidgetCapsuleArrangementCalculatorTests`（新排列语义）、`DeskBoxDataBackupServiceTests`（重复 manifest/可读归因）、`PerformanceLoggerTests`（并发撕裂检测）、`QuickCaptureUndoConstantContractTests`（撤销常量关系）。

## 4. 延后清单（9 项，未在本批）

| 条目 | 理由 | 触发条件 |
|---|---|---|
| FLAY-02 拖动吸附跨屏快照 | 行为改动需 GUI 手感验证 | 下一运行窗口 |
| DEF-096 批量边距「静止几何」 | 行为语义需 GUI 对照 | 同上 |
| FWIN-02/03/04 | 设计意图判定（激活兜底 vs 删除） | 方案补审轮 |
| FQC-11/12/15 | 产品/数据迁移决策 | 决策后 |
| DEF-101 广播隔离泛化 | 现存订阅者自防护，逐点改造收益低（高危实例 FEVT-01 已修） | SafeBroadcast 助手立项 |
| FANI-02 自适应阶梯删除 | 热路径，注释矛盾已更正 | 卫生批次 |
| FARC-03 完整替代 | 硬化已落地（null 日志＋注释），AOT 安全写法需运行时实测 | `scripts/run-aot-*.ps1` 窗口 |
| FLAY-01 之 96/36 常量 DPI 化 | 视觉行为改动需 GUI 验证 | 同 GUI 窗口 |

## 5. 遗留观察（下波裁决）

1. `QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 两键因 helper 删除成为孤儿键（不在授权键清单，未删；12 文件 parity 未破坏）。
2. `WidgetShell.xaml.cs:1836` `PrewarmCompactTransitionCompositionResources` 预热的三个缓存模板经 DEF-089 改 per-start 后不再被消费（一次性微小成本，无害）。
3. `FolderWatcherService.LastEventAt` 语义微调为 UTC ticks 存储（仅诊断消费方）。
4. `scripts/extract_widgetmanager.ps1` 名单含已删方法名（脚本 Write-Warning 继续，无碍）。

## 6. 交付状态

- 全部改动位于工作树，**未提交**（本轮任务未含提交指令）；建议按波次拆分提交（P1 批 / P2 批 / P3 卫生批 / 文档批）。
- 台账联动：DEF-108/109（P1×2）立项并标记已修复；DEF-085~107 中 20 条标记已修复、DEF-090 部分修复（注释已更正、阶梯删除延后）、DEF-096/101 维持延后；B 轮编号发现随批次修复（明细见本报告 §2）。
- 新实例：PID 4652 @ 规范 Debug 路径，供用户人工验证（重点：搜索「保存到随记/附加到 Todo」后宿主操作不丢数据、边距对话框取消恢复、随记主题翻转即时刷色、设置页配色明暗基线）。
