# R16-A 核心服务与数据面专项审查报告（round-16）

> 审查基线 commit `2d64b5c0`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R16-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 差异化声明

- **基线漂移**：R15 审查基线（`08bdd98e`）以来仅一个代码提交 `76ef0b28`（R15 修复批：DEF-130/131，3 文件 +20/−7：`WidgetManager.Groups.cs` +14、两张契约测试 manifest 收缩）+ 台账提交 `2d64b5c0`（纯文档，8 文件）。`git diff 08bdd98e..2d64b5c0 --name-only`（11 文件）确认代码面变更全部落在该批内，其余全部源文件与 round-09~15 七轮深读时的版本逐字节一致，历轮全文审读结论按「文件零变更」直接继承。
- **本轮主攻方向**（按任务指令）：
  1. **round-15 修复批复核**——`76ef0b28` 全量 diff 逐行审读：DEF-130 的 6 条 manifest 死条目退役后两张表是否有残余死条目（对 185 个 manifest 键做「键 ↔ 文件存在性」全量机械校验）+ 断言逻辑方向核实；DEF-131 的 Merge 双侧 Cancel 插入位置、覆盖面、与全仓取消点清单的闭环性；
  2. **MergeWidgetsAsync 取消链 × 群组切换状态机全排列推演**——切换状态机 9 个状态（S1 未请求 → S9 结算后簿记）× merge 取消落点逐状态推演，含门池 Remove/Release 配对、capsule 身份双写、首帧等待 token 链接面；
  3. **盲区深读**——`WidgetManager.ZOrder.cs`（1,351 行，多轮定点但**本轮首次全文**）、`SearchPopupViewModel.cs`（1,525 行，**首次全文**）、`Contracts/` 2 文件全读、`App.Aot*Smoke` 抽样深化（`App.AotRecycleBinSmoke.cs` 703 行全文 + 27 装置清单复核）、TodoWidgetViewModel 五分部覆盖确认。
- **不再重复深读**：SettingsService、FileService 家族、DeskBoxDataBackupService、Cloud/WebDav/Updater、MusicVolume、ResilientJsonStore、TodoRecurrence、QuickCaptureService、DirectStartup 族、DesktopOrganization 引擎、LocalizationService、FolderWatcherService、SearchEngineService、WeatherService、EverythingSearchService、WidgetManager 非 ZOrder 各分部、Groups.cs 全文（R15 刚完成 2,771 行首次全文 + 本轮 +14 行 diff 逐行审读）等——round-06~15 已全文主审且基线无漂移，仅做台账锚点定位复核。

### 1.2 覆盖率声明

**全文逐行读（本轮新覆盖或修复批复核）**：

- `76ef0b28` 全量 diff（3 文件）逐行复核 + 触碰文件锚点重定位（Groups.cs 全部既有锚点 +14 行漂移逐一核对）
- `Services/WidgetManager.ZOrder.cs`（1,351 行，**首次全文**——含临时抬升租约/expanded 租约、raised-band guest 登记/清扫/代际守卫、交互泄漏看门狗、50ms 鼠标采样器、restore monitor、QuickReveal dismiss 队列、空闲 peer 归一化全路径）
- `ViewModels/SearchPopupViewModel.cs`（1,525 行，**首次全文**——含搜索代际/CTS 生命周期、结果池三列表、tab 重建、LoadMore 合并、推荐缓存 TTL、元数据任务门）
- `Contracts/IFileDropTarget.cs`、`Contracts/ITodoReminderPresenter.cs` 全读
- `App.AotRecycleBinSmoke.cs`（703 行全文——回收站精确恢复装置的六阶段状态机/证据结构/原生调用断言）
- 配套策略/协调器：`WidgetGroupSwitchRequestCoordinator.cs`（166 行全文）、`WidgetSurfaceSwitchGatePool.cs`（43 行全文）、`SearchResultRanker.GetIdentityKey` 契约段

**模式扫描 + 命中处精读**：

