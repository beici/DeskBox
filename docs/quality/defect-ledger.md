# DeskBox 缺陷台账（更新至 2026-09-02 R2 Round 1）

> 维护规则：每轮迭代开始时从本表选取输入；每个问题闭环（修复 + 回归验证）后更新状态。
> 优先级定义：P0 = 生产环境核心功能失效；P1 = 高频可复现、明显影响体验；P2 = 低频或边界场景；P3 = 代码质量/卫生。

---

## 新增缺陷（R2 Round 1）

| 编号 | 标题 | 状态 | 优先级 | 根因分类 | 位置 | 说明 | 来源 |
|---|---|---|---|---|---|---|---|
| **DEF-034** | WidgetLayerService 双重 lock 竞态窗口期 | **已修复（663c593）** | P1 | 线程同步 | `src/DeskBox/Services/WidgetLayerService.cs:919-927` | 两次 lock(s_desktopLayerLock) 之间无原子保证；TryApplyMinimalWindowMoves 返回 false 后到第二次 lock 前 z-order 可能变化，IsWindowChainAlreadyHighestToLowest 短路未重检查；导致多余 DeferWindowPos 事务，极端下 DWM 微卡顿 | F8 Round 1 task-0 |
| **DEF-035** | StoreStartupService.GetStartupTask() UI 线程阻塞 | **已修复（663c593+3f2504d）** | P2 | 线程模型/启动性能 | `src/DeskBox/Services/StoreStartupService.cs:123` | StartupTask.GetAsync().AsTask().GetAwaiter().GetResult() 在 UI 线程同步等待 Windows Runtime；Store 构建 + 首次启动时 UI 冻结风险；非 Store 构建不受影响 | F8 Round 1 task-2 |
| **DEF-036** | Step4StartupToggle_Toggled async void 内同步调用 GetStartupTask | **已修复（cf0fadb+4701b39）** | P2 | 线程模型 | `src/DeskBox/Views/OnboardingWindow.Hotkey.cs:317` | Onboarding 步骤 4 切换自动启动开关时同步调用 StartupService.SetEnabled → Store 路径触发 DEF-035；应改为 async Task + SafeFireAndForget | F8 Round 1 task-2 |
| **DEF-038** | 刷新代际护栏未覆盖「用户拨动 vs 在途刷新」 | **已修复（917bcf9，Toggled 入口递增代际）** | P2 | 线程模型/UI 时序 | `src/DeskBox/Views/OnboardingWindow.Hotkey.cs:325` | 代际护栏只拦「新刷新 vs 旧刷新」；用户拨动不递增代际，Store 渠道 Pending 收敛窗口内在途回调仍回弹 IsOn+覆写 AutoStart。R2 Round 2 收敛审查（deleg_8d8b2d7b）NO-GO 指出，一行修复后闭环，CI run 917bcf9e 全绿 | F8 Round 2 收敛审查 |
| **DEF-039** | FileInfo.FileModified 在 es-ES/fr-FR/ru-RU 三语种携带非法 .NET 日期格式字母（aaaa / aaaa/M/j / гггг/М/д ЧЧ:мм），string.Format 按字面量渲染 → 文件格子副标题时间戳显示假字（俄语用户看到 гггг/9/2 ЧЧ:мм）| P2 | 1.3.8 (f515632) 起 | Models/WidgetItem.cs:111 LocalizeFormat + Strings/{es-ES,fr-FR,ru-RU}.json | R3 主线亲审（subagent 429 后接管），Python 全 12 国占位符扫描 | ✅ 已修复 41d70e7：三国改 dd/MM/yyyy·dd.MM.yyyy；新增契约测试 JsonLocales_DateTimeFormatSpecifiers_OnlyUseValidNetFormatLetters 锁死格式字母白名单，闭合索引级比对盲区 |
| **DEF-040** | StackSurface PropertyChanged 订阅生命周期缝隙：容器销毁路径 Unloaded 不保证触发，WidgetStackItem.PropertyChanged 长期持有 content 实例（ItemVisuals.cs:973-1002，EVT-02 同族新实例）| P3 | R4 收敛审查 | FileSurfaceContent.ItemVisuals.cs:973-1002 | deleg_96eac299 | 📌 挂账（加固即可：handler 内查 XamlRoot/IsLoaded 或弱引用）|
| **DEF-041** | _stackProjectionTransitionPending 无 Reuse/Dispose 复位点：投影切换入队后 Reuse/Dispose 致标志残留（同一调度循环内自愈，毫秒级窗口）| P3 | R4 收敛审查 | FileSurfaceContent.SelectionAndMenus.cs:1335-1368 | deleg_96eac299 | 📌 挂账（ResetStackInteractionVisuals 补复位）|
| **DEF-042** | HandleNativeFileDropAsync 前置段（插入点捕获/屏幕坐标/排序数学）位于 try 之外，async void 面（QueueNativeFileDropImport TryEnqueue）残留缝隙；全局兜底 EVT-03 已覆盖 | P3 | R4 收敛审查 | FileSurfaceContent.xaml.cs:2949-3004 | deleg_96eac299 | 📌 挂账（仅记录不处置）|
| **DEF-043** | Todo 数据双写者无串行化：TodoWidgetViewModel.SaveAsync（29 调用点）与 TodoReminderService（后台 3 写点）并发读改写同一 TodoWidgetStore，旧快照覆盖新数据（勾选回滚/重复提醒）；对照 Glance/Weather/QuickCapture 均有 _gate，唯独 Todo 无 | P1 | F9 从零全量审查 | TodoWidgetViewModel.FilteringAndAppearance.cs:68-75 × TodoReminderService.cs:259/321/417 | 主线亲验实锤 | ✅ 已修复 fed3c63+3305d24+c56fcdb+9deb194+c10d333：Store 路径门控+MutateAsync；提醒服务 3 写点改原子变更；经 App 中继 + TodoWidgetContent Loaded/Unloaded 订阅，VM ApplyExternalStoreChange 合并后台写入|
| **DEF-044** | Everything_QueryW 同步阻塞无超时，_nativeGate 全程持有；Everything 无响应时搜索子系统挂死且 CT 无效 | P2 | F9 | EverythingSearchService.cs:438/:211 | SA-B 亲验实锤 | ✅ 已修复 fed3c63：Query 与 3s 超时竞速，在途委托独占门释放，超时返回空页+query-timeout 诊断 |
| **DEF-045** | 未启用 Everything 时每次击键执行全量安装检测（注册表双视图+进程枚举，无 TTL） | P2 | F9 | EverythingSearchService.cs:190-196/:84-86 | SA-B 亲验实锤 | ✅ 已修复 fed3c63：Query 与 3s 超时竞速，在途委托独占门释放，超时返回空页+query-timeout 诊断（结果缓存 30s）|
| **DEF-046** | 天气风向负值负模索引越界（UI 线程，畸形 MSN 响应触发） | P2 | F9 | WeatherWidgetViewModel.DataProcessing.cs:626-630 | SA-B 亲验实锤 | ✅ 已修复 fed3c63：Query 与 3s 超时竞速，在途委托独占门释放，超时返回空页+query-timeout 诊断（取模修正+NaN 防护）|
| **DEF-047** | CTS 取消后同步 Dispose 在途注册回调 ODE（日志噪音） | P3 | F9 | SearchPopupViewModel.cs:696-712 | SA-B | 📌 挂账 |
| **DEF-048** | WeatherCodeMapper 声明小写比较未实现，匹配大小写敏感退化 | P3 | F9 | WeatherCodeMapper.cs:98-99 | SA-B | 📌 挂账 |
| **DEF-049** | MSN icon 29→29 非法 WMO 码恒等映射笔误 | P3 | F9 | WeatherCodeMapper.cs:207 | SA-B | 📌 挂账 |
| **DEF-050** | 文件格子图标列数被统一槽宽机制挤掉（用户报告：右侧大片空白、密度宽松时甚至 1 列） | P2 | 合并 1.4.9 后实机 | 三层根因（容器偏移量实测定位）：①**主因** ItemsWrapGrid 在行宽恰好等于 columns×ItemWidth 时把最后一项换行——itemWidth=73.60、panelW=294.40 实测排布 x=0/73.6/147.2 后换行，右侧空出整槽；②面板 realize 早于视口生效（viewport=0）走回退槽宽，而 0→294.4 的 SizeChanged 在 Loaded 才订阅被错过，部分格子永久卡窄槽；③槽宽用 Ceiling(内容宽) 在临界宽度再丢一列 | 实机截图+容器 ActualOffset 实测 | ✅ 已修复 |
| **DEF-050** | MSN 日期解析失败仍追加空行，UI 错位 | P3 | F9 | WeatherService.cs:498-504/559-566 | SA-B | 📌 挂账 |
| **DEF-051** | 远程 JSON 无大小上限（Glance/Weather 数处） | P3 | F9 | GlanceImageService.cs:614-620、WeatherService.cs:83 | SA-B | 📌 挂账 |
| **DEF-052** | FileMetaService LRU 软缺陷：失败 null 永久缓存不重试、在途不淘汰 | P3 | F9 | FileMetaService.cs:236-304 | SA-B | 📌 挂账 |
| **DEF-053** | DeskBox 内容刷新任务引用竞态（旧 finally 清新引用） | P3 | F9 | SearchEngineService.cs:485-505/553-559 | SA-B | 📌 挂账 |
| **DEF-054** | Saka/Bangla 历固定起点近似漂移 | P3 | F9 | GlanceTraditionalCalendarService.cs:225-258 | SA-B | 📌 挂账（标注近似或表驱动）|
| **DEF-055** | 窗口壳层 Closed 后 zombie 回调系统面：SettingsWindow ~20 处 async void 无 _isClosed 复检 + OnboardingWindow.xaml.cs:131 Closed 内同步清理（F7-B5/EVT-03 同族） | P3 | F9 | SettingsWindow.*.cs 全部 | SA-W3+主线证伪降级 | 📌 挂账 |
| **DEF-056** | 胶囊悬停自动展开掉帧/卡顿（用户报告：展开不够丝滑、偶尔卡顿） | P2 | 用户报告 + 实机 perf 日志取证 | 三层根因：①**主因** `RaiseForExpandedState` → `AcquireExpandedWidgetLayer` → `EnsurePeerOrderHighestToLowest` 在 `StartBoundsTransition` 前一条语句同步下发**全量 12 窗口** DeferWindowPos 批，`EndDeferWindowPos` 迫使 DWM 重采样全部亚克力格子 → 165Hz 下实测单帧 110–195ms、46 帧丢 34；同一格子的**收起**动画（批次延后到动画结束）实测 dropped=0/maxFrameMs=7.8，构成对照实验；②`ResolveSkipForFrameRate` 用四舍五入除数，165Hz+60fps 档解析为 skip=3=**55fps**，低于档位标称值也低于项目 60fps 红线，且窗口几何按 55Hz 步进而 Composition 透明度/缩放按 165Hz 插值；③几何时钟在 `PrepareCompactTransition`（Composition 动画真正启动处）**之前**取时间戳，两条时间线相位错开整个准备开销 | `[Perf] CompactAnimation` + `[ZOrder] Window order minimized` 时间戳重合，含前后对照统计 | ✅ 已修复：EnsurePeerOrder 改先验证→单窗口抬升→仅兜底全量；帧率档位改「不低于所选帧率」；时钟起点与 Composition 对齐。实测展开 median maxFrameMs 17.5→8.8ms、dropped median 3→0、≥90ms 停顿 27/160→2/10（仅剩首两次冷 JIT） |
| **DEF-057** | 点击格子标题栏空白处出现多个格子边缘闪烁；空闲层级整理永不收敛 | P2 | 用户报告 + 实机 z-order 日志取证 | 双根因：①**owner 组连带重排** — 动态层级模式下静置格子以 Explorer `SHELLDLL_DefView` 为 owner（Win+D 存活），而 `Win32Helper` 的 6 个 z-order 原语（`ClearWindowTopMost`/`SetWindowTopMost`/`BringWindowToFront`/`BringWindowTemporarilyToFront`/`SetWindowToBottom`/`SetWindowToDesktopLevel`）与 `TryAttachToDesktopIconLayer`/`RestoreOriginalOwner` 两处裸 `SetWindowPos` **均未传 `SWP_NOOWNERZORDER`**，Windows 顺带移动共享 owner，把同 owner 的其余 11 个格子一起重排 → 多格子亚克力背景被重采样即「边缘闪烁」；`WidgetLayerService` 的 peer 排序原语本就传了该标志，属原语层遗漏；②**最小移动规划器锚点顺序错误** — `TryApplyMinimalWindowMoves` 按目标索引**降序**下发 mover，而 `SetWindowPos` 只能 insert-after，锚点（目标前驱）索引更小、在批中更晚落位，导致整批报成功但链表并未到位（实测 `moved=9→8→7→6` 逐轮递减、永不为 0，每轮都再闪一次）；③`IsWindowChainAlreadyHighestToLowest` 对 `HWND_TOP` 边界无条件返回 false，零写入快路径被禁用 | `[ZOrder] Window order minimized moved=/kept=` 逐轮序列 + LIS 反例手算（目标 ABC / 现状 CBA 降序下发得 ACB） | ✅ 已修复：原语补 `SWP_NOOWNERZORDER`（**HWND_BOTTOM 除外**，见下）；LIS+move 规划抽出为纯策略 `WidgetPeerOrderMovePlanner` 并改**升序**下发；HWND_TOP 边界改真实校验。实测每轮固定 `moved=1 kept=11`、启动后出现 `Window order already correct`（零 SetWindowPos），12 格子 z-order 秩 3–14 连续、远高于 `Progman@35` |
| **DEF-058** | DEF-057 修复过程自伤：`HWND_BOTTOM` 也加了 `SWP_NOOWNERZORDER`，格子收起后有概率暂时消失/变空白 | P1 | 用户实机报告（修复中途） | 抬升（TOPMOST/NOTOPMOST/TOP）永远把被拥有窗口留在 owner 之上，因此屏蔽 owner 移动是安全的；但**下沉到 HWND_BOTTOM 只因 Windows 会把 owner 一起下移才保持可见**，屏蔽后格子落到桌面壁纸之下，渲染为空白直到后续某次 z-order 整理把它抬回。日志侧证：`[ShowDesktop] Self-heal verified, nothing to restore reason=minimize-storm` 连续触发而 `visibleWidgets=12`（逻辑可见、物理被壁纸遮住） | 用户报告 + minimize-storm 自愈watcher 触发记录 | ✅ 已修复：拆分 `ZOrderRaiseFlags`（含 NOOWNERZORDER）与 `ZOrderBottomFlags`（不含），`TryAttachToDesktopIconLayer` 的 placeAtBottom 分支同步去除该标志；契约测试锁死「抬升必须带、下沉必须不带」 |
| **DEF-059** | 点击格子标题栏空白处，相邻两格子的相对边缘各闪一次（用户截图标注：办公文档右缘 + 网络左缘，纵向仅在两窗重叠区间） | P2 | 用户报告 + 实机日志/几何取证 | **一次不移动的点击做了完整的「抬升→回落」往返**：`TitleBarGrid_PointerPressed` 立刻调 `ActivateAllVisibleWidgetsFromTitle`（owner 分离 + TOPMOST + NOTOPMOST + HWND_TOP + SetForegroundWindow），`BeginWindowDragCore` 又接着做 `SimplifyBackdropForInteraction` + `ElevateForInteraction`（第二次 TOPMOST 脉冲）+ `ResizeGuideOverlay.BeginDrag`（枚举全部格子边界）；释放时 `RestoreTemporarilyRaisedWidgetsToDesktopLayer` 再把 owner 挂回并下沉，随后 120ms 的 `Idle peer normalize` 又对 12 窗口重排一次。两个格子矩形相距 14px（实测 1721 vs 1735），DWM 阴影跨越该间隙落在邻居边缘上，**每次相对次序翻转都要重绘两侧阴影** → 用户看到的两条竖带闪烁。用户连点 6 次，日志显示 6 组完全相同的事务链，而系统激活只在第 1 次发生（`pointerActivation=True` 仅 1 条），证明闪烁来自程序自身的往返而非系统激活 | 日志 19:01:33–34 六组 `TemporaryRaise acquired/owner detached/TitleActivatedAll/owner attached/TemporaryRaise restored/Window order minimized moved=1` + 窗口矩形与截图黄框像素反算 | ✅ 已修复：`BeginWindowDragCore` 拆成「arm（按下）/engage（越过 4px 阈值）」两段，抬升、背板降级、吸附会话、胶囊栏拖拽、格子组抬升全部搬到 `EngageWindowDrag`；释放侧的层级恢复/背板刷新/边界持久化按 `wasEngaged`/`hasMoved` 门控。实测标题栏空白连点 3 次后日志 `[ZOrder]`/`[WidgetLayer]` **零条**（原为每次 6 条）。**行为变更（有意）**：不移动的点击不再抬升格子（旧行为的抬升在释放时即被撤销，本无留存价值），点击后的层级仍由系统激活 + 失焦恢复链路负责 |
| **DEF-060** | 悬停自动展开动画「依然不够丝滑」：会话内每个格子首次展开/收起明显跳一下（用户二次报告，重点项） | P2 | 用户报告 + 实机逐帧取证 | 几何时间线用**绝对经过时间**解析进度：`progress = elapsed / duration`。冷格子首次展开时 `PrepareCompactTransition` 揭开展开态可视树，其**首次光栅化**同步阻塞 UI 线程 117–176ms（实测 `firstFrameMs=122.3/175.9/117.1`），于是第一个能落地的几何帧一上来就把进度算到 0.44，窗口**直接跳到接近半开**，整段 265ms 动画只剩 6/46 帧可用；中途停顿同理（`maxFrameMs=58–103`）被折算成一次大跨步。设置成本被证伪：新增 `CompactTransitionSetup` 埋点实测 totalMs 仅 0.5–1.6ms（presentation/freeze/refreshRate/border/prepare 全部 ≤1.2ms），非根因；暖态（第二次起）本就 dropped=0、maxFrameMs 7.7–10.5，说明问题**只在首次光栅化**这一段 | `[Perf] CompactAnimation frames=/dropped=/maxFrameMs=/firstFrameMs=/stalledMs=` 冷暖两轮对照 + `CompactTransitionSetup` 分项埋点 | ✅ 已修复：新增纯策略 `WidgetCompactTransitionProgressPolicy`，进度改为**逐帧限幅累计**（单帧最多推进 2 个提交间隔），停顿被吸收成「动画略微变长」而非跳步，累计吸收上限 `clamp(duration×0.75, 60, 220)ms` 后回归真实时间以免拖沓，看门狗同步加上该预算；Composition 淡入淡出改为**首个已提交几何帧**才启动（原在 Prepare 处启动，正好跑在那段光栅化停顿里），并在吸收 ≥12ms 停顿时按剩余区间 `ResyncCompactTransitionFades` 重发以保持两条时间线同相；顺带去掉逐帧 4 个闭包分配。实测冷态 File 展开 **frames 6→42**、QuickCapture 收起 32→44、Search 收起 26→42（全程有帧可画），暖态不变 |
| **DEF-061** | 胶囊收起动画尾段发卡，且完全收起瞬间相邻的几个格子一起变暗闪一下再恢复（用户报告） | P2 | 用户报告 + 实机 z-order/perf 取证 | 收起末尾 `RestoreLayerAfterExpandedState` 无条件调用 `MoveToDesktopBottom(HWnd)`，它经 `TryAttachToDesktopIconLayer(placeAtBottom:true)` 下发 `SetWindowPos(HWND_BOTTOM)`——这是**唯一必须允许 Windows 连带移动共享 owner 的 z-order 调用**（屏蔽会落到壁纸之下，见 DEF-058）。owner（Explorer 桌面视图）一动，挂在它下面的 12 个格子全部重排，**每个亚克力格子都要重采样背板** → 用户看到整组变暗一帧；同一批 DWM 工作压在收起动画最后一帧之后，尾段因此发卡。收起末尾其余结算成本已埋点证伪：`CompactCollapseSettle totalMs=1.3–2.7ms`（shellReset/viewState/surface/hover 分项全部 ≤1.4ms）| `[WidgetLayer] Desktop owner attached bottom=True` + 随后 `Idle peer normalize count=12 moved=1~3`；`CompactCollapseSettle`/`CompactLayerRestore` 埋点 | ✅ 已修复：新增 `Win32Helper.PlaceWindowBelow`（走 `ZOrderRaiseFlags`，含 `SWP_NOOWNERZORDER`）+ `WidgetLayerService.TryReturnToRestingBandBelow`（owner 挂回但不置底，拒绝 topmost 锚点）+ `WidgetManager.TryReturnWidgetToRestingBand`（按空闲顺序策略取「本该压在它上面的那一个」作锚点，一次 insert-after 直接落到最终槽位）；`MoveToDesktopBottom` 降级为无锚点时的兜底，已在桌面底层时连这一次写入都省掉。实测静置态悬停扫掠：`bottom=True` **0 次**、`Window order minimized` **0 次**、`Resting band rejoin` 逐次出现、收起动画 frames=46 dropped=0 maxFrameMs 7.9–8.5 stalled=0 |
| **DEF-062** | 标题栏右键「边距设置」的参考系不符合预期：用户要的是「相对最近的格子/图标/文件夹」的四边边距，实际只把其他格子当参考、其余情况退回工作区边缘，且界面不显示每条边到底在参考什么、默认还停在「统一边距」单值模式 | P2 | 用户需求研判 | 三处缺口：①`ResolveSideBoundary` 的候选集只有 `GetOtherVisibleWidgetRects`，**桌面图标/文件夹几何在整个仓库中不存在**（只有 `DesktopBlankHitTest` 的 `LVM_HITTEST` 命中测试，无 `LVM_GETITEMRECT`/`IFolderView`）；②垂直重叠容差是裸 8 物理像素，未按 DPI 缩放（吸附引擎的阈值是按 DPI 缩放的，两套标准不一致）；③几何逻辑是 `WidgetWindowBase` 分部类里的 `private static`，只能用源码文本断言，无法单元测试 | 需求 + 代码走查（`WidgetWindowBase.TitleAppearance.cs:606` 候选集、`:624` 裸容差） | ✅ 已实现：新增 `DesktopIconGeometryService`（复用 `DesktopBlankHitTest` 的跨进程模式，在 Explorer 内分配远端 RECT 缓冲，`LVM_GETITEMCOUNT` + `LVM_GETITEMRECT`/LVIR_BOUNDS，`ClientToScreen` 转屏幕坐标，1.5s 缓存、512 项上限、120ms `SMTO_ABORTIFHUNG`；桌面视图查找复用 `WidgetLayerService.GetDesktopIconViewHandle()` 以继承「绝不强制创建 WorkerW」的登录期安全规则）；几何抽成纯策略 `WidgetMarginReferenceCalculator`（四边最近邻居：格子 + 图标，仅在该侧无物时退回工作区，并回报参考类型；容差改按 DPI 缩放）；对话框默认「分边设置」，每个输入框标题显示「上 · 相对最近的桌面图标」并在预览移动后自动重算，另加一行说明；12 语言各新增 5 个键。实机验证：跨进程读到 **24 个桌面图标矩形**；回归 3236/3236 |
| **DEF-063** | 「边距设置…」对话框显示不全：用户只看到「上/左」两个输入框，以为没有右/下设置（用户报告 + 截图） | P2 | 用户报告 + 实机几何取证 | 编辑器是开在**格子自己的 XamlRoot** 上的 `ContentDialog`，而格子只有 **391×408 物理像素**（1.25 缩放 = 313×326 DIP，最小允许尺寸更是 50×50 DIP）。`ContentDialog` 的默认模板既**不滚动内容**、宽度下限又来自共享主题资源（`ContentDialogMinWidth`=320），于是对话框被宿主窗口直接裁掉：2×2 网格的第二行（下/右）整行落在窗口下沿之外，只剩第一行可见；四个输入框的标题还把「上（像素）· 相对最近的格子」整句塞进 `TextBox.Header`，换行后把每行高度又抬高一倍，加剧溢出。窗口化 XAML Popup 也不是选项——它不拿 Win32 焦点（同仓库 `StackPopoverInlineRenameWindow` 的注释即为此结论），文本输入会失效 | 用户截图（只有上/左）+ `EnumWindows` 实测格子矩形 391×408 + 主题下限 320 | ✅ 已修复：新增纯策略 `WidgetDialogLayout`（工具窗口尺寸/置位数学：请求内容盒 → 可读区间钳制 → 工作区钳制 → 居中于宿主格子并留 8px 边距）+ 共享宿主 `WidgetToolDialogWindow`（真实 WinUI 窗口、系统标题栏、always-on-top、`IsShownInSwitchers=false`、内容包在 ScrollViewer 里、Enter=保存/Esc=取消、`TaskCompletionSource<bool>` 回传结果）+ `WidgetWindowBase.ToolDialog` 共享入口。边距编辑器与「自定义颜色」双双改走该宿主，**预算从显示器工作区来、不再受格子尺寸限制**；分边面板改 Auto 行 + 单行标题（参考对象降为副标题 + 完整句子进 tooltip）+ 窄宿主自动改单列；顺带修掉两个连带缺陷：预览改 **160ms 防抖**（原来每个按键都移动窗口并落盘，"150" 的首位 "1" 会先把格子甩出去，按键还可能在这段布局风暴里丢失），以及**程序化写回被当成用户编辑**（WinUI 对程序化 `Text` 写入也抛 `TextChanged`，且可能延迟到写入返回之后，旧 `suppress` 布尔挡不住 → 同步回写把「下=0」标记成用户编辑并真的把格子拖到邻居身上；改为记录编辑器自己写入的值并识别回声）。实机验证：编辑器窗口 525×535 物理、四边输入框与保存/取消全部可见无滚动；输入 33 一次落位（y=107→85，边界 52+33）、非焦点边实时重算（下=22 正确）、取消回位、保存持久化；颜色选择器 475×785 全显；回归 3253/3253 |
| **DEF-064** | 边距编辑器把「下 / 右」报成屏幕边缘，而那两侧明明有别的格子（用户报告：下=445「屏幕边缘」） | P2 | 用户报告 + 实机复现 + verbose 日志 | 测量主体错了：胶囊必须先**悬停展开**才会出现标题栏（右键菜单也才存在），而展开后的临时矩形**把下方的胶囊整段盖住**——实测 `(2169,590,391,52)` 的胶囊悬停后变成 `391x345`，底边 935，于是「下」这一侧在其横向跨度内确实没有严格位于下方的对象，退回工作区边缘得 `1380−935=445`，与用户截图完全一致；「右」侧格子本来就贴着 2560 的工作区右缘，报屏幕边缘是正确的。同一个错误主体还会污染**应用**路径：输入的数值会按展开矩形去贴一个用户从未看到的边界 | 实机复现 + 新增 `[WidgetMargin] Reference resolve subject=/live=/四边距离与参考类型` 日志 | ✅ 已修复：边距的测量与应用统一改用**静止几何**为主体——新增 `WidgetWindowBase.RestsCollapsed`（Smart 恒为收起、Click 看持久化的 `Config.IsCollapsed`，与启动初值共用同一判定，不再复制 switch）+ `ResolveMarginSubjectBounds`（静止收起且当前不在胶囊尺寸时，用 `GetCompactBounds(live)` 推导胶囊矩形作为主体）；`ApplyOwnMarginTarget` 改为「对主体解出目标 → 活动窗口按同一增量位移」，因此悬停展开态与已收起态得到完全一致的结果。实机验证：同一胶囊 `subject=2169,740 391x52 live=2169,740 391x311` → `下=25/Widget`（修前为 329「屏幕边缘」）、`上=22/Widget`、`左=18/Widget`、`右=0/WorkArea`；输入 `下=60` 后胶囊精确落到 y=705（=817−60−52），保存持久化；回归 3254/3254 |
| **DEF-056** | Migration_1_To_2 与 Migration_2_To_3 完全重复段 | P3 | F9 | SettingsMigrationService.cs:127-176 | SA-W3 | 📌 挂账 |
| **DEF-037** | CancelBackgroundMemoryCleanupDelay 旧 CTS 未 Dispose | **已修复（663c593+4701b39，含 Schedule 路径孪生点）** | P2 | 资源管理/S1 | `src/DeskBox/App.xaml.cs:3111` | Interlocked.Exchange 替换旧 CTS 后仅 Cancel() 未 Dispose；旧 CTS 的 registered wait handles 延迟到 GC 才释放；每次 cancel 泄漏 ~16 bytes + native wait registration | F8 Round 1 task-0 |

