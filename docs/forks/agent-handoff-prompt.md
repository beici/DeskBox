# DeskBox fork 网络工作接手提示词

> 本文件写给接手本仓库 fork 网络后续工作的 agent。读三样东西即可开工:本文件全文、`docs/forks/fork-network-analysis.md` 的「〇、行动决策」(唯一权威入口)、`AGENTS.md`(全条有效)。**接手后第一步必须是现状甄别(第 2 节)**——部分任务可能已被其他会话完成,文档状态未必是实树现状。分析报告的每条结论都经过两轮代码核对并带 file:line 证据;若你发现与实树冲突的事实,**先核实再改文档**。

## 0. 环境与身份

- 仓库 `E:\DeskBox`,分支 `wip/fix-bug`;origin=beici/DeskBox(本 fork),upstream=Tianyu199509/DeskBox(上游)。
- **提交/PR 身份只允许 `Simon <1047078635@qq.com>`**;禁止 Co-Authored-By/Generated with/任何助手署名——`.githooks/commit-msg` 与 CI 强制(新克隆先 `git config core.hooksPath .githooks`)。
- Windows + Git Bash。gh CLI 未安装;GitHub 未认证 API 限额 60 次/小时且出口为共享代理 IP、极易耗尽——**优先用 git 协议与本地对象**(`git ls-remote`、`git fetch`、本地 diff),curl 仅在必要时用。git 网络走仓库本地配置:`http.proxy=http://127.0.0.1:7897` + `http.sslBackend=schannel`(对 github.com 全局已带代理;在仓库目录外操作 git 时显式 `-c` 传这两项)。
- 构建/运行纪律(AGENTS.md 全文有效):改应用代码前**先停可执行路径在本仓库内的 DeskBox.exe**;构建 `dotnet build .\src\DeskBox\DeskBox.csproj -c Debug --no-restore -p:Platform=x64`;构建是否真落盘以 `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.dll` 写时间晚于构建开始为准;测试 `dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore -p:Platform=x64`(全绿 = passed==total,失败:0 也可能是中止);零售发布只走 `scripts/publish-aot-retail.ps1`。
- **架构红线**:`AppSettings` 冻结线(改 schema 必须带版本迁移)、本地化 JSON 键/占位符全语言一致(**合并 1.5.9 已完成,现为 13 个,含韩语**;新增键一律按 13 文件补齐)、`docs/architecture/[重要勿删]widget_zorder_lifecycle.md`(碰 z-order 前必读)、新组件必须走共享内容窗口路径、`native/` Rust ABI 冻结(ABI 2 / capability mask 511 / 十导出)、NuGet 双 lock(`packages.lock.json` + `packages.aot.lock.json` 都要提交)。

## 1. 权威文档

- `docs/forks/fork-network-analysis.md`——fork 网络分析 + 行动决策。**〇节是唯一权威**:4 项立项 / 9 项待办池(三档)/ 不采用清单 / 上游合并任务;其余章节是证据与历史判定(部分已被核对注修正,以核对注为准)。
- `docs/architecture/current_architecture.md`——当前架构;**§Fork additions 是本 fork 自有服务清单,合并与重构不得破坏**:WidgetShowDesktopSelfHealService、WidgetCompactTransitionProgressPolicy、MusicSettingsStore(schema v10)、DesktopIconGeometryService、WidgetMarginReferenceCalculator、WidgetPeerOrderMovePlanner、WidgetTitleAppearanceSettings、QuickCaptureClipboardColorSettings、src/DeskBox.Abstractions。
- 完成任何一项后:更新报告〇节对应状态 + 文末修订记录;每项独立提交。

## 2. 第一步:甄别已完成的工作(接手后必做)

