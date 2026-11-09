# DeskBox Fork 网络分析报告

- 版本:v1.2(2026-10-24:网络分析 → 行动决策 → 代码核对,见附「修订记录」)
- 生成日期:2026-10-24
- 上游仓库:<https://github.com/Tianyu199509/DeskBox>(main = `1fdf0bd9`,另含 `feat/160-kickoff`、`feat/native-menu-parity`、`release/1.5.9` 等分支)
- 数据快照:2026-10-23T21:57Z 前后(API 分页枚举 + git 协议对比)
- 本仓库:<https://github.com/beici/DeskBox>(即 fork 网络成员之一,当前分支 `wip/fix-bug`)

术语约定:

- **净新增提交**:指既不在 upstream 任何引用可达历史上、也不在本仓库 beici/DeskBox 线上的提交。"领先提交数"(ahead)只对 upstream main 计数,因此可能大于净新增(多出部分是 beici 线合入)。
- 提交作者 `Simon <1047078635@qq.com>` 是本仓库维护者身份。其提交即使存在于其他 fork、且本仓库当前没有,也按"**维护者侧工作**"归类,不算外部贡献。
- 价值评级三档:**可直接借鉴**(拿来即用,移植成本低)/ **有启发**(思路值得参考,需按本仓库架构重做)/ **仅了解**。

**阅读指南**:时间紧迫只读「〇、行动决策」(核对结论 + 立项/待办池/不采用清单,反映 2026-10-24 最新状态);「一、摘要」给网络面结论;查某项工作的证据与实现细节看「三、总览表」与「四、重点详解」;「五、结论与建议」是决策定稿前的原始建议,仅供追溯理由。

---

## 〇、行动决策(2026-10-24,与维护者逐项确认)