---

## 原有挂账（维持，本轮复核无升级）

| 编号 | 标题 | 状态 | 优先级 | 根因分类 | 位置 | 说明 |
|---|---|---|---|---|---|---|
| DEF-016 | QuickCapture 失活回落缺门控 | 待修 | P2 | 渲染/生命周期 | QuickCaptureWidgetWindow（已随 DEF-027 删除，此缺陷无生产触发面） |
| MEM-01 | 托盘旧 Icon 未确定性 Dispose | 挂账 | P3 | 资源管理/S1 | `src/DeskBox/App.Tray.cs:866` — 析构路径已有 Dispose，热替换时旧 Icon 仍延迟释放 |
| MEM-02 | 3 处 SoftwareBitmap 未确定性释放 | 挂账 | P3 | 资源管理/S1 | `src/DeskBox/Services/IQuickCaptureClipboardReader.cs:99,124` — 两处 SetSoftwareBitmap(await decoder.GetSoftwareBitmapAsync()) 均未 using，与 QuickCaptureService.cs:1842 已有 using var 规范不一致 |
| ANI-03 | 每帧委托分配 | 挂账 | P3 | 动画卫生/S2 | 未触及 |
| ANI-04 | EnableDependentAnimation 反模式 | 挂账 | P3 | 动画卫生/S2 | 未触及 |
| ANI-05 | 转场中断透明度跳变 | 挂账 | P3 | 动画卫生/S2 | 未触及 |
| ANI-06 | SetIsTranslationEnabled 不复位 | 挂账 | P3 | 动画卫生/S2 | 未触及 |
| WIN-07 | SearchPopupWindow 前台失败无降级 | 挂账 | P3 | 窗口交互/S3 | `src/DeskBox/Views/SearchPopupWindow.xaml.cs:336,436` — 两处 SetForegroundWindow 返回值未检查；已有 Activate() + BringWindowTemporarilyToFront() 前置，降级需求低 |
| WIN-08 | 拓扑协调器 async void | 挂账 | P3 | 事件/S6 | `src/DeskBox/Services/DisplayTopologyTransitionCoordinator.cs:62` — 标准 timer handler 模式 |
| LAY-05~08 | 布局卫生项 | 挂账 | P3 | 布局/S4 | 未触及 |
| QC-06~15 | 随记卫生项 | 挂账 | P3 | 随记/S5 | 未触及 |
| EVT-02 | TodoItem DataContextChanged 旧 item 订阅不退订 | 挂账 | P3 | 事件/S6 | `src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs:421` |
| EVT-03 | 全局兜底无熔断 | 挂账 | P3 | 异常卫生/S9 | 双专项合并立案 |
| EVT-04 | RegistrationChanged 死事件 | 已修复 | P3 | — | F7 批次处置 |
| CFG-03~09 | 持久化卫生项 | 挂账 | P3 | 持久化/S7 | 未触及 |
| THR-06 | 剪贴板服务状态字段裸读写 | 挂账 | P3 | 线程卫生/S8 | 未触及 |
| THR-07 | StoreStartupService UI 线程同步 IPC | **已升级为 DEF-035** | — | — | 见新增项 |
| EXC-04 | 空 catch 全量定性 | 挂账 | P3 | 异常卫生/S9 | 本轮新增 6 处空 catch 全部符合 S9 豁免（显式 fallback 返回值），无新立案 |
| EXC-05 | DateTimeOffset.Parse+catch | 挂账 | P3 | 异常卫生/S9 | 未触及 |
| EXC-06 | CitySearchService.Predefined 无防护 | 挂账 | P3 | 异常卫生/S9 | 未触及 |
| ARC-02 | current_architecture.md 漏记 Search/Glance | 挂账 | P3 | 架构卫生/S10 | 未触及 |
| ARC-03 | search-core 文档领先仓库 | 挂账 | P3 | 架构卫生/S10 | 未触及 |
| ARC-04 | 3 项 DI 注册未接线 | 挂账 | P3 | 架构卫生/S10 | 未触及 |
| ARC-05 | WidgetContentFactory 7 处重复实例化 | 挂账 | P3 | 架构卫生/S10 | 未触及 |