- 本提示词与报告写于 2026-10-24;**在你接手之前,部分任务可能已被其他 agent/会话完成**——4 项立项、9 项待办、上游合并都有可能已经动过。
- 第一动作:以**实树为准**逐项甄别——`git log --oneline`(提交主题与任务对应)、`git status`/`git diff`,必要时读代码确认实现是否完整、是否带测试;产出一张"已完成 / 部分完成 / 未动"清单,并**回写报告〇节**(每项标状态 + 证据 SHA/行号),同时更新修订记录。
- 甄别纪律:不要轻信文档与提交信息(本仓库历史上发生过"提交信息与实际不符"),以代码行为为准;发现"声称完成但实现不完整/有缺陷"的,如实降级为部分完成并记录缺口。
- **冲突处理——分析最优解方案**:同一问题若存在多个实现(上游 vs 本仓库、已完成实现 vs 报告推荐方案),逐处比较:更符合本仓库架构决策(〇节)、风险更小、测试更完整的一方胜出;落选方中可合并的价值(测试、守卫、文档)择优并入;取舍理由写进合并提交与报告。禁止机械保留"先来的"或"文档写的"。
- **现状快照(2026-10-25,以实树 + 报告修订记录为准,可能已过期)**:报告 v1.8-v1.13 记录——任务一 A(合并 upstream main 34 提交)**已完成**;立项 补充 1(拖入修复)、补充 2(启动沉底)**已完成**;待办池 #9(单格隐藏文字)、#1①(O(n²) 位置字典)、#5(托盘 show 期降级)**已完成**;测试基线 6117/6117,AOT 门 profile 65 / WMC1510 808。**待做**:立项 补充 3(番茄钟)、补充 4(截图增强,先出 AOT 替换设计评审);待办池第二档 #6/#7/#12(动前问用户)、第三档 #2/#4/#11(观察);任务一 B(kickoff 持续观察)。

## 3. 任务一:上游合并(A 已完成;B 持续观察)

> **状态(2026-10-25)**:A 已完成(v1.8,34 个 1.5.9 提交合并落地,AOT 重钉 profile 65 / WMC1510 808);B 持续观察。以下原方案保留,供 B 转正时与追溯使用。

现状(2026-10-24 核实,动手前先 `git fetch upstream --prune` 重新确认):

- upstream/main = `1fdf0bd9`(分析快照后零新增);`wip/fix-bug` 落后 main **34 个提交**(merge-base `bbb9e697`)——内容是 **1.5.9 发布 train**:Win11 原生右键菜单、更新体验六批、**新增韩语(语言 12→13)**、配置清空恢复提议、托盘图标隐藏样式、格子内回收站投放、长文件名三环链、隐藏修剪宽限 3s→30s+80MB 地板等。
- `upstream/feat/160-kickoff` 新增 **22 个提交**(`055a6569..1493e3bd`,领先 main 35):设置进程拆分成独立宿主进程的大特性线,**未进 main**。

### A. 合并 upstream/main(必要)

1. `git fetch upstream --prune`,确认 main 是否又有前进(以实树为准,下述行号可能漂移)。
2. `git merge upstream/main` 进 `wip/fix-bug`。
3. **冲突处理铁则——选最优解,不机械 ours/theirs**:
   - 逐处按"哪个实现更符合本仓库当前架构与〇节决策"语义取舍;
   - 上游与本仓库各自修过同一 bug 时,保留本仓库实现并在合并提交信息记录等价性;
   - 上游新功能对照报告〇节决策状态:已立项的收,不采用/放弃的丢;
   - §Fork additions 清单里的自有服务与文件合并后**行为必须不变**。
4. **约定变化——语言 12→13**:合并后本地化硬约定 = 13 个 JSON(含韩语);后续所有立项/待办项文案按 13 文件补齐并跑键覆盖一致性检查。本报告早期文字按 12 语言书写,以合并后实树为准;vi-VN 仍属未经产品决策的私扩(将成第 14 语言),判定不变。
5. **AOT 门禁重钉**:1.5.9 train 改过门禁计数(WMC1510 875→790、SettingsViewModel 可绑定属性 34→33、冒烟必需键改名),与本仓库 864 基线冲突。合并后重跑 `scripts/publish-aot-audit.ps1`,按合并后实树重钉,**禁止静默漂移**。
6. **合并后验证清单**:全量测试绿(数量以合并后为准,1.5.9 基线 5803)→ AOT 审计零漂移 → 13 语言键覆盖 → dev-restart 冒烟 → 重点回归 §Fork additions 自有服务行为(SelfHeal / CompactTransition / MusicSettingsStore 迁移 / DesktopIconGeometry / QuickCapture 颜色设置)→ **复核报告〇节核对结论中受 1.5.9 影响的行**:#1③ 修剪门控(上游改了隐藏修剪宽限与 80MB 地板)、菜单/搜索弹窗相关(1.5.9 重做右键菜单)、AOT 计数。

### B. feat/160-kickoff(观察,禁止现在合并)