- TodoWidgetViewModel 五分部（3,019 行）：`async void` / 同步等待 / `Task.Run` / `File|Directory.Delete` / `new Thread|Timer` / `AllocHGlobal` / `Process.Start` 横向扫描——唯一命中 `DetailAndAttachments.cs:404` `File.Delete` 即 DEF-115 修复位点（删除前同 store 引用扫描），零新命中；确认 R12/R13 已完成五分部全文覆盖（TodoWidgetViewModel.cs/ItemOperations/EditingAndUndo/FilteringAndAppearance R12，DetailAndAttachments R13）
- 全仓取消点清单 grep 复核（`_widgetGroupSwitchRequests.Cancel` ×7 位点 + `CancelAll`）与成员移除调用图（`RemoveWidgetAsync:1817`、`FeatureWidgets.cs:554/1108` → `RemoveWidgetFromGroupAsync`）
- 两张契约测试 manifest 185 键「键 ↔ 文件存在性」机械校验（脚本见 §3.1）
- 台账锚点 grep 定点复核（§3.2）

**未覆盖及原因**：

- round-06~15 已全文主审的大文件：基线零漂移（diff 反向核实），仅锚点复核
- 12 语言 JSON（归 B 代理）；`WidgetLayerService.cs`（round-06~09 反复主审面，本轮经 ZOrder 分部调用点间接复核）
- `publish-aot-audit.ps1`（R12 起持续留档观察项，本轮未触碰）

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| O-9 家族（CurrentCulture 比较器与仓库 Ordinal 惯例不一致，纯展示排序） | `ViewModels/SearchPopupViewModel.cs:660`（`MergeLoadedPage` 合并页排序 `ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)`） | LoadMore 合并后的展示序兜底键；同方法内 Name 排序分支（:1012）用的是 OrdinalIgnoreCase，本位点为该文件内不一致的孤例；与 R12 已记录的 SearchResultRanker:63 / WidgetStackGroupingService:280 及 R15 的 Groups:679、ItemHydration:115 同水位。首次全文审读该文件时发现，此前各轮（含 Format-arity 与键位扫描）均未覆盖此行 |

（其余扫描命中均为已收录位点：`WidgetManager.ZOrder.cs:1318` `TrayLayerStateChanged?.Invoke` 与 `WidgetManager.Groups.cs:2299` `WidgetGroupsChanged?.Invoke` 即 round-08 S06 已收录的 DEF-101 家族位点，行号漂移 1329→1318 / 2285→2299，非新位点。）

---

## 3. 存量复核（范围内挂账条目现状）

### 3.1 round-15 修复批（DEF-130/131，`76ef0b28`）复核——本轮重点

| 条目 | 结论 | 当前树证据 |
|---|---|---|
| **DEF-130（两张契约 manifest 6 条死条目退役）** | ✅ 修复正确，**机械校验零残余** | ① 对 `ModuleBoundaryContractTests.cs` + `SettingsSliceOwnershipContractTests.cs` 全部 manifest 键做「键 ↔ 文件存在性」脚本化校验：**185/185 键全部指向现存文件，零死条目**；② `ShellDataObjectBuilder.cs=5` 条目已退役并留先例注释（DEF-127）；五个 OnboardingWindow 分部条目已退役并留注释（DEF-128，注释锚定提交号 `08bdd98e`）；③ `OnboardingWindow.Completion.cs=5` 的处置经核实为「条目退役 + 注释钉死」双落地：文件尚存（`OnLanguageChanged` 活方法）但当前树 facade 访问为 0，注释原文「(5) survives as legal slack: the file is live (OnLanguageChanged) with zero facade access today」——台账「合法 slack 钉死保留」的表述以注释形式实现，条目本体已退役；由于断言只对**实际发生违规的文件**反查 manifest，退役一条 0 违规条目在当前树零行为差异，若该文件未来新增 facade 访问则按 NEW 严格拦截（ratchet 收紧，语义正确）；④ 断言方向再证：`AssertViolationManifest`（:339-358）与 `FacadePassthroughAccess_OnlyShrinks` 均为 actual→manifest 单向遍历，从不校验「每条 manifest 条目 ↔ 文件存在」——退役后死条目类缺陷不再可能从这两张表产生 |
| **DEF-131（MergeWidgetsAsync 双侧 Cancel）** | ✅ 修复正确，全仓拓扑入口防线闭合（状态机全排列推演见 §3.2） | ① 取消位置正确：`WidgetManager.Groups.cs:766-774`，在取得 `_widgetGroupGate`（:737）**之后**、组解析（:749-754）与同组早退（:755-760）**之后**、chrome/成员数校验与任何拓扑变更**之前**——比同族位点（RemoveWidgetFromGroupAsync :1329 在取门前取消）更优，消除了「取消后阻塞等门」的窗口；② 双侧覆盖带 null 守卫（`sourceGroup is not null` / `targetGroup is not null`），双侧同 surface 时 Cancel 幂等（coordinator 按 groupId 移除单个请求）；③ 与 `:1329/:1520/:1741`、`WidgetManager.cs:2214`、`Surfaces.cs:219,229` 构成完整取消点清单（7 处，grep 全量复核）；④ 成员移除调用图闭环：`RemoveWidgetAsync:1817`、`FeatureWidgets.cs:554/:1108` 全部经 `RemoveWidgetFromGroupAsync` 间接取消，`DissolveAllWidgetGroupsAsync:118` 经 Dissolve 间接取消——**不存在第二个漏配入口** |