16 个候选行动项已逐项确认;其后五个留档项又补做核对并逐项决策。最终结果:**4 项立项(已全部实施:拖入无响应修复、启动沉底修复、番茄钟组件、截图增强)、9 项进入待办池(3 项经代码核对降级,第 9 项由立项转入,其中第 9 项与 #1①、#5 已于 2026-10-25 实施;分三档:建议排期 #1(O(n²))/#5/#9、看产品需要 #6/#7/#12、转观察 #2/#4/#11)、3 项经代码核对不成立移出、4 项不采用/放弃(每格背景+清爽桌面、AI 整理线、独立日历格、滴答桥接)、大方向不立项、其余维护者侧小项留档**。本轮仅整理决策,未动代码;实施时按对应 fork 分支摘取。上游已出现可合并的 34 个落后提交(1.5.9 train)并带来两条约定变化(语言 12→13、AOT 门禁重钉),合并任务见「上游合并任务」;后续工作由其他 agent 接手时,投喂 [docs/forks/agent-handoff-prompt.md](agent-handoff-prompt.md)。**接手 agent 注意:接手第一步先按提示词第 2 节以实树甄别各项真实状态(已完成/部分完成/未动;同一问题存在多个实现时分析最优解方案,不轻信文档与提交信息);待办池与立项全部关闭后,进入提示词第 9 节的迭代式深度审查循环(深度审查→修复→再审查;每项修复前过"是否有必要实施"论证,只修 P0/P1,反过度工程)。**

### 核对结论(2026-10-24)

下方的立项/待办池建议在定稿前,又经两个子代理逐条对**当前 wip/fix-bug 代码**核对(主会话对 4 条最颠覆的判定做了二次抽查,全部复核通过)。fork 修复多数针对旧基线,以下判定以当前代码为准,证据均为 file:line。

**站得住(核对属实)**

- **#1 性能①O(n²)【确凿】**:`WidgetViewModel.ItemHydration.cs:259/287` 的 `Items.IndexOf` 位于逐项 `for` 循环内,`Items.Move` 本身也是 O(n),大文件夹每次全量刷新付 O(n²);全 VM 无位置索引。②盲重载【部分存在】:仅 query watcher/监视错误/缓冲超限等 5 条兜底路径走全量(`FolderWatcherService.cs:457/735/761/958/997`),自描述事件有增量通道;"每次 shell 失效都全量"的频率需真机日志。③钩子期 Trim【部分存在】:4 处 Trim 全部空闲门控,但常驻 WH_KEYBOARD_LL(`ReservedHotkeyHookService`)与 WH_MOUSE_LL(`DesktopDoubleClickActivationService`)期间仍会 trim、无摘钩协调——风险窗口(首次输入的一次性回缺页)远小于声明字面。
- **#6 网络出口未单点化【属实】**:穷举共 **10 处构造/8 个文件**(AppUpdateService:31/45、FeedbackService:105、WebDavBackupTransport:19/59、GlanceImageService:777、ReleaseNotesService:329、WeatherService:29、WindowsLocationHelper:18、DeskBoxDragData:28),无共享 transport/工厂。属架构债而非缺陷 bug。(2026-10-24 合并 1.5.9 后复核:AppUpdateService 因上游单飞下载新增默认客户端工厂,9 处→10 处;ReleaseNotesService 行号漂移 258→329。)
- **#7 图标避让缺失【属实】**:`DesktopIconGeometryService` 仅有只读 `GetIconRects()`(LVM_GETITEMRECT),全仓库无 `LVM_SETITEMPOSITION` 写调用;唯一消费方把图标当边距测量参考。避让将是新能力而非缺陷修复。
- **#9/#11/#12 特性缺失【属实】**:per-tile 文字开关不存在,只有全局"文件名行数=0"(`FileWidgetIconLayout.cs:40`);文件格无分页(`FileSurfaceContent.Navigation` 仅文件夹导航);图标/缩略图缓存仅内存(`IconHelper.cs:58-74`),native thumbnail proxy 不落盘。

**降级保留(核对后缩水)**

- **#2 re-bed**:无对话框 SHOW/HIDE/REORDER 事件钩子属实(SelfHeal 仅挂 MINIMIZE/FOREGROUND),**但** `WidgetManager.ZOrder.cs:579-655` 已有 5s×2 轮询 bedding watchdog("组件下方存在外来应用窗口"即泄漏见证并 re-bed),对话框抬 band 稳态约 10 秒内自愈——fork 补的是**时效与确定性**,非功能缺失。
- **#5 Win11 26200**:本仓库用 SecondWindow 模式且赋值期已有 15×2s 重试 + 两级降级(策略类原 `TrayContextMenuModePolicy`;合并 1.5.9 后托盘菜单整体重做为 Win11 原生单列主菜单,策略已内联至 `App.Tray.cs:1665`);缺口仅剩 **show 期 E_NOTIMPL 落泛化 catch**(菜单静默失效、不崩溃、不降级)——**该评估基于旧托盘栈,1.5.9 新栈需重新核对**;26200 上的崩溃形态本身需真机复现。
- **#4 托盘/.lnk**:4a **不成立**(`App.Tray.cs:303-321` 图标创建 15×2s 重试 + `:533-611` flyout 重试+原生菜单降级均已存在);4b 部分存在——不持久化属实(图标缓存仅内存),但提取失败已回退 Shell 默认帧(`IconHelper.cs:1251-1271`),白图标常态已消除 → 从"缺陷修复"降级为"可选增强(图标资源持久化)"。

**核对不成立,移出待办池(3 项)**

- **#3 Win10 材质两修**:controller 成功路径已互斥清理 legacy accent(`WidgetWindowBase.Backdrop.cs:107-111`,`DisableAccentPolicy` + `LegacyAccentBackdropActive=false`);`TransparentTintBackdrop` 仅用于 Solid 材质且显式传色(`Backdrop.cs:308-313`,WinUIEx 文档确认 alpha 即不透明度)——"双 tint 叠加"与"画透明"均与当前代码不符,romin2026 补丁针对的是旧基线。
- **#8 搜索弹窗卡片点击**:当前接线完整(`SearchPopupWindow.xaml.cs:2062-2086`,"单击选中、双击打开"语义明确,双击手势被刻意保护;DataContext 缺失根因已显式修复 `:1649-1661`)——fork 修的是旧基线问题,本仓库不存在。
- **#13 玻璃材质+圆角档位**:本仓库外观设置已有材质档位 Acrylic/AcrylicBase/Solid(Win11 由 shell 补 Mica/MicaAlt)与圆角三档 Square/Small/Round(`Features/Appearance/AppearanceSettingsViewModel.cs:166-170`、`SettingsService.cs:83-85`,经 DWMWA_WINDOW_CORNER_PREFERENCE 应用),且已有 5s 轮询 bedding watchdog——QingLanLay 线与本仓库现状大量重叠,移出待办;其"点击桌面清除选中"子项未核对,如需另行评估。

### 实施必要性复核(2026-10-24)

存在性核对回答"问题在不在",本节回答"值不值得做":结合当前代码形态、真实收益与风险,把当时的 1 项立项与 8 项待办共 9 项分为三档(第 9 项其后由维护者转入待办池,归第一档)。两个关键事实影响判断:①本仓库同步刷新已带 `preserveExistingIconWhenMissing: true`(`WidgetViewModel.ItemHydration.cs:219/258`),图标闪烁已有缓解,削弱 #12 的动机;②图标几何当前只用作边距测量参考而非落位避让(`WidgetWindowBase.TitleAppearance.cs:929`),#7 是能力缺口但非缺陷。

**第一档 · 有必要,建议排期(3 项)**

| # | 事项 | 必要性结论 |
|---|---|---|
| 9 | 文件格单格隐藏图标文字 | 维持必要;按维护者决策**由立项转入待办池排期,已于 2026-10-25 实施**。确认真空缺;fork 基线较旧,实施按当前 `FileWidgetIconLayout`/`WidgetConfig` 适配重做而非直搬,工作量小 |
| 1 | 性能 O(n²) 列表同步 | **有必要,但范围收窄为仅①**。`Items` 是 `ObservableCollection`,`Move` 自身 O(n) 且逐项触发 CollectionChanged,大文件夹全量刷新成本真实;修复局部于两个方法(位置字典),低风险,**①已于 2026-10-25 实施**(见下方完成状态)。②盲重载与③钩子期 Trim 经核对均受既有设计门控(兜底路径/空闲门控),**不实施**——改它们反而有回归风险 |
| 5 | 托盘菜单 show 期 E_NOTIMPL 降级 | **有必要,已于 2026-10-25 实施**(见下方完成状态)。真实缺口(菜单静默失效)、修复极小、不碰敏感区,随手级合入 |

**第二档 · 看产品需要,默认不动(3 项)**

| # | 事项 | 必要性结论 |
|---|---|---|
| 7 | 桌面图标吸附/避让 | 能力缺口属实,但属 UX 增强而非缺陷——组件盖住图标时用户可自行拖开;"落位自动推开图标"是否要做是产品决策,决策通过后再立项 |
| 12 | ThumbnailDiskCache 磁盘缓存 | 闪烁动机已被现有 `preserveExistingIconWhenMissing` 缓解;剩余收益是冷启动与内存峰值,需先用性能日志证实痛点再决定 |
| 6 | OfflineNetwork 离线边界 | 零售包安装已全离线,运行期联网(更新/天气/WebDAV)是产品设计;离线模式是产品功能而非修复,进路线图随产品决策 |

**第三档 · 暂无必要,转观察(3 项)**

| # | 事项 | 必要性结论 |
|---|---|---|
| 2 | WinEvent 事件驱动 re-bed | 5 秒轮询 watchdog 已兜底(约 10 秒自愈),在 z-order 勿删区引入事件钩子的风险大于收益;除非真机反馈 10 秒窗口可感,否则不做 |
| 4 | .lnk 图标资源持久化 | 默认帧兜底已消除白图标常态;仅当观感反馈不可接受再做 |
| 11 | 文件格分页+跨页搜索 | **撤回此前"先收策略层"的建议**:无 UI 接线的策略层移植会形成无消费方的半成品代码,不符合本仓库卫生;应随功能立项整体实施(策略层+UI 一起做) |

### 补充核对与决策:五个留档项(2026-10-24)

"未逐一确认"的五个留档项随后补做了与主清单相同的两轮核对(两个子代理并行,主会话对两条"病灶存在"判定 firsthand 复核通过),结论与决策如下。

**缺陷修复类(agogo233 三修)**

| 项 | 存在性核对 | 决策 |
|---|---|---|
| 拖入格子偶发无响应/空包掩盖 | **病灶存在**:拖放路由事件"到达即写"时间戳(`ContentWidgetWindow.NativeDragDrop.cs:480/1128`)+ 500ms 掩盖窗(:1618);`GetStorageItemsAsync` 失败产出空包无回退(`DeskBoxDragData.cs:175-232`),`SkippedCount>0` 无处理(`FileSurfaceContent.xaml.cs:3176`)——Hidden/System .lnk 拖入即触发 | **已实施(2026-10-24,按合并后实树适配)**:pending 握手 + 结果上报 + 原生回退保留 + Dispose 幂等,详见立项表完成状态;新增 8 例契约测试 |
| 多选拖动排序 | **已被本仓库解决**:9e414687 自行实现等价块移动数学(`WidgetViewModel.SortingAndWatchers.cs:141`)+ 单测(`FileItemMultiDragTests.cs:841-882`) | 无需移植。遗留疑点:单选前向拖放疑似落点偏后一格(`SortingAndWatchers.cs:102` 无前移修正),需运行时验证后另议 |
| 启动抬升沉底 | **病灶存在**(默认 Dynamic 模式):启动落位走"相对前台"(`WidgetManager.cs:984` → `RelativeLayerRestorePolicy.cs:35`),前台为第三方窗口时组件被插到其正下(高于其余应用);bedding watchdog 仅 pinned 模式生效,无自愈兜底;`MoveToDesktopBottom` 原语已在(`WidgetLayerService.cs:72`) | **已实施(2026-10-25,按合并后实树适配)**:lease 增绝对沉底策略位 + 启动刷新守卫,详见立项表完成状态;fork 两例策略测试逐字移植 + 契约测试适配 |

**特性类(四项)**

| 项 | 存在性核对 | 决策 |
|---|---|---|
| 番茄钟组件 | **真空缺**(无任何计时类组件);接触面中、纯增量,标准 kind 注册面已列清单(`WidgetRegistry.cs:61-64`、`WidgetContentFactory.cs:20/51`、`FeatureWidgetSettings` 描述符派生、13 语言) | **立项实施**:按本仓库接线面重做,带三层测试与 13 语言 |
| 独立日历格 | **部分存在**:日历呈现已抽象(`ICalendarPresentationSource` → `GlanceWidgetContentAdapter(calendarSource:)`),fork 方案近即插即用(接触面小;纯日历面拆离 = 中);fork 自身 2/12 语言无测试,移植须补齐 | **不采用**(维护者确认:Glance 内日历已够用) |
| 截图增强 | **真空缺**(无任何捕获能力;QuickCapture 图像读取线已有,图像写剪贴板需新建);接触面中到大——**风险点:System.Drawing.Common 在 Native AOT 无官方支持,须改 WIC/SoftwareBitmap;新增 NuGet(Vortice)触发双 lock + WMC1510 审计义务** | **立项(做,谨慎)**:先出 WIC/SoftwareBitmap 的 AOT 替换设计评审,再动码;新增 NuGet 走双 lock + WMC1510 审计 |
| 滴答清单桥接 | **真空缺、无产品路线**;实质为读滴答桌面端本地 SQLite(非云 API);移植大(Protocol+Bridge 两个新项目 + `TodoWidgetStore`→`ITodoRepository` 工厂改造约 15 文件);多进程先例充分(thumbnail proxy/Updater 等形态) | **不采用**(维护者确认:无接入第三方服务的产品计划;架构留作未来参考) |

### 上游合并任务(2026-10-24 登记)

**现状快照(已核实)**:upstream/main = `1fdf0bd9`(分析快照后零新增);本仓库 `wip/fix-bug` 落后 main **34 个提交**(merge-base `bbb9e697`),内容为 **1.5.9 发布 train**——Win11 原生右键菜单(单竖列/毛玻璃/命名空间)、更新体验六批(24h 检查/Toast 动作化/后台下载/状态徽章)、**新增韩语(语言 12→13)**、配置清空恢复提议、托盘图标隐藏样式、格子内回收站投放、长文件名三环链、隐藏修剪宽限 3s→30s 等。另有 `upstream/feat/160-kickoff` 新增 **22 个提交**(`055a6569..1493e3bd`,现领先 main 35):设置进程拆分成独立宿主进程的大特性线(批 1-4 + 审计 R1-R5),**未进 main**。

**任务 A(必要,接手后优先):合并 upstream/main 的 34 个落后提交**

合并铁则:

1. **不能影响现有功能**——fork 自有服务(`current_architecture.md` §Fork additions:`WidgetShowDesktopSelfHealService`、`WidgetCompactTransitionProgressPolicy`、`MusicSettingsStore`(schema v10)、`DesktopIconGeometryService`、`WidgetMarginReferenceCalculator`、`WidgetPeerOrderMovePlanner`、`WidgetTitleAppearanceSettings`、`QuickCaptureClipboardColorSettings`、`src/DeskBox.Abstractions`)与本报告〇节 4 项立项/9 项待办的工作面,合并后行为必须不变。
2. **冲突选最优解**——逐处按"哪个实现更符合本仓库当前架构与决策"语义取舍,禁止机械 ours/theirs;上游与本仓库各自修过同一问题时,保留本仓库实现并在合并提交信息记录等价性;上游新功能对照本报告决策状态取舍(已立项的收,不采用/放弃的丢)。
3. **约定变化——语言 12→13**:1.5.9 已引入韩语,合并后本仓库本地化硬约定从 12 个 JSON 升级为 **13 个**(后续立项/待办项的文案补齐按 13 文件执行;本报告早期文字按 12 语言书写,以合并后实树为准;vi-VN 属未经产品决策的私扩,判定不变——它将是第 14 语言)。
4. **AOT 门禁重钉**:1.5.9 train 调整过门禁计数(WMC1510 875→790、SettingsViewModel 可绑定 34→33、冒烟必需键改名等),与本仓库 864 基线冲突;合并后重跑 `scripts/publish-aot-audit.ps1` 按实树重钉,禁止静默漂移。
5. **合并后验证清单**:全量测试绿(数量以合并后为准,1.5.9 为 5803)、AOT 审计零漂移、13 语言键覆盖一致、dev-restart 冒烟;重点回归 fork 自有服务(SelfHeal/CompactTransition/MusicSettingsStore 迁移/DesktopIconGeometry/QuickCapture 颜色设置);**复核本报告〇节核对结论中受 1.5.9 影响的行**——#1③ 修剪门控(上游改了隐藏修剪宽限与 80MB 地板)、菜单/搜索弹窗相关(1.5.9 重做右键菜单)、AOT 计数。

> **完成状态(2026-10-24,任务 A 已执行)**:upstream/main `1fdf0bd9` 的 34 个提交已按铁则合并进 `wip/fix-bug`(merge-base `bbb9e697`)。55 个冲突文件逐一语义取舍,要点:z-order 三件套取上游带完整性扫描与事件驱动看门狗、保留本仓库 `TryReturnWidgetToRestingBand` 与 `ClearTopMostPreservingForeground` out 参签名;Onboarding 取本仓库 Task 流(上游旧场景向导随文件删除);托盘/热键/更新/设置服务按"上游结构 + 本仓库语义"并集(更新下载保留上游单飞包装、内部保留 FARC-05 克隆守卫);`DragDropPermissionService.TryGetShortcutRunAsFlag` 保留本仓库纯文件实现(MS-SHLLINK,无 COM);Strings 12 文件三方键合并(每语言 3140 键),ko-KR 补齐 155 个 fork 专有键成为第 13 语言。验证结果:全量测试 **6096/6096 绿**;AOT 审计**重钉 profile 64→65、WMC1510 796→808** 后零漂移通过(808 = 上游 1.5.9 790 + 本仓库绑定面 18 的实测值);13 语言键覆盖一致(3140 键 × 13 文件,占位符零失配);fork 自有服务回归由测试套覆盖(SelfHeal/CompactTransition/MusicSettingsStore v15 迁移/DesktopIconGeometry/QuickCapture 颜色)。本报告〇节核对结论的 file:line 锚点已按合并后实树刷新(#2/#3/#4/#5/#8/#13、拖入行、#6 计数 9→10);上游历史计数(875→790 等)由本仓库 808 重钉取代。

**任务 B(观察,禁止现在合并):`upstream/feat/160-kickoff`**

设置宿主拆分线(独立宿主进程 + IPC,与本 fork 的 settings coordinator 体系/`DeskBox.Abstractions` 契约大面积重叠)。动作:定期 `git fetch upstream` 观察其是否进 main;进 main 后按任务 A 流程专项评估(重点:layout/settings 域权威语义、宿主 IPC 面与本 fork 私有设置的冲突面);用户点名提前采用时,先出专项冲突评估再动。

> **观察记录(2026-10-25)**:`git fetch upstream` 核实——main 仍为 `1fdf0bd9` 零新增;kickoff 前进 4 个提交 `1493e3bd..7f65041c`(审计轮 R6 `ce64599e`/R7 `ae89b501`/R8 `2b137778`/R9 `7f65041c`,修复设置宿主拆分线大量 P0/P1),累计领先 main 39 个提交,仍未进 main;维持只观察不合并。

**接手提示词**:后续工作交给其他 agent 时,投喂 [docs/forks/agent-handoff-prompt.md](agent-handoff-prompt.md)。

### 立项(待实施,合计 4 项)

| # | 事项 | 来源 | 关键提交 | 备注 |
|---|---|---|---|---|
| 补充 1 | 拖入格子偶发无响应/空包掩盖修复 | agogo233 | [2952c9c1](https://github.com/agogo233/DeskBox/commit/2952c9c1b62f3bc434cc76bb4fd5fe7a0d09e102) | 病灶 firsthand 复核存在;**已实施(2026-10-24,见下方完成状态)** |
| 补充 2 | 启动抬升沉底修复 | agogo233 | [5c3a767f](https://github.com/agogo233/DeskBox/commit/5c3a767f0af2b8515bd6bc99e68bc581125f55af) | 默认 Dynamic 模式无自愈兜底;按 z-order 勿删文档评审合入;**已实施(2026-10-25,见下方完成状态)** |
| 补充 3 | 番茄钟组件(Pomodoro kind) | bupocc | [47dfd480](https://github.com/bupocc/DeskBox/commit/47dfd4802362566a967491fb2bf586f66e97733a)、[1df8bff3](https://github.com/bupocc/DeskBox/commit/1df8bff3c558b777155ef4b6511a93e1ecaa5b1d) | 真空缺;纯增量,按本仓库 kind 接线面重做,三层测试 + 13 语言;**已实施(2026-10-25,见下方完成状态)** |
| 补充 4 | 截图增强(区域/无遮挡窗口捕获 + 图像写剪贴板) | bbkien2312 | [49d306e9](https://github.com/bbkien2312/DeskBox-mrKBB/commit/49d306e965eb525123980a502aeefadff9886e20)、[ad4480c5](https://github.com/bbkien2312/DeskBox-mrKBB/commit/ad4480c5ae2090aca9a3ef6a5235e01d2bb93555) | 真空缺;**前置条件已满足(2026-10-25):AOT 替换设计评审定稿,见 [screenshot-capture-design.md](screenshot-capture-design.md)**——零新增 NuGet(弃 Vortice 与 WGC,Win10 21H2 黄边横幅不可关)、GDI(BitBlt/PrintWindow)+WIC 管线;下游复用 QuickCapture 图像读取线;**已实施(2026-10-25,见下方完成状态)** |

> **完成状态(2026-10-25,补充 1 已执行)**:拖入修复按 1.5.9 合并后实树适配落地(fork 原思路 + 两处本地化偏差,均有因):①`GroupFileDrop_Drop` 改为"到达置 pending + 临时时间戳",不再到达即定局,也不再取消已排程的原生回退(修复原生回调先排程时空包不可恢复的次序缺口);②`FileSurfaceContent.Root_Drop` 所有返回/异常路径经 `NotifyHostXamlDropResult` 上报真实结果,宿主按 **窗口句柄**解析(组窗口成员 id ≠ 窗口 id,fork 原按 id 查找会静默失效),空包(`GetStorageItemsAsync` 整批失败)上报失败 → 清掩码 → 原生 CF_HDROP 回退导入同一释放;③冲突解除路径上报成功(本地偏差:原生导入守卫只复查 busy 不复查传输冲突,照 fork 上报失败会让原生路径重试刚被阻断的传输);④新拖拽手势清残留 pending(fork 未做,组标签页落点会毒化后续导入);⑤`DroppedFileBatch.Dispose` 幂等。新增 `DropResponseMaskContractTests`(8 例);全套 6104/6104 通过(5 例为测试镜像缺 `.github`/`rust-toolchain.toml` 的环境假失败,补齐后 5/5);AOT 门 profile 65 无漂移(纯 C# 变更,WMC1510 808 不变)。

> **完成状态(2026-10-25,补充 2 已执行)**:启动沉底按 1.5.9 合并后实树适配落地:①`WidgetTemporaryRaiseLeasePolicy` lease 增 `AbsoluteSettle` 策略位,`Acquire` 携带、`Release`/`Forget` 保留,交互 `Acquire` 默认 relative 覆盖启动值(fork 两例策略测试逐字移植);②启动两入口标 absolute——`startup-restore` 批次抬升(`RaiseVisibleWidgetsTemporarily(…, absoluteSettle: true)`)与 `startup-explorer-desktop-ready` 刷新(`RefreshVisibleWidgetDesktopLayers(…, absoluteDesktopBottom: true)`),2.3s lease 恢复与 2.4s 启动刷新均落到桌面绝对底层;③刷新守卫 `absoluteDesktopBottom && !_widgetsRaisedFromTray && !IsWidgetInteractionActive`——托盘抬升或交互进行中不作绝对沉底;④绝对分支走 `MoveToDesktopBottom(HWnd)` 绕过"相对前台"策略,Release 前记前台窗口句柄+类名日志(新 helper `GetWindowClassNameSafe`);fork 的 `QuickCaptureWidgetWindow` 宿主在本树已被 DEF-027 移除,改动全部映射到 `ContentWidgetWindow` 唯一实现;⑤契约测试适配:刷新重入两处签名、`ClearTopMostOnly` 双分支(absolute 走 `MoveToDesktopBottom`,relative 保留 out-disposition + PreservePeerOrder)、托盘恢复无参调用钉死。全套测试 **6107/6107** 通过;AOT 门 profile 65 无漂移(纯 C# 变更);冒烟实例(01:06 构建)正常起窗。

> **接手甄别(2026-10-25,按提示词第 2 节流程执行)**:上表与各完成状态块经实树逐项核验——任务 A 合并(`de394a01`+`ec3ba646`)、补充 1(`d8cb48ae`)、补充 2(`4df21ef4`)、待办 #9(`4bc898a2`)、#1①(`cb6f532f`)、#5(`d03648fb`)全部属实,代码锚点抽验一致,**无降级项、无部分完成虚报**;v1.14 交接流程登记随后补提交为 `7f533583`。环境侧记:本机当前用户为 `developer\developer`,dotnet SDK 仅存在于 `C:\Users\developer\.dotnet`(须设 `DOTNET_ROOT` 并显式调用该 dotnet.exe);测试需 TMP/TEMP 重定向到仓库内 `.testtmp`(系统 TEMP 下 testhost 写入被本机行为防御软件拦截,重定向后 6117 例中仅余 10 例环境性失败,处置随补充 3 实施块记录)。

> **完成状态(2026-10-25,补充 3 已执行)**:番茄钟按本仓库接线面移植落地,bupocc `feature/tomato-clock` 三个有效提交(`47dfd480`/`1df8bff3`/`27377589`)合一,弃其 legacy 面(v1 元数据+`LegacyPomodoroBreakMinutes` 本 fork 从未有过)与分支内非番茄噪音(`23b7c0b9` z-order 等)。**架构适配**(与 bupocc 的差异均有因):①设置走 per-kind 小 store `PomodoroSettingsStore`(`data/pomodoro/settings.json`,`MusicSettingsStore` 先例;六字段平面序列化,**完全不动 AppSettings 冻结线,无 v16 迁移**;增进程级 `static event Changed` 供各 VM/设置节实例同步,`MusicWidgetViewModel` 各持实例先例);②`PomodoroTimerStateMachine` 602 行忠实移植(纯墙钟 UTC deadline/无内部 timer/Skip/Reset/自然完成后永远暂停),时长边界钳制内置(轮 1-12/专注 1-120/休 1-60);③适配器派生 `WidgetContentAdapterBase`(residency roadmap 4.6 契约:所有 IWidgetContent 必须过基座);④winmm `PlaySound` P/Invoke 收进 `Platform/Win32Helper.TryPlaySystemSoundAlias`(ModuleBoundary 契约:新 P/Invoke 归 Platform);⑤设置节走 `GlanceWidgetSettingsSection` 代码后置模式(禁触 settings facade)+设置窗三重门(SectionRoutes/SubSectionTagOrder/SectionTemplate)+`update-settings-search-catalog.ps1` 重生成;⑥原生通知服务增 `MuteAudio` 选项(完成提醒静音播放,与倒计时声并存);⑦capsule/常驻注意:`WidgetShell` 增 `PersistentAttentionBorder`(三色阶段描边+呼吸动画,折叠/性能停用路径同步停动画)、紧凑呈现 live 文本快路(`IsLiveTextUpdate`+`LiveStateKey`,每秒倒计时只换文本不重置动画)、capsule 主操作=开始/暂停。**测试**:新增 4 文件(状态机 19/store 7 含坏文件回退/VM 8/集成 5),重钉 8 个既有契约面(FeatureWidgetKind/WidgetContentFactory 五处/FeatureWidgetEntryFactory/WidgetSettingsMenuHelper/WidgetTitleIconMode)+JsonSerializationBaseline(42→43 文件、99→102 调用,含 9 个 AotStage5B4* 锁步链字面量与方法名标记)+AotStage4D1B(6→7 卡:SettingsCard 235/Expander 202/总 511)+ToggleSwitch 白名单(PomodoroSettingsSection=2);全量 **6162/6163**,唯一失败=已知环境项(HKCU 注册表写入被本机行为防御拦,见甄别块侧记;与番茄钟无关)。**AOT 审计**:XAML 变更后重跑 `publish-aot-audit.ps1`,profile 65 零漂移通过(新增 XAML 未突破 WMC1510 808 上限,无需重钉;首轮失败系审计运行中并发修改文档触发源快照守卫,静默重跑通过)。**13 语言** 各 +35 键(3146→3181,占位符零失配;12 语言逐字取 bupocc,ko-KR 自译)。提交:`b84a2bcb`(纵向切片:kind/工厂/注册/窗口管理/内容/设置节/语言/测试)+ 完成提醒·capsule·常驻注意提交 + 本文档登记。遗留观察:`QuickCaptureStoreGateTests.ConcurrentSaves` 首现 `File.Move` 首存分支竞态 flake(`ResilientJsonStore.SaveCoreAsync` 两并发同见无主文件→次 Move 撞名;重跑即绿,既有问题非本项引入,复现再立项)。

> **完成状态(2026-10-25,补充 4 已执行)**:截图增强按 [screenshot-capture-design.md](screenshot-capture-design.md) 定稿分两笔落地。**地基 `8ae3d3ee`**:`Win32Helper` 屏幕捕获区(GDI BitBlt/CreateDIBSection/PrintWindow(PW_RENDERFULLCONTENT)+ GDI alpha=0 修补 0xFF + WIC `BitmapEncoder` PNG 编码,**零新增 NuGet**,弃 Vortice/WGC——Win10 21H2 黄边横幅不可关);`ScreenshotSettingsStore`(per-kind 小 store,`MusicSettingsStore` 先例,int 手势字段复用 `GlobalHotkeyGesture` 归一化);JsonSerializationBaseline 43→44 文件含 9 个 AotStage5B4* 锁步链字面量同步。**主提交 `418c5cb4`**:冻结屏捕获窗 `ScreenshotCaptureWindow`(ctor 未激活先冻结整屏→悬停选窗(EnumWindows 过滤自身/同进程/系统桌面)/拖拽≥4px 自由区域/点击空白=整屏;Copy=DataPackage+`Clipboard.Flush` 且套 `DeskBoxClipboardWriteScope` 防 QuickCapture 回捕;Save=新 `FileSavePickerService`(`FileOpenPickerService` 同构);窗口目标 PrintWindow 失败回退冻结帧裁剪);`ScreenshotHotkeyService` 默认 Ctrl+Alt+S 挂托盘窗口(`CallbackDeferral.WrapSubclassProc` 契约,主快捷键冲突检查);设置节 ScreenshotSettings 入 General 族(启用开关+录制/重置);与 bbkien 的必要偏差:KeyboardAccelerators 替代 RootGrid.KeyDown(无焦点子元素不触发)、presenter/IsShownInSwitchers 走 `WindowShellState`(StartupResilience 契约)、硬编码越南语全量 13 语言化、AppSettings 改 per-kind store。**顺带修复既有缺陷**:`ScreenCaptureService.TryEncodePngAsync` 目标已存在时静默保留旧字节返回 true,改 `File.Move(overwrite:true)` 原子替换。**测试**:全量 **6173/6174**(唯一失败=已知环境项 HKCU 注册表);重钉 ModuleBoundary 清单(+ScreenshotHotkeyService)、AotStage4D1B(SettingsCard 237/203、Expander 38/24、总 516)、ToggleSwitch 白名单(ScreenshotSettingsSection=1)、General 排序钉(Performance<Screenshot<Displays);搜索目录 +3 条。**AOT 审计** profile 65:首轮失败=目录创建处 CS8600 新警告(可空性),修复并入后零漂移通过。**13 语言** +24 键(窗口 13+设置 11,3181→3205,ko-KR 自译)。冒烟实例(08:49 构建)正常起窗。

### 待办池(9 项,均已记录;实施分档见「实施必要性复核」——第一档 #1/#5/#9 建议排期,第二档 #6/#7/#12 看产品需要,第三档 #2/#4/#11 转观察)

| # | 事项 | 来源 | 关键提交 | 核对后定位 |
|---|---|---|---|---|
| 9 | 文件格单格隐藏图标文字 | PepinoLatte | [30934a7](https://github.com/PepinoLatte/DeskBox/commit/30934a7b6e760e551a9c0a255c54a7aa6d54bb77) | 自立项转入第一档;**已实施(2026-10-25,见下方完成状态)**;可空覆盖 + 备份白名单 + 13 语言 + 7 例测试,按当前 `FileWidgetIconLayout`/`WidgetConfig` 适配 |
| 1 | 性能线:O(n²) 列表同步(主);盲重载降级、钩子期 Trim(次,观察) | ForgetiExistence | [c75f7306](https://github.com/ForgetiExistence/DeskBox/commit/c75f73067b7e240371615b02b95ebc9742a4bd2d) | ①确凿,**已于 2026-10-25 实施(见下方完成状态)**;②③部分存在,风险窗口小 |
| 6 | OfflineNetwork 硬离线网络边界(transport 层) | dandostoja | [b9e8b2f0](https://github.com/dandostoja/DeskBox-LOCAL/commit/b9e8b2f0b0a09a6e30860ba4f72c1bbb84e40a17) | 10 处出口/8 文件无单点(合并 1.5.9 后重核),属实;**用户决策(2026-10-25):暂不实施——离线保证非当前需求,防火墙规则可达等价效果,随产品需要重开** |
| 7 | 桌面图标格点吸附/避让互操作(不含 Bucket 组件) | nizarhamza | [0e7604b](https://github.com/nizarhamza/DeskBox_Fork/commit/0e7604bbaf66455c3a584819235e4c203bd74878) | 只读几何确认、零写位置,属实;**用户决策(2026-10-25):暂不实施——待"组件压图标"困扰确认为日常痛点后再立项(届时先设计评审)** |
| 11 | 文件格分页 + 跨页搜索(策略层先行) | lateautumn233 | [6411c8e6](https://github.com/lateautumn233/DeskBox/commit/6411c8e68568901c89bbb7834c5556059882594d) | 特性缺失确认,策略层近直接移植 |
| 12 | ThumbnailDiskCache 缩略图磁盘缓存 | bbkien2312 | [414c7719](https://github.com/bbkien2312/DeskBox-mrKBB/commit/414c7719b88a1aecdb7bbf8fcd1f6bd0151e0d64) | 仅内存缓存确认,native proxy 不落盘;**用户决策(2026-10-25):立项实施;已实施(2026-10-25,见下方完成状态)** |
| 2 | WinEvent 事件驱动桌面带 re-bed | romin2026 | [927a9231](https://github.com/romin2026/DeskBox/commit/927a9231a451f3b49f9234d6ec25e0b66b2940ec) | 降级:时效优化(现有 5s 轮询约 10 秒自愈) |
| 5 | 托盘菜单 show 期 E_NOTIMPL 降级补丁 | wrust0608 | [c6dbed3a](https://github.com/wrust0608/DeskBox/commit/c6dbed3ab70b236edd74a72d539fe94e84e12020) | 降级:仅剩 show 期落泛化 catch 不降级的缺口;**已于 2026-10-25 实施(见下方完成状态)** |
| 4 | .lnk 图标资源持久化(可选增强) | zerovoxxx | [13ee4de2](https://github.com/zerovoxxx/DeskBox/commit/13ee4de2a7caff114c676f7458681224b0bf7fc1) | 降级:白图标常态已被默认帧兜底,持久化为增强 |

> **完成状态(2026-10-25,待办池 #9 已执行)**:单格隐藏图标文字按 1.5.9 合并后实树适配落地(fork 30934a7 的思路与几何语义,UI 面按本树习语重做):①`WidgetConfig` 增 `IconLabelHiddenOverride`(`bool?`,`JsonIgnoreCondition.WhenWritingNull`,null=跟随全局);`FileWidgetIconLayout.Calculate` 增第 4 参,新 helper `ResolveEffectiveFileNameLineCount`(隐藏覆盖→Hidden 档、强制显示→`Max(Min,lines)`,越界全局值先归一);②`WidgetViewModel` 增 `EffectiveIconLabelHidden`/`SetIconLabelHiddenOverride`/`SetEffectiveIconLabelHidden`,设置存储走"最小覆盖"(期望态等于全局态时清 null,后续继续跟随全局变更);③入口两处——右键"查看"菜单 `Widget.IconLabel.Hidden` 开关(组窗口成员生效)+ 设置页"格子单独管理"两张 toolkit:SettingsCard(本树习语;fork 的 Border+SettingCardIdentityGridStyle 卡已废弃):格子选择 ComboBox + 隐藏开关 ToggleSwitch,事件深度计数抑制刷新重入;④备份白名单 `WidgetStyleBackupProjection.WidgetKeys` 增 `iconLabelHiddenOverride`(跨设备同步样式);桌面整理预览卡 `DesktopOrganizationPreviewCard` 传参同步;⑤13 语言 × 6 新键(3140→3146,ko-KR 自译),占位符奇偶校验通过;⑥测试:fork `FileWidgetIconLabelOverrideTests` 7 例逐字移植;`SettingsSliceOwnershipContractTests` LayoutAndSettings 17→19 + 新 FileWidgetSettingsSection.xaml.cs=4;连带重钉 `AotStage4D1B`(SettingsCard Header 226→228/Description 193→195/合计 493→497,两张新卡)、`ToggleSwitchAccessibleNameContractTests`(1→2)、`SettingsSearchCatalog`(2 条目)。全套测试 **6114/6114** 通过;AOT 门 profile 65 无漂移(exit 0,findings 0,WMC1510 808 不变);冒烟实例(02:04 构建)正常起窗。

> **完成状态(2026-10-25,待办池 #1 ① 已执行)**:O(n²) 位置字典按 fork c75f7306 的①方案适配落地(②盲重载与③钩子期 Trim 按必要性复核结论不实施):①`SyncFolderItems` 与 `ApplyReconciledManualOrder` 不再在循环内调 `Items.IndexOf`,改为每趟先建 `Dictionary<WidgetItem, int>` 位置索引(`WidgetItem` 未覆写 `Equals`,引用相等即位置语义);②`Insert`/`Move` 后仅对尾部重索引(新 helper `BuildPositionIndex`/`ReindexPositions`,Move 取 `Math.Min(currentIndex, targetIndex)` 起点),查不到时保留防御性插入回退;③稳态(纯数据刷新、无重排)降为单次 O(n) 遍历。范围注记:同形状的循环内 `Items.IndexOf`(`SortingAndWatchers.cs:277` 排序应用、`Stacks.cs:1965` 投影重建)不在本次核对范围,留档待后续评估。测试:新增 `ItemSyncPositionIndexContractTests` 3 例(禁逐项 IndexOf、索引跟随语义、helper 签名钉住);全套测试 **6117/6117** 通过;AOT 门 profile 65 无漂移(exit 0,findings 0,WMC1510 808 不变);冒烟实例(03:01 构建)正常起窗。

> **完成状态(2026-10-25,待办池 #5 已执行)**:托盘 show 期 E_NOTIMPL 降级按 1.5.9 合并后实树适配落地(fork c6dbed3a 只包赋值期 try/catch,本树赋值期已有 catch-all + 重试降级链,残余缺口在 show 期):`IsSecondWindowHostFailure` 从只认 XamlRoot `ArgumentException` 扩展为同时认 E_NOTIMPL(`0x80004001`,Win11 26200 二窗宿主拒绝场景),使 show 期该异常进入既有"重建一次 → 连续两次失败降级原生 PopupMenu"阶梯,而非落入只记日志的泛化 catch 导致菜单静默失效;赋值期路径(`TryAssignTrayContextFlyout` catch-all → 15×2s 重试 → 阈值降级)无需改动。`TrayContextMenuResilienceTests` 过滤器断言同步扩展;全套测试 **6117/6117** 通过;AOT 门 profile 65 无漂移(exit 0,findings 0,WMC1510 808 不变);冒烟实例(03:31 构建)正常起窗。

> **完成状态(2026-10-25,待办池 #12 已执行)**:缩略图磁盘缓存按 bbkien `414c7719` 的缓存面移植落地(该提交九文件中其余为本 fork 已删的截图窗面,不适用;后续演进 `3a79e4ec` 仅 x86 减半,本 fork 无 x86,取原版上限)。**管线**:`IconHelper.CreateMediaThumbnailAsync` 在 `s_thumbLoadSemaphore` 临界区内、native 缩略图加载之前插入磁盘层,**仅非视频**(视频预览的唯一可靠解码器是 Windows 缩略图提供器,继续走原路径);键=SHA256(全路径|Length|mtimeTicks|clamp(w,24,256)),与内存层 `GetFileIconVersion` 同一身份语义,文件变化自动失效;存 PNG 字节(与 proxy/WIC 落盘格式一致),tmp+`File.Move(overwrite:true)` 原子写,命中时显式 touch `LastAccessTimeUtc` 供 LRU;后台单飞行逐出(先超 2000 文件、再旧到 128MB);失败永不缓存(异常全捕获返回 null 走原路径)。**与 bbkien 的偏差**(均有因):①孤儿清扫——原子写崩溃残留的 `*.png.tmp-*` 不匹配 `*.png` 逐出扫描,补 1 小时龄门控的专项清扫;②`EnsureInitialized` 去掉同步目录枚举(只建目录+排队后台清理,启动零 IO),App dispatcher 初始化后按需调用;③测试缝 `DirectoryPathOverrideForTests`(静态类无 ctor 注入先例);④目录取 bbkien 原路径 `RootPath/cache/thumbnails`(备份枚举根是 `DataDirectory`,`cache/` 天然在备份外,与内存层互不影响)。**诊断**:`PerformanceLogger` 增 `DiskThumbnailCacheCount/Bytes` 两计数器并入 `thumbCacheMB` 日志行。**测试**:新增 6 例(bbkien 零测试):落盘回读+PNG 完整性+无 tmp 残留、身份键复用与文件变更失效、解码宽度参与键、8 路并发单飞行合并、缺失源返回 null、孤儿 tmp 清扫;`ModuleBoundary` 破坏性文件操作清单 +2(tmp+move 原子写与孤儿清扫,自管 app-data 面)。零 `JsonSerializer` 调用,**不触发** JsonSerializationBaseline 及 AotStage5B4* 锁步链。GDI+ 缩放(`System.Drawing` 经 H.NotifyIcon 链已在树且 AOT 零售在用,Win10 地板安全)。全量 **6179/6180**(唯一失败=已知环境项 HKCU 注册表写入被行为防御拦截,见甄别块侧记;与缩略图无关);AOT 门 profile 65 零漂移通过(纯 C# 变更,WMC1510 808 不变)。

### 收尾:迭代式深度审查循环与反过度工程(立项与待办池全部关闭后)

**触发条件**:4 项立项与 9 项待办池全部完成或按决策正式关闭(含甄别阶段确认已完成者),且任务一(上游合并)已落地。

**循环体**:每一轮 = ①**深度代码审查**——优先审查本轮新增/修改的代码及其交叉面,兼顾全局红线(AOT 门禁与 nameof 棘轮、13 语言键覆盖一致、内容窗口路径、`AppSettings` 冻结线、z-order 勿删语义、`native/` ABI 冻结)、并发与资源释放(句柄/管道/临时目录/事件订阅)、错误吞噬与静默失败;②对发现的 bug 与重大缺陷**优化修复**;③修完进入下一轮审查。**退出条件**:连续一整轮深度审查未发现任何 P0/P1 重大缺陷(仅剩已记档的观察项与低危债)即收敛;一旦发现即修复并继续下一轮。

**反过度工程(硬要求,适用于循环内每一项动作)**:

- 修复以"消除缺陷"为界——不引入新抽象、新框架、新依赖,不顺手重构与缺陷无关的代码,不改公共契约形态;
- 每项修复前先回答"**是否有必要实施**":缺陷是否真实可感(有 file:line 或复现证据,不是理论洁癖)、修复收益是否大于风险、是否存在更小的修法;任一答案为否,记录到本报告待办池/观察项,**不实施**;
- 问题分级:P0(崩溃/数据损坏/核心功能失效)、P1(明显缺陷)、P2(优化机会)——当前轮只修 P0/P1,**P2 一律记档**;
- 区分"缺陷修复"与"锦上添花":后者转待办池记档,不进当前轮;
- 每轮结束在修订记录写一行:轮次、发现、修复、遗留,让循环可审计。

完整定义见接手提示词第 9 节。

### 明确不采用 / 放弃

- **第 10 项** 每格背景 + 清爽桌面(Pictus08):**不采用**。
- **第 14 项** AI 桌面整理 + 布局设计器(GIT-LINCC,维护者侧单提交巨石):**放弃**。
- **其余维护者侧小项**(第 15 项):宿舍电费组件、Dock/监控镜像、按拓扑记忆图标尺寸、白牌化改名、lean 出厂默认值——先不做,仅在详解中留档。
- **大方向**(第 16 项):Command API/MCP、AI 子进程 harness、数据式主题包——本轮都不立项,仅记入路线图。
- ~~未逐一确认的其余"有启发"项~~(番茄钟组件、截图增强、agogo233 拖放/启动修复、独立日历格、进程外桥接)**已于 2026-10-24 补充核对并逐项决策**(见「补充核对与决策」):拖入修复、启动沉底修复、番茄钟、截图增强(带 AOT 前置条件)**立项**;独立日历格与滴答桥接**不采用**。

---

## 一、摘要

### 网络概况

| 指标 | 数值 |
|---|---|
| fork 总数(API 全量枚举) | 393 |
| 零独有提交(所有分支 tip 都在 upstream 历史内) | 354(90.1%) |
| 枚举后已删除或转私有 | 2 |
| 有独有内容的 fork | 36 |
| 网络净新增提交总数(不含 beici 线与 upstream) | 366 |
| 星标分布 | 379 个 0★、13 个 1★、1 个 2★ |
| pushed 时间分布 | 2026-10:102 个;2026-09:165 个;更早:126 个 |

这是一个典型的"一次性 fork"网络:九成 fork 只是点了一下 Fork 按钮,所有分支都停在 upstream 的历史提交上,没有任何自己的工作。星标全网最高 2★,无外部流量意义上的活跃生态。**实质工作集中在 36 个 fork 上**,其中真正值得本仓库关注的约 15 个。

### 最值得注意的五个发现

1. **外部贡献的主体是一批"小而准"的缺陷修复与性能修复,且质量普遍高于功能开发。** 性能线(ForgetiExistence,净新增 6 个提交、+206 行)直接对症"大文件夹卡顿";romin2026 的事件驱动桌面带 re-bed 修复补上了本仓库 self-heal 体系未覆盖的日常失效路径;zerovoxxx 的登录期托盘韧性与 .lnk 图标资源恢复都带完整测试。这批修复大多只动托管层、不碰 schema 与 Rust 层,是全网络性价比最高的部分。

2. **维护者身份(Simon)的 54 个独立提交散布在 10 个 fork 上,而本仓库当前一个都没有。** 包括:GIT-LINCC 上的 AI 桌面整理 + 独立布局设计器(单提交 +42052/-34304)、Zagadka-3906 的宿舍电费组件、QingLanLay 的圆角玻璃材质与桌面恢复看门狗、Lurbing7 的 Dock/系统监控实验线(15 个中 14 个)、romin2026 的四个 cloud-backup 分支(去重 9 个)、agogo233 的 9 个、ScyPolit 的 5 个、以及 joeshu / luziyi123448-gif / nnlpwm21 各 1 个。这些工作质量不低(多带契约测试与 12 语言同步),但游离在本仓库之外,需要逐一决策收拢或明确放弃。

3. **"按本仓库架构新增 WidgetKind"已经有完整的外部范本。** bupocc 的番茄钟组件(注册/工厂/适配器/12 语言/三层测试全齐)可直接作为新组件接线样板;nizarhamza 的 Bucket 组件附带 SysListView32 图标格点吸附与避让互操作,与本仓库 `DesktopIconGeometryService` 天然互补。

4. **若干 fork 指向了同一批方向性机会,但都不能直接搬。** 本地 Command API / MCP 控制面(wanvfx、wuya11111)、AI 子进程 harness(STMfan,规避 AOT 与密钥落盘)、数据式主题包(zjzonline)、组件内分页 + 跨页搜索(lateautumn233)、外部 SaaS 进程外桥接(ScyPolit)——都与 upstream 完全没有的方向对应,实现各有可取处,移植时必须按本仓库的插件化契约(`DeskBox.Abstractions`)、AOT 门禁与 12 语言约定重做。

5. **网络里也有一批"工程红线"反例,是反向的验证样本。** joeshu 放松 AOT 审计脚本("警告减少也算通过"),与本仓库 WMC1510 零漂移红线相反;lmyyds1 直接删除 10/12 语言 JSON 换体积;bbkien2312 与 wrust0608 各自加入第 13 语言 vi-VN(是否扩语言是产品决策,不能由 fork 单方面引入);agogo233 的混合线把 native capability mask 从冻结的 511 推到 1023。这些做法都明确不可移植。

> **核对修正(2026-10-24)**:发现 1 写于代码核对之前,其中两处已被修正——romin2026 的桌面带 re-bed 并非"覆盖缺失"(本仓库 5 秒轮询 bedding watchdog 约 10 秒自愈,fork 补的是时效),Win10 材质两修经核对在本仓库**不存在所述问题**;zerovoxxx 的登录期托盘韧性也已有既有重试/降级。逐条证据见「〇、行动决策」核对结论。

---

## 二、方法与数据快照

按任务要求的方法执行,有两处因环境所限做了等价替换,如实说明:

1. **枚举(GitHub API 分页)**:对 `https://api.github.com/repos/Tianyu199509/DeskBox/forks?per_page=100` 翻页 4 次(100/100/100/93),共 **393 个 fork**,记录 `full_name / stargazers_count / pushed_at / default_branch / created_at`。任务建议的 `gh` CLI 本机未安装,改用同一 REST API 的 curl 调用;全程未抓取网页。
2. **初筛(API 配额受限的等价方案)**:未认证 GitHub API 限额 60 次/小时且出口 IP 为共享代理,无法对每个 fork 做 compare API 调用。改用 git 协议完成同等工作:
   - 对全部 393 个 fork 执行 `git ls-remote --heads`,取得**每个 fork 的全部分支 tip**(不只默认分支);
   - 在本地裸仓库构建 upstream 全引用可达提交集合(919 个 SHA,覆盖 main、feat 分支与全部标签),逐一比对分支 tip;
   - 结果:354 个 fork 的**所有**分支 tip 都落在 upstream 历史内(零独有提交);37 个 fork 存在"宇宙外"分支。
3. **等价 compare(本地化)**:把 37 个 fork 的 62 个分支抓取到本地裸仓库,用 `git rev-list --count` 计算 ahead/behind、`git diff --shortstat main...tip` 计算规模;再叠加本仓库 origin 引用可达集合(821 个 SHA)剔除 beici 线合入,得到每个分支的**净新增提交**清单。
4. **深挖(两个子代理并行)**:候选 37 个(>10),按任务预案拆为两组并行深挖——A 组 10 个重型 fork 线,B 组 26 个中小型 fork;统一摘要结构,主会话对 9 项将进入结论的关键声明做了逐项抽查(提交对象、diffstat、关键文件内容),全部属实。
5. **不可达 2 个**:`Echoes-26/-DeskBox`、`alan3024/DeskBox` 在枚举时在列,但 `git ls-remote` 报 Repository not found(枚举后删除或转私有),无法分析。
6. 分支级 diffstat 可能因合并了不在 upstream main 上的上游历史(如插件化系列)而虚高;本报告以"净新增提交"与其自身 diffstat 为准,已知虚出处单独标注(bupocc、beici)。

---

## 三、总览表

36 个有独有内容的外部 fork(按净新增提交数降序),加本仓库自身与聚合行。表内一句话结论写于代码核对之前;受核对影响的行(romin2026、zerovoxxx、QingLanLay、GIT-LINCC、Pictus08、2510227870,以及补充核对的 bupocc、agogo233、bbkien2312、zhonghengchen、ScyPolit)最新判定以「〇、行动决策」为准:

| fork | Star | 最后推送 | 净新增(主要分支) | 一句话结论 |
|---|---|---|---|---|
| [agogo233/DeskBox](https://github.com/agogo233/DeskBox) | ★0 | 2026-10-19 | 52(main 51 + feat 分支 1) | 跟随 dev-1.6 的长期混合分叉:桌面外快捷方式、拖放稳健性、启动沉底修复有参考价值;capability mask 已被推到 1023,整线不可直接合并 |
| [bbkien2312/DeskBox-mrKBB](https://github.com/bbkien2312/DeskBox-mrKBB) | ★0 | 2026-08-30 | 46(main) | 截图增强 + ThumbnailDiskCache 缩略图磁盘缓存 + 大组件图标保活;自建更新通道与 vi-VN 第 13 语言不合规 |
| [romin2026/DeskBox](https://github.com/romin2026/DeskBox) | ★0 | 2026-10-12 | 32(skins 18 + main 13 + zorder-fix 1,另有维护者侧 cloud-backup 9) | SkinPack 皮肤包、Win10 材质两连修、WinEvent 事件驱动桌面带 re-bed;后三者接近可直接摘取 |
| [lateautumn233/DeskBox](https://github.com/lateautumn233/DeskBox) | ★0 | 2026-09-15 | 32(search/tmp/upstream 三次 rebase 同源) | 组件内分页 + 跨页搜索,策略层/索引/高亮/缩写匹配纯托管可测试、12 语言合规,接近直接移植 |
| [wanvfx/DeskBox](https://github.com/wanvfx/DeskBox) | ★0 | 2026-08-30 | 18(main) | 完整本地 Command API(命名管道 JSON-RPC + CLI + MCP + 审计 + 只读/破坏性分级),方向性最强,须按插件化契约重做 |
| [dandostoja/DeskBox-LOCAL](https://github.com/dandostoja/DeskBox-LOCAL) | ★0 | 2026-10-04 | 17(main) | 硬离线网络边界(OfflineNetwork 单点 503 transport),零 schema/架构冲突,几乎拿来即用 |
| [Ci-Tu/DeskBox](https://github.com/Ci-Tu/DeskBox) | ★0 | 2026-10-14 | 16(main) | 双击 Ctrl 搜索预设 + 弹窗失活隐藏,**已被 upstream 吸收**,fork 自行封存 |
| [Lurbing7/DeskBox](https://github.com/Lurbing7/DeskBox) | ★0 | 2026-10-07 | 15(codex_tags-card,14 个为维护者侧) | 实为维护者侧 Dock/系统监控实验线的外部镜像;SensorHost 子进程供数思路可作 SystemMonitor 参考 |
| [wrust0608/DeskBox](https://github.com/wrust0608/DeskBox) | ★0 | 2026-10-03 | 13(feat_vi-vn-localization) | 越南语全量本地化(第 13 语言 + 安装器 + 审计工具),工程正规但与 12 语言冻结约束冲突;Win11 26200 托盘崩溃修复可摘 |
| [joeshu/DeskBox](https://github.com/joeshu/DeskBox) | ★0 | 2026-09-20 | 13(codex_stage7b-arm64-actions 12 + main 1) | lean 文件组件出厂默认值有小参考;放松 AOT 审计的做法与门禁红线相反,不借鉴 |
| [bupocc/DeskBox](https://github.com/bupocc/DeskBox) | ★0 | 2026-09-09 | 10(develop,含番茄钟 7 + WebDAV 9 子集) | 番茄钟 + WebDAV 备份 + 持久置顶,架构遵守度高;番茄钟是新组件最完整外部范本,可直接借鉴 |
| [STMfan/DeskBox-STMFeatured](https://github.com/STMfan/DeskBox-STMFeatured) | ★0 | 2026-10-15 | 9(main) | AI 格子(子进程 harness + 行协议 + 环境变量密钥)+ 音乐频谱;harness 路线对 AI 方向有启发 |
| [zjzonline/DeskBox-Themes](https://github.com/zjzonline/DeskBox-Themes) | ★0 | 2026-09-09 | 9(deskbox-themes 8 + main 1) | 纯 JSON 数据式主题包引擎(schema v1),与冻结线/AOT 兼容;主题方向底稿 |
| [timeclockouo/DeskBox](https://github.com/timeclockouo/DeskBox) | ★0 | 2026-09-08 | 9(main 4 + icon-fix 3 + patch 2) | 图标位图质量/裁剪阈值试凑式小补丁 + 安装器 CI;仅了解 |
| [Zagadka-3906/DeskBox](https://github.com/Zagadka-3906/DeskBox) | ★0 | 2026-09-25 | 8(feature_dorm-electricity-widget,全部维护者侧) | 宿舍电费组件 + ServerChan 推送(维护者身份,本仓库尚无);推送通道可复用 |
| [zerovoxxx/DeskBox](https://github.com/zerovoxxx/DeskBox) | ★0 | 2026-10-16 | 7(feat-custom) | 登录期托盘菜单韧性 + .lnk 图标资源恢复,带测试的真实缺陷修复,可直接借鉴 |
| [ScyPolit/DeskBox_Personalized](https://github.com/ScyPolit/DeskBox_Personalized) | ★1 | 2026-10-13 | 7(main,5 个为维护者侧) | 滴答清单进程外 AOT 桥接 + 协议程序集 + 兼容性探针;外部 SaaS 集成架构参考 |
| [ForgetiExistence/DeskBox](https://github.com/ForgetiExistence/DeskBox) | ★0 | 2026-09-23 | 6(perf-lag-fixes = main) | O(n²)→O(n) 列表同步 + 盲重载降级 + 钩子期禁 TrimWorkingSet;性能线最高价值,可直接移植 |
| [wuya11111/DeskBox-Agent](https://github.com/wuya11111/DeskBox-Agent) | ★0 | 2026-09-02 | 6(main) | MCP server + 命名管道 Agent 控制 + 布局命令面;与插件化路线同向 |
| [QingLanLay/DeskBox](https://github.com/QingLanLay/DeskBox) | ★0 | 2026-10-23 | 5(main 4 + glass 1,全部维护者侧) | 圆角玻璃材质 + 桌面恢复看门狗 + 点击桌面清除选中(维护者身份,本仓库尚无);质量高 |
| [lmyyds1/DeskBox](https://github.com/lmyyds1/DeskBox) | ★1 | 2026-08-26 | 5(main) | 个人精简版:删 10/12 语言、去商店;违反本地化约定,仅了解 |
| [dejush1021-crypto/DeskBox](https://github.com/dejush1021-crypto/DeskBox) | ★0 | 2026-10-03 | 4(main + patch-1/2/3 去重) | 全部 README 微调;仅了解 |
| [zhonghengchen/DeskBox](https://github.com/zhonghengchen/DeskBox) | ★0 | 2026-09-03 | 4(main) | 独立日历格(复用 Glance 呈现面);点子好但 Strings 仅 2/12、无测试 |
| [ajie4979/DeskBox-ajie4979](https://github.com/ajie4979/DeskBox-ajie4979) | ★0 | 2026-09-14 | 2(main) | README 更名 + 英文版,纯文档 |
| [GIT-LINCC/DeskBox](https://github.com/GIT-LINCC/DeskBox) | ★1 | 2026-09-28 | 1(单提交巨石,维护者侧) | AI 桌面整理 + 独立布局设计器(+42k);事务化应用 + 校验回环是好设计,12 语言文案被破坏性改写需逐键评审 |
| [Pictus08/DeskBox](https://github.com/Pictus08/DeskBox) | ★0 | 2026-10-06 | 1(feat_widget-colors-clean-desktop) | 每格子背景覆盖 + 组件/桌面图标一键隐藏恢复,约定齐全,可直接借鉴 |
| [nizarhamza/DeskBox_Fork](https://github.com/nizarhamza/DeskBox_Fork) | ★0 | 2026-09-14 | 1(feature_bucket-widget) | Bucket 组件 + SysListView32 图标格点吸附/避让;避让互操作与 DesktopIconGeometryService 互补,可直接借鉴 |
| [PepinoLatte/DeskBox](https://github.com/PepinoLatte/DeskBox) | ★0 | 2026-09-27 | 1(main) | 文件格单格隐藏图标文字(可空覆盖 + 备份白名单 + 12 语言),约定样板,可直接借鉴 |
| [luziyi123448-gif/DeskBox](https://github.com/luziyi123448-gif/DeskBox) | ★0 | 2026-10-06 | 1(codex 分支,维护者侧) | 按显示器拓扑记忆图标尺寸(维护者身份,本仓库尚无);规范小特性 |
| [2510227870/DeskBox](https://github.com/2510227870/DeskBox) | ★0 | 2026-09-06 | 1(dev_20260906) | 修复搜索弹窗推荐卡片单击/双击无响应;真实小缺陷,思路可参考 |
| [GeneYu404/DeskBox](https://github.com/GeneYu404/DeskBox) | ★0 | 2026-10-14 | 1(file-only) | csproj 编译开关裁掉非 File 组件;裁剪 hack,仅了解 |
| [YSDXL/DeskBox](https://github.com/YSDXL/DeskBox) | ★0 | 2026-08-27 | 1(main) | CoupleMemo 情侣备忘小组件(内容窗口路径可扩展性旁证);仅了解 |
| [nnlpwm21/DeskBoxWhite](https://github.com/nnlpwm21/DeskBoxWhite) | ★0 | 2026-10-08 | 1(main,维护者侧) | 整体更名白牌化(1413 个文件重命名 + 技术分析报告),非行尾/白主题;仅了解 |
| [fairhopeweb/DeskBox](https://github.com/fairhopeweb/DeskBox) | ★0 | 2026-10-08 | 1(imgbot) | ImgBot 压缩 docs 图片;仅了解 |
| [Rodgershahha/DeskBox](https://github.com/Rodgershahha/DeskBox) | ★0 | 2026-09-02 | 1(feat-lgq) | README 路径说明 + global.json rollForward 微调;仅了解 |
| [BumptiousChen/DeskBox](https://github.com/BumptiousChen/DeskBox) | ★0 | 2026-10-16 | 1(my-modification) | 仅提交一个开发版 .lnk 二进制(提交测试);仅了解 |
| **beici/DeskBox(本仓库)** | ★0 | 2026-10-23 | —(wip/fix-bug 领先 upstream 214,均属自身线) | 网络成员之一,自有服务清单见 `docs/architecture/current_architecture.md` §Fork additions |
| 其余 354 个 fork(聚合行) | 0–1 | — | **0** | 所有分支 tip 都在 upstream 历史内,无任何独有提交(逐个清单可按第二节 API 查询复现) |
| Echoes-26/-DeskBox、alan3024/DeskBox(聚合行) | — | — | — | 枚举后已删除或转私有,无法分析 |

---

## 四、重点详解

> 37 个候选(含本仓库)已按任务预案分两个子代理并行深挖;以下为汇总与去重后的结果。除单独标注者外,各 fork 的自有提交均未改动 `native/` Rust 层、未绕开内容窗口架构;对 Strings / AppSettings / AOT 门禁的触碰情况在每节"实现方式"中说明。
>
> 本节保留深挖时的**原始判定**;其后又一轮对当前代码逐条核对(结论见「〇、行动决策」核对结论),受影响章节末尾以「核对注」标出。声称经核对属实的章节:ForgetiExistence(①确凿、②③部分存在)、nizarhamza(避让缺失属实)、dandostoja(9 处网络出口无单点属实)、lateautumn233(分页缺失属实)、PepinoLatte(per-tile 开关缺失属实)、bbkien2312(无磁盘缓存属实);bupocc、STMfan、zjzonline、wanvfx、wuya11111、ScyPolit、agogo233、joeshu、Lurbing7、Zagadka-3906 等留档章节未做逐条核对。

### romin2026/DeskBox(★0,pushed 2026-10-12)——可直接借鉴

- **规模**:feature_skins-fonts-icons(18 个净新增提交,+946/-432,13 文件);main(13 个,+1168/-528,22 文件);fix_desktop-band-winevent-rebed(1 个,+338/-71,4 文件)。另有 feat_cloud-backup-ui/webdav/scoped、feat_platform-interop-win32 四个分支为维护者侧提交(去重 9 个,cloud backup PR 线,该能力已随 upstream 1.5.4/1.5.5 落地,不重复评)。
- **变更内容**:
  - **新增功能(UI·UX)**:SkinPack 皮肤包体系——三套预设(DarkGlass / LightMinimal / HighContrast),`SkinPackCatalog`(176 行)、CoreSettings.SelectedSkinId 持久化、设置 ComboBox、AOT 可绑定属性注册、双语标签——[7e48ad12](https://github.com/romin2026/DeskBox/commit/7e48ad1221965e4b2ec4cbfb6f375dff8794a091)、[2de0b503](https://github.com/romin2026/DeskBox/commit/2de0b503e591c312f703d6410e04d0c600ef6dd4)。
  - **缺陷修复(Win10 兼容)**:DesktopAcrylic 成功后停止叠加 legacy acrylic XAML tint([efd93655](https://github.com/romin2026/DeskBox/commit/efd9365557fd2df7e60f4ab443d4a1fb0e8980b5));TransparentTintBackdrop 激活时改画纯色表面填充([75814e81](https://github.com/romin2026/DeskBox/commit/75814e81feabc87da1f2f1a87efc37eb9d2b8be0))。
  - **缺陷修复(z-order)**:事件驱动桌面带 re-bed——WinEvent 钩子(EVENT_SYSTEM_FOREGROUND / OBJECT_SHOW / HIDE / DESTROY / REORDER)替代轮询,去抖 + VerifyDesktopPinnedBedding 验证 + 全程 SWP_NOACTIVATE,修"系统对话框(删除确认等)抬升 DefView band 把组件顶到所有应用之上"——[927a9231](https://github.com/romin2026/DeskBox/commit/927a9231a451f3b49f9234d6ec25e0b66b2940ec)(新增 `WidgetManager.DesktopBandEvents.cs` 306 行)。
- **实现方式**:SkinPack 走 AppSettings facade partials(Appearance/Feature)+ SettingsViewModel.AotBindableProperties(动了 AOT 门禁相关文件,且按门禁补注册);Win10 修复在 ContentWidgetWindow / StackPopover 背后材质路径;re-bed 改 WidgetManager.ZOrder(-82 行)换事件驱动。Strings 有 2 文件键值修订;AppSettings 加 SelectedSkinId(透传扩展);未动 native/。
- **对本仓库的价值**:【可直接借鉴】两个 Win10 材质修复贴着"Win10 21H2 兼容底线",近乎直接摘取;927a9231 的 WinEvent re-bed 与本仓库 `WidgetShowDesktopSelfHealService` 互补(覆盖"对话框抬升 band"这一日常失效路径),机制自包含带测试,按 `docs/architecture/[重要勿删]widget_zorder_lifecycle.md` 并入现有 self-heal 体系即可;SkinPack 为次要参考。

> **核对注(2026-10-24)**:材质两修经对当前代码核对**不成立**(controller 与 legacy tint 按构造互斥 `WidgetWindowBase.Backdrop.cs:107-111`;TransparentTintBackdrop 显式传色),已移出待办;re-bed 降级为**时效优化**待办(本仓库 5 秒轮询 bedding watchdog 约 10 秒自愈,`WidgetManager.ZOrder.cs:409-443`)。

### ForgetiExistence/DeskBox(★0,pushed 2026-09-23)——可直接借鉴

- **规模**:perf-lag-fixes 6 个净新增提交(= main,4 实作 + 2 CI),+1068/-13,仅 8 文件。
- **变更内容**:
  - **性能**:核心修复([c75f7306](https://github.com/ForgetiExistence/DeskBox/commit/c75f73067b7e240371615b02b95ebc9742a4bd2d),+206)——① `WidgetViewModel.ItemHydration` 用位置字典替换 `Items.IndexOf` 逐项循环,O(n²)→稳态 O(n);② `FolderWatcherService` 无法自描述的事件改置 BlindReloadRequested,避免每次 shell 失效都整表重建;③ 低级输入钩子安装期间拒绝 TrimWorkingSet(钩子回调在 OS 输入路径内,缺页会拖慢全机)。
  - **文档**:独立性能评审(root-cause-analysis.md + optimization-plan.md,814 行,[55c62a16](https://github.com/ForgetiExistence/DeskBox/commit/55c62a1675b69a9458570548e6c3b9a507f17b1c))。
- **实现方式**:三处修复相互独立、注释详尽、诚实处理边界(盲批次仍重建、防御性回退 IndexOf 路径);未动 Strings/schema/native。
- **对本仓库的价值**:【可直接借鉴】三处全部直击本仓库同源代码路径(大文件夹卡顿),改动小、论证清楚,可作为性能工作清单逐条评审移植;814 行性能评审文档本身就是可吸收资产。

> **核对注(2026-10-24)**:①O(n²) 经核对**确凿**(`WidgetViewModel.ItemHydration.cs:259/287`);②盲重载**部分存在**(仅 query watcher/错误/缓冲超限等 5 条兜底路径走全量,自描述事件有增量通道);③钩子期 Trim **部分存在**(4 处 Trim 均空闲门控,但常驻低级钩子期间仍会 trim 且无摘钩协调,风险窗口小于声明字面)。保留待办,以①为主。**①已于 2026-10-25 实施落地**(位置字典,见〇节完成状态)。

### zerovoxxx/DeskBox(★0,pushed 2026-10-16)——可直接借鉴

- **规模**:feat-custom 7 个净新增提交(含 2 个上游合并、1 个身份规范提交),+857/-116,29 文件。
- **变更内容**:
  - **缺陷修复**:Windows 登录时 shell 未就绪导致托盘菜单初始化失败([e0da46af](https://github.com/zerovoxxx/DeskBox/commit/e0da46af8532384f823f05b35d386cda87b0612d),新增 TrayContextMenuHostWindow + ToolWindowStyle 重试);快捷方式图标资源丢失恢复([13ee4de2](https://github.com/zerovoxxx/DeskBox/commit/13ee4de2a7caff114c676f7458681224b0bf7fc1),新增 ShortcutIconResourceReader 131 行 + IconHelper 重构,持久化提取 .lnk 图标资源)。
  - **工程化**:三个 fork 版本 release 记录(1.5.6.1/1.5.7.1/1.5.8.1)、commit 身份钩子。
- **实现方式**:小步提交、每个缺陷带测试(ShortcutIconResourceReaderTests 184 行、TrayMenuWindowStyleTests 50 行);未动 schema/Strings。
- **对本仓库的价值**:【可直接借鉴】两个修复都是真实用户可见缺陷且质量高:登录期托盘韧性可对齐本仓库 App.Tray 路径;.lnk 图标资源持久化可直接补强快捷方式图标路径。

> **核对注(2026-10-24)**:托盘登录韧性经核对**不成立**(本仓库已有 15×2s 图标重试 `App.Tray.cs:303-321` 与 flyout 重试+原生菜单降级 `:533-611`);.lnk 图标持久化降级为**可选增强**(提取失败已回退 Shell 默认帧 `IconHelper.cs:1251-1271`,白图标常态已消除)。

### nizarhamza/DeskBox_Fork(★0,pushed 2026-09-14)——可直接借鉴

- **规模**:feature_bucket-widget 单提交 0e7604b,+3739/-38,64 文件。
- **变更内容**:
  - **新增功能**:Bucket 组件——文件夹式玻璃格片,置顶优先 + 最近文件呈现、五种固定形状、拖放(Ctrl 复制)、独立 `Desktop Buckets\` 存储根、独立 per-widget JSON 存储、Explorer 拖放与逐文件菜单([0e7604b](https://github.com/nizarhamza/DeskBox_Fork/commit/0e7604bbaf66455c3a584819235e4c203bd74878))。
  - **关键互操作**:桌面图标格点吸附与图标避让——落定的 Bucket 四舍五入到系统图标网格并把被压住的图标推开的 SysListView32 互操作(DesktopIconLayoutService / DesktopIconAvoidanceCoordinator,以 LVS_AUTOARRANGE 门控避免与 Explorer 自动排列打架)。
  - **工程化**:12 语言全补、五个测试文件(表面/存储/形状/格点/避让)、契约测试更新;新 WidgetKind.Bucket 全管线接线。
- **实现方式**:标准内容窗口路径 + 独立存储根(不混入 File 组件托管存储);图标避让走 Win32Helper.DesktopIcons 互操作,关闭/移走/退出时还原图标位置。
- **对本仓库的价值**:【可直接借鉴】图标格点吸附/避让互操作与本仓库已有的 `DesktopIconGeometryService` 直接互补,是"组件落到桌面不压图标"的关键拼图;Bucket 本身也是可选的新组件候选(与 File 组件的边界处理克制)。

### dandostoja/DeskBox-LOCAL(★0,pushed 2026-10-04)——可直接借鉴

- **规模**:main 17 个净新增提交,+213/-44,11 文件;提交粒度细、单提交改动极小。
- **变更内容**:
  - **性能与工程化/隐私**:硬离线网络边界——`OfflineNetwork` 静态 transport(40 行,恒返本地 503、永不开 socket,测试可注入 handler,[b9e8b2f0](https://github.com/dandostoja/DeskBox-LOCAL/commit/b9e8b2f0b0a09a6e30860ba4f72c1bbb84e40a17)),随后把 updater、feedback、release notes、在线图片源、weather、URL 物化、WebDAV、GPS/IP 定位、glance 客户端、location 客户端逐个路由到该边界;移除直连 HttpClient 构造。
  - **工程化**:离线 x64 安装器审计 CI gate([84ee3924](https://github.com/dandostoja/DeskBox-LOCAL/commit/84ee3924d72830eb22da3b6a9ee0ac72ac1718e3))与 LOCAL_OFFLINE_BUILD.md。
- **实现方式**:集中改 `Services/` 传输构造点(OfflineNetwork.cs + 各服务装配处),无 schema 变更、无 Strings、无 native、无架构宿主改动。
- **对本仓库的价值**:【可直接借鉴】模式仅 40 行、零 schema/架构冲突,与"零售包全离线"哲学同构,既可作企业/隐私用户的离线模式产品选项,也可作为测试的确定性网络边界;按本仓库 SettingsService/facade 惯例包一层开关即可。

### lateautumn233/DeskBox(★0,pushed 2026-09-15)——有启发

- **规模**:三个分支为同一套工作的三次 rebase:search(10 个净新增,+9403/-23,75 文件,基点 1.4.3)、tmp(11 个,+9530/-2679,基点 1.5.2,多一个文件面板清理提交)、upstream(11 个,内容同源);作者全部 lateautumn233。
- **变更内容**:
  - **新增功能**:文件组件内分页(Page/Tab 管理:策略层 + TabBar UI + 布局策略)与跨页搜索——`WidgetPageSearchPolicy`(123 行)+ `WidgetPageSearchIndex`(145 行,配 425 行测试,[6411c8e6](https://github.com/lateautumn233/DeskBox/commit/6411c8e68568901c89bbb7834c5556059882594d));搜索入口、结果浮层与 TabBar 接线([8bef15b2](https://github.com/lateautumn233/DeskBox/commit/8bef15b21da85aa6071a0e7f1f724d24986af4ee));缩写/Pinyin 首字母匹配 + SearchHighlighter 高亮([8fb8a10a](https://github.com/lateautumn233/DeskBox/commit/8fb8a10ac49ac0ed8454e4c4264bca92c63fbea1));搜索激活/关闭生命周期处理([153f2381](https://github.com/lateautumn233/DeskBox/commit/153f23814428704c3f74095f16c093dc242bac10))。
  - **缺陷修复(性能)**:切换页面时避免图标缓存被清除([d46f11a8](https://github.com/lateautumn233/DeskBox/commit/d46f11a8633c4ffe9dfbfe3bbe07b49a5f2460b2))。
  - **文档翻译**:12 语言搜索键一次补齐([bca55167](https://github.com/lateautumn233/DeskBox/commit/bca5516722e7acb3d7b721a6e3baf2aab9aa2a24)),符合全键覆盖惯例。
- **实现方式**:新增 FileSurfaceContent.Pages / .Search partial、WidgetPageTabBar、WidgetPageSearchResults 控件,AppSettings 增分页/搜索切片;Policy/Index/Matcher 均为纯托管可测试类;未动 native/、未动 AOT 门禁文件。
- **对本仓库的价值**:【有启发】"组件内分页 + 跨页搜索"是 upstream 没有的组件能力,整体接入需按 ContentWidgetWindow 当前 partial 结构重做接线;但 WidgetPageSearchPolicy/Index、SearchHighlighter、缩写匹配器几乎不依赖 UI,接近直接移植,且测试齐备。

### bupocc/DeskBox(★0,pushed 2026-09-09)——可直接借鉴

- **规模**:develop 10 个净新增提交(7 实作 + 3 合并;feature_webdav 9 / feature_tomato-clock 7 为其子集),实作合计约 +6.8k/-0.9k、约 90 个文件。分支级 diffstat 因合并不在当前 upstream main 上的上游历史(插件化系列)虚大到 373 文件,以自有提交为准。
- **变更内容**:
  - **新增功能**:番茄钟组件([47dfd480](https://github.com/bupocc/DeskBox/commit/47dfd4802362566a967491fb2bf586f66e97733a),+2420)与阶段/提醒完善([1df8bff3](https://github.com/bupocc/DeskBox/commit/1df8bff3c558b777155ef4b6511a93e1ecaa5b1d),+2782);WebDAV 备份同步与冲突解决([cd24f3f2](https://github.com/bupocc/DeskBox/commit/cd24f3f25b394e008315ef970450ab6bc5412e81),+661)。
  - **UI·UX**:组件窗口 z-order 控制细化——持久"总在最前"开关 + 分组同步([23b7c0b9](https://github.com/bupocc/DeskBox/commit/23b7c0b9f3e1a4b1ea11bbcd67b64092061b22b1),+551,触及 WidgetLayerService / WidgetManager.ZOrder 等勿删文档覆盖区域)。
- **实现方式**:番茄钟完整走共享内容窗口路径(PomodoroWidgetContent/Adapter/ContentProvider、WidgetRegistry、WidgetContentFactory、独立状态机与 ViewModel,附状态机/集成/ViewModel 三层测试);12 语言 JSON 每次同步补齐;WebDAV 密码存 Windows PasswordVault(不入 AppSettings),但向 AppSettings 追加 4 个字段未做 schema 版本迁移(纯增量字段,风险低但不合本仓库约定);z-order 改动带 WidgetAlwaysOnTopContractTests 回归契约测试。
- **对本仓库的价值**:【可直接借鉴】番茄钟是"按本仓库架构新增 WidgetKind"的最完整外部范本(注册/工厂/12 语言/测试全齐);WebDAV 备份 + PasswordVault 方向可直接评审复用;z-order 持久化与本仓库自愈/层策略工作有重叠,合入前需对齐勿删文档语义。

> **核对注(2026-10-24)**:番茄钟经核对为本仓库**真空缺**(无任何计时类组件),已**立项实施**(纯增量,按标准 kind 接线面重做,三层测试 + 13 语言);WebDAV 备份与持久置顶维持原判定留档。

### bbkien2312/DeskBox-mrKBB(★0,pushed 2026-08-30)——有启发

- **规模**:main 46 个净新增提交,全部 bbkien2312,+7588/-324,86 文件。
- **变更内容**:
  - **新增功能**:截图捕获强化三连——区域捕获([49d306e9](https://github.com/bbkien2312/DeskBox-mrKBB/commit/49d306e965eb525123980a502aeefadff9886e20))、无遮挡选中窗口捕获(新增 `SelectedWindowScreenshotService` 268 行,[ad4480c5](https://github.com/bbkien2312/DeskBox-mrKBB/commit/ad4480c5ae2090aca9a3ef6a5235e01d2bb93555))、Ctrl+C 复制;新建 `ThumbnailDiskCache` 缩略图磁盘缓存(238 行,[414c7719](https://github.com/bbkien2312/DeskBox-mrKBB/commit/414c7719b88a1aecdb7bbf8fcd1f6bd0151e0d64));fork 自建更新通道(fork build 号、更新 manifest 发布管线、GitHub asset 上传脚本)。
  - **缺陷修复**:大文件组件图标被卸载导致闪烁(修正 ItemHydration 生命周期,[5ef7dbcf](https://github.com/bbkien2312/DeskBox-mrKBB/commit/5ef7dbcff54f393aad29e3f04afcd8682ffe8cb7))、Windows 运行时安装加固、release 管线若干修复。
  - **文档翻译**:自加第 13 个语言文件 `Strings/vi-VN.json`(2342 行)——但 zh-TW 未同步新增键,键覆盖不齐。
- **实现方式**:全部在托管层:IconHelper/Win32Helper、WidgetViewModel.ItemHydration(图标水合与内存)、ScreenshotCaptureWindow、AppSettings(fork 更新设置);未动 native/、未动组件宿主架构。
- **对本仓库的价值**:【有启发】ThumbnailDiskCache + 图标水合修正对"大文件组件内存/图标闪烁"有直接参考价值(本仓库尚无磁盘缓存方案);截图增强自包含可摘;fork 自建更新通道与本仓库 direct-release updater 冲突,仅了解;第 13 语言引入方式(键覆盖不齐)违反本仓库约定。

> **核对注(2026-10-24)**:截图增强经核对本仓库**真空缺**,接触面中到大——**已立项(谨慎实施)**:先出 System.Drawing → WIC/SoftwareBitmap 的 AOT 替换设计评审再动码;新增 NuGet(Vortice)走双 lock + WMC1510 审计;下游可复用 QuickCapture 图像读取线。

### wanvfx/DeskBox(★0,pushed 2026-08-30)——有启发

- **规模**:main 18 个净新增提交,全部 Zoyayayayaya,+8904/-40,85 文件。
- **变更内容**:
  - **新增功能**:本地 Command API——命名管道 JSON-RPC 服务端 + `DeskBox.Cli` 命令行入口 + MCP server,覆盖 todo / quick-capture / file / 生命周期 / search / groups / organize / settings / music / weather / glance 九组命令——[bbafa465](https://github.com/wanvfx/DeskBox/commit/bbafa465d3c2eb09fc03f2a3953b57d66164a6f8)、[6ddeabba](https://github.com/wanvfx/DeskBox/commit/6ddeabba73ccb35b86bf99638cea0f5fa2026354)、[69da7ff9](https://github.com/wanvfx/DeskBox/commit/69da7ff9966bc46e55f9d27a15d92c4b4817d3a0)。
  - **UI·UX / 安全**:设置面板新增 Command API 区段;AppSettings 增 EnableCommandApi / CommandApiReadOnly / AllowDestructiveCommands 三开关,管道限当前用户、调用写审计日志。
  - **性能与工程化**:JSON 序列化基线冻结到 44 个 context;自行定位并修复 WeatherJsonContext 命名冲突导致的 STJ 源生成阻塞(P0);新建 DeskBox.Cli / DeskBox.Protocol 项目并入 sln;完整变更日志与已知问题登记。
- **实现方式**:纯托管实现,未动 native/;新增 `Services/CommandApi`(Dispatcher/Registry/Handlers)经 WidgetManager 暴露组件面;AppSettings 三属性为透传扩展;Strings 12 语言同步了 Command API 区段键(合规);动了 AOT 阶段契约测试计数(按门禁流程重钉)。
- **对本仓库的价值**:【有启发】"AI 可控命令面 + 审计 + 只读/破坏性分级"是 upstream 完全没有的方向,工程质量高(协议测试 550+ 行);但引入两个新项目、命名管道服务与安全面,移植必须按本仓库 pluginization / `DeskBox.Abstractions` 契约与 AOT 源生成门禁重做,不能直接搬。

### agogo233/DeskBox(★0,pushed 2026-10-19)——有启发

- **规模**:main 51 个净新增提交(42x agogo233 + 9x 维护者侧,+30898/-770,337 文件);feat/shortcut-outside-desktop 1 个(+641/-24,29 文件)。整线合并了 upstream 1.5.6/1.5.8 train。
- **变更内容**:
  - **新增功能**:文件拖入格子默认创建"桌面外快捷方式"(ShortcutOutsideDesktop,带设置项 + 12 语言键 + 双策略测试)——[0ab1d8fd](https://github.com/agogo233/DeskBox/commit/0ab1d8fdadc6b84e1ec8ac54a26459b7035158d1)、[8279e3e3](https://github.com/agogo233/DeskBox/commit/8279e3e3514c20497cdb7c743a70fea7e375f07d)。
  - **缺陷修复**:桌面拖入格子偶发无响应与空包掩盖([2952c9c1](https://github.com/agogo233/DeskBox/commit/2952c9c1b62f3bc434cc76bb4fd5fe7a0d09e102),动 `ContentWidgetWindow.NativeDragDrop` / `DeskBoxDragData`);文件面板多选拖动排序(新增 `ReorderDropIndexCalculator` + 111 行测试,[a25612af](https://github.com/agogo233/DeskBox/commit/a25612af9c54e3f6e08359ce7b791db5c22853f4));启动时组件抬升沉到桌面绝对底层(动 `WidgetManager.ZOrder` / `WidgetTemporaryRaiseLeasePolicy`,[5c3a767f](https://github.com/agogo233/DeskBox/commit/5c3a767f0af2b8515bd6bc99e68bc581125f55af));合并后 AOT 兼容的 Open With 枚举修复([b9332b26](https://github.com/agogo233/DeskBox/commit/b9332b26c47fb55bb998be274d2ace5a45c8f1f6))。
  - **性能与工程化**:自建便携版 x64 CI;大量 merge + 门禁对齐提交(7 locale 键集对齐、AOT stage 重钉、**native capability mask 钉到 1023,已偏离 upstream 冻结 ABI 511**)。
  - **维护者侧(Simon)**:备份投影 AOT 安全源生成序列化、MusicSettingsStore 序列化基线棘轮、Abstractions chrome-mode 别名、AOT 审计 stage 清单对齐。
- **实现方式**:整条线是跟随 upstream 1.5.6/1.5.8 及 dev-1.6(pluginization、GlancePackage、Abstractions、native plugin_signature 能力位 ed25519)的长期混合树;自身功能集中在 DragDrop / FileSurfaceContent / 设置 facade;动了 12 语言 Strings(净增键,合规);未新开 WidgetContent 路径外的宿主。
- **对本仓库的价值**:【有启发】shortcut-outside-desktop 默认值、拖放空包掩盖修复与多选重排计算器思路可摘取,但整线基于 dev-1.6 混合树无法整体合并;启动沉底修复与本仓库 z-order 体系(`WidgetShowDesktopSelfHealService` 等)重叠,移植前需按 z-order 生命周期文档合并设计。

> **核对注(2026-10-24,2026-10-25 更新)**:三修已逐一核对——拖入无响应/空包掩盖**病灶存在,已立项,已于 2026-10-25 实施落地**(适配合并后实树,见〇节立项表完成状态);启动抬升沉底**病灶存在(默认 Dynamic 模式),已立项,已于 2026-10-25 实施落地**(绝对沉底策略位 + 启动刷新守卫,见〇节立项表完成状态);多选重排**已被本仓库 9e414687 等价解决**,无需移植,仅遗留"单选前向拖放疑似落点偏后一格"疑点待运行时验证。

### wrust0608/DeskBox(★0,pushed 2026-10-03)——有启发

- **规模**:feat_vi-vn-localization 13 个净新增提交,全部 wrust0608,+6088/-1059,59 文件。
- **变更内容**:
  - **文档翻译**:越南语全量本地化——新增 `Strings/vi-VN.json`(2679 行)并把 12 语言契约测试改为 13 语言([9f7919d5](https://github.com/wrust0608/DeskBox/commit/9f7919d5ff45ce4ac6a2ca875c82c85be921c0c5)、[693ddc52](https://github.com/wrust0608/DeskBox/commit/693ddc52b52444b9baecc648ade6bca41b1922ec));配套 Inno Setup `Vietnamese.isl`(403 行,[0b2e854e](https://github.com/wrust0608/DeskBox/commit/0b2e854edd12fddbc62a64722f469e1df70a0846));术语校准文档、安装预览验证记录。
  - **缺陷修复**:Windows 11 build 26200 启动时托盘 ContextFlyout 抛 E_NOTIMPL 崩溃——try/catch 降级 ContextMenuMode.PopupMenu([c6dbed3a](https://github.com/wrust0608/DeskBox/commit/c6dbed3ab70b236edd74a72d539fe94e84e12020),9 行)。
  - **性能与工程化**:tools/localization-audit(硬编码字符串扫描 + 本地化清单审计脚本)。
  - (另有品牌替换/支持 QR 一类私有化改动,不适用本仓库。)
- **实现方式**:LocalizationService / SettingsService / WeatherCodeMapper / Package.appxmanifest / Resources.resw 增补 vi-VN;SettingsViewModel.AotBindableProperties 按 AOT 门禁补属性;未动 native/、未动组件架构。
- **对本仓库的价值**:【有启发】本地化工程做法正规(键覆盖校准 + 安装器 + 审计工具,audit 脚本可直接搬),但"第 13 语言"与"12 个 JSON 键/占位符全覆盖一致"的冻结约束冲突,是否扩语言是产品决策;c6dbed3a 托盘崩溃修复是 Win11 新 build 上的真实崩溃,可近乎直接摘取。

> **核对注(2026-10-24)**:该项经核对**缩水**——本仓库托盘菜单走 SecondWindow 模式且赋值期 E_NOTIMPL 已有重试+两级降级(`TrayContextMenuModePolicy`),真实缺口仅剩 show 期 E_NOTIMPL 落泛化 catch(菜单静默失效、不崩溃);26200 上的崩溃形态需真机复现。待办保留,工作量很小。

### joeshu/DeskBox(★0,pushed 2026-09-20)——有启发

- **规模**:codex_stage7b-arm64-actions 12 个净新增提交,全部 joeshu,+1963/-2000,16 文件;main 与 codex_lean-file-widget-defaults 各 1(同一 lean defaults 提交,后者为维护者侧镜像)。
- **变更内容**:
  - **性能与工程化**:lean file widget 默认值——调低 FileWidget/Performance/WidgetShell 设置切片的默认档位(性能优先出厂默认,[b23dccd1](https://github.com/joeshu/DeskBox/commit/b23dccd1f628783178f08665853c29a093521bd0),同步 PerformanceSettingsPolicy 与基线测试);fork 分发 CI(x64 only)。
  - **AOT 审计放松(负面样本)**:publish-aot-audit.ps1 改为"警告数减少也算通过"([2bb18c63](https://github.com/joeshu/DeskBox/commit/2bb18c63e82cd3e99fd0934b2e438262f9a09e06)),retail publish 允许已知 AOT 分析警告。
- **实现方式**:SettingsSlice 三处默认值 + PerformanceSettingsPolicy + SettingsService;其余为 CI/审计脚本;未动 Strings、native、组件宿主。
- **对本仓库的价值**:【有启发】"lean 默认值"是低成本的出厂性能档思路,但本仓库设置切片有契约基线测试,改默认值须走基线重钉;其 AOT 审计放松方向与本仓库"无 WMC1510 断言会静默漂移"的红线相反,明确不借鉴。

### Ci-Tu/DeskBox(★0,pushed 2026-10-14)——仅了解

- **规模**:main 16 个净新增提交,全部 Ci-Tu,+727/-92,43 文件。fork 已于 2026-10-14 自行封存(FORK-ARCHIVED.md)。
- **变更内容**:**新增功能**:搜索热键加"双击 Ctrl"预设并保留给搜索([97e73fc9](https://github.com/Ci-Tu/DeskBox/commit/97e73fc90cf24dd8b5b0739ffe196deec6f8184b),动 GlobalHotkeyGesture / SearchHotkeyService / SettingsService);搜索弹窗在其它窗口夺取激活时自动隐藏([f2a65cac](https://github.com/Ci-Tu/DeskBox/commit/f2a65cac292d51a509f5bc8f98e07b6274431831),SearchPopupWindow 58 行)。**工程化**:补丁随 upstream 演进的重接线——双 Ctrl 预设改走 ISearchSettings 契约 + SettingsCoordinator([632afcbc](https://github.com/Ci-Tu/DeskBox/commit/632afcbecfe583e67eb07d01af2ad257dfd4df12))。
- **实现方式**:托管层小改:热键模型、搜索设置切片/协调器、SearchPopupWindow 激活处理;Strings 12 语言 +24/-12;AppSettings 有触及(设置契约);未动 native/ 与组件宿主。
- **对本仓库的价值**:【仅了解】双击 Ctrl 预设与弹窗失活隐藏**均已被 upstream main 吸收**(GlobalHotkeyGesture.DoubleControl 与 SearchPopupWindow 的 Deactivated 处理现已在 upstream),残余价值仅是其"补丁随上游演进改契约测试"的维护姿势。

### Lurbing7/DeskBox(★0,pushed 2026-10-07)——仅了解(维护者侧镜像)

- **规模**:codex_tags-card 15 个净新增提交(14x 维护者侧 Simon + 1x Lurbing7,+17733/-200,181 文件);main 1 个(.dsh AI 维护文件)。
- **变更内容**:
  - **维护者侧实验线**:Dock 组件里程碑 + 侧边纵向布局([77169158](https://github.com/Lurbing7/DeskBox/commit/771691587280108b75b4e3cc2976dae05524d146),单提交 8196 行);微信/QQ 通知注意力恢复(preview.2,[ab7ae21d](https://github.com/Lurbing7/DeskBox/commit/ab7ae21d742d90d7970d695da457afbf7dd46b7d));双历时间与监控缩放优化(preview.3,[41444b66](https://github.com/Lurbing7/DeskBox/commit/41444b6635bc0392345f85fc49f6a7d794d5cdfe));1.6.0-preview.1~3 发布三连。
  - **外部贡献(Lurbing7 本人)**:仅 `.dsh/skills/deskbox` AI 维护者文件布局([f9c2a628](https://github.com/Lurbing7/DeskBox/commit/f9c2a62885196ca92407518eb084c2c9d7e8f315))。
- **实现方式**:Dock 经 WidgetRegistry / WidgetContentFactory 注册,走共享内容窗口路径(架构合规);SystemMonitor 传感器经独立 `DeskBox.SensorHost` 子进程(LibreHardwareMonitorLib,非 AOT、带许可证清单)供数;Strings 12 语言 +1296 行(合规);未动 native/ Rust。
- **对本仓库的价值**:【仅了解】对 fork 网络而言这不是外部贡献,而是维护者侧 Dock/监控实验线的外部镜像;若该线确属本仓库规划,其 SensorHost 独立进程供数的思路可作为 planned SystemMonitor placeholder 的实现参考(规避 AOT 与驱动依赖进主进程)。

### STMfan/DeskBox-STMFeatured(★0,pushed 2026-10-15)——有启发

- **规模**:main 9 个净新增提交,+2977/-74,80 文件。
- **变更内容**:
  - **新增功能**:AI 格子(AiWidgetContent,经内容窗口路径注册)+ 设置页 AI 区块([17a910fe](https://github.com/STMfan/DeskBox-STMFeatured/commit/17a910fed0efa993fb712f5bbb9f810a78e4a9af));音乐组件音频频谱(WinRT loopback 采集 AudioLoopbackCapture + AudioSpectrumView)与自适应布局。
  - **性能与工程化**:补齐频谱控件 WinRT AOT 源生成声明([aceee2e0](https://github.com/STMfan/DeskBox-STMFeatured/commit/aceee2e0ef058e596cfa194218062ff1faff09c9)),同步更新 9 个 AotStage 契约测试计数。
  - **UI·UX**:AI 格子保存反馈与回车发送修复([b0bee14c](https://github.com/STMfan/DeskBox-STMFeatured/commit/b0bee14cd5048f5ce4918944752828ecd489d829))。
- **实现方式**:AI 调用采用**子进程 harness**模式——spawn `dsh.exe`(默认 DeepSeek),请求经 stdin JSON(源生成 `JsonSerializerContext`,AOT 安全)、事件经 stdout 行协议,密钥只进环境变量,5 分钟超时 + 整树杀进程;对话状态独立成 AiConversation。12 语言 JSON 全覆盖;AppSettings 未动(选项存 WidgetConfig)。
- **对本仓库的价值**:【有启发】"子进程 harness + 行协议 + 环境变量传密钥"是 AOT 应用集成 LLM 的一条干净路线(规避 AOT HTTP/JSON 与密钥落盘问题),对规划中的 AI 方向有直接参考价值;频谱采集实现亦可参考。

### zjzonline/DeskBox-Themes(★0,pushed 2026-09-09)——有启发

- **规模**:deskbox-themes 8 个净新增提交 + main 1(社区文档),themes 分支 +3360/-704(61 文件)。
- **变更内容**:
  - **新增功能**:数据式主题引擎([83772afd](https://github.com/zjzonline/DeskBox-Themes/commit/83772afd0148f71f748c6ff9733752ace3704bc8),+ThemePackService 370 行、ThemePack 模型、themes/schema-v1.json、alpine-mist 与 smoke-glass 两个示例主题);双语主题市场页([2c6fe290](https://github.com/zjzonline/DeskBox-Themes/commit/2c6fe290f47458685bda6600b4f30b463bd65a00));搜索面板着色与文件夹控件([131684a5](https://github.com/zjzonline/DeskBox-Themes/commit/131684a52a32238e0ba6aa94b3d92476f48828e3))。
  - **文档**:themes/README、docs/themes/ARCHITECTURE.md、README 重构为社区入口。
- **实现方式**:主题是纯 JSON 数据包(schemaVersion 校验 + minimumDeskBoxVersion 门控),ThemePackService 加载并投影到 WidgetShell / ContentWidgetWindow / QuickCapture / SearchPopup / WidgetWindowBase.Backdrop 等材质层;AppSettings 仅 +6、12 语言 JSON 各 +6;带 ThemePackServiceTests(144 行)。
- **对本仓库的价值**:【有启发】"纯数据主题包 + schema 版本化"与本仓库 AppSettings 冻结线、AOT 约束高度兼容,是 skins/主题方向可直接评审的底稿;但其触及 WidgetWindowBase 边界/背景路径,合入需对齐勿删的 z-order/生命周期语义。

### ScyPolit/DeskBox_Personalized(★1,pushed 2026-10-13)——有启发

- **规模**:main 7 个净新增提交(5x 维护者侧 Simon + 2x ScyPolit;维护者部分本仓库当前没有),+4664/-118,89 文件。
- **变更内容**:
  - **新增功能**:滴答清单(TodoQingdan)待办桥接([8fcdd69a](https://github.com/ScyPolit/DeskBox_Personalized/commit/8fcdd69abd9b282fe6586f06d3d3dadb9fc16113))——桥接进程 + 协议程序集 + Todo 组件 UI 集成。
  - **工程化**:分发工作流、ARM64/Native AOT 锁文件还原修复、兼容性探针脚本(js,331 行)+ PowerShell 门禁([25ca2d65](https://github.com/ScyPolit/DeskBox_Personalized/commit/25ca2d65c388b35e8e348c7a5353834e8f14c866))。
  - **缺陷修复**:合并上游 1.5.7 并修复 Todo 清单同步([755d0fc8](https://github.com/ScyPolit/DeskBox_Personalized/commit/755d0fc87bed841ca443de4593887c2ebc396bae))。
- **实现方式**:桥接是**独立 AOT 进程**(src/DeskBox.TodoQingdan.Bridge/Program.cs,221 行)+ 独立 Protocol 程序集,主应用经 Contracts 接口(ITodoIntegrationSettings)解耦;附 4 个测试文件(约 700 行)。
- **对本仓库的价值**:【有启发】与外部 SaaS(滴答清单)集成采用进程外桥接 + 协议程序集 + 兼容性探针,隔离面干净、可失败可降级,是"云同步/第三方账号"类功能的架构参考;本仓库暂无对应路线。

> **核对注(2026-10-24)**:经核对**真空缺**,维护者决策为**不采用**(无接入第三方服务的产品计划);桥的架构形态(独立 AOT 桥进程 + 协议程序集 + 本地库读取)留作未来第三方集成的参考范式,多进程先例(thumbnail proxy/Updater)同样可复用。

### wuya11111/DeskBox-Agent(★0,pushed 2026-09-02)——有启发

- **规模**:main 6 个净新增提交,+4258/-47,63 文件。
- **变更内容**:
  - **新增功能**:DeskBox Agent + MCP 控制([06d672b2](https://github.com/wuya11111/DeskBox-Agent/commit/06d672b2a700b852e0520224f0442ed10e186cc))——新增 src/DeskBox.Mcp AOT 项目(MCP server,Program.cs 459 行)、AgentPipeServer(命名管道)、AgentCommandService(1567 行,布局/待办/桌面整理命令面)、WidgetManager 布局存储扩展;透明日历材质模式([c9a8fd8a](https://github.com/wuya11111/DeskBox-Agent/commit/c9a8fd8a1a9a44e4b384420d75ffe9e68cd9abbb))。
  - **工程化**:中文 Agent 与 MCP 双安装器、发布管线与管道测试脚本、AgentCommandContractTests。
- **实现方式**:外部代理经 MCP/命名管道下发结构化命令,应用侧以命令模型 + 服务执行(桌面整理走既有 Coordinator/Transaction);Strings 各 +1,AOT 契约测试同步。
- **对本仓库的价值**:【有启发】与仓库插件化路线图方向一致(能力端口、命令面),其命令模型分层与 MCP server 形态值得对照评审;但 1.5k 行的 AgentCommandService 需要按本仓库门禁重新切块,不宜直接搬。

### Zagadka-3906/DeskBox(★0,pushed 2026-09-25)——有启发(维护者侧)

- **规模**:feature_dorm-electricity-widget 8 个净新增提交(**全部维护者身份,本仓库当前没有**),+3518/-363,52 文件。
- **变更内容**:
  - **新增功能**:宿舍电费组件([d040fe63](https://github.com/Zagadka-3906/DeskBox/commit/d040fe6394e104210e568339e65db26a1b3df2a1))——集中式宿舍电网查询、独立历史周期([88ab8e40](https://github.com/Zagadka-3906/DeskBox/commit/88ab8e40a45296d8377d725ef19afadd793a1228))、响应式历史视图、Lihu 二期支持;ServerChan 推送通知(每日余额提醒,[24354c9b](https://github.com/Zagadka-3906/DeskBox/commit/24354c9baa3cf22f63343e40da04d3169046eca5))。
  - **缺陷修复**:换房间后陈旧数据清理([36660cee](https://github.com/Zagadka-3906/DeskBox/commit/36660ceef75424291e9442fdbfa0d5547f243b8e))。
- **实现方式**:完整走内容窗口路径(FeatureWidgetKindContractTests 同步),DormElectricitySettingsSlice + 220 式透传(AppSettings +34),LihuElectricityService(359 行,Web 抓取)+ ServerChanService(60 行)+ 通知调度,附测试;12 语言 JSON 各 +68。
- **对本仓库的价值**:【有启发】内容为校园垂直场景,泛化价值有限;但 ServerChan 轻量推送通道与"查询型数据源 + 通知调度"的组织方式可复用到天气/系统监控类组件。

### QingLanLay/DeskBox(★0,pushed 2026-10-23)——有启发(维护者侧)

- **规模**:main 4 + codex_backup-our-fixes-20261023 3(为 main 子集)+ codex_rounded-glass-material-settings 1,合计 5 个净新增提交(**全部维护者身份,本仓库当前没有**),main 分支 +3933/-345,86 文件(含一次 1.5.9 合并)。
- **变更内容**:
  - **UI·UX**:圆角玻璃材质外观设置([5e8d5eda](https://github.com/QingLanLay/DeskBox/commit/5e8d5eda43b962edecfcb8b66aa7e6ac48bc162b))——玻璃材质 + 大/小圆角/直角档位,含 Windows 10 降级说明。
  - **缺陷修复**:桌面恢复与窗口层级修复 + 玻璃材质完善([63452abc](https://github.com/QingLanLay/DeskBox/commit/63452abcf28a9db179dfd80656da020633005ac3),+3159,含恢复看门狗);固定桌面格子始终保持在应用窗口后方([ef56d837](https://github.com/QingLanLay/DeskBox/commit/ef56d837869f31109d361f90d334aebd4383c804),+242);点击桌面后清除文件选中与焦点边框([c3cd1d00](https://github.com/QingLanLay/DeskBox/commit/c3cd1d009753ec24d4efbdcd8e689d13ea5e8a32),+282)。
- **实现方式**:WidgetShell.Material + WindowCornerRegion + WidgetMaterialVisualCalculator 扩展材质系统;桌面恢复线新增 DesktopShellRecoveryWatchdog / OwnedWindowLayerSafety / WidgetZOrderRestore 等大量契约与原生层测试(约 1500 行测试);SettingsSlice 与 12 语言同步。
- **对本仓库的价值**:【有启发】内容质量高但属维护者侧平行工作——本仓库尚无这些提交;玻璃材质与圆角档位、点击桌面取消选中若要落本仓库,应直接从这些提交摘取而不是重新发明。

> **核对注(2026-10-24)**:玻璃材质与圆角档位经核对**本仓库已有**(材质档位 Acrylic/AcrylicBase/Solid+Mica,`AppearanceSettingsViewModel.cs:166-170`;圆角三档 Square/Small/Round,`SettingsService.cs:83-85`),轮询版 bedding watchdog 也已有——本节主体**移出待办**;"点击桌面清除选中"子项未核对,如需另行评估。

### GIT-LINCC/DeskBox(★1,pushed 2026-09-28)——有启发(维护者侧)

- **规模**:main 单提交 cd763216(**维护者身份,本仓库当前没有**),+42052/-34304,43 文件;其中 12 个 Strings JSON 全文重写占 diff 大头,真正新代码约 +6.5k。
- **变更内容**:
  - **新增功能**:AI 桌面整理 + 独立 AI 桌面布局设计器([cd763216](https://github.com/GIT-LINCC/DeskBox/commit/cd76321691b82e12d94dcdba41869480f0876c8e))——DesktopOrganizationAiService(1137 + 流式/草稿 473)、DesktopLayoutDesignService(1314)、DesktopOrganizationDeepInspector(564)、DesktopOrganizationTaskView.Ai(848)、DesktopLayoutDesignerWindow(438+691)、AI 模型层(445)与设置区块。
  - **工程化**:AppSettings 以 slice + 透传惯例 +26(非密钥偏好);附 AiService 测试与切片契约更新。
  - **文案**:12 语言 JSON 同步改写(措辞简化、删除部分 InfoTip 键)——属破坏性文案改动,合入需逐键评审。
- **实现方式**:AI 服务输出结构化布局方案,经 DeepInspector 校验桌面现状、Coordinator/Transaction 以事务方式应用,设计器窗口独立预览;provider/baseUrl/model/自定义提示词在设置中配置。
- **对本仓库的价值**:【有启发】作为"AI 布局设计器"方向的原型记录有价值(事务化应用 + 校验回环是好设计);但文案破坏性改写与单提交巨石形态使其只能按主题摘取,不能整体合入。

> **核对注(2026-10-24)**:维护者决策为**放弃**该实验线;仅当未来重启 AI 方向时按主题(事务化应用+校验回环)回顾本提交。

### PepinoLatte/DeskBox(★0,pushed 2026-09-27)——可直接借鉴

- **规模**:main 单提交 30934a7,+502/-13,23 文件。
- **变更内容**:**新增功能**:文件格子按格子单独隐藏图标文字([30934a7](https://github.com/PepinoLatte/DeskBox/commit/30934a7b6e760e551a9c0a255c54a7aa6d54bb77))——WidgetConfig.IconLabelHiddenOverride 可空覆盖(WhenWritingNull,仅偏离全局时写盘),有效行数解析统一(FileWidgetIconLayout),叠放弹窗与整理预览卡同步生效。**工程化**:iconLabelHiddenOverride 进入格子样式备份白名单随云备份同步;7 例新测试 + 切片所有权与设置卡计数契约登记。
- **实现方式**:严格遵循仓库既有约定——可空覆盖、12 语言各 +6、右键菜单接入、云备份投影同步。
- **对本仓库的价值**:【可直接借鉴】小而规范的单格覆盖特性,约定遵守度堪称 fork 样板,可直接评审合入。

> **核对注(2026-10-24)**:per-tile 开关缺失经核对属实(`FileWidgetIconLayout.cs:40` 仅有全局"文件名行数"开关,无逐格覆盖);维护者先立项、后转入待办池第一档排期。

### Pictus08/DeskBox(★0,pushed 2026-10-06)——可直接借鉴

- **规模**:feat_widget-colors-clean-desktop 单提交 483094d,+1034/-16,42 文件。
- **变更内容**:
  - **新增功能**:每格子独立背景色/透明度 + 共享十色历史 + 右键背景入口;清爽桌面模式——一键同时隐藏组件与桌面图标,带恢复路径([483094d](https://github.com/Pictus08/DeskBox/commit/483094df0cc95221c3cedb0208766062faeef879))。
  - **工程化**:CleanDesktopIconSession / DesktopIconVisibilityBackend / WidgetManager.CleanDesktop,附针对性测试;LOCAL-CUSTOMIZATIONS.md 记录 fork 差异。
- **实现方式**:背景设置进 WidgetShellSettingsSlice(AppSettings +4),清理桌面经桌面图标可见性后端 + 会话恢复;12 语言 JSON 各 +4,契约测试同步。
- **对本仓库的价值**:【可直接借鉴】"每格子背景覆盖"与"组件+桌面图标一键隐藏/恢复"都是高频需求点,实现遵循本仓库约定且改动可控,可作为外观与桌面管理线的候选特性整体评审。

> **核对注(2026-10-24)**:维护者决策为**不采用**;技术判定保留作存档。

### luziyi123448-gif/DeskBox(★0,pushed 2026-10-06)——有启发(维护者侧)

- **规模**:codex_topology-appearance-upstream-20261007 单提交 6414a50(**维护者身份,本仓库当前没有**),+317/-30,13 文件。
- **变更内容**:**新增功能**:文件图标尺寸按显示器拓扑独立记忆([6414a50](https://github.com/luziyi123448-gif/DeskBox/commit/6414a50c39095d8eadf1c95ebd4d6c1694221170))——WidgetTopologyAppearanceService 按(分辨率×缩放)拓扑保存与恢复图标尺寸档位,附 docs/architecture/topology-appearance.md。
- **实现方式**:扩展 WidgetTopologyLayoutService 与布局切片(AppSettings +3),契约测试同步,含 134 行新测试。
- **对本仓库的价值**:【有启发】多显示器拓扑差异化外观是合理规划点;实现薄且规范,若本仓库需要可按此方向扩展。

### zhonghengchen/DeskBox(★0,pushed 2026-09-03)——有启发(点子级)

- **规模**:main 4 个净新增提交,+66/-4,8 文件。
- **变更内容**:**新增功能**:独立日历组件——新增 WidgetKind.Calendar,经 CalendarWidgetContentProvider 复用 Glance 的日历呈现面([389a5b6e](https://github.com/zhonghengchen/DeskBox/commit/389a5b6e37101ceed41ef4ca6af12bff719339b9)),注册进 FeatureWidgetSettings 与创建/工厂路径。
- **实现方式**:方向正确(复用而非复制 Glance 内容),但改动薄:Strings 只补了 en-US/zh-CN 两个文件(2/12,违反全覆盖约定),无测试。
- **对本仓库的价值**:【有启发】"日历脱离 Glance 独立成格"是合理的组件规划点子,值得进入本仓库组件路线讨论;实现需按 12 语言 + 契约测试标准重做。

> **核对注(2026-10-24)**:经核对**部分存在**——本仓库日历呈现已抽象(`ICalendarPresentationSource` → `GlanceWidgetContentAdapter(calendarSource:)`),fork 方案近即插即用、接触面最小(纯日历面拆离则升级为中);维护者决策为**不采用**(Glance 内日历已够用);若未来重启,须补齐 fork 缺失的 12 语言与测试。

### timeclockouo/DeskBox(★0,pushed 2026-09-08)——仅了解

- **规模**:main 4 + icon-fix-150 3 + patch-1 1 + patch-2 1(主题高度重复,实为两件事的反复迭代),每次 1 文件、合计约 +120 行;main 线 37 领先中 33 个为 beici 线合入。
- **变更内容**:**UI·UX/缺陷修复**:图标位图质量参数调整([29223086](https://github.com/timeclockouo/DeskBox/commit/2922308699f84b0d9f5c606f87b20610703ba2d2))、图标加载与 padding/拒绝条件重构([6f8ef6a7](https://github.com/timeclockouo/DeskBox/commit/6f8ef6a7fcf166d6533381d1855338738b717117)、[0d222565](https://github.com/timeclockouo/DeskBox/commit/0d22256570b2872843b8852f02481812094bb6f3));**工程化**:build-installer.yml 构建安装器工作流([b81d7555](https://github.com/timeclockouo/DeskBox/commit/b81d7555d6dea85def8d39df6fa14d4fb4044821))。
- **实现方式**:均为对 IconBitmapQuality/图标加载的小参数与阈值调整(多数为网页"upload"式提交),无测试、无 Strings/schema 影响。
- **对本仓库的价值**:【仅了解】方向(图标渲染质量与裁剪阈值)可关注,但改动是试凑式小补丁,无评审级实现。

### 小型与琐碎变更(合并节)

- **[GeneYu404/DeskBox](https://github.com/GeneYu404/DeskBox/commit/a3305df7edaac53f2a17a79d124e3598a70a0883)**:file-only 单提交 +142/-19,csproj 条件编译裁掉非 File 组件(DeskBoxFileOnly 开关)。裁剪 hack,仅了解("按场景裁组件"思路可留档)。
- **[YSDXL/DeskBox](https://github.com/YSDXL/DeskBox/commit/d3b8aeb3eaa6e4775d2e2b1d4e7fd7d17255467f)**:CoupleMemo 情侣备忘小组件,+254/-11,按原生 provider 模式接线。内容窗口路径可扩展性旁证,仅了解。
- **[nnlpwm21/DeskBoxWhite](https://github.com/nnlpwm21/DeskBoxWhite/commit/acff79b164f2e0f5be458314ccfdb290caa028c3)**:整体更名/白牌化(1413 个文件重命名、解决方案与工程改名、品牌文本 + 技术分析报告;维护者身份,本仓库没有)。不是行尾转换也不是白主题实现,仅了解。
- **[fairhopeweb/DeskBox](https://github.com/fairhopeweb/DeskBox/commit/6311d6b2343ca0b33b37d2a952cb9278ac0ae496)**:ImgBot 压缩 docs 图片(约省 10-20%),仅了解。
- **[BumptiousChen/DeskBox](https://github.com/BumptiousChen/DeskBox/commit/a4c406fcb3e1e877c0f3dec206824be273ca502b)**:仅提交一个"DeskBox 开发版.lnk"快捷方式二进制(提交测试),仅了解。
- **[Rodgershahha/DeskBox](https://github.com/Rodgershahha/DeskBox/commit/7525ca8529de46d057edd4ebae69d1ed6d7c21d9)**:README 测试项目路径说明 + global.json rollForward 微调(+4/-1),仅了解。
- **[ajie4979/DeskBox-ajie4979](https://github.com/ajie4979/DeskBox-ajie4979/commit/760431a17695182a09060a9510098a31725da1e5)**:README 更名 + 英文版搬运(+510/-510),纯文档,仅了解。
- **[dejush1021-crypto/DeskBox](https://github.com/dejush1021-crypto/DeskBox/commit/0e2b9c267a8a688d612baad418eca8accefbc61b)**:全部 README 微调(下载信息修订、排版重写、空英文 README),仅了解。
- **[2510227870/DeskBox](https://github.com/2510227870/DeskBox/commit/2ec0899f2f5881e04303330209288094d02c4100)**:修复搜索弹窗推荐应用卡片单击/双击无响应(SearchPopupWindow 事件处理重排,+54/-17)。~~真实小缺陷修复~~ **经核对不成立**:本仓库"单击选中、双击打开"接线完整(`SearchPopupWindow.xaml.cs:2043-2078`,DataContext 根因已修 `:1633-1646`),fork 修的是旧基线问题。仅了解。
- **[lmyyds1/DeskBox](https://github.com/lmyyds1/DeskBox/commit/e8a59b300497a35394008f1a31600161e937218b)**:个人精简版——删除 10/12 语言 JSON、安装器语言包与 MUI 资源,移除微软商店与反馈入口;另有启动恢复组件桌面层([86a47ae0](https://github.com/lmyyds1/DeskBox/commit/86a47ae0594c23ddaac5521634836c0a631080e9))、桌面点击隐藏组件([15bf8ed3](https://github.com/lmyyds1/DeskBox/commit/15bf8ed3630e0134ca2629ed4689ec28e230e195))、管理员右键启动菜单(ElevatedLaunchService,附测试)。删除本地化底层直接违反本仓库 12 语言约定,不可移植;"管理员方式打开"与"点击桌面隐藏组件"两个点子可记录。

---

## 五、结论与建议

> **决策状态(2026-10-24)**:本章为原始建议;各项已与维护者逐项确认,并经两个子代理对当前代码逐条核对(核对结论见「〇、行动决策」,主会话二次抽查 4 条颠覆性判定全部复核通过)——第 8 项(单格隐藏图标文字)核对属实,先立项后由维护者转入待办池第一档;1/5/6/7 与分页搜索、缩略图缓存共 6 项核对属实进入待办池;2/4 经核对降级保留;3/10 与玻璃材质共 3 项经核对不成立移出;每格背景+清爽桌面不采用,AI 整理线放弃,其余留档。下表保留理由供实施时参考。

### 5.1 建议移植(可直接摘取,按性价比排序)

| # | 来源 | 内容 | 关键提交 | 理由 | 决策 |
|---|---|---|---|---|---|
| 1 | ForgetiExistence | 性能三连修:O(n²)→O(n) 列表同步、盲重载降级、钩子期禁 TrimWorkingSet | [c75f7306](https://github.com/ForgetiExistence/DeskBox/commit/c75f73067b7e240371615b02b95ebc9742a4bd2d) | 与本仓库同源代码逐行对应,+206 行直击"大文件夹卡顿",可逐条评审;附 814 行性能评审文档 | 待办 |
| 2 | romin2026 | WinEvent 事件驱动桌面带 re-bed | [927a9231](https://github.com/romin2026/DeskBox/commit/927a9231a451f3b49f9234d6ec25e0b66b2940ec) | 补上 self-heal 体系未覆盖的"系统对话框抬升 DefView band"失效路径;自包含 306 行 partial 带测试,按 z-order 生命周期文档并入 | 待办(核对后降级:现有 5s 轮询 watchdog 约 10 秒自愈,本项为时效优化) |
| 3 | romin2026 | Win10 材质两连修(acrylic 叠加/tint 表面) | [efd93655](https://github.com/romin2026/DeskBox/commit/efd9365557fd2df7e60f4ab443d4a1fb0e8980b5)、[75814e81](https://github.com/romin2026/DeskBox/commit/75814e81feabc87da1f2f1a87efc37eb9d2b8be0) | 贴着 Win10 21H2 兼容底线的小补丁,近乎直接摘 | **核对不成立,移出**(当前代码 controller 与 legacy tint 按构造互斥,Backdrop.cs:105-111) |
| 4 | zerovoxxx | 登录期托盘菜单韧性 + .lnk 图标资源持久化恢复 | [e0da46af](https://github.com/zerovoxxx/DeskBox/commit/e0da46af8532384f823f05b35d386cda87b0612d)、[13ee4de2](https://github.com/zerovoxxx/DeskBox/commit/13ee4de2a7caff114c676f7458681224b0bf7fc1) | 真实可感知缺陷、带完整针对性测试,分别对接 App.Tray 与快捷方式图标路径 | 降级:4a 不成立(本仓库已有 15×2s 重试+降级,App.Tray.cs:284-295),4b 保留为可选增强 |
| 5 | dandostoja | OfflineNetwork 硬离线边界 | [b9e8b2f0](https://github.com/dandostoja/DeskBox-LOCAL/commit/b9e8b2f0b0a09a6e30860ba4f72c1bbb84e40a17) | 40 行单点 transport,零 schema/架构冲突;既是离线模式产品项,也是测试的确定性网络边界 | 待办 |
| 6 | nizarhamza | SysListView32 图标格点吸附/避让互操作 | [0e7604b](https://github.com/nizarhamza/DeskBox_Fork/commit/0e7604bbaf66455c3a584819235e4c203bd74878) | 与本仓库 DesktopIconGeometryService 互补,是"组件落桌不压图标"的关键拼图;Bucket 组件可一并评估 | 待办(不含 Bucket) |
| 7 | wrust0608 | Win11 26200 托盘 ContextFlyout E_NOTIMPL 崩溃降级 | [c6dbed3a](https://github.com/wrust0608/DeskBox/commit/c6dbed3ab70b236edd74a72d539fe94e84e12020) | 9 行,真实崩溃,近乎直接摘 | 待办(核对后降级:赋值期已有重试+两级降级,仅剩 show 期泛化 catch 缺口) |
| 8 | PepinoLatte | 文件格单格隐藏图标文字 | [30934a7](https://github.com/PepinoLatte/DeskBox/commit/30934a7b6e760e551a9c0a255c54a7aa6d54bb77) | 小而规范,可空覆盖 + 备份白名单 + 12 语言,可直接评审合入 | 待办(第一档;维护者由立项转入) |
| 9 | Pictus08 | 每格背景覆盖 + 组件/桌面图标一键隐藏恢复 | [483094d](https://github.com/Pictus08/DeskBox/commit/483094df0cc95221c3cedb0208766062faeef879) | 高频需求点,实现遵循本仓库约定且改动可控 | 不采用 |
| 10 | 2510227870 | 搜索弹窗推荐卡片单击/双击无响应修复 | [2ec0899](https://github.com/2510227870/DeskBox/commit/2ec0899f2f5881e04303330209288094d02c4100) | 真实小缺陷,需按当前 SearchPopupWindow 重做后合入 | **核对不成立,移出**(本仓库"单击选中、双击打开"接线完整,SearchPopupWindow.xaml.cs:2043-2078) |

### 5.2 建议评估(有启发,按方向归组)

> 决策(2026-10-24):仅"组件内分页+跨页搜索"与"ThumbnailDiskCache"两项进入待办池;番茄钟与截图增强后经补充核对**立项**(截图带 AOT 前置条件),日历格与进程外桥接(滴答)经维护者确认**不采用**;Command API/MCP、AI harness、主题包三个大方向本轮不立项,仅记路线图。

- **组件能力线**:组件内分页 + 跨页搜索(lateautumn233,策略层近乎直接移植)、番茄钟/WebDAV 备份(bupocc,新组件接线范本 + PasswordVault 方案)、ThumbnailDiskCache 与截图增强(bbkien2312)、Bucket 组件(nizarhamza)、独立日历格(zhonghengchen,点子级)。
- **命令/AI 面方向**(沿插件化路线推进):本地 Command API(wanvfx)、MCP + Agent 命令面(wuya11111)、AI 子进程 harness(STMfan,规避 AOT 与密钥落盘的干净路线)、AI 桌面整理/布局设计器(GIT-LINCC,维护者侧,事务化应用 + 校验回环是好设计)。
- **外观/主题方向**:数据式主题包(zjzonline,与 AppSettings 冻结线兼容的底稿)、圆角玻璃材质与恢复看门狗(QingLanLay,维护者侧)、SkinPack(romin2026,次要)、按显示器拓扑记忆图标尺寸(luziyi123448-gif,维护者侧)。
- **集成架构参考**:外部 SaaS 进程外桥接(ScyPolit)、SensorHost 独立进程供数(Lurbing7,若 SystemMonitor 进入规划)。
- **出厂默认值**:lean 文件组件默认档位(joeshu,仅默认值部分,须走本仓库基线重钉)。

### 5.3 维护者侧提交的回收决策(优先处理)

> 决策(2026-10-24):QingLanLay 的玻璃材质+恢复看门狗 → 经代码核对**移出待办**(材质档位、圆角三档、轮询 bedding watchdog 本仓库均已有,见「〇、行动决策」核对结论);GIT-LINCC 的 AI 整理线 → **放弃**;其余(romin2026 cloud-backup 四分支、agogo233 的 9 个、ScyPolit 的 5 个、joeshu/luziyi/nnlpwm21 各 1 个)→ 留档不动。romin2026 cloud-backup 能力已随 upstream 落地,无需动作。

10 个 fork 上共 54 个 `Simon <1047078635@qq.com>` 署名的提交不在本仓库:GIT-LINCC 1、Zagadka-3906 8、QingLanLay 5、Lurbing7 14、romin2026 9(cloud-backup 四分支,该能力已随 upstream 落地,核对即可)、agogo233 9、ScyPolit 5、joeshu 1、luziyi123448-gif 1、nnlpwm21 1。其中多数带契约测试与 12 语言同步,质量不低于主干线。

### 5.4 明确不适用(及理由)

- **lmyyds1 的删语言精简线**:直接违反 12 语言全覆盖约定。
- **bbkien2312 / wrust0608 的第 13 语言 vi-VN**:是否扩语言是产品决策;且 bbkien2312 的键覆盖不齐(在现约束下属缺陷)。若未来扩语言,wrust0608 的工程包(契约测试改造 + Inno Setup isl + 审计脚本)是现成参考。
- **joeshu 的 AOT 审计放松**("警告减少也算通过"):与本仓库 WMC1510 零漂移红线相反。
- **agogo233 整线合并**:dev-1.6 混合树 + native capability mask 511→1023,偏离冻结 ABI 2/511。
- **fork 自建更新通道**(bbkien2312、timeclockouo 安装器 CI):与本仓库 direct-release updater 及零售管线冲突。
- **白牌化/品牌替换**(nnlpwm21、wrust0608 品牌部分)、ImgBot、README 搬运、.lnk 测试提交:无技术内容。
- **Ci-Tu 的两项功能**:已被 upstream main 吸收,无需动作。

### 5.5 观察名单

按作者活跃度与方向相关性,值得定期回看的是:romin2026(z-order/材质修复线)、ForgetiExistence(性能线)、agogo233(dev-1.6 跟随线,拖放/启动修复)、lateautumn233(组件内分页搜索)、zerovoxxx(缺陷修复)。其余 fork 无持续跟进价值。

---

## 附:复现说明

- fork 全量清单(393 行,含 star/pushed_at/default_branch/created_at)可由第二节第 1 条的 API 查询翻页复现。
- 本报告的"净新增"判定基于 git 协议本地对比(upstream 全引用可达集合 919 SHA + 本仓库 origin 引用可达集合 821 SHA),与 compare API 结果等价;提交链接均指向各 fork 仓库的真实 commit 对象,可逐条打开验证。
- 分析期间 2 个 fork(Echoes-26/-DeskBox、alan3024/DeskBox)已删除或转私有,不在详析范围内。

### 修订记录

- **v1(2026-10-24)**:fork 网络全量分析——393 个 fork 枚举、全分支初筛、36 个候选(62 分支)本地抓取统计、两个子代理深挖、报告定稿。
- **v1.1(2026-10-24)**:16 个行动项与维护者逐项确认(4 轮问答),新增「〇、行动决策」;第五节补决策状态标注。
- **v1.2(2026-10-24)**:两个子代理逐条核对当前 wip/fix-bug 代码(主会话二次抽查 4 条颠覆性判定)——3 项经核对不成立移出待办池(Win10 材质两修、搜索弹窗点击、玻璃材质+圆角档位),3 项降级保留(re-bed→时效优化、托盘 E_NOTIMPL→仅剩 show 期缺口、.lnk 持久化→可选增强),核对结论并入〇节、相关章节加「核对注」、摘要与总览表加修正提示。
- **v1.3(2026-10-24)**:实施必要性复核——待办池分三档(建议排期 #1(范围收窄为 O(n²) 一项)/#5;看产品需要 #6/#7/#12;转观察 #2/#4/#11);撤回"先收策略层"的旧建议;补充 `preserveExistingIconWhenMissing` 与图标几何用途两处影响判断的代码事实。
- **v1.4(2026-10-24)**:五个留档项补充核对与决策——agogo233 拖入无响应/启动沉底病灶确认存在并**立项**、多选重排已被本仓库 9e414687 解决(遗留单选落点疑点);番茄钟真空缺**立项**;日历格部分存在留档待产品决策;截图增强(System.Drawing 的 AOT 风险)与滴答桥接(移植大、无产品路线)默认留档待补决策;新增「补充核对与决策」一节,立项增至 4 项,五个相关章节补「核对注」。
- **v1.5(2026-10-24)**:三项挂起决策落定——独立日历格与滴答桥接确认**不采用**;截图增强**立项**(前置条件:System.Drawing → WIC/SoftwareBitmap 的 AOT 替换设计先行评审,谨慎实施);立项增至 5 项,相关核对注与第五节同步更新。
- **v1.6(2026-10-24)**:第 9 项(文件格单格隐藏图标文字)由维护者决策**由立项转入待办池第一档**;立项收敛为 4 项,全文交叉引用(〇节总览、必要性复核、待办池、5.1 表与核对注)同步更新。
- **v1.7(2026-10-24)**:登记上游合并任务——核实 upstream/main 零新增但 wip/fix-bug 落后 34 个提交(1.5.9 发布 train),feat/160-kickoff 新增 22 个提交(设置宿主拆分,未进 main,仅观察);写明合并铁则(不影响现有功能/冲突选最优解/语言 12→13/AOT 门禁重钉/合并后验证清单);生成接手提示词 `docs/forks/agent-handoff-prompt.md`。
- **v1.8(2026-10-24)**:任务 A 执行完毕——34 提交合并落地(见上方「完成状态」块);〇节核对结论锚点按合并后实树刷新(#2 bedding watchdog → `WidgetManager.ZOrder.cs:579-655`;#3 → `Backdrop.cs:107-111`;#4 → `App.Tray.cs:303-321/:533-611`;#5 托盘策略 1.5.9 后内联至 `App.Tray.cs:1665` 且新栈待重核;#8 → `SearchPopupWindow.xaml.cs:2062-2086/:1649-1661`;#13 → `Features/Appearance/AppearanceSettingsViewModel.cs:166-170`;拖入行 → `NativeDragDrop.cs:480/1128/:1618`、`FileSurfaceContent.xaml.cs:3176`;#6 计数 9→10 处;#9 `FileWidgetIconLayout.cs` 已移至 Services/);本仓库语言约定行(番茄钟立项/待办 #9)改 13 语言;AOT 重钉记录(796→808、profile 64→65)入完成状态块。
- **v1.9(2026-10-25)**:补充 1(拖入无响应/空包掩盖修复)实施完毕——pending 握手 + 全路径结果上报(宿主按窗口句柄解析)+ 原生回退保留(不再到达即取消)+ 新手势清残留 pending + `DroppedFileBatch.Dispose` 幂等;相对 fork 原版两处有意偏差(冲突解除上报成功、按 HWND 而非 id 找宿主)均记因于立项表完成状态块;新增 `DropResponseMaskContractTests` 8 例;全套测试 6104/6104、AOT 门零漂移、无新增语言键(纯代码路径,13 语言键集不变)。
- **v1.10(2026-10-25)**:补充 2(启动抬升沉底修复)实施完毕——lease 增 `AbsoluteSettle` 策略位(交互覆盖为 relative),启动两入口(startup-restore 批次抬升 + startup-explorer-desktop-ready 刷新)标 absolute,刷新守卫排除托盘抬升与进行中交互,绝对分支走 `MoveToDesktopBottom` 绕过相对前台策略并记前台窗口日志;fork 的 QuickCapture 宿主映射到本树唯一实现 `ContentWidgetWindow`;两例策略测试逐字移植 + 三例契约测试适配;全套测试 6107/6107、AOT 门零漂移、无新增语言键。
- **v1.11(2026-10-25)**:待办池 #9(文件格单格隐藏图标文字)实施完毕——`WidgetConfig.IconLabelHiddenOverride` 可空覆盖 + `FileWidgetIconLayout.ResolveEffectiveFileNameLineCount` 几何归一 + 右键"查看"菜单开关 + 设置页"格子单独管理"两张本树习语 SettingsCard(选择器 + 隐藏开关)+ 最小覆盖存储(期望态等于全局态时清 null)+ 备份白名单 `iconLabelHiddenOverride` + 13 语言 6 新键(3140→3146,ko-KR 自译)+ fork 7 例测试逐字移植;连带重钉 4 处契约(SliceOwnership LayoutAndSettings 17→19 + FileWidgetSettingsSection=4、AotStage4D1B 226→228/193→195/493→497、ToggleSwitchAccessibility 1→2、SearchCatalog 2 条目);全套测试 6114/6114、AOT 门零漂移(profile 65、WMC1510 808)、冒烟通过。
- **v1.12(2026-10-25)**:待办池 #1①(O(n²) 列表同步位置字典)实施完毕——`SyncFolderItems`/`ApplyReconciledManualOrder` 循环内 `Items.IndexOf` 全部替换为每趟一次构建的 `Dictionary<WidgetItem, int>` 索引 + 尾部重索引(②盲重载、③钩子期 Trim 按复核结论不实施);新增 `ItemSyncPositionIndexContractTests` 3 例;全套测试 6117/6117、AOT 门零漂移(profile 65、WMC1510 808)、冒烟通过。环境侧记:当夜 360 清理 NuGet 缓存损坏 `microsoft.windows.sdk.buildtools.msix` 包(丢失 build/buildTransitive 目录导致 pri 生成链静默失效),已从包内 nupkg 原地修复;`SortingAndWatchers.cs:277`/`Stacks.cs:1965` 同形状留档。
- **v1.13(2026-10-25)**:待办池 #5(托盘 show 期 E_NOTIMPL 降级)实施完毕——`IsSecondWindowHostFailure` 扩展认 E_NOTIMPL(0x80004001,Win11 26200),show 期该异常接入既有重建/降级阶梯(赋值期 catch-all 链本已有,无需改);`TrayContextMenuResilienceTests` 断言同步;全套测试 6117/6117、AOT 门零漂移、冒烟通过。
- **v1.14(2026-10-25 交接流程登记)**:接手提示词增补并重排编号——新增第 2 节「现状甄别」(部分任务已由其他会话完成,接手 agent 以实树为准逐项甄别已完成/部分完成/未动,同一问题多实现时分析最优解方案,不轻信文档与提交信息)与第 9 节「迭代式深度审查循环」(立项与待办池全部关闭后:深度审查→修复→再审查,退出条件为连续一轮无 P0/P1 重大缺陷);**反过度工程硬要求**入文(每项修复前过"是否有必要实施"论证、只修 P0/P1、P2 记档、不引入新抽象/不顺手重构);提示词补现状快照(截至 v1.13:合并 A 与补充 1/2、#9/#1①/#5 已完成,测试 6117/6117、AOT profile 65/WMC1510 808)并同步 13 语言现状;本报告〇节补「收尾:迭代式深度审查循环与反过度工程」小节与接手注意。
- **v1.15(2026-10-25)**:提示词第 8 节改写为**鼓励使用 subagent**——唯一硬约束为同时运行不超过 5 个;工作模式不设限(勘察/验证/设计/并行实施/对抗审查等均由接手 agent 自行分析决定,单 agent 串行同样合法),示例仅启发;第 7 节"不同时开多条写树实施线"限制同步解除;验收标准(身份/红线/证据/文档回写)与模式脱钩。
- **v1.16(2026-10-25)**:接手甄别与上游观察登记——第 2 节流程执行完毕:任务 A 合并与补充 1/补充 2/待办 #9/#1①/#5 六项已实施工作经实树逐项核验属实、无降级项(〇节新增「接手甄别」确认块,提交号 `de394a01`/`d8cb48ae`/`4df21ef4`/`4bc898a2`/`cb6f532f`/`d03648fb`,v1.14 登记 `7f533583`);任务 B 观察记录:main `1fdf0bd9` 零新增,kickoff `1493e3bd..7f65041c` +4 审计轮(R6-R9,设置宿主拆分 P0/P1 修复),领先 main 39,未进 main;环境侧记入甄别块(用户 `developer\developer` 的 per-user SDK 路径、测试 TMP/TEMP 重定向与残余 10 例环境性失败的处置指引)。
- **v1.17(2026-10-25)**:补充 3 番茄钟组件实施登记——〇节立项表补充 3 行标记已实施并新增完成状态块:per-kind `PomodoroSettingsStore` 架构(AppSettings 冻结线零接触)、状态机/适配器/P-Invoke/设置节四处契约适配、8 个既有契约面+JsonSerializationBaseline+AotStage5B4* 锁步链重钉、13 语言 +35 键、全量 6162/6163(唯一失败为已知环境项);AGENTS.md 组件清单同步加 Pomodoro。子代理政策(v1.15)首次全配运行:5 槽并行(规格/接线面/测试环境/状态机移植/13 语言)全部产出,主会话保留集成与提交。
- **v1.18(2026-10-25)**:补充 4 截图增强**设计评审登记**——三路勘察(bbkien 规格/本仓库接线面/WIC-AOT 预研)合一,设计定稿 [docs/forks/screenshot-capture-design.md](screenshot-capture-design.md):**零新增 NuGet**(弃 Vortice——WGC 无遮挡线在 Win10 21H2 黄边横幅不可关[IsBorderRequired 为 20348+/contract v12]且需 D3D interop;改 GDI `PrintWindow(PW_RENDERFULLCONTENT)`+`BitBlt` 冻结帧,回退冻结帧裁剪);像素管线 GDI 采集+WIC 编码(照 `QuickCaptureService.cs:2120-2152` AOT 生产模式,全零 alpha 强制不透明照 `deskbox-thumbnail-proxy/main.rs:2380` 先例);剪贴板复用 `Operations.cs:365-399` 写链(含 `DeskBoxClipboardWriteScope.MarkWrite` 防 QuickCapture 回捕);设置走 `ScreenshotSettingsStore`(PomodoroSettingsStore 先例,不碰 AppSettings v15);预告 JsonSerializationBaseline 43→44 九文件锁步链。立项表补充 4 行标前置已满足,实施按设计 §10 六提交序列推进。
- **v1.19(2026-10-25)**:补充 4 截图增强**实施登记**——〇节立项表补充 4 行标记已实施并新增完成状态块;设计 §10 序列按"地基+主功能"压缩为两笔提交(`8ae3d3ee` 地基:Win32Helper GDI/WIC 捕获区+ScreenshotSettingsStore+锁步链 43→44;`418c5cb4` 冻结屏捕获窗+Copy/Save 导出+托盘热键+设置节+24 键×13 语言+5 处契约重钉),另顺带修复 `TryEncodePngAsync` 目标已存在时静默保留旧字节的既有缺陷;AOT 审计 profile 65 首轮 CS8600 新警告(可空性)修复后零漂移通过;全量 6173/6174(唯一失败=已知环境项);current_architecture.md Fork additions 补 per-kind store 双例与截图管线三件。**四项立项至此全部实施完毕**,待办池仅余二档 #6/#7/#12(需用户决策)与三档观察项。
- **v1.20(2026-10-25)**:二档待办用户决策登记——**#12 ThumbnailDiskCache 立项实施**(缩略图落盘,重启即显,容量上限+逐出);**#6 OfflineNetwork 与 #7 图标格点避让暂不实施**(#6:离线保证非当前需求、防火墙可达等价效果;#7:待"组件压图标"确认为日常痛点再立项,届时先设计评审)。待办池待 #12 完成后关闭,随后进入 §9 迭代审查循环。
- **v1.21(2026-10-25)**:待办池 #12 ThumbnailDiskCache 实施登记(完成状态块见上;`IconHelper` 媒体路径磁盘层,仅非视频,SHA-256 身份键,2000 文件/128MB LRU 逐出;新增 6 例测试,ModuleBoundary 清单 +2;全量 6179/6180,唯一失败=已知环境项)。**至此 4 项立项与 9 项待办池全部完成或按用户决策正式关闭,§9 迭代审查循环触发条件达成,下一阶段进入循环第 1 轮**(优先审查本轮新增:番茄钟/截图/缩略图缓存交叉面)。
- **v1.22(2026-10-25)**:§9 迭代审查循环**第 1 轮登记**——三路只读评审(缩略图+横切/番茄钟/截图)并行加主会话自审:大面积 CLEAN(信号量/逐出/原子写/事件配对/冻结线/契约钉值/13 语言全等);判 P0=0,P1=5 全部修复(`PomodoroSettingsStore`/`ScreenshotSettingsStore` 跨实例缓存失效——版本计数 + `Changed` 移至写链持久化后触发,并新增 2 例跨实例回归测试;截图窗 CAS 门 ctor 异常不复位;热键 `RefreshRegistration` 未查 `_isSubclassInstalled` 致"UI 显 Active 但和弦死";`PrintWindow` bool 被忽略致安全/UWP 窗导出纯黑 PNG(现 false 即回退冻结帧裁剪);`FindWindowAtPoint` 选中 cloaked/点击穿透窗(DWMWA_CLOAKED + WS_EX_TRANSPARENT/TOOLWINDOW 过滤));P2=20 项记档不修(声音/通知 bool 弃置、子类 TryEnqueue 无计数、Ensure catch 弃字段、关机次序、picker 错误与取消不可分、UI 线程同步 BitBlt、缩略图 GetOrAdd 竞态、O(N) 诊断、空 catch 等,全文见本块)。
- **v1.23(2026-10-25)**:§9 第 2 轮登记(只审第 1 轮修复面)+ 三档观察巡检 + P2 分级——第 2 轮判 **P1×1 已修**:版本戳投毒(版本在持久化前同步递增,窗口期读者把旧盘数据打上新版本号,Changed 刷新变 no-op;截图热键"UI 显新手势、实际注册旧和弦"每次录制必现)→ 改为 `AdvanceVersionAndNotify`(版本递增移到写链 `PersistAsync` 成功后,版本号恒等于盘上状态),`Update` 改走 `EnsureCacheLoadedLocked` 版本化装载(顺带修掉 P2 的 stale-base last-writer-wins),新增 2 例投毒回归测试(写窗期内 Load 后排水必见新值);P2×2 记档(PrintWindow TRUE-but-blank DIB 仍可导黑、SaveAsync 同窗竞态理论面)。三档巡检:#2/#4/#11 **全部维持观察**,无恶化、无本轮四笔工作意外影响(#4 文档锚点 IconHelper.cs:1233-1253→1251-1271 已刷新)。P2 影响分级(子代理 20 项全重建):**高 3**(关机不冲刷 per-kind 写链丢最后一次设置写盘 App.xaml.cs:6784;缩略图命中路径全目录枚举诊断 ThumbnailDiskCache.cs:82;截图覆盖层 UI 线程同步全屏 BitBlt ScreenshotCaptureWindow.xaml.cs:70-74)/中 6/低 11——高 3 项列为第 3 轮修复候选(改动小、每次使用受益,多屏支持另立功能提案不占名额)。
- **v1.24(2026-10-25)**:§9 第 3 轮登记——第 2 轮修复(1ca0ef62)复核**无 P0/P1**(投毒修复成立、测试真钉住;余 P2×3 记档:持久化失败仍推进版本的既有丢写语义、SaveAsync 未入链但零调用方、录制对话框同步反馈瞬态误报但 Changed 路径自愈)。经用户决策,高 3 项 P2 升级修复:①关机冲刷 per-kind 写链(两 store 写链 static 化 `s_chainLock`+`WaitForAllPendingPersists`,`App.xaml.cs:6784` settings-flush 步追加冲刷,+2 例 StaticDrain 测试);②缩略图诊断 30s 时间门控(`Interlocked.CompareExchange` 占位,命中路径零磁盘成本,+DiagnosticsIntervalOverrideForTests 缝与 1 例行为断言,两处空 catch 补日志);③截图覆盖层 ctor 不再 UI 线程同步全屏 BitBlt(窗口保持隐藏,`InitializeAsync` 后台捕冻结帧后才 TOPMOST 显示,防自摄次序保持;热键路径 `ShowScreenshotCaptureAsync` await 后 Activate)。**§9 退出条件达成**(第 3 轮复核无 P0/P1,升级修复均过门禁),循环结束;残留 P2 记档见 v1.22/v1.23/v1.24 块。