- 未进 main 的设置宿主拆分线(独立宿主进程 + IPC)。与本 fork 的 settings coordinator 体系 / `DeskBox.Abstractions` 契约大面积重叠,风险高。
- 动作:定期 `git fetch upstream` 观察是否进 main;进 main 后按任务 A 流程**专项评估**(重点:layout/settings 域权威语义、宿主 IPC 面与本 fork 私有设置的冲突面)。用户点名提前采用时,先出专项冲突评估再动。

## 4. 任务二:立项实施(补充 1/2 已完成;待做补充 3、4)

> **状态(2026-10-25)**:补充 1(拖入修复,v1.9)、补充 2(启动沉底,v1.10)已完成;**待做:补充 3(番茄钟)、补充 4(截图增强,先出 AOT 替换设计评审)**。以下要点保留。

按报告〇节「立项」表执行,以下是要点与病灶坐标(合并上游后行号可能漂移,以实树为准):

1. **拖入无响应/空包掩盖修复**(来源 agogo233 `2952c9c1`):病灶 = 拖放路由事件"到达即写"时间戳(`ContentWidgetWindow.NativeDragDrop.cs:462/1109`)+ 500ms 掩盖窗(:1545)+ `GetStorageItemsAsync` 失败产出空包无回退(`DeskBoxDragData.cs:175-232`;`FileSurfaceContent.xaml.cs:3932` 的 `SkippedCount>0` 无处理)。方案:成功才写时间戳 + 空包回退合并 + `DroppedFileBatch` Dispose 幂等;约 150 行;fork diff 直接参考;完成后回归拖放测试。
2. **启动抬升沉底修复**(来源 agogo233 `5c3a767f`):病灶 = 启动落位走"相对前台"(`WidgetManager.cs:984` → `RelativeLayerRestorePolicy.cs:35` BehindForeground → 组件插到第三方窗口正下),bedding watchdog 仅 pinned 模式。修法 = 落位走绝对桌面底(`MoveToDesktopBottom` 原语已在 `WidgetLayerService.cs:72`)+ 守卫映射到本仓库 `_sessionManager.IsInteractionActive` / `_widgetsRaisedFromTray`;**实施前读 z-order 勿删文档并出小设计**。
3. **文件格单格隐藏图标文字**(来源 PepinoLatte `30934a7`):按当前 `FileWidgetIconLayout`/`WidgetConfig` 适配(不可直搬);可空覆盖(WhenWritingNull)+ 进格子样式备份白名单 + 13 语言 + 测试。
4. **截图增强**(来源 bbkien2312 `49d306e9`/`ad4480c5`,基础 UI 在更早的 `13eded91`):**第一步先出 System.Drawing → WIC/SoftwareBitmap 的 AOT 替换设计并评审,通过才动码**(System.Drawing.Common 在 Native AOT 无官方支持);新增 NuGet(Vortice.Direct3D11)须同时更新双 lock 并重跑 AOT 审计;下游复用 `QuickCaptureClipboardService` 图像读取线,图像写剪贴板新建。

每项:独立提交(Conventional Commits,只带 Simon 身份)→ 全量测试 + AOT 门 + 13 语言 → 更新报告〇节状态与修订记录。

## 5. 任务三:待办池(第一档已清空;第二档动前问用户,第三档观察)

> **状态(2026-10-25)**:第一档 #9(v1.11)、#1①(v1.12)、#5(v1.13)已完成;剩余第二档(#6/#7/#12,实施前逐项与用户确认)、第三档(#2/#4/#11,观察)。

- **第一档(建议排期)**:#1 性能 O(n²)(**范围仅①**:`WidgetViewModel.ItemHydration.cs:259/287` 位置字典;②盲重载、③钩子期 Trim 经核对受既有设计门控,**不实施**)/ #5 托盘菜单 show 期 E_NOTIMPL 降级补丁 / #9 单格隐藏文字(= 任务二第 3 项,同一项)。
- **第二档(先证实再动,别自作主张)**:#6 OfflineNetwork(9 处 HttpClient 出口清单在报告〇节)/ #7 桌面图标避让 / #12 缩略图磁盘缓存(闪烁动机已被现有 `preserveExistingIconWhenMissing` 缓解)。
- **第三档(观察)**:#2 WinEvent re-bed / #4 .lnk 持久化 / #11 分页搜索(随功能立项整体做,不要单独移植策略层)。
- 疑点:单选前向拖放疑似落点偏后一格(`SortingAndWatchers.cs:102` 无前移修正)——先运行时验证再议。