### 3.2 MergeWidgetsAsync 取消链 × 切换状态机全排列推演（DEF-131 修复有效性验证）

切换状态机状态划分（`WidgetManager.Groups.cs` 行号为当前树）：

| 状态 | 位置 | merge Cancel 落点行为 |
|---|---|---|
| S1 | 未 Begin | 无请求可取消，无影响 |
| S2 | `:986` `await switchGate.WaitAsync(request.CancellationToken)` | WaitAsync 立即抛 OCE → catch（:989-992）返回 false，finally 不 Release（gateEntered=false）——干净中止 |
| S3 | 门内校验段 `:994-:1014` | `:994` `ThrowIfCancellationRequested` 命中 → return false——干净中止 |
| S4 | 内容准备 `:1128` `PrepareContentSwitchAsync(…, request.CancellationToken)` | token 已链接，OCE 上抛至 `:1076-1083` catch →「superseded」返回 false，`finally :1134-1147` 正确吸收 loadingDelay——干净中止 |
| S5 | 准备后检查点 `:1161` `ThrowIfCancellationRequested` | 命中 → `:1076` → false——干净中止 |
| S6 | 首帧等待 `:1180`（`frameTimeout` 为**独立 CTS，未链接** request token） | **Cancel 不可达**：等待继续至首帧到达或 900ms 超时（`WidgetGroupFirstFrameTimeout`，:21-22）；随后进入 S7 |
| S7 | 身份结算 `:1198-:1202`（TransferCapsuleIdentity + ActiveMemberId + CommitSurfaceHost） | 无取消检查点（注释 :1172-1175 明示「视觉交换与身份提交之间不得被取消」的有意设计）——**不可中止** |
| S8 | `:1224-:1227` `transition.CompleteAsync(…, CancellationToken.None)` | 显式不可取消——不可中止 |
| S9 | 结算后簿记 `:1253-:1297` | 无取消面 |

推演结论：

1. **修复消除的交错面**：S2~S5（等待门、门内校验、内容准备、准备后检查点）原为切换在途的主要时长窗口（PrepareContentSwitchAsync 含内容物化，首帧等待前可达秒级）；merge 先取消后改拓扑后，这些状态下旧切换必然在触碰任何共享状态前退出，R15-A 描述的主竞态窗口被实质性关闭。
2. **残留边界（修复时已声明，非回归）**：S6~S9 与 merge 仍可交错——`frameTimeout` 未链接 request token，切换一旦越过 `:1161` 即进入「不可中止段」。最坏交错为「切换以源组成员为目标 × merge 退休源组」：切换在孤儿组对象上结算（`group` 为已被 `:848` 移出 Settings 的 sourceGroup 引用），merge 退休循环 `:904-912` 可在 `CommitSurfaceHost` 前后退休其正在提交的 persistentWindow，产生注册表瞬时身份错挂；以目标组成员为目标时，merge 退休循环跳过 `activeTargetId`（= 切换的 previousActiveId，其窗口正是被提交的 persistentWindow），错挂面更窄。两种终态均被下次导航/归一化自愈，与 R15-A 影响描述（「最坏瞬时身份错挂，DEF-123 家族同级，可自愈」）逐点吻合——**属修复时接受的最小修法边界，不构成修复回归，不重复立案**。
3. **门池配对证伪**：merge 在 `:920` 对源 surface 执行 `_widgetSurfaceSwitchGates.Remove(...)`，而被取消/在途切换的 finally（`:1090`）随后 `switchGate.Release()`——`WidgetSurfaceSwitchGatePool.Remove`（:25-36）**显式不 Dispose 移除的门**（注释：「a completed caller may still execute its final Release」），Release 落在仍被局部引用的 SemaphoreSlim 上，无 ObjectDisposedException 面。配对正确，留档防重复怀疑。