---

## 已闭环项（R6/R1/F6/F7 批次累计）

| 编号 | 标题 | 闭环轮次 | 备注 |
|---|---|---|---|
| DEF-001~013 | F6 批次 A/B/C/D 修复 | F6 | 详见各批次 remediation 报告 |
| DEF-014~017 | F6 批次 A 窗口交互 | F6 | |
| DEF-018~026 | F6 批次 C 稳定性 | F6 | DEF-018 复核为死代码删除 |
| DEF-027 | F6 批次 D 死宿主删除 | F6 | 13 文件删除 |
| ANI-02 | 硬件自适应动画死代码删除 | F7 | 约 1000 行 |
| ARC-06 | WidgetSurfaceSnapshotCache 死代码删除 | F7 | 已删除 |
| WIN-05/06 | [重要勿删] 文档失同步两处校正 | F6-D | 已与 DEF-027 合并 |
| R1-CS0169 | HardwareAdaptiveAnimationService 未使用字段 | F7 | 随死代码链路删除 |
| R1-CS0414 | OnboardingWindow._desktopOrganizationCompleted CS0414 | F7 | 字段现已使用 |

---

## 统计

- **待修 P1**：0
- **待修 P2**：1（DEF-016，位于已删除死宿主，随 DEF-027 跳过）
- **R2 新增 5 项（DEF-034~038）已全部修复，第 2 轮收敛辨定达成**（663c593/cf0fadb/3f2504d/4701b39，CI run 33561624305 全绿；收敛审查 GO 后 2×P2 加固 + 3×P3 注释于 bb708e3 闭环，run bb708e3c 全绿）
- **挂账 P3**：约 25 项（详见台账）
- **本轮新增**：4 项（1 P1 + 3 P2）