## 6. 明确不做(防自作主张)

每格背景+清爽桌面(Pictus08)、AI 桌面整理线(GIT-LINCC)、独立日历格、滴答清单桥接、lmyyds1 删语言线、未经产品决策的第 14+ 语言(vi-VN)、AOT 审计放松(joeshu 式)、fork 自建更新通道、白牌化、Command API/MCP、AI harness、数据式主题包(本轮不立项,仅路线图)。完整理由见报告〇节「明确不采用 / 放弃」与第四节。

## 7. 工作方式

- 逐项推进,每项完成即更新文档并提交;并行化程度与实施方式(含 subagent 用法)由你自行分析决定,见第 8 节。
- 顺序建议:第 2 节现状甄别 → 剩余立项:补充 3(番茄钟)→ 补充 4(截图,先出 AOT 替换设计评审)→ 第二档待办逐项与用户确认 → 待办池全部关闭后进入第 9 节迭代审查循环。
- 任何产品级取舍(第二档三项、kickoff 是否提前采用)问用户,不要替用户决定。
- 报告文档随做随更:状态、证据、修订记录一个都不能少。

## 8. 子代理使用(鼓励使用;同时运行不超过 5 个)

- **鼓励主动使用 subagent 提升工作效率**:并行勘察、验证、移植预研、冲突分析、审查分工、对抗性复核等都适合;是否开团、开几个、如何分块,由你根据任务规模与耦合度自行分析决定。
- **唯一硬约束:同时运行的 subagent 不超过 5 个**;超出时排队,先完成先补位。
- **工作模式不设限**:只读勘察、证据核对、设计草案、并行实施互不冲突的文件、红蓝对抗式审查、验证分工等均可,单 agent 串行推进同样合法——以"该任务用哪种方式最快且质量可控"为唯一判据。下面仅作启发示例,不是规定:
  - 合并后验证分工(全量测试 / AOT 审计 / 13 语言键覆盖 / 自有服务回归各一);
  - 合并冲突面按文件或子系统切块,逐块产出"差异语义 + 最优解建议 + 证据";
  - 各立项项的移植预研(病灶复核 + fork diff 精读 + 设计草案)并行。
- 无论用什么模式,验收标准不变:提交身份只许 Simon、六条架构红线、结论带证据(file:line / commit SHA)、做完更新报告〇节状态与修订记录——这些与模式无关,是结果要求。

## 9. 迭代式深度审查循环(待办池全部清空后进入)

**触发条件**:4 项立项 + 9 项待办池全部完成(或按报告决策正式关闭,含甄别阶段确认已完成的),且任务一的上游合并已落地。

**每一轮的循环体**:

1. **深度代码审查**——优先审查本轮新增/修改的代码及其交叉面,兼顾全局红线:AOT 门禁与 nameof 棘轮、13 语言键覆盖一致、内容窗口路径、`AppSettings` 冻结线、z-order 勿删语义、`native/` ABI 冻结;并发与资源释放(句柄/管道/临时目录/事件订阅);错误吞噬与静默失败(空 catch、吞异常后假成功)。
2. **优化修复**——对发现的 bug 与重大缺陷逐项修复;每项修复动手前先过第 3 条的必要性论证。
3. 修完进入**下一轮审查**,以此迭代循环。

**退出条件**:连续一整轮深度审查**未发现任何 P0/P1 重大缺陷**(仅剩已记录在案的观察项与低危债)即可收敛;一旦发现即修复并继续下一轮。

**反过度工程(硬要求,适用于循环内每一项动作)**:

- 修复以"消除缺陷"为界——**不引入新抽象、新框架、新依赖**,不顺手重构与缺陷无关的代码,不改公共契约形态;
- 每项修复前先回答"**是否有必要实施**":缺陷是否真实可感(有 file:line 或复现证据,不是理论洁癖)、修复收益是否大于风险、是否存在更小的修法;任一答案为否,记录到报告待办池/观察项,**不实施**;
- 问题分级:P0(崩溃/数据损坏/核心功能失效)、P1(明显缺陷)、P2(优化机会)——**当前轮只修 P0/P1,P2 一律记档**;
- 区分"缺陷修复"与"锦上添花":后者转待办池记档,不进当前轮;
- 每轮结束在报告修订记录写一行:轮次、发现、修复、遗留,让循环可审计。