### 3.3 挂账条目锚点复核（基线零漂移声明下，触碰文件锚点重定位 + 代表性 grep）

| 编号/条目 | 现状 | 当前树证据 |
|---|---|---|
| DEF-048（WeatherCodeMapper 声明小写比较未实现） | 维持 | `WeatherCodeMapper.cs:99-100` 注释「Normalize: trim, lowercase for comparison」+ `description.Trim()` 无 ToLower |
| DEF-049（MSN icon 29→29 恒等映射） | 维持 | `WeatherCodeMapper.cs:209` `29 => 29` |
| DEF-053（内容刷新任务引用竞态） | 维持 | `SearchEngineService.cs:511-517` finally lock 内无条件 `_deskBoxContentRefreshTask = null` |
| DEF-076（遗留 settings.json 一次性迁移非原子） | 维持 | 未触碰（R13 锚点 `SettingsService.cs:941` 裸 WriteAllTextAsync） |
| DEF-078（SMTC `Try*Async` 裸调簇 / async void 静默失效簇） | 维持 | `MusicSessionService.cs` Try* 族裸调仍在（grep 计数 17）；各家族位点未触碰 |
| DEF-080（ar-SA 天气解析/缓存错乱） | 维持 | `WeatherService.cs` `ToString("yyyy-MM-dd` 4 处无 InvariantCulture |
| DEF-079 / DEF-082 / MEM-01 / MEM-02 / EXC-06 / THR-06 / DEF-070 家族 / DEF-101 家族 | 维持 | 所在文件零漂移；MEM-02 两位点（`IQuickCaptureClipboardReader.cs:99,124`）本轮 grep 再证 |
| **DEF-070 家族写侧（R15 §2 新位点，触碰文件重定位）** | 维持 | `WidgetManager.Groups.cs:868`（回滚 `WidgetGroups = groupSnapshot` 整表替换，原 :854 +14）；`WidgetGroupMutationSnapshot.Restore` 段同步后移 |
| **DEF-092 家族（R15 §2，触碰文件重定位）** | 维持 | `Groups.cs:2783` `IsEmpty => false` + `:2454` 消费点 `state is null \|\| state.IsEmpty`（原 :2769/:2440 +14），死条件分支维持 |
| **O-9 家族 Groups 位点（R15 §2，触碰文件重定位）** | 维持 | `Groups.cs:679` `GetWidgetGroupJoinTargets` 合并候选 `CurrentCultureIgnoreCase`（插入点之前，行号不变） |
| **DEF-101 家族 Groups/ZOrder 位点** | 维持 | `Groups.cs:2299`（原 :2285 +14）、`ZOrder.cs:1318`（原 :1329，round-08 S06 收录） |
| O-27（TodoWidgetStore.RebaseManagedAttachmentPathsAsync 分离 Load/Save） | 维持 | `TodoWidgetStore.cs:639`，机制未变 |
| O-30~O-33（R15 观察项） | 维持 | O-30 `OnboardingWindow.Completion.cs` using 簇未变；O-31 `GlanceWidgetStore.SaveAsync` 仍不调 `MigrateLegacyStoreIfNeededLockedAsync`（Load/Update 两处调用 :92/:131，Save 无）；O-32 `static-baseline.json` `async_void_count=249` 仍为删除前值（slack 语义合法通过）；O-33 dwell 字段 `Groups.cs:29-31`（+14 内 +1 漂移）、DissolveAll 循环语义未变 |
| O-1~O-26（R9~R13 观察项） | 维持 | 所在文件零漂移，按「逐字节一致」继承 |

---

## 4. 新发现问题清单

（本轮无新立案。round-15 两项修复经逐行复核与全排列推演均正确落地；四个盲区深读面除 §2 一处已知家族位点外零新增。）

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-34 | MergeWidgetsAsync 的取消落在校验之前：`76ef0b28` 插入的双侧 Cancel（:766-774）之后仍有四条不触及拓扑的拒绝路径（chrome 解析失败 ：780-793、合并策略拒绝 ：795-807、成员超上限 ：815-821、Promote 失败回滚 ：866-885），这些路径会留下「切换被取消但合并未发生」的瞬态——在途切换本轮不完成，下次悬停/滚轮手势重新 Begin 自愈。与同族入口（RemoveWidgetFromGroupAsync 在取门前即取消、同样存在取消后拒绝路径）完全同水位，属既定模式的固有代价而非本次修复引入；仅留档，勿误判为 DEF-131 回归 | `Services/WidgetManager.Groups.cs:766-821,866-885` | 高（机制）/ 极低（触发面：拖拽合并被拒 × 同 surface 切换在途的双毫秒级重叠） |
| O-35 | R15-A 对 DEF-130 的修法建议包含一个结构性守卫（断言「manifest 每个键在 src/DeskBox 下存在对应文件」），本次整改未实施——两张表对「未来删除文件忘记退役条目」这一缺陷类别仍无测试级防线（本次是靠人工复核 + 机械校验补上的）。一行守卫用例即可闭合该类别，建议随下一批卫生提交补入 | `tests/DeskBox.Tests/ModuleBoundaryContractTests.cs`、`SettingsSliceOwnershipContractTests.cs` | 高 |
| O-36 | `QueueIdleWidgetZOrderNormalization` 的空闲归一化在「有窗口处于 compact 动画渲染中」分支（:572-582）以 120ms 无上限重排队，而相邻的「bounds transition 活跃」分支（:551-570）有 `MaxIdleNormalizeAnimationDefers=15` 次封顶后放弃——两分支防护策略不对称。compact 动画均有界完成，实际循环必然终止；仅当组合层动画异常悬挂时退化为 120ms 一次的轻量轮询（每拍仅做谓词检查，无 SetWindowPos）。属防御深度不一致的卫生面，非缺陷 | `Services/WidgetManager.ZOrder.cs:551-582` | 高（机制）/ 无（实际后果） |

---

## 6. 统计

- **新立案**：0 条 —— P0×0，P1×0，P2×0，P3×0。连续第十一轮 P0/P1 = 0；立案数收敛趋势延续（R11→R16 = 3→5→2→3→2→0）
- **观察项**：3 条（O-34 ~ O-36）
- **已知模式新位点**：1 行（O-9 家族 ×1：SearchPopupViewModel.cs:660）
- **round-15 修复批复核（本轮重点）**：DEF-130/131 逐行复核**全部正确落地、零回归**——①DEF-130：185/185 manifest 键文件存在性机械校验通过、死条目清零、断言单向语义核实、`Completion.cs=5` 以注释钉死（ratchet 收紧语义正确）；②DEF-131：取消位置优于同族（门内、拓扑变更前）、双侧覆盖、全仓 7 处取消点清单与成员移除调用图双闭环；切换状态机 S1~S9 全排列推演确认 S2~S5 交错面被关闭，S6~S9 残留与修复时声明边界逐点吻合（非回归）；门池 Remove/Release 配对经源码注释与调用链双证
- **盲区盘点结论**：`WidgetManager.ZOrder.cs`（1,351 行）首次全文审读零立案（租约/代际/有界重试/封顶放弃纪律良好）；`SearchPopupViewModel.cs`（1,525 行）首次全文审读除 1 处 O-9 位点外零新增（搜索代际、CTS 生命周期、`_uiContext.Post` 封送、元数据任务门均为正面范式；DEF-047 位点 :699-715 在位维持）；Contracts 2 文件为纯接口声明；`App.AotRecycleBinSmoke.cs` 703 行装置全文深读零立案（六阶段状态机与证据结构自洽），其余 26 装置三向一致性结论自 R15 继承；TodoWidgetViewModel 五分部确认 R12/R13 已全文覆盖、本轮模式扫描零新命中
- **证伪留档**：4 项（WidgetSurfaceSwitchGatePool.Remove 不 Dispose 门——merge 退休 surface 后在途切换 Release 无 ODE 面；`SearchResultRanker.GetIdentityKey` 恒返回非空字符串——MergeLoadedPage 字典键无 null 风险；ZOrder:1318 / Groups:2299 裸广播为 round-08 S06 已收录位点仅行号漂移；S6 首帧等待 frameTimeout 不链接 request token 属修复时声明的结算段不设防语义，非新缺口）
- **总体结论**：连续第十一轮 P0/P1 = 0，本轮零立案。round-15 修复批（唯一基线漂移）质量良好：manifest ratchet 恢复与现实一致，群组拓扑取消防线经全排列推演确认闭合、残余边界与修复声明一致。本轮四个盲区（ZOrder 分部、SearchPopupViewModel、Contracts、AOT 回收站装置）深读均未发现可立案位点，核心服务与数据面维持历史最高加固水位。
