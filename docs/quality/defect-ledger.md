# DeskBox 缺陷台账（更新至 2026-09-02 R2 Round 1）

> 维护规则：每轮迭代开始时从本表选取输入；每个问题闭环（修复 + 回归验证）后更新状态。
> 优先级定义：P0 = 生产环境核心功能失效；P1 = 高频可复现、明显影响体验；P2 = 低频或边界场景；P3 = 代码质量/卫生。

---

## 新增缺陷（R2 Round 1）

| 编号 | 标题 | 状态 | 优先级 | 根因分类 | 位置 | 说明 | 来源 |
|---|---|---|---|---|---|---|---|
| **DEF-034** | WidgetLayerService 双重 lock 竞态窗口期 | **已修复（663c593）** | P1 | 线程同步 | `src/DeskBox/Services/WidgetLayerService.cs:919-927` | 两次 lock(s_desktopLayerLock) 之间无原子保证；TryApplyMinimalWindowMoves 返回 false 后到第二次 lock 前 z-order 可能变化，IsWindowChainAlreadyHighestToLowest 短路未重检查；导致多余 DeferWindowPos 事务，极端下 DWM 微卡顿 | F8 Round 1 task-0 |
| **DEF-035** | StoreStartupService.GetStartupTask() UI 线程阻塞 | **已修复（663c593+3f2504d）** | P2 | 线程模型/启动性能 | `src/DeskBox/Services/StoreStartupService.cs:123` | StartupTask.GetAsync().AsTask().GetAwaiter().GetResult() 在 UI 线程同步等待 Windows Runtime；Store 构建 + 首次启动时 UI 冻结风险；非 Store 构建不受影响 | F8 Round 1 task-2 |
| **DEF-036** | Step4StartupToggle_Toggled async void 内同步调用 GetStartupTask | **已修复（cf0fadb+4701b39）** | P2 | 线程模型 | `src/DeskBox/Views/OnboardingWindow.Hotkey.cs:317`（**R14 补注：该位点已随旧版引导流死代码删除（DEF-128），机制修复的历史有效性保留**）| Onboarding 步骤 4 切换自动启动开关时同步调用 StartupService.SetEnabled → Store 路径触发 DEF-035；应改为 async Task + SafeFireAndForget | F8 Round 1 task-2 |
| **DEF-038** | 刷新代际护栏未覆盖「用户拨动 vs 在途刷新」 | **已修复（917bcf9，Toggled 入口递增代际）** | P2 | 线程模型/UI 时序 | `src/DeskBox/Views/OnboardingWindow.Hotkey.cs:325`（**R14 补注：位点已随旧版引导流死代码删除（DEF-128）**） | 代际护栏只拦「新刷新 vs 旧刷新」；用户拨动不递增代际，Store 渠道 Pending 收敛窗口内在途回调仍回弹 IsOn+覆写 AutoStart。R2 Round 2 收敛审查（deleg_8d8b2d7b）NO-GO 指出，一行修复后闭环，CI run 917bcf9e 全绿 | F8 Round 2 收敛审查 |
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
| **DEF-063** | 「边距设置…」对话框显示不全：用户只看到「上/左」两个输入框，以为没有右/下设置（用户报告 + 截图） | P2 | 用户报告 + 实机几何取证 | 编辑器是开在**格子自己的 XamlRoot** 上的 `ContentDialog`，而格子只有 **391×408 物理像素**（1.25 缩放 = 313×326 DIP，最小允许尺寸更是 50×50 DIP）。`ContentDialog` 的默认模板既**不滚动内容**、宽度下限又来自共享主题资源（`ContentDialogMinWidth`=320），于是对话框被宿主窗口直接裁掉：2×2 网格的第二行（下/右）整行落在窗口下沿之外，只剩第一行可见；四个输入框的标题还把「上（像素）· 相对最近的格子」整句塞进 `TextBox.Header`，换行后把每行高度又抬高一倍，加剧溢出。窗口化 XAML Popup 也不是选项——它不拿 Win32 焦点（同仓库 `StackPopoverInlineRenameWindow` 的注释即为此结论），文本输入会失效 | 用户截图（只有上/左）+ `EnumWindows` 实测格子矩形 391×408 + 主题下限 320 | ✅ 已修复：新增纯策略 `WidgetDialogLayout`（工具窗口尺寸/置位数学：请求内容盒 → 可读区间钳制 → 工作区钳制 → 居中于宿主格子并留 8px 边距）+ 共享宿主 `WidgetToolDialogWindow`（真实 WinUI 窗口、系统标题栏、always-on-top、`IsShownInSwitchers=false`、内容包在 ScrollViewer 里、Enter=保存/Esc=取消、`TaskCompletionSource<bool>` 回传结果）+ `WidgetWindowBase.ToolDialog` 共享入口。边距编辑器与「自定义颜色」双双改走该宿主，**预算从显示器工作区来、不再受格子尺寸限制**；分边面板改 Auto 行 + 单行标题（参考对象降为副标题 + 完整句子进 tooltip）+ 窄宿主自动改单列；顺带修掉两个连带缺陷：预览改 **160ms 防抖**（原来每个按键都移动窗口并落盘，"150" 的首位 "1" 会先把格子甩出去，按键还可能在这段布局风暴里丢失），以及**程序化写回被当成用户编辑**（WinUI 对程序化 `Text` 写入也抛 `TextChanged`，且可能延迟到写入返回之后，旧 `suppress` 布尔挡不住 → 同步回写把「下=0」标记成用户编辑并真的把格子拖到邻居身上；改为记录编辑器自己写入的值并识别回声）。实机验证：编辑器窗口 525×535 物理、四边输入框与保存/取消全部可见无滚动；输入 33 一次落位（y=107→85，边界 52+33）、非焦点边实时重算（下=22 正确）、取消回位、保存持久化；颜色选择器 475×785 全显；回归 3253/3253。**（1.5.5 上游合并更新）**取色器宿主已改回上游方案：`WidgetWindowBase.Foreground.cs` 现为 `BuildWidgetForegroundColorPickerFlyout()`（`ShouldConstrainToRootBounds = false`），由 `ContentWidgetWindow.Commands.cs` 经 `ShowFlyoutWithInteraction(..., ContentWidgetShell)` 显示。故本条「双双改走该宿主」现仅对边距编辑器与标题外观编辑器成立——两者仍走 `WidgetToolDialogWindow`（`WidgetWindowBase.TitleAppearance.cs`）。取舍理由：上游 1.5.1（`c409af2`）独立修复了同一缺陷（编辑器被格子窗口裁切），且不约束 Flyout 是本仓库既有约定（托盘菜单、组件右键菜单、设置下拉均如此）；`WidgetDialogLayout` / `WidgetToolDialogWindow` 基建与配套测试继续被边距编辑器使用，未成死代码 |
| **DEF-064** | 边距编辑器把「下 / 右」报成屏幕边缘，而那两侧明明有别的格子（用户报告：下=445「屏幕边缘」） | P2 | 用户报告 + 实机复现 + verbose 日志 | 测量主体错了：胶囊必须先**悬停展开**才会出现标题栏（右键菜单也才存在），而展开后的临时矩形**把下方的胶囊整段盖住**——实测 `(2169,590,391,52)` 的胶囊悬停后变成 `391x345`，底边 935，于是「下」这一侧在其横向跨度内确实没有严格位于下方的对象，退回工作区边缘得 `1380−935=445`，与用户截图完全一致；「右」侧格子本来就贴着 2560 的工作区右缘，报屏幕边缘是正确的。同一个错误主体还会污染**应用**路径：输入的数值会按展开矩形去贴一个用户从未看到的边界 | 实机复现 + 新增 `[WidgetMargin] Reference resolve subject=/live=/四边距离与参考类型` 日志 | ✅ 已修复：边距的测量与应用统一改用**静止几何**为主体——新增 `WidgetWindowBase.RestsCollapsed`（Smart 恒为收起、Click 看持久化的 `Config.IsCollapsed`，与启动初值共用同一判定，不再复制 switch）+ `ResolveMarginSubjectBounds`（静止收起且当前不在胶囊尺寸时，用 `GetCompactBounds(live)` 推导胶囊矩形作为主体）；`ApplyOwnMarginTarget` 改为「对主体解出目标 → 活动窗口按同一增量位移」，因此悬停展开态与已收起态得到完全一致的结果。实机验证：同一胶囊 `subject=2169,740 391x52 live=2169,740 391x311` → `下=25/Widget`（修前为 329「屏幕边缘」）、`上=22/Widget`、`左=18/Widget`、`右=0/WorkArea`；输入 `下=60` 后胶囊精确落到 y=705（=817−60−52），保存持久化；回归 3254/3254 |
| **DEF-065** | 「下载」格子「消失」（用户报告） | P1 | 用户报告 + 配置/日志双取证 | 格子未被删除也未丢配置（`isDisabled=false`、窗口句柄存活），是**胶囊位置被污染后跨重启暴露**：`compactPlacement.positionMarginY=356`（逻辑）×1.25=445 物理边距 → 启动恢复 `y=1380−445−52=883`，而正确槽位是 y=590（边距 590.4）；52px 的胶囊被下方「安全」的展开面板（892..1293）盖掉 91%，肉眼看就是「消失」。污染向量（本批堵住前仍活跃）：①胶囊必须悬停展开才能右键出菜单，展开态下 `RefreshCompactPlacementFromExpandedBounds` 会 `Config.CompactPlacement=null` 后**以展开面板为基准重新锚定**（pivot=面板角）并持久化——面板比胶囊高 293px，锚点边距因此差了整整一个面板高度；②`CaptureCompactPlacement` 对传入矩形不做种类校验，面板矩形也能被存成胶囊位置。对照验证：其他 13 个格子的 `cp.marginY` 全部与实时位置吻合，唯独「下载」的恢复值 883 与实时 y=883 严格相等、与持久化的 `y=590` 矛盾 | settings.json vs `.bak` 比对 + `bab0f7f5@` 位置轨迹（197 次 @590 → 26 次 @883，翻转发生在 9/6 03:59 重启）+ 13 格子横向对照 | ✅ 已修复（三层防线）：①`RefreshCompactPlacementFromExpandedBounds`/`EnsureCompactPlacementFromExpandedBounds` 增加 **Peek 守卫**（`RestsCollapsed && ViewState==Peek` 时不动用户已有的胶囊位置、也不以 Peek 播种初始位置）；②`CaptureCompactPlacement` 对面板尺寸矩形（高 > 1.5×胶囊高）**重新推导胶囊再入库**；③边距路径在胶囊主体移动时改为 `CaptureCompactPlacement(target, persist:true)` 显式平移胶囊位置（通用 refresh 对 Peek 已是 no-op）。数据侧：停应用后把「下载」的 `marginY` 356→590.4（已留 `.pre-def065-repair` 备份），重启后胶囊回到 (2169,590) 并持久化。回归 3257/3257 |
| **DEF-066** | 1.5.0 上游合并回归四连：①构建断裂（协调器 `List.Length`×3、QuickCapture 颜色按钮访问器缺失、XAML 编译内部错误级联）；②标题栏点击收起三方法随 `Collapse.cs` 整文件被上游版覆盖（DEF-059 回退）；③`MoveWindowWithoutPersisting` 回到逐帧新建 4 个闭包（DEF-060 回退，且丢失上游新增的 committed/commitFailed 失败协议）；④`FileServiceTests` 一处收尾大括号被合并吞掉 | P1 | 合并后构建/回归暴露 | 合并对 `Collapse.cs` 整文件取上游侧，我方分部类内的方法随文件丢失而调用点保留（`ContentWidgetWindow.WindowInteraction.cs` 为我方版本）；协调器文件上游本就带病（`List` 用 `.Length`） | 构建 9 错误 + 回归定位 | ✅ 已修复 e2f09d9：恢复三方法+候选字段；提交回调改为**缓存三项 + committed/commitFailed 走字段派发**（保住 DEF-060 零逐帧分配且兼容上游失败协议）；补齐访问器、修正括号 |
| **DEF-067** | 备份快照竞态测试偶发闪断：`ExportBackupAsync_ArchivesStableSnapshotWhenSourceChangesAfterStaging` 在 CI 红过一次（重跑即过）——断言依赖「源文件修改晚于暂存」的时序假设，机器快慢影响结果 | P2 | CI 历史取证（run 34050253378 @ f17d052，快照拿到旧内容 "Light" 而断言期望新内容 "Dark"） | DeskBoxDataBackupServiceTests.cs:98 | 时序敏感竞态断言；当前 main 已绿，但关键节点闪断会误报 | 📌 挂账（专项收紧：以显式同步点代替时间窗假设；守护的是备份链路，值得修扎实） |
| **DEF-068** | 「本机绿、CI 红」无头性盲区：本机装了 Windows App SDK 运行时，VM/服务再引入 WinRT 激活工厂（ColorHelper/Colors 等）本地照常全绿，CI 干净环境抛 REGDB_E_CLASSNOTREG——随记颜色批次 5 个用例在 CI 红（2501f0c 修复 ParseColor），是此盲区的实锤案例 | P2 | CI run 34388896131（5 失败） vs 本机 3533 全绿对照 | `TodoItemViewModel.ParseColor`（已修 2501f0c）；结构性风险仍在 | 环境差异掩盖无头性回归 | 📌 挂账（专项：本地测试用与 CI 一致的隔离宿主——runsettings 或干净容器定期跑；立即可用的纪律：ViewModel/Service 禁止 new SolidColorBrush 与 WinRT 激活工厂，颜色走 struct + 视图层转换器，已有 `ColorToSharedBrushConverter` 范式） |
| **DEF-056** | Migration_1_To_2 与 Migration_2_To_3 完全重复段 | P3 | F9 | SettingsMigrationService.cs:127-176 | SA-W3 | 📌 挂账 |
| **DEF-037** | CancelBackgroundMemoryCleanupDelay 旧 CTS 未 Dispose | **已修复（663c593+4701b39，含 Schedule 路径孪生点）** | P2 | 资源管理/S1 | `src/DeskBox/App.xaml.cs:3111` | Interlocked.Exchange 替换旧 CTS 后仅 Cancel() 未 Dispose；旧 CTS 的 registered wait handles 延迟到 GC 才释放；每次 cancel 泄漏 ~16 bytes + native wait registration | F8 Round 1 task-0 |

---

## 2026-09-13 全面深度代码审查（5 路并行：文件安全/并发生命周期/设置持久化/原生互操作/组件功能）

> 范围：全库主审 + 主线逐条亲验复核。基线 3533/3533 全绿。**结论：无 P0/P1**；原生互操作（Rust ABI 冻结契约、P/Invoke 编组、更新器链路、安装器卸载安全）与 12 语言键位对（2689 键）经机器+人工双重核验干净。新增 16 项如下（5 P2 + 11 P3），每条均经主线读码确认后入账。
> **处置决定（2026-09-13）**：用户确认全部暂不修复、维持挂账观察（含 DEF-072）。

| 编号 | 标题 | 优先级 | 发现途径 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|---|
| **DEF-069** | CopyEntryAsync 失败回滚误删「外来」同名目标文件：`File.Copy(overwrite:false)` 遇目标已存在时抛 IOException 且**不触碰**该文件，catch 却无条件 `File.Delete(destinationPath)`——删的是别的进程/用户放在那里的文件，不是本操作的半成品。受控孪生 `CopyFileWithProgressAsync`（TransferProgress.cs:315-364）已用 `FileMode.CreateNew + destinationCreated` 修过同一问题并留注释「永不删除预先存在的目标」，旧路径漏改。触发面：托管存储根迁移的目录回退复制（RelocateDirectoryAsync/MoveDirectoryAsync fallback → CopyDirectoryAsync → CopyEntryAsync），计划期与复制期之间被外部写入者（云盘同步等）抢注同名即触发 | P2 | 全库审查（文件安全）+ 主线亲验 | `src/DeskBox/Services/FileService.cs:2106-2118` | 回滚清理未区分「本操作创建的半成品」与「外来既有文件」 | 📌 挂账（修法三行：仿孪生路径跟踪 destinationCreated，仅清理本操作创建的文件） |
| **DEF-070** | 设置共享活对象无锁并发：`Settings` 返回活单例，`_lock` 只护 SettingsService 自身变更器与序列化；UI 侧 `Widgets.Add`（WidgetManager.cs:951）、`RecentOrganizationHistory.Insert(0,…)`（OrganizerService.cs:412）不加锁，而后台防抖保存（SaveDebounced → Task.Run → SaveToFileOnlyAsync）在 `lock(_lock)` 内对**同一活对象**做 `SerializeToUtf8Bytes`。正向竞态：序列化枚举列表时 UI 并发增删 → InvalidOperationException 被 catch → 该次防抖保存丢失（下次保存补上，若进程在下次保存前死掉则真丢）；反向竞态：normalizer 在锁内 `RemoveAll`/替换列表时 UI 无锁枚举同一列表 → UI 线程异常被全局兜底吞掉 | P2 | 全库审查（设置持久化）+ 主线亲验两侧 | `src/DeskBox/Services/SettingsService.cs:821-848,912`、`WidgetManager.cs:951`、`OrganizerService.cs:412` | 同一对象图的读写两端持不同锁（一端无锁） | 📌 挂账（方向：序列化前在锁内做快照，或 UI 变更统一走加锁变更器；碰撞窗口毫秒级、低频） |
| **DEF-071** | 群组拖出预览「锁内跨线程 SetWindowPos」UI 挂死风险：后台 16ms 轮询（Grouping.cs:515 Task.Run）每拍调 `preview.Update` → `lock(_gate)` 内直接 `SetWindowPos/ShowWindow`（WidgetDetachPlacementPreviewWindow.cs:265-312）；HWND 归 UI 线程所有，跨线程 SetWindowPos 向属主线程同步发消息；若此刻 UI 线程走到 `Hide/MarkCommitted/FadeOutAndHideAsync/Dispose`（同持 `_gate`），UI 线程等锁、后台线程等 UI 泵消息 → 双死等，只能杀进程 | P2 | 全库审查（并发代理）+ 主线亲验机制 | `src/DeskBox/Views/WidgetWindowBase.Grouping.cs:515-554`、`WidgetDetachPlacementPreviewWindow.cs:157-177,265-312` | 同一把锁横跨「跨线程 Win32 同步调用」与「UI 线程短临界区」 | 📌 挂账（修法：锁内只换状态快照，Win32 调用移到锁外；触发需毫秒级窗口、概率低后果重） |
| **DEF-072** | 本地化键 `Settings.QuickCapture.RecordColors.Reset.Title` 缺失：SettingsWindow.xaml:1878 与设置搜索目录 SettingsSearchCatalog.cs:103 引用该键，但 12 个语言 JSON 全都没有它（其余键 12 语言完全对齐）→ 设置页「功能格子 → 随记 → 记录颜色」第三张卡片标题直显原始键名，任何语言都一样，搜索建议同样露出 | P2 | 全库审查（设置代理机器比对）+ 主线复核 | `src/DeskBox/Views/SettingsWindow.xaml:1878`、`src/DeskBox/Services/SettingsSearchCatalog.cs:103`、`src/DeskBox/Strings/*.json` | 记录颜色设置区块新增时漏配键（内层按钮用的是另一存在的键 `QuickCapture.ClipboardColor.Reset`） | 📌 挂账（修复成本极低：12 文件各补一键。**本次审查唯一已发布版本用户可见项**，建议随下个版本尽快带出） |
| **DEF-073** | 跨盘目录移动遇只读文件必失败且留「分裂树」：目录复制把源文件的 ReadOnly 属性一并复制到目标（CopyFileMetadata，TransferProgress.cs:706-714），随后源清理用 `Directory.Delete(recursive:true)`（:686-688）——.NET 删除只读文件会 ACCESS_DENIED（单文件路径已专门处理 clearedReadOnly，目录路径没有）→ 必抛 UnauthorizedAccessException → FileTransferSourceCleanupException：目标已完整、源目录原封不动，条目记失败，用户看到两份且不明所以 | P2 | 全库审查（文件安全）+ 主线亲验 | `src/DeskBox/Services/FileService.TransferProgress.cs:684-701`、`FileService.cs:2194-2210` | 目录源清理未先清只读属性 | 📌 挂账（修法：源清理前递归去 ReadOnly 或仿单文件路径逐项清属性；无数据丢失，仅体验受损） |
| **DEF-074** | 迁移步骤失败被吞但 SchemaVersion 仍强置最新：RunMigrationsAsync 对每步 catch 记日志继续，末尾无条件 `settings.SchemaVersion = CurrentSchemaVersion` → 失败那步（如 9→10 音乐库迁移的文件 IO 错误）永不重试，旧形状数据静默留存 | P3 | 全库审查（设置持久化）+ 主线亲验 | `src/DeskBox/Services/SettingsMigrationService.cs:63-82` | 失败路径未阻断版本推进 | 📌 挂账（方向：全部成功才推进版本，或记失败步骤号下轮重跑） |
| **DEF-075** | 「损坏回退默认」档案以 SchemaVersion=1 落盘，下次启动重跑破坏性迁移：加载失败 catch（:740-747）`new AppSettings()`（默认版本 1）后未像「缺文件」路径（:682-689）那样盖 CurrentSchemaVersion；首存把「当前形状数据+版本1」写盘 → 下次启动 6→7 重置 Everything 开关、7→8 用总开关覆写动画细项，冲掉用户在损坏会话里重做的选择 | P3 | 全库审查（设置持久化）+ 主线亲验 | `src/DeskBox/Services/SettingsService.cs:740-747 vs 682-689` | 恢复路径漏盖版本号 | 📌 挂账（一行修：catch 分支同样盖 CurrentSchemaVersion） |
| **DEF-076** | 遗留 settings.json 一次性迁移拷贝非原子且不可重试：直接 `WriteAllTextAsync`（未走 ResilientJsonStore temp+replace），中途断电留半截文件；下次启动因 `_settingsPath` 已存在早退，旧版文件永远不再读取 → 半截被隔离、配置跌回默认 | P3 | 全库审查（设置持久化）+ 主线亲验 | `src/DeskBox/Services/SettingsService.cs:750-772` | 一次性路径漏用弹性写入器 | 📌 挂账（换 ResilientJsonStore；触发概率极低） |
| **DEF-077** | SearchHistoryService 是唯一不走 ResilientJsonStore 的存储：裸 `File.WriteAllText`，崩溃中断即损坏；载入 catch 后继续（丢全部历史自愈）。仅限搜索历史/收藏 | P3 | 全库审查（设置持久化）+ 主线抽查 | `src/DeskBox/Services/SearchHistoryService.cs:301` | 掉队于弹性存储规范 | 📌 挂账（顺手统一到 ResilientJsonStore） |
| **DEF-078** | async void / TryEnqueue(async) 异常静默失效簇：全局 OnUnhandledException 设 `Handled=true`（App.xaml.cs:4421-4425，**不崩溃**、仅日志），因此一组无 try/catch 的 async void 处理器在底层抛异常时表现为「点击没反应」：紧凑胶囊主操作（勾 todo → SaveAsync 磁盘失败）、音乐四紧凑按钮与展开视图按钮（陈旧 SMTC 会话 Try*Async 抛 COM 异常，MusicSessionService.cs:225-267 同类 GetSessionOptions 均有防御唯独 Try* 裸调）、多处 `TryEnqueue(async () =>…)`（ContentWidgetWindow.WindowInteraction.cs:369/449、WidgetManager.cs:1929、WidgetManager.ZOrder.cs:151/182、ContentWidgetWindow.Commands.cs:141/424/431；同文件 1840/1860/2549 三处已 try/catch 属例外） | P3 | 全库审查（并发+组件代理；主线复核降级：非崩溃、为静默失效） | 多点（见说明） | 异常卫生依赖全局兜底，缺操作级反馈 | 📌 挂账（方向：高频用户入口加 try/catch + 失败提示；音乐 Try*Async 包一层与 GetSessionOptions 同式防御） |
| **DEF-079** | JIT 音量后端 ISimpleAudioVolume RCW 泄漏：AudioSessionHandle.Dispose 只释放 Control，`Volume`（每次 TryGet/TrySetSessionVolume 经 QueryInterface 新取的 RCW）从不释放 → 拖音量滑条每次 tick 沉积一个 RCW 到 GC 终结。仅 `#if !DESKBOX_NATIVE_AOT` 的 C# 后端；零售 AOT 走 Rust 后端不受影响 | P3 | 全库审查（组件代理）+ 主线抽查 | `src/DeskBox/Services/MusicVolumeService.cs:294-318,538-541` | 孪生 COM 指针漏释放 | 📌 挂账（Dispose 补 ReleaseComObject(Volume) 一行） |
| **DEF-080** | ar-SA 区域格式下天气解析/缓存错乱：应用未设全局 CurrentCulture，区域格式为沙特阿拉伯时默认乌姆阿拉历+阿拉伯-印度数字 → MSN 日报日期以 Hijri 串写进 weather-cache.json；Open-Meteo 小时条用 `ToString("yyyy-MM-dd")`/`"yyyy-MM-ddTHH"` 与 Gregorian API 串比对永不匹配 → FindCurrentHourIndex 恒回退 0（24h 陈旧起点）；`DateTimeOffset.Parse(iso)` 在该文化下对公元 2026 抛异常被 catch → IsDaytimeHour 恒 true | P3 | 全库审查（设置代理，行为推导） | `src/DeskBox/Services/WeatherService.cs:501,526-528,561`、`ViewModels/WeatherWidgetViewModel.DataProcessing.cs:515,531` | 文化敏感解析/格式化未锁 InvariantCulture | 📌 挂账（方向：日期往返一律 InvariantCulture；触发面：区域格式设为沙特阿拉伯的用户） |
| **DEF-081** | 托管拖入撤销按路径无身份校验：widget 托管 drop 的 undo 只查 `File.Exists(DestinationPath)` 即把该路径**当前内容**移回原位；对照桌面整理撤销有 MatchesSnapshot（大小+LastWriteTime）指纹校验。同名被替换后撤销会搬走「顶替者」（GetAvailablePath 保证不覆盖，数据不销毁） | P3 | 全库审查（文件安全） | `src/DeskBox/Services/OrganizerService.cs:368-398` | 历史条目未存指纹无法比对 | 📌 挂账（方向：历史条目补 Size/LastWriteTimeUtc 指纹，与桌面整理撤销对齐） |
| **DEF-082** | StripBlockingAttributes 崩溃窗口遗留属性丢失：为绕 WinRT UNABLE_TO_MASK_PATH 临时清除用户文件 Hidden/System 属性、finally 恢复；进程在窗口期内被杀则属性永久丢失（内容无损） | P3 | 全库审查（文件安全） | `src/DeskBox/Services/FileService.cs:889-925` | 非原子「改-用-还原」包裹外部文件 | 📌 挂账（观察项；恢复已 best-effort 并有日志） |
| **DEF-083** | RevealFromTray 自动恢复忽略「托盘唤起会话」不变量：`_autoRestoreTimer`(1200ms) 到点直接 `RestoreDesktopLayer(force:true)`，未像 TopMostSafetyTimer 那样让位于 WidgetsRaisedFromTray 唤起会话（z-order 文档 §4 规则）；F7 唤起会话期间单格「显示」会在 1.2s 后独自沉底（下次显示器事件自愈，外观级） | P3 | 全库审查（并发代理） | `src/DeskBox/Views/ContentWidgetWindow.xaml.cs:840-866` | 同类自救定时器护栏不一致 | 📌 挂账（补同一护栏一行；自愈） |
| **DEF-084** | 更新器备忘簇（设计取舍/低危，合并记录）：①直发渠道仅 hash 校验、无 Authenticode 签名验证（hash 与安装包同源，源被攻破即失效；GitHub 兜底渠道用 GitHub 计算 digest 略强）；②HttpClient.Timeout=20s 只覆盖到响应头，中段静默断流时下载挂住直到手动取消（取消清理 tmp 的路径已验证干净）；③DeskBox.Updater.log 无轮转上限（主程序日志 5MB 封顶，更新器没有） | P3 | 全库审查（原生/更新代理，主线认可为取舍） | `src/DeskBox/Services/AppUpdateService.cs:31,190-236`、`src/DeskBox.Updater/Program.cs:113-134,389-401` | 直发更新器的常见取舍 | 📌 挂账（可选加固：下载包装包做 WinVerifyTrust、下载循环加读超时、更新器日志加轮转） |

---

## 原有挂账（维持，本轮复核无升级）

| 编号 | 标题 | 状态 | 优先级 | 根因分类 | 位置 | 说明 |
|---|---|---|---|---|---|---|
| DEF-016 | QuickCapture 失活回落缺门控 | **✅ 已消除（R15 复核改判）** | P2 | 渲染/生命周期 | QuickCaptureWidgetWindow（已随 DEF-027 整体删除，缺陷载体不复存在于任何代码路径） |
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
- **2026-09-13 全面深度代码审查新增**：16 项（5 P2 + 11 P3，无 P0/P1，全部经主线逐条亲验后入账），详见「2026-09-13 全面深度代码审查」分节；其中 DEF-072（本地化键缺失）为唯一已发布版本用户可见项，修复成本极低
- **round-08（2026-09-25）全量并行代码缺陷深度审查新增**：23 项（3 P2 + 20 P3，无 P0/P1），详见下节；raw 立案 30 条经去重合并（10 专项报告见 `docs/quality/rounds/round-08/`，总报告同目录）
- **round-09（2026-09-27）双路全量代码缺陷深度审查新增**：5 项（P0×0、P1×0、P2×0、**P3×5**：DEF-110~114），详见下节；4 条存量挂账改判「已修复（现状核实）」（DEF-040/077/081/088）、2 项遗留观察解除；连续第四轮 P0/P1 = 0
- **round-10（2026-09-27）双路全量代码缺陷深度审查新增**：2 项（P0×0、P1×0、P2×0、**P3×2**：DEF-115/116），详见下节；round-09 修复批经双代理独立复核零回归；无存量改判；连续第五轮 P0/P1 = 0，立案数收敛 5 → 2
- **round-11（2026-09-27）双路全量代码缺陷深度审查新增**：3 项（P0×0、P1×0、P2×0、**P3×3**：DEF-117~119），详见下节；round-10 修复批经双代理独立复核零回归；无存量改判；连续第六轮 P0/P1 = 0（R8→R11 = 23→5→2→3，P3 平台期）
- **round-12（2026-09-27）双路全量代码缺陷深度审查新增**：5 项（P0×0、P1×0、P2×0、**P3×5**：DEF-120~124，其中 DEF-123 为 round-11 DEF-117 修复引入的回归——「下轮审上轮」机制首次捕获跨轮回归），详见下节；连续第七轮 P0/P1 = 0
- **round-13（2026-09-27）双路全量代码缺陷深度审查新增**：2 项（P0×0、P1×0、P2×0、**P3×2**：DEF-125/126），详见下节；round-12 修复批经双代理独立复核零回归（DEF-123 主目标达成，成功分支残留单列 DEF-125 随轮收口）；无存量改判；连续第八轮 P0/P1 = 0
- **round-14（2026-09-27）双路全量代码缺陷深度审查新增**：3 项（P0×0、P1×0、P2×0、**P3×3**：DEF-127~129，全部为死代码/清理类且互相关联须按序整批处置），详见下节；round-13 修复批经双代理独立复核零回归；无存量改判；连续第九轮 P0/P1 = 0
- **round-15（2026-09-27）双路全量代码缺陷深度审查新增**：2 项（P0×0、P1×0、P2×0、**P3×2**：DEF-130/131；A-01 前半与 B-01 为同一发现去重合并），详见下节；DEF-016 改判「已消除」；round-14 大删除批经双代理交叉验证干净；连续第十轮 P0/P1 = 0
- **round-16（2026-09-27）双路全量代码缺陷深度审查新增**：1 项（P0×0、P1×0、P2×0、**P3×1**：DEF-132——round-15 DEF-130 修复的执行偏差，A 面零立案），详见下节；round-15 修复批复核零回归（DEF-131 正确落地）；连续第十一轮 P0/P1 = 0

---

## round-08 全量并行代码缺陷深度审查（2026-09-25，10 路并行）

> 代码基线 `wip/fix-bug` @ `77f2b4b4`（含 upstream v1.5.5 合并）。纯静态审查，未运行测试。每条均经主流程对当前树二次核验（总报告 §7）。
> 历史结论复核：round-06/07 存量 79 条 = 维持 36 / 改形复发 1 / 已消除 42，无已修复项回退。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-085** | 边距对话框打开期间胶囊自动折叠后取消恢复：把打开时捕获的展开面板矩形套回已折叠窗口，折叠态绕过 DEF-065 面板重推导守卫，取消的预览位移在下次收起/重启回弹 | P2 | `src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs:573,593-619`（配合 `Collapse.cs:553-569,4295-4321,4753-4771`、`Bounds.cs:519-523`、`WidgetCompactInteractionPolicy.cs:168-177`） | 取消恢复无条件 SetWindowPos+`RefreshCompactPlacementAfterBoundsMove`，无 `RestsCollapsed` 重推导；DEF-065 家族复发向量 | 📌 挂账（S04/round-08） |
| **DEF-086** | settings.json 全仓唯一缺「新架构档案只读」防护：降级/交叉构建后首次保存静默丢弃未知字段（widget-layout/revisions/weather-cache 三处均有此防护，唯独最大 store 缺席） | P2 | `src/DeskBox/Services/SettingsService.cs:705-868,942-1058`（对照 `WidgetLayoutStore.cs:98`、`SyncRevisionsStore.cs:105-115`、`WeatherCacheStore.cs:192-196`） | typed slice 无法表示新 schema 字段，覆写即丢弃 | 📌 挂账（S07/round-08） |
| **DEF-087** | 钩子线程同步握手 Wait(1500)+Join(650) 占 UI 线程主路径，钩子线程假死可冻结约 2.15s（r06-THR-02 维持 + S03-WIN-13 同型并入，调用面 6 处） | P2 | `ReservedHotkeyHookService.cs:187,325,363`；`DesktopDoubleClickActivationService.cs:251,352,368`；`GlobalHotkeyService.cs:156`；`SearchHotkeyService.cs:150`；`OnboardingWindow.Hotkey.cs:94`（**R14 补注：该文件已随旧版引导流死代码整体删除（DEF-128），async 孪生保留于 Settings 侧**）；`SettingsWindow.HotkeyAndAppearance.cs:361` | hook 线程 handshake 用同步 Wait/Join 阻塞调用线程（UI） | 📌 挂账（S08+S03/round-08） |
| **DEF-088** | 拖出看门狗 Timer `using var` 声明于同步方法内，方法返回即 Dispose，15s 饥饿看门狗永不触发（诊断失效，无泄漏） | P3 | `src/DeskBox/Helpers/NativeFileDragOut.cs:255-271` | `using var` 生命周期=方法体 | 📌 挂账（S01/round-08） |
| **DEF-089** | 共享 Composition 动画模板每次启动重灌关键帧，依赖未文档化重复进度语义；KeyFrameAnimation 无清除 API，最坏会话级缓慢劣化 | P3 | `WidgetShell.xaml.cs:2135-2199,3067-3078,3273-3278,986-993`；`WidgetTrayAnimationController.cs:423-440,542-558`；`WidgetCompositionResources.cs:32-60` | 每次启动重灌关键帧 | 📌 挂账（S02/round-08） |
| **DEF-090** | 帧率档位上线后自适应 ladder 成死代码（NormalizeFrameRate 恒>0 → cap 恒激活、escalation 不可达）；cap 注释与地板除实现相反 | P3 | `WidgetWindowBase.Collapse.cs:3419-3440,3651-3691`；`WidgetCompactFrameSkipPolicy.cs:54-118` | 档位与旧自适应机制并存，前者恒短路后者 | 📌 挂账（S02/round-08） |
| **DEF-091** | `WidgetShellSettingsSlice` 帧率字段注释残留上游「always at or under the target」旧语义，与 DEF-056 修复后「永不低于所选档位」相反，有诱导复发风险 | P3 | `src/DeskBox/Models/WidgetShellSettingsSlice.cs:47-57` | 上游注释未随 fork 行为校正 | 📌 挂账（S10/round-08） |
| **DEF-092** | 两处死代码：无人调用的三参确认队列重载 + 无引用的音乐进度私有方法 | P3 | `WidgetManager.cs:2200`；`ContentWidgetWindow.xaml.cs:433` | 演进残留 | 📌 挂账（S03/round-08） |
| **DEF-093** | [重要勿删] 手册三处失同步：热键 LL 兜底/去重描述过时、QuickReveal 第三层级模式未记载、监视器间隔写死 200ms（实际 50ms） | P3 | `docs/architecture/[重要勿删]widget_zorder_lifecycle.md` §3.1/§1/§4 vs `SettingsService.cs:134`、`WidgetManager.ZOrder.cs:1086`、`GlobalHotkeyService.cs` | 1.5.5 演进后手册未跟（不触碰 z-order 核心约定） | 📌 挂账（S03/round-08） |
| **DEF-094** | capture-lost 路径无条件释放临时唤起租约，缺 `wasEngaged` 门，可提前拆台他人手势的 2.3s 展示窗（外观级） | P3 | `WidgetWindowBase.Interaction.cs:783`（对照 `:501-506`） | 捕获丢失分支漏加同款门 | 📌 挂账（S03/round-08） |
| **DEF-095** | `ReferenceEquals(subject, live)` 对两个 `RectInt32` 结构体装箱比较恒 false，DEF-065 通用刷新分支成死代码，恒走 `CaptureCompactPlacement` | P3 | `WidgetWindowBase.TitleAppearance.cs:805-817`（声明 `:777,785`） | 结构体经 ReferenceEquals 必然分别装箱 | 📌 挂账（S04/round-08，主流程核验实锤） |
| **DEF-096** | 批量边距对 Smart/Peek 折叠源静默跳过、对 Click 持久展开源按活动面板矩形测量，与单格 DEF-064「静止几何」语义不一致 | P3 | `WidgetManager.BulkAppearance.cs:132-137`；`WidgetWindowBase.TitleAppearance.cs:699-731` | DEF-064 未覆盖批量分支 | 📌 挂账（S04/round-08） |
| **DEF-097** | 多选复制剪贴板写入无重试无 try/catch，经 async void KeyDown 静默失败（DEF-078 同族新位点） | P3 | `QuickCaptureSurfaceContent.xaml.cs:2764-2771`（调用链 `:2689-2704`） | 剪贴板占用时复制无反馈 | 📌 挂账（S05/round-08） |
| **DEF-098** | 设置入口配色编辑器初始值/对比度基线硬编码深色常量（0xF5F5F5/0x282828），浅色主题误拒误放行 | P3 | `SettingsWindow.QuickCaptureColors.cs:87-103`（消费 `:61-70`） | 有效色解析未感知主题 | 📌 挂账（S05/round-08） |
| **DEF-099** | `WidgetRemoved` 事件全仓库零订阅者，死通知路径 | P3 | `WidgetManager.cs:456,1901` | 声明+触发，无消费方 | 📌 挂账（S06/round-08） |
| **DEF-100** | `SearchRequested` 通知链无消费方，语义已被 `OpenSearchPopup` 直调取代 | P3 | `SearchWidgetContent.xaml.cs:43,292`、`SearchWidgetContentAdapter.cs:51-64` | 同上 | 📌 挂账（S06/round-08） |
| **DEF-101** | 除三条主干（DEF-019）外约 20 个 Services 广播源仍为裸 `?.Invoke`，无逐处理器隔离（EVT-01 泛化） | P3 | `MusicSessionService.cs:463-512`、`SearchHistoryService.cs:93-247`、`WidgetManager.ZOrder.cs:1329` 等（全表见 S06 报告 §3） | 单订阅者异常中断后续通知 | 📌 挂账（S06/round-08） |
| **DEF-102** | QuickCaptureStore 无 per-path 门控，与 TodoWidgetStore（DEF-043 修复）门控不对称，属规范缺口 | P3 | `QuickCaptureStore.cs:51-62`（对照 `TodoWidgetStore.cs:33-35`） | 双写者并发窗口 | 📌 挂账（S08/round-08） |
| **DEF-103** | EverythingSearchService.Dispose `_isDisposed` 裸字段 + `_nativeGate.Wait(1s)` 阻塞调用线程、超时静默跳过 CleanUp | P3 | `EverythingSearchService.cs:641-671` | 退出/重建路径 | 📌 挂账（S08/round-08） |
| **DEF-104** | 恢复归档重复 manifest.json 条目抛 `InvalidOperationException`（SingleOrDefault），逃出 `InvalidDataException` 归一化，恢复对话框显示英文运行时消息 | P3 | `DeskBoxDataBackupService.cs:1684,1724-1726`；`SettingsWindow.CloudBackup.cs:469` | catch 过滤器不含 InvalidOperationException | 📌 挂账（S09/round-08） |
| **DEF-105** | WebDAV PROPFIND 畸形 href（http 前缀但非法 URI）抛未分类 `UriFormatException`，破坏列表验证/保留清理/快照列表 | P3 | `WebDavBackupTransport.cs:243-246,228-231` | 服务器畸形 href 未归一化 | 📌 挂账（S09/round-08） |
| **DEF-106** | 合并模式恢复遇 `"id": null` 条目抛 `ArgumentNullException`（`TryGetValue(null)`），该域恢复确定性失败至放弃（有安全 zip 兜底，但无坏数据归因） | P3 | `DeskBoxDataBackupService.cs:1538-1541,1606-1609` | `item is null` 有防、`item.Id is null` 无防 | 📌 挂账（S09/round-08） |
| **DEF-107** | 同步投影/存储层（SyncEnvelope/Projection/Outbox/Revisions/State）已入库含测试但生产零消费方，而 sync-protocol 契约文档头仍标「未立项实现」——文档与代码互为滞后 | P3 | `src/DeskBox/Sync/SyncProjection.cs`、`SyncOutboxStore.cs:17` 等；`docs/architecture/sync-protocol-contract-20260918.md:3` | 1.5.5 合并带入未接线层 | 📌 挂账（S10/round-08） |

### 存量条目状态变更（round-08 复核）

| 编号 | 变更 | 依据 |
|---|---|---|
| ANI-03 | **改形复发**：`MoveWindowWithoutPersisting` 三回调已缓存（DEF-060/066 有效），但 `CommitCollapseAnimationBounds` 每帧新建 OnCommitted/OnCommitFailed 闭包（DEF-066 e2f09d9 引入，约 3 次分配/帧/窗） | `WidgetWindowBase.Collapse.cs:3699-3739`（S02） |
| DEF-070 | 维持 + 新读侧实例：`DesktopAutoOrganizationWatcher.cs:592-603` 后台无锁枚举同一活列表 | S08 |
| DEF-071 | 维持：群组拖出预览 lock 内跨线程 SetWindowPos/ShowWindow 双死等机制未变 | `WidgetWindowBase.Grouping.cs:516-545`（S08） |
| THR-06 | 维持：QuickCaptureClipboardService 诊断/生命周期字段跨线程裸读写 | `QuickCaptureClipboardService.cs:13-19,55-63,126-145`（S08） |
| EXC-06 | 维持 + 补充维度：双检锁无 volatile 发布（原 r08-THR-07 并入） | `CitySearchService.cs:53-75`（S08/S09） |
| DEF-075 | 残余收窄后维持：内层 `SettingsService.cs:741-750` 盖戳有效；外层 catch `:846-868` 仍以 SchemaVersion=1 落盘（`Models/AppSettings.cs:18` 默认 1） | S07 + 主流程核验 |
| DEF-067 / DEF-074 | **闭环确认（R8 复核）**：两专项一致确认已随迁移管线/测试收紧消除 | S07/S10 |
| ANI-04 | **闭环确认（R8 复核）**：FeedbackPresenter 依赖动画标志已移除 | S02 |
| MEM-02 | 维持（3 处中 1 处随死宿主删除） | S01 |
| MEM-01 / DEF-040 / DEF-051 / DEF-052 / WIN-07 / WIN-08 / WIN-09 / LAY-05~07 / QC-06~09,12,14 / EVT-02 / EVT-03（改善）/ CFG-03 / CFG-04 / EXC-03（改善）/ EXC-04 / EXC-05 / ARC-02~05 / DEF-016 / DEF-072 / DEF-076~084 相关挂账 | 维持（各专项报告 §2 有逐条当前树证据；DEF-078 面积扩大并新增 DEF-097 同族位点） | S01~S10 |

### round-08 统计

- **新增**：23 项（P2×3：DEF-085/086/087；P3×20：DEF-088~107）
- **历史复核**：维持 36 / 改形复发 1（ANI-03）/ 已消除 42（复核基数 79，无已修复项回退）
- **正面结论**：1.5.5 合并干净（无冲突标记残留、ABI 十导出一致、死宿主无复活）；VERIFY-1/2/3 未回退；连续两轮 P0/P1 = 0
- **待整改排期**：P2×3 + 存量 P2（DEF-070/071/072）优先；P3 按「速赢批/恢复健壮性批/线程卫生批/广播隔离批」归并（总报告 §4）

---

## R8-AB 整改批次（2026-09-25，随 A/B 双轮审查执行）

> 方案 `rectify/R8-AB-remediation-plan.md`（独立完善性审查 NO-GO→按指令修订→GO）；整改报告 `rectify/R8-AB-remediation-report.md`。
> 门禁：构建 0 错误；x64 全量回归 **4285/4285**（净增 59 用例）；12 语言 2895 键 parity；新实例 PID 4652 @ 规范 Debug 路径。改动位于工作树未提交。
> **提交（2026-09-25 拆分落库，工作树清零）**：C1 `cf145f71`（P1 批＋钩子/设置底座，逐 commit worktree 编译核验通过）→ C2 `a672a777`（持久化加固）→ C3 `f3402280`（P3 修复扫尾，含 FEVT-02 补齐）→ C4 `5078325e`（死代码/死键清理＋DEF-072 补键＋全部测试）→ C5 `34bd10a0`（文档/台账/勘误）；收口回归 **4286/4286**；实例重启 PID 17256。
> **收口批补录（同日）**：DEF-072（12 语言补键，2896 parity，`5078325e`）与 DEF-075 残余（外层 catch 盖戳 CurrentSchemaVersion＋契约用例，`cf145f71`）已随批修复——上两节历史行中这两条的「📌 挂账」状态以本行为准，更新为 **✅ 已修复（R8-AB 收口批）**。

---

## 历史 P2 专项批（2026-09-25，清零代码类 P2）

> 输入：台账存量 P2（DEF-069/070/071/073/067）。门禁：构建 0 错误；x64 全量回归 **4295/4295**（净增 9 用例）。提交：`6070dabc`（测试钉死＋去闪断）、`c943b139`（DEF-070/071 修复）。

| 编号 | 处置 | 说明 |
|---|---|---|
| **DEF-069** | ✅ 已修复（现状核实＋测试钉死） | 逐行核实当前树已随近期整改落地（CopyEntryAsync 委托 CreateNew 受控核心、按句柄回滚只删自建对象）；本批以 2 个回归用例钉死契约（外来同名目标在冲突回滚中分毫不动、目录回退中止保留完成子项与外来内容）。`6070dabc` |
| **DEF-073** | ✅ 已修复（现状核实＋测试钉死） | 两处源清理调用点均收口至 `DeleteSourceTreeByManifest`（按句柄身份校验＋只读同句柄清除＋fail-closed）；本批以 3 个用例钉死（同卷回退/managed 引擎/字面跨盘——后者 Hardware 门控空跑）。`6070dabc` |
| **DEF-070** | ✅ 已修复（`c943b139`） | `SettingsService` 新增 `AddWidget`/`RecordOrganizationHistoryEntry`/`GetWidgetsSnapshot` 锁内 API；全部 11 处锁外 `Widgets.Add` 写点收口（WidgetManager 创建路径×2、FeatureWidgets×5、Storage、组织事务、AOT smoke×2），组织 watcher 改读快照；grep 复验仅剩 `lock (_lock)` 内写点。并发守恒用例锁死（96 并发 Add × 8 轮保存全量 id 守恒）。 |
| **DEF-071** | ✅ 已修复（`c943b139`） | 拖出预览窗口重构为「锁内规划、锁外应用」：`_gate` 8 个持有者全部 UI 线程安全，Win32 调用（SetWindowPos/ShowWindow/SetLayeredWindowAttributes）全部移出锁块；16ms 跟手节奏不变；源码契约测试断言锁块内零 Win32 调用。 |
| **DEF-067** | ✅ 已修复（`6070dabc`） | 备份快照竞态测试以确定性屏障替代 10ms 轮询时间窗（暂存大文件写满因果性地证明 settings 暂存已关闭）；守护意图不变。 |

> 残留记录：DEF-070 的其余无锁**枚举**读点（WidgetTopologyLayoutService:400/543、DesktopOrganizationCoordinator、Transaction.Restore:368-372 等，均为 UI 线程同线程读或后续批次候选）与 `DesktopOrganizationHistoryStore.WriteTempFileAsync` 不持 `_lock` 的历史写器维持观察，不构成本批立案；`QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 孤儿键维持观察。

### 新增缺陷（B 轮 P1×2，本批已修复）

| 编号 | 标题 | 优先级 | 位置 | 状态 |
|---|---|---|---|---|
| **DEF-108** | 搜索「保存到随记」`new QuickCaptureStore()` 绕过 QuickCaptureService 缓存直写，后续任一随记操作整文档覆盖丢条目 | P1 | `SearchResultActionService.cs:87-111`（原位点） | ✅ 已修复（R8-AB：新增 `AddExternalLinkedFileItemAsync` 走服务门控/缓存/落盘；注入改造；端到端用例钉死） |
| **DEF-109** | 搜索「附加到 Todo」分离 Load/Save 直写 todo.json，常驻 VM 整文档保存丢弃外部条目 | P1 | `SearchResultActionService.cs:41-63`（原位点） | ✅ 已修复（R8-AB：`MutateAsync` 原子变更＋`NotifyExternalStoreChanged` 复用 PublishStoreChanged 中继合并；端到端用例钉死） |

### round-08（DEF-085~107）状态更新

- **✅ 已修复（R8-AB 批次）20 条**：DEF-085、086、087、088、089、091、092、093（手册三处勘误）、094、095、097、098、099、100、102、103、104、105、106、107。
- **📌 部分修复**：DEF-090（注释矛盾已更正；自适应阶梯死代码删除延后至卫生批次）。
- **📌 延后维持**：DEF-096（批量边距「静止几何」语义——需 GUI 对照）；DEF-101（广播隔离泛化——高危实例 FEVT-01 已随批修复，泛化改造待 SafeBroadcast 助手立项）。

### B 轮编号发现处置（未重复编号，随批修复/延后）

- **✅ 随批修复**：FQC-01/02/04、FWIN-01、FLAY-01（96/36 DPI 化子项延后）、FLAY-03、FTHR-01、FEXC-01/02/03、FCFG-04/05/06/07/08、FTHR-03~09、FANI-03/04、FMEM-01/02、FEXC-04/05（含 FEVT-06）、FEVT-02/04/05、FQC-05~10/13/14、FARC-01/02/05、FARC-03（硬化；完整替代待 AOT 实测）。
- **📌 延后维持**：FLAY-02、FQC-11/12/15、FARC-03 完整替代。
- 明细与逐条落点见 `rectify/R8-AB-remediation-report.md` §2。

### 遗留观察（下波裁决）

1. `QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 孤儿键（helper 删除连带，未删）。
2. ~~`WidgetShell.xaml.cs:1836` 预热的三个缓存模板经 DEF-089 per-start 改造后不再被消费~~ **已解除（round-09 复核）**：`PrewarmCompactTransitionCompositionResources`（WidgetShell.CompositionResources.cs:32-48）与 `StartCompactOpacity/Scale/TranslationAnimation`（WidgetShell.xaml.cs:2069-2121）消费同一 `_compositionResources` 实例。
3. ~~`FolderWatcherService.LastEventAt` 改 UTC ticks 存储~~ **已落地（round-09 复核）**：`_lastEventAtTicks` 以 UTC ticks 经 Interlocked 存取（FolderWatcherService.cs:97,145-153）。
4. `scripts/extract_widgetmanager.ps1` 名单含已删方法名（脚本告警继续）。

---

## round-09 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `097b04a3`。纯静态审查，未运行测试。R9-A「核心服务与数据面」+ R9-B「界面交互与工程面」两专项报告与总报告见 `docs/quality/rounds/round-09/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-110** | TodoReminderService 提醒去重集合 `_sessionNotifiedKeys` 跨线程无同步：`Clear` 三处在 UI 线程（Start/Refresh），`Add` 在 `store.MutateAsync`（内部 `ConfigureAwait(false)`）线程池回调；并发 Clear+Add 可损坏 HashSet（漏/重提醒或当轮异常被吞） | P3 | `TodoReminderService.cs:26,91,119,133,439`；`TodoWidgetStore.cs:177-180` | 无锁 HashSet 双线程读写；触发需 30s 扫描 tick 与设置变更毫秒级重叠 | ✅ 已修复（R9 批：`ConcurrentDictionary<string,byte>` TryAdd/Clear，语义不变） |
| **DEF-111** | `LocalizationService.cs` 文件头连写 4 个 UTF-8 BOM（od 字节级实证，全仓唯一双 BOM 以上文件）；编译无碍但破坏字节级工具链预期 | P3 | `LocalizationService.cs:1` | 带 BOM 追加式重写累积 | ✅ 已修复（R9 批：归一为单 BOM，零内容变更） |
| **DEF-112** | SearchEngineService `GetRecentNotesAsync`/`GetUpcomingTodosAsync` 零调用死代码（约 90 行，grep 仅命中声明处），内含直连 store 旧模式，有旁路复活风险（DEF-092 同族、DEF-108/109 反面教材） | P3 | `SearchEngineService.cs:400-440,742-791` | 搜索空态推荐改版遗留 | ✅ 已修复（R9 批：两方法整段删除；`TruncateText`/`Search.Todo.Due` 活代码仍在用，保留） |
| **DEF-113** | 更新器安装成功路径 `_ = RestartApp(options.AppPath)` 丢弃布尔返回值且不回传 outcome：安装成功但重启失败（exe 损坏/杀软拦截/ACL）时应用保持关闭且主程序无任何信号；对照失败路径有 `RestartAfterIncompleteUpdate`、手动渠道 `:45` 检查布尔 | P3 | `DeskBox.Updater/Program.cs:58-61` | 成功路径未对称处理重启失败分支 | ✅ 已修复（R9 批：布尔检查 + `restart-failed` outcome，无需新键） |
| **DEF-114** | 云备份「运行中跳过」测试以 50ms 时间窗 + `Assert.Single(Files)` 断言「无第二次运行」：错误排队若 50ms 内未完成上传则假通过——漏报方向弱断言，违背 DEF-067 确立的「显式同步点代替时间窗」纪律 | P3 | `tests/DeskBox.Tests/CloudBackupTransportTests.cs:296-307` | 断言的是完成数而非启动信号，且无排队信号可等待 | ✅ 已修复（R9 批：第二次上传启动信号 + 250ms 有界负向断言，3 连跑稳定） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-078 家族**：`QuickCaptureSurfaceContent.xaml.cs:1234` `ItemsList_ItemClick`（async void）与 `:2298` 上下文菜单 Edit lambda 两处 `OpenDetailAfterSavingAsync` 调用无外层守卫。**主流程复核勘误（完善性审查 NO-GO 改判）**：磁盘写失败路径已被 `SaveDetailAsync` 内部 catch（`:1566-1575`，`App.Log` + `quick-detail-save-error` 反馈，commit `618ccba5` 起）覆盖、非静默；真实残余逃逸面为详情打开的同步 UI 段异常与 `_detailSaveGate.WaitAsync()`（try 之前）异常——**R9 批已收口**（两处调用点 try/catch + `quick-detail-open-error` 反馈，与 `Root_Drop` 同型；DEF-078 记「部分修复（新位点收口）」），其余位点维持。
- **DEF-070 家族**：`WidgetManager.FeatureWidgets.cs:707,709,759-760,890`（`Widgets.Remove/RemoveAll/DeletedWidgetIds.Add` 仍锁外）；`DesktopOrganizationTransaction.cs:180-184,275-277` 与 `.Restore.cs:361,368-373`（持 OperationGate 不持 SettingsService._lock）——维持挂账（低频维护路径，与台账既有残留同水位）。
- **EXC-06 家族**：`LocalizationService.cs:313-467` 12 个语言表 getter 双检锁无 volatile，与 CitySearchService 同型——并入 EXC-06 维持。
- **孤儿键**：443 候选键（2896 键中约 15.3%）无直接/前缀引用，含整簇死键——维持观察（清理需先排除反射/动态拼接面）。

### 存量条目状态变更（round-09 复核）

| 编号 | 变更 | 依据 |
|---|---|---|
| **DEF-040** | 📌 挂账 → **✅ 已修复（现状核实）** | `FileSurfaceContent.ItemVisuals.cs:1553,1578,1604,1614-1619` 订阅前先退订 + 字典跟踪 + XamlRoot 守卫；Dispose 链至 `FileSurfaceContent.xaml.cs:5287` |
| **DEF-077** | 📌 挂账 → **✅ 已修复（现状核实）** | `SearchHistoryService.cs:258,302` Load/Save 均走 ResilientJsonStore（双代理独立复核一致） |
| **DEF-081** | 📌 挂账 → **✅ 已修复（现状核实）** | `OrganizerService.cs:465-467` undo 以 `UndoReceiptStillMatches` + `DestinationIdentity` 校验对象身份 |
| **DEF-088** | 📌 挂账 → **✅ 已修复（现状核实）** | `NativeFileDragOut.cs:259-271` watchdog 改显式 Timer，全拖拽期存活、每退出路径释放 |
| 遗留观察 #2 / #3 | **解除 / 已落地** | 见上节删除线标注 |
| DEF-074/075/087/102/103/104/105/106/108/109 | 再证闭环（无回退） | A 报告 §3 逐条当前树证据 |
| MEM-01（位点更新至 App.Tray.cs:1026-1030）/ MEM-02 / DEF-048/049/050/051/052/053/056/076/078/079/080/082 / THR-06 / EVT-02 / EXC-06 | 维持 | 两专项报告 §3 逐条证据；DEF-052 部分改善（在途不淘汰已修） |

### 证伪留档（避免后续轮次重复怀疑）

- `FileService.ShellTransfer.cs` IFileOperation 裸 vtable 槽位（MoveItem=14/CopyItem=16/PerformOperations=21/GetAnyOperationsAborted=22、IShellItem::GetDisplayName=5）经 Microsoft Learn IFileOperation 方法全集（20 方法、无 SetApplicationName）逐槽核对**全部正确**，不立案（round-09 A 报告 §1.3）。

### round-09 统计

- **新增**：5 项（P0×0、P1×0、P2×0、P3×5）；**连续第四轮 P0/P1 = 0，首次 P2 = 0**。
- **正面结论**：12 语言 2896 键 parity / 占位符 arity / 日期格式字母 / 引用键完整性四项机械校验全绿；XAML 资源引用 0 失效；Rust ABI 十导出 / 掩码 511 / panic=abort 零漂移；R8-AB + 历史 P2 批整改（147 文件 diff）逐文件审读无回归；文件安全与持久化面处于历史最高加固水位。
- **整改（R9 批，随轮完成）**：DEF-110~114 全部 ✅ 已修复 + DEF-078 两处新位点收口。方案 `rectify/R9-remediation-plan.md`（独立完善性审查 NO-GO→按指令修订→GO）；报告 `rectify/R9-remediation-report.md`。门禁：Debug x64 构建 0 错误（22 警告 ≤ 基线）；`static_gate.py` PASS（Windows 侧首次留档运行，脚本盘符路径兼容修复 + 基线刷新留档 `rectify/r9-static-gate.json`）；x64 全量回归 **4295/4295**；DEF-114 用例 3 连跑稳定；新实例 PID 31372 @ 规范 Debug 路径。

---

## round-10 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `e3ad11ad`。纯静态审查，未运行测试。R10-A「核心服务与数据面」+ R10-B「界面交互与工程面」两专项报告与总报告见 `docs/quality/rounds/round-10/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-115** | TodoRecurrenceService 克隆附件遗漏 `StorageMode`（默认变 linked）且跨事项浅共享托管物理文件无删除协调：下一期附件误分类致删除不清理成孤儿、健康扫描归因失真（对照 `CloneTodoItem` 规范克隆正确复制该字段，属遗漏）；两侧共享同一 FilePath，源事项删附件 `File.Delete` 致下一期悬空，行为不对称 | P3 | `TodoRecurrenceService.cs:79-88`；`Models/TodoAttachment.cs:18`；`TodoWidgetViewModel.DetailAndAttachments.cs:391-399` | 手写初始化器漏字段 + 浅共享无引用协调；托管目录按事项分目录、事项删除无整目录清理 | ✅ 已修复（R10 批：克隆补 StorageMode + 删除托管文件前同 store 引用扫描；2 个新契约用例钉死，回归 4297/4297） |
| **DEF-116** | Format 调用点实参数与键占位符失配：①`TodoItemViewModel.cs:582` Collapse 分支传 1 参而 `Todo.RecurrenceHistory.Collapse` 无占位符；②`SearchPopupViewModel.cs:691-697` 以 2 实参服务 `Search.Status.PartialResults`（仅 {0}）——`string.Format` 静默吞多余实参，契约靠容错维系（当前无用户可见破损） | P3 | `ViewModels/TodoItemViewModel.cs:582`、`ViewModels/SearchPopupViewModel.cs:691-697` | 调用点按分支并集最大占位符数书写；「调用点实参 ↔ 键占位符」层此前无任何校验覆盖（本轮首扫） | ✅ 已修复（R10 批：Collapse 去死实参；PartialResults 分支单独 Format 只传 {0}；展示文本零变化） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-080 家族**：`CloudBackupService.cs:607`（快照名 current-culture 时间戳 → ar-SA 下 InvariantCulture 解析恒失败、优雅回退 LastModified 排序——唯一有解析消费方的位点，修复 DEF-080 时首选）；`AppUpdateService.cs:395`；`AttachmentStorageService.cs:45`、`DeskBoxDataBackupService.cs:263,319,380,2897`、`DeskBoxDragData.cs:264,535`、`ResilientJsonStore.cs:372`（外观级）。
- **DEF-101 家族**：`CloudBackupService.cs:207,284,358`、`AppUpdateService.cs:644`（同文件 `FileTransferSessionRegistry.RaiseStateChanged` :318-330 已是隔离范式）。
- **THR-06 同族**：`AppDiagnosticsService.cs:121-160`（纯诊断字段）。
- **DEF-078 家族**：`GlanceWidgetSettingsSection.xaml.cs:73-78` OnLoaded async void → RefreshFromStoreAsync 无 catch（存储已弹性自愈，残余 UI 段，理论性较强）。
- **孤儿键**：重扫 573 候选（R9 报 443，方法差异），簇分布一致，维持观察。

### 存量条目状态变更（round-10 复核）

- **无改判**。round-09 修复批（DEF-110/111/112/113/114 + DEF-078 两处收口）经双代理独立逐行复核**全部正确落地、无回归**（字节级/diff 级/grep 级验证）；R8-AB 关键修复（DEF-086/087/102/104/105/108/109）再证在位；维持类条目（DEF-048/049/050/051/052/053/055/076/078/079/080/082/083/084/090、EVT-02、MEM-01/02、EXC-06、THR-06）均给当前树证据；无已修复项回退。
- **正面结论**：12 语言六项机械校验全绿（2896 parity / 占位符 arity / 日期字母 / 1500 引用键 0 缺失含动态键族展开验证 / Format 调用点 arity 首扫 / 孤立花括号 0）；XAML 197 资源键 0 失效、799 事件绑定 0 缺失处理器；Rust ABI 零漂移（十导出/ABI 2/掩码 511/panic=abort）；渲染事件 7 对全配对、订阅生命周期纪律全面良好；scripts/installer 自 round-08 零漂移。**连续第五轮 P0/P1 = 0，立案数收敛 5 → 2。**

### round-10 整改批（2026-09-27，随轮完成）

- **方案**：`rectify/R10-remediation-plan.md`（独立完善性审查首审 **GO**，5 条建议全采纳：文件数笔误、SearchPopup 行号校正 ：689-694、AOT 契约字面串顺序约束留痕、新增 VM 删除用例、防御姿态）。
- **落地**：DEF-115（克隆补 `StorageMode` + `DeleteAttachmentAsync` 删除前同 store 引用扫描 + 2 个新契约用例：`TryCreateNextOccurrence_PreservesAttachmentStorageModes`、`DeleteAttachmentAsync_KeepsSharedManagedFileWhileOtherItemReferencesIt`）；DEF-116（Collapse 去死实参；PartialResults 分支单独 Format，展示文本零变化）。
- **门禁**：构建 0 错误（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**（五项与基线持平，留档 `rectify/r10-static-gate.json`）；x64 全量回归 **4297/4297**（+2 用例）；新实例 PID 29740 @ 规范 Debug 路径。报告 `rectify/R10-remediation-report.md`。

---

## round-11 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `8a8a5bde`。纯静态审查，未运行测试。R11-A「核心服务与数据面」+ R11-B「界面交互与工程面」两专项报告与总报告见 `docs/quality/rounds/round-11/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-117** | FolderWatcherService 重连/双失败路径在 UI 线程同步探测失效文件夹（含 UNC 阻塞）：StartAsync 双失败分支裸用同步 `ProbeFolderAccess`（Directory.Exists + 属性枚举，离线 SMB 阻塞数秒），主路径已用 Task.Run 版 `ProbeFolderAccessAsync`；`ReconnectTimer_Tick`（UI 线程）与 StartAsync 入口同步 `TryResolveExistingPathForTraversal`——离线路径每轮重连 tick 重复阻塞 UI（THR-07/DEF-035 同族，触发面限网络离线） | P3 | `FolderWatcherService.cs:265-271,288-310,861-866,211-216` | 同文件主路径/分支探测方式不一致；`ProbeFolderAccessAsync` 即现成 Task.Run 等价物 | ✅ 已修复（R11 批：双失败分支改异步探测 + 代际守卫镜像；入口/重连解析包 Task.Run，回退语义钉死 path；回归 4297/4297） |
| **DEF-118** | DragDropPermissionService 诊断路径 Process 对象未 Dispose：`GetProcessesByName("explorer").FirstOrDefault()` 数组其余元素与选中实例均无 using——每次拖放诊断泄漏 ≥1 个进程句柄至终结器（低频非增长）；同仓 QuickLookPreviewService/MemoryReclaimer 已确立 using 规范 | P3 | `DragDropPermissionService.cs:804-828` | Process 句柄依赖终结器兜底；FirstOrDefault 遗弃数组其余元素 | ✅ 已修复（R11 批：try/finally 全量 Dispose 数组元素——比报告建议的 using var 首实例更彻底） |
| **DEF-119** | 安装器 .NET 运行时检测只覆盖机器级布局：仅探 `{autopf}\dotnet` 与 `{pf}\dotnet`，漏 `%LOCALAPPDATA%\Microsoft\dotnet`（未提权 .NET 安装器/dotnet-install/VS 组件默认落位）→ 用户级 .NET 10 误判缺失：冗余下载约 55MB + 计划外 UAC + 机器级重复安装（应用最终可用）；对照同文件 App Runtime 检测有 -AllUsers 兜底 | P3 | `installer/DeskBox.Dependencies.iss:105-107`、`installer/DeskBox.Dependencies.arm64.iss:105-107` | 检测点集合固定两布局；x64/arm64 同位点 | ✅ 已修复（R11 批：增补 `{localappdata}\Microsoft` 探测点 ×2 变体 + 提权上下文注释；PATH 兜底留痕） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-078 家族**：`MusicWidgetContent.xaml.cs:432` 进度条拖放提交链（async void → CommitSeekAsync → `MusicSessionService.cs:266` TrySeekAsync 裸调陈旧 SMTC 会话）——seek 入口补入家族清单。
- **孤儿键**：本轮 172 候选（方法差异），簇分布与 R9/R10 一致，维持观察。

### 存量条目状态变更（round-11 复核）

- **无改判**。round-10 修复批（DEF-115/116）双代理独立复核正确落地、无回归（并为 DEF-115 引用扫描补充作用域充分性论证：循环历史候选同在 Items、托管目录按事项隔离使跨格子共享不可能）；DEF-110~113 抽样在位；DEF-054 自 R8 后首次实读维持。
- **证伪留档**：`DirectStartupTaskXmlReader` 裸 vtable 五槽位（GetFolder=7/Connect=10/GetTask=13/GetXml=20/Release=2）经 Windows SDK taskschd.h 逐槽核对全部正确；**QuickCaptureService（2,207 行）首次全文审读零立案**（tombstone/undo-window/附件 GC 逐路径自洽）。
- **正面结论**：12 语言六项机械校验全绿（含 Format 调用点三形态 arity 复跑）；XAML 709 事件绑定与 195 资源键零失效；Rust ABI 零漂移；publish-aot-retail.ps1 全文通过。**连续第六轮 P0/P1 = 0。**

### round-11 整改批（2026-09-27，随轮完成）

- **方案**：`rectify/R11-remediation-plan.md`（独立完善性审查 GO）。
- **落地**：DEF-117（双失败分支 `await ProbeFolderAccessAsync`；ReconnectTimer_Tick 与 StartAsync 的 `TryResolveExistingPathForTraversal` 包 Task.Run，生成代际守卫保持）；DEF-118（GetExplorerTokenSnapshot try/finally 全量 Dispose Process 数组）；DEF-119（两个 Dependencies .iss 增补 `{localappdata}\Microsoft` 探测点）。
- **门禁**：构建 0 错误；`static_gate.py` PASS；x64 全量回归全绿；新实例核验。报告 `rectify/R11-remediation-report.md`。

### round-11 整改批收口（同日）

- **审查**：`rectify/R11-remediation-plan.md` 独立完善性审查首审 **GO**（5 条建议全采纳——A1 tick 回退语义钉死、A2 双失败分支代际守卫镜像、A3 提权上下文注释、S2 文件计数更正、S1 验证措辞对齐）。
- **门禁**：构建 0 错误（22 警告 ≤ 基线 24）；x64 全量回归 **4297/4297**；`static_gate.py` **PASS**（五项与基线持平，留档 `rectify/r11-static-gate.json`）；新实例 PID 19732 @ 规范 Debug 路径。报告 `rectify/R11-remediation-report.md`。

---

## round-12 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `7d201074`。纯静态审查，未运行测试。R12-A + R12-B 两专项报告与总报告见 `docs/quality/rounds/round-12/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-120** | LocalizationService 默认「跟随系统」（出厂默认）下每次 T() 取词都同步读一次注册表（无记忆化），被 1,200+ 调用点放大为热路径重复系统调用；注册表值仅安装器写入、进程内恒定 | P3 | `LocalizationService.cs:159-174,37-49,265-297`；`CoreSettingsSlice.cs:21` | 安装语言特性接入时未记忆化解析结果 | ✅ 已修复（R12 批：Lazy<string> 静态记忆化，仅 System 分支触发求值，语义零变化） |
| **DEF-121** | 桌面整理三处硬编码 CJK 顿号「、」作列表分隔符，违反同仓 `IsChinese ? "、" : ", "` 既有范式（SettingsViewModel 两处对照），11 个非中文语言 UI 全部可见 | P3 | `DesktopOrganizationSettingsSection.xaml.cs:844,865`、`DesktopOrganizationTaskView.xaml.cs:295` | Join 分隔符未走语言分支 | ✅ 已修复（R12 批：两类各加 ListSeparator 静态属性对齐 IsChinese 范式） |
| **DEF-122** | 快捷方式文件名后缀硬编码英文 `" - Shortcut.lnk"`（Alt 拖放创建），中文 UI 下也带英文命名（Explorer 原生「 - 快捷方式」）；同文件已用 `Widget.CreateShortcut` 键，后缀漏配 | P3 | `FileSurfaceContent.ShortcutDrop.cs:107-109` | 后缀未本地化 | ✅ 已修复（R12 批：新增键 `Widget.CreateShortcutSuffix` ×12（键集 2897×12 对齐），后缀参数传入静态助手，空名兜底同键 TrimStart 覆盖） |
| **DEF-123** | **round-11 DEF-117 修复引入的重入竞态**：StartAsync 入口解析改 await 后，「Stop→写 _requestedPath→读代际」移到首个 await 之后——离线 UNC 慢解析的旧调用让出 UI 线程期间新目录可完整插队启动；旧调用恢复后 Stop 掉新目录 watcher、覆写 _requestedPath、此刻才读代际=最新值、守卫恒通过 → watcher 持久错挂旧目录且新目录静默失刷新（健康显示 Watching；重新导航自愈）。修复前该段同步执行、语义天然「后到者赢」 | P3 | `FolderWatcherService.cs:213-231`（交错面 :267-279、:851-914） | 代际认领在 await 之后，守卫无法识别「await 期间已被取代」；Stop() 锁内 `_watchGeneration++`（:583）提供守卫基础 | 📌 挂账（R12 批修复：入口快照 entryGeneration + await 后、Stop 前校验；ReconnectTimer_Tick 解析 await 同款） |
| **DEF-124** | CompleteTrackedImportAsync 跨线程封送 lambda 无守卫：非 UI 线程完成路径经 TryEnqueue(async) 封送，被 await 调用抛异常时（同文件 :322-334 已证实 Cancel 可抛 AggregateException 且有防御先例）`TrySetResult` 永不执行 → 后台调用方永久挂起 + 导入忙态卡死 | P3 | `FileSurfaceContent.ImportProgress.cs:238-243` | 结果置位在 await 之后且无 finally | ✅ 已修复（R12 批：try/finally 包裹，TrySetResult 移入 finally） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-097 家族**：`FileSurfaceContent.SelectionAndMenus.cs:1313-1341` CopyPathsToClipboard（SetContent+Flush 无守卫，3 入口；同仓其余剪贴板写点均已守卫）。
- **DEF-078 家族**：`FileSurfaceContent.xaml.cs:4719` QuickLook 键盘导航 TryEnqueue 无内层守卫（本轮 25 处全量盘点闭合）。
- **ANI-06 家族**：3 文件 5 位点 SetIsTranslationEnabled(true) 从不复位（Navigation.cs:330/368/392、StackAnimations.cs:270、SearchPopupWindow.xaml.cs:2375）。
- **DEF-080 家族**：显示级 current-culture 时间戳 7 位点（OrganizationHistoryEntry.cs:90、QuickCaptureItemViewModel.cs:395、SettingsViewModel.AboutAndUpdates.cs:518/521、RuntimeDiagnostics.cs:68、QuickCaptureDiagnostics.cs:105）。
- **O-9 家族**：SearchResultRanker.cs:63、WidgetStackGroupingService.cs:280 CurrentCulture 排序（纯展示序）。

### 存量条目状态变更（round-12 复核）

- **无改判**。round-11 修复批复核：DEF-118/119 正确落地；DEF-117 目标达成但引入 DEF-123 回归（立案随轮修复）。DesktopOrganization 自动整理引擎 10 文件首次全文审读零立案；FileService.PathResolution 驱动器根链接疑点经 dotnet/runtime 源码契约证伪留档。
- **正面结论**：12 语言机械校验全绿（Format arity 三形态复跑 0 失配）；XAML 709 事件绑定与 195 资源键零失效；Rust ABI 零漂移；25 处 TryEnqueue(async) 全量盘点闭合。**连续第七轮 P0/P1 = 0。**

### round-12 整改批（2026-09-27，随轮完成）

- **方案**：`rectify/R12-remediation-plan.md`（独立完善性审查：**首审不通过**——W1 tick 探测 await 残留窗口（probe 成功路径可复现 DEF-123）+ 台账预写流程问题；按指令修订（tick 两个 await 后均校验 + 台账改待实施占位）后复审 **通过**，另采纳 5 条建议：W4 字段核正、W5 参数化后缀 + 兜底覆盖、W1 残留语义留档、W5 验证措辞、DEF-123 行回填表述绑定）。
- **落地**：DEF-123（StartAsync 入口快照 + 解析 await 后校验；ReconnectTimer_Tick 顶部快照 + 解析/探测两个 await 后均校验）、DEF-124（封送 lambda try/finally）、DEF-120（Lazy 记忆化）、DEF-121（两类 ListSeparator + 3 站点）、DEF-122（新键 ×12 + 参数化后缀 + 兜底覆盖）。
- **门禁**：构建 0 错误（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**（12 语言 2897 键对齐，其余五项与基线持平，留档 `rectify/r12-static-gate.json`）；x64 全量回归 **4297/4297**；新实例 PID 21488 @ 规范 Debug 路径。报告 `rectify/R12-remediation-report.md`。

---

## round-13 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `7387efeb`。纯静态审查，未运行测试。R13-A + R13-B 两专项报告与总报告见 `docs/quality/rounds/round-13/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-125** | FolderWatcherService.StartAsync **成功分支**缺代际复核（DEF-123 修复不完备残留）：失败分支已有「probe await + 守卫」，成功分支在 query await 后直接 SetHealth + 取消已排程重连（_reconnectPath=null/_reconnectAttempt=0）——query await 期间被取代时旧调用结算覆盖新调用状态 | P3 | `FolderWatcherService.cs:285-306` | DEF-123 修复的守卫未覆盖 query await 窗口的成功路径 | ✅ 已修复（R13 批：queryStarted await 后补同款代际复核，与失败分支守卫对称；回归 4297/4297） |
| **DEF-126** | SearchPopupWindow 关闭观察器子类钩子从不移除：`_isPopupCloseWatcherInstalled` 只写死状态，OnWindowClosed 全量清理链缺 RemoveWindowSubclass——全仓 10 处 comctl32 子类化唯一无对称卸载（其余 9 处含 WM_NC_DESTROY 兜底先例）；当前「永不主动关闭只隐藏」模型下无即时危害，隐患在卸载顺序变化时残留 thunk | P3 | `SearchPopupWindow.xaml.cs:79-80,168-176,4508-4542` | 「永不主动关闭」模型清理清单漏 Win32 对称项 | ✅ 已修复（R13 批：OnWindowClosed 顶部三行卸载 + 字段复位，NC_DESTROY 兜底省略理由留档） |

### 已知模式新位点（并入既有编号，不新立案）

- **ANI-06 家族**：+9 位点（R12 仅记 5/14，本轮全量补齐）。
- **DEF-080 家族**：+4 位点（TodoItemViewModel 显示级）。

### 存量条目状态变更（round-13 复核）

- **无改判**。round-12 修复批复核：DEF-120/121/122/124 正确落地；DEF-123 主目标达成、守卫链全排列推演自洽（仅成功分支残留即 DEF-125）；O-20 解除（本轮唯一观察项解除）。EverythingSearchService 全文、WidgetManager 主文件+Storage 全文、Todo DetailAndAttachments 全文、100 个零提及 Services 文件全模式扫描零新增立案。
- **正面结论**：2897 键 ×12 parity、占位符 arity 0 失配、Format 调用点 230+3 处 0 失配、XAML 197 资源键/757 事件绑定零失效、Rust ABI 与 scripts/installer 零漂移。**连续第八轮 P0/P1 = 0。**

### round-13 整改批（2026-09-27，随轮完成）

- **方案**：`rectify/R13-remediation-plan.md`（独立完善性审查**首审 GO**，3 条建议全采纳：成功分支行号更正 :302-312、WatchedPath 可接受性留档、NC_DESTROY 兜底省略理由）。
- **落地**：DEF-125（queryStarted await 后补代际复核，与失败分支守卫对称）；DEF-126（OnWindowClosed 顶部三行对称卸载 + 字段复位）。
- **门禁**：构建 0 错误；x64 全量回归 **4297/4297**；`static_gate.py` **PASS**（五项与基线持平，留档 `rectify/r13-static-gate.json`）；新实例 PID 32244 @ 规范 Debug 路径。报告 `rectify/R13-remediation-report.md`。

---

## round-14 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `435b1a1a`。纯静态审查，未运行测试。R14-A + R14-B 两专项报告与总报告见 `docs/quality/rounds/round-14/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-127** | ShellDropDelegator 委托发射子系统整体死代码 + 设计依据文档互相矛盾：`TryDelegateDrop` 链与 `TryCreateHdropDataObject` 全仓零调用点（活路径走 ShortcutFileLauncher/ShellExecuteEx）；类注释自称「与 Explorer 行为一致」与 ShortcutFileLauncher.cs:30-34 实测记载（该路径真机弹 Open with 被放弃，drop_on_shortcut_open.md §10.2）完全相反；6 个测试钉死死路径契约（BuildHdropBytes 仅被死链使用，主流程核验闭合） | P3 | `ShellDropDelegator.cs:149-248`、`ShellDataObjectBuilder.cs:56-146,145-152`、`NativeDropTarget.cs:506-509`、`ShellDataObjectBuilderTests.cs` | 发射方案切换时未删除旧实现与旧注释 | ✅ 已修复（R14 批：死类删除 + ShellDataObjectBuilder/SHCreateItemFromParsingName/死测试整删 + 注释校正；回归 4291/4291） |
| **DEF-128** | OnboardingWindow 旧版五步流程整体死代码（~1,500 行跨 8 文件）：活映射仅 TaskStep 面板（StepCount=4），六个旧面板 Collapsed 且 .cs 零引用；旧 Setup 链调用图封闭于死集（主流程核验闭合）；DEF-036/038/087 的 onboarding 位点全部落在死链上（台账靶向失准）；孤儿键最大簇的实体来源 | P3 | `OnboardingWindow.xaml:174-260,845-1523`、`xaml.cs:560,608`、`Appearance.cs` 整文件、`Features/Completion/Storage/Hotkey/TaskFlow.cs` 死成员 | 重构为 TaskStep 任务流时旧面板/Setup/动画退役未删除 | ✅ 已修复（R14 批：七个 Collapsed 死面板（含实施发现的第七面板 StepOrganizationPanel）+ 5 分部整删 + 孤儿字段 7 项 + 3 活方法死分支；孤儿键 230 ×12 删除（2897→2667）；契约测试 4 文件裁剪；AutoStart 激活期自纠行为随旧流退役（显式声明）；AOT 审计通过） |
| **DEF-129** | 品牌标志 BrandLogoHost 嵌在永久 Collapsed 死面板内：真实 Logo（deskbox.svg 全仓唯一展示位）引导全程不可见，intro 交接协议操作不可见元素；StartBrandLogoShine Forever 动画自 Loaded 空转至关闭；死面板与活协议互相咬合（DEF-128 的前置迁移项） | P3 | `OnboardingWindow.xaml:851-885` ↔ `IntroAnimations.cs:39-46,121-128,212-224,253-277` | 重构时品牌标志随旧面板折叠，活协议引用指向旧面板内元素 | ✅ 已修复（R14 批：BrandLogoHost 迁至 RootGrid Row 0 居中 + IsHitTestVisible=False，deskbox.svg 唯一展示位恢复可见，shine 空转消除；GUI 目检列入人工复验清单） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-070 家族读侧**：`SearchEngineService.cs:287-288`、`:553-555`（线程池枚举活 Widgets）。
- **DEF-092 家族**：`TodoItemViewModel.cs:388` MarkerGlyph 死属性（两分支恒等笔误化石）。
- **O-26 家族**：`FolderWatcherService.cs:819-829`（catch 结算无代际复核，近不可达）。
- **DEF-078 家族**：B 面 +1 位点。

### 存量条目状态变更（round-14 复核）

- **无改判**。round-13 修复批复核：DEF-125（五个 await 窗口守卫网全貌清点闭合）与 DEF-126（全仓 14 处子类化全部对称卸载）正确落地、零回归。Helpers 拖放 COM 家族 13 文件首次全文（4 组裸 vtable 逐槽核对正确、IDropTargetHelper 疑点证伪留档）；TodoItemViewModel 首次全文（Todo 双 VM 家族收官）。
- **正面结论**：2897 键 ×12 parity、Format 调用点 230 处 0 失配、XAML 195 资源键/事件绑定零失效、Rust ABI 零漂移。**连续第九轮 P0/P1 = 0。**

### round-14 整改批（2026-09-27，随轮完成）

- **方案**：`rectify/R14-remediation-plan.md`（独立完善性审查**首审 NO-GO**——M1 漏 DesktopOrganization.cs/3 个活方法死分支/未声明 AutoStart 自纠退役，M2 契约爆炸半径 4 文件非计数类缺失；按指令重写 v2 后复审 **GO**，S1~S5 全采纳）。
- **落地**：W1 DEF-127（死子系统 ~540 行 + 死测试 6 用例删除）；W2 DEF-129（BrandLogoHost 迁至标题行，deskbox.svg 恢复可见）；W3 DEF-128（七个 Collapsed 死面板——含实施发现的第七面板 StepOrganizationPanel、5 分部整文件、孤儿字段 7 项、3 活方法死分支删除；孤儿键 230 ×12；契约测试 4 文件「保 Settings 半段」裁剪；**AutoStart 激活期自纠行为随旧流退役**）。实施中发现的计划外事实：第七死面板 StepOrganizationPanel（B 代理与首轮审查均未单列），经引用图核验后纳入死集。
- **门禁**：构建 0 错误（20 警告净降 2）；x64 全量回归 **4291/4291**（4297 − 6 死路径用例）；`static_gate.py` **PASS**（12 语言 2667 键；async void −2/空 catch −4 均删除驱动；留档 `rectify/r14-static-gate.json`）；`publish-aot-audit.ps1 -Platform x64` **通过**（WMC1510=742=上限、ABI 2/掩码 511/十导出、260 秒）；新实例 PID 2696。报告 `rectify/R14-remediation-report.md`。

---

## round-15 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `08bdd98e`。纯静态审查，未运行测试。R15-A + R15-B 两专项报告与总报告见 `docs/quality/rounds/round-15/`。全部立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-130** | round-14 删除批遗漏收缩两张契约 manifest，6 条死条目指向已删除文件（ModuleBoundary: ShellDataObjectBuilder.cs=5；SettingsSlice: OnboardingWindow.Appearance/Completion/DesktopOrganization/Features/Hotkey/Storage 五分部）——ratchet 记忆失真，同名文件复活即获静默豁免；违反「删文件必须退役预算」既定惯例。**A-01 前半与 B-01 同一发现，去重合并** | P3 | `ModuleBoundaryContractTests.cs:32`、`SettingsSliceOwnershipContractTests.cs:344-349` | round-14 收缩了 FolderPickerModernization 但遗漏这两张 manifest | ✅ 已修复（R15 批：6 条死条目退役 + 先例注释；`Completion.cs=5` 合法 slack 按审查钉死保留；回归 4291/4291） |
| **DEF-131** | `MergeWidgetsAsync` 是唯一不取消在途 Surface 切换请求的群组拓扑变更入口（全仓取消点清单 :1315/:1506/:1727/WidgetManager.cs:2214/Surfaces.cs:219,229 均不含 merge）：合并持全局 _widgetGroupGate、切换持 per-surface switchGate，两锁不相交；切换结算段（:1156-1237）无取消检查，期间合并可退休窗口/改写 ActiveMemberId/重排 capsule order，最坏瞬时身份错挂（可自愈，DEF-123 家族同级） | P3 | `WidgetManager.Groups.cs:722-927`（对照取消点） | 合并漏配「请求协调器取消 + switchGate」双防线，防御网系统性不对称 | ✅ 已修复（R15 批：MergeWidgetsAsync 同组校验后对两侧 surface 各补 Cancel，全仓拓扑入口防线闭合；回归 4291/4291） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-116 家族**：`SettingsWindow.Maintenance.cs:493-504`（局部变量键选择形态——Format-arity 扫描器加固候选）。
- **DEF-078 家族**：`SettingsWindow.DataTools.cs:39/78/112`（破坏性维护按钮 async void 无 catch）+ `OnboardingWindow.TaskFlow.cs:110/147`（理论）。
- **DEF-080 家族**：`Maintenance.cs:248/407/419`、`Feedback.cs:470`（显示级）。

### 存量条目状态变更（round-15 复核）

- **DEF-016 → ✅ 已消除（R15 复核改判）**：唯一载体 QuickCaptureWidgetWindow 已随 DEF-027 整体删除。
- **round-14 大删除批验证干净（本轮主目标）**：删除键 230 ×12 零反向残留、2,667 键 parity 抽验通过、已删符号/面板/字段零残余引用、XAML 嵌套平衡、63 命名元素零悬空、435 事件绑定与 189 资源键零失效、BrandLogoHost 交接协议自洽、TaskStep 活流闭合、契约裁剪断言成立；唯一残余 DEF-130。`WidgetManager.Groups.cs`（2,771 行）首次全文审读；App.Aot*Smoke 27 装置三向一致性全绿。
- **正面结论**：机械校验全绿（2667 键 ×12 parity、Format arity、XAML 资源/事件绑定、Rust ABI 零漂移）。**连续第十轮 P0/P1 = 0。**

### round-15 整改批（2026-09-27，随轮完成）

- **方案**：`rectify/R15-remediation-plan.md`（独立完善性审查**首审不通过**——R1：W1 把仍存在的 `Completion.cs=5` 合法松弛误列进死条目（门禁不可拦截的静默漂移面）；按 R1~R3 修订（死条目钉死到行 + Completion 保留 + ModuleBoundary 新注释 + 锚点更正）后复审 **GO**）。
- **落地**：DEF-130（6 条死条目退役，`Completion.cs=5` 保留为合法 slack）；DEF-131（MergeWidgetsAsync 双侧 Cancel，全仓拓扑入口防线闭合）。
- **门禁**：构建 0 错误；x64 全量回归 **4291/4291**；`static_gate.py` **PASS**（五项与基线持平，留档 `rectify/r15-static-gate.json`）；新实例 PID 25320。报告 `rectify/R15-remediation-report.md`。

---

## round-16 双路全量代码缺陷深度审查（2026-09-27，2 路并行）

> 代码基线 `wip/fix-bug` @ `2d64b5c0`。纯静态审查，未运行测试。R16-A + R16-B 两专项报告与总报告见 `docs/quality/rounds/round-16/`。立案经主流程当前树核验后入账。

### 新增缺陷

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-132** | round-15 DEF-130 修复越界删除被 R15 方案与审查明文钉死保留的 `OnboardingWindow.Completion.cs = 5` 棘轮条目：四处文档声称「按审查指令保留」而 diff 实际删除了该行，注释还声称「survives as legal slack」——文档与 manifest 实态矛盾。运行时无害（棘轮只罚增），但活文件未来新增合法 facade 访问将以 NEW 违规失败 CI——修复自身复发了它要消除的「棘轮记忆失真」 | P3 | `SettingsSliceOwnershipContractTests.cs:344-348`（引入提交 76ef0b28） | 实施 Edit 的 old_string 把 Completion 行并入删除块（R15 审查警示成真） | ✅ 已修复（R16 批：恢复该一行 + 注释如实记载修复史；与 R15 方案钉死值逐字符比对一致） |

### 已知模式新位点（并入既有编号，不新立案）

- **DEF-078 家族**：+2（AttachmentTileStrip 缩略图链，外观级低水位）。

### 存量条目状态变更（round-16 复核）

- **无其他改判**。round-15 修复批复核：DEF-131 正确落地（零让出点核验通过）；DEF-130 主体干净，唯一残余 DEF-132 已随轮修复。ZOrder 分部（1,351 行）、SearchPopupViewModel（1,525 行）、Contracts、AOT 回收站装置（703 行）等 4 个盲区首次全文深读零立案；15 个历轮从未点名文件闭合零立案。
- **正面结论**：2667 键 ×12 parity、Format arity 227 处 0 失配、XAML 189 资源键/727 事件绑定零失效、Rust ABI 零漂移、ModuleBoundary 复算 0 GREW。**连续第十一轮 P0/P1 = 0。**

### round-16 整改批（2026-09-27，随轮完成）

- **方案与复核**：DEF-132 为一行恢复（与 R15 方案钉死值逐字符比对一致）——方案与复核由 R16 审查自身承担（其发现即诊断、其修法建议即方案，见 `round-16/B-…报告.md` §4 B-01 建议修法），主流程按建议逐字符执行并经门禁验证。
- **门禁**：构建 0 错误；x64 全量回归 **4291/4291**；`static_gate.py` **PASS**（五项与基线持平，留档 `rectify/r16-static-gate.json`）；新实例 PID 23980。

---

## round-17 五路全量代码缺陷深度审查（2026-10-27，5 路并行 + Lead 独立复核）

> 代码基线 `wip/fix-bug` @ `61213206`，工作树含 215 文件未提交改动 + 13 个未跟踪源码文件。纯静态审查，未运行代码/测试，未修改源码。
> 5 份专项报告 + 总报告见 `docs/quality/rounds/round-17/`（`S1-…`/`S2-…`/`S3-…`/`S4-…`/`S5-…`、`总报告-全量代码缺陷审查.md`、`lead-verified-findings.md`）。
> **本轮协议明令禁止 SubAgent 读取 `docs/quality/**` 与 `docs/audit/**` 历史结论**，因此本节全部条目均从代码独立得出；「存量条目状态变更」本轮不适用（历史条目维持原状态，待 Round-18 交叉比对）。
> 规模：984 文件 / 314,283 行全量模式扫描（100%），约 45,000 行深读。**P0=0，P1=1，P2=22，P3=33，合计 56 条**。

### 新增缺陷 — P1

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-133** | 截图标注导出：文本标注必然渲染成实心色块（屏幕显示正常、导出错误） | P1 | `Controls/ScreenshotAnnotationRasterizer.cs:1245-1252` | `GdiText.TryRender` 覆盖度取 `max(green, blue)`，而 DIB 为 32bpp BI_RGB（BGRA）、品红哨兵 `0x00FF00FF` 与白色文字 `0x00FFFFFF` 的蓝通道都是 255 → 覆盖度恒 255 → `DrawText` 以 alpha=1 全不透明混合，文字矩形变实心块。类内注释自称「only green carries the signal」与实现矛盾。现有单测只用 1×1 帧断言 `AlphaAt(0,0)==255`，无法发现 | ✅ 已修（R17 整改批：`coverage[i] = buffer[offset + 1]`，新增用例 `Render_TextPaintsGlyphCoverageInsteadOfAFilledBox`） |

### 新增缺陷 — P2

| 编号 | 标题 | 优先级 | 位置 | 根因/机制 | 状态 |
|---|---|---|---|---|---|
| **DEF-135** | 关闭序列在托盘/钉窗/日志尾部之前释放单实例互斥体 | P2 | `App.xaml.cs:6975-6981` vs `:6767-6773` | 步骤序列末尾无条件 `ReleaseMutex()+Dispose()+null`，而 `finally` 注释恰好论证「提前释放会让新实例在本进程仍写盘时启动」；释放后仍有 `_trayWindow?.Close()`/`ShutdownScreenshotServices()`/`DrainLogQueue()`/`ArmShutdownExitBackstop`/`Exit()` | ✅ 已修（R17 整改批） |
| **DEF-136** | 三个 per-kind 设置 store 的 `SaveAsync` 永不抛异常 | P2 | `MusicSettingsStore.cs:170-201`、`PomodoroSettingsStore.cs:215-251`、`ScreenshotSettingsStore.cs:207-221` | 写链 link 内 catch 吞掉落盘异常后正常返回 → 注释承诺的 durable 契约失效 | ⬜ 待修 |
| **DEF-137** | `LoadFromDisk` 无重试，瞬时读失败被固化为默认值并回写覆盖 | P2 | `PomodoroSettingsStore.cs:169-177,289-321`、`MusicSettingsStore.cs:227-259` | 共享冲突 → 静默返回默认值 → `_cachedVersion` 戳成当前版本 → 下次 Update/Save 用默认值覆盖真实设置，两代内 `.bak` 也被覆盖 | ⬜ 待修 |
| **DEF-138** | 过期的 `SnoozedUntil` 永久静音该条待办的全部提醒 | P2 | `TodoReminderService.cs:806-813` | `return now >= snoozedUntil && now <= snoozedUntil + Grace` → 宽限期后无条件早返回，值持久化不复原 | ⬜ 待修 |
| **DEF-139** | `ResilientJsonStore` 提交路径缺 flush-to-disk（同文件回退路径却有） | P2 | `ResilientJsonStore.cs:370-394`、`SettingsService.cs:1381,1406` | 提交用 `File.WriteAllTextAsync`/`WriteAllBytesAsync` 无 `WriteThrough`；in-place 回退路径却有 `FileOptions.WriteThrough + Flush(flushToDisk:true)` → 断电「改名已生效、内容未落盘」 | ⬜ 待修 |
| **DEF-140** | 托盘显示路径裸 `await` 批量动画 idle 任务，与隐藏路径不对称 | P2 | `Services/WidgetManager.cs:1665` vs `:1710-1742` | show 分支无 try/catch/finally，task fault 即跳过 `ScheduleCapsuleArrangementConsistencyCheck`（胶囊栏失去自愈）；hide 分支注释明写该 task 会 fault | ⬜ 待修 |
| **DEF-141** | `InvalidateBackdropSampleAfterMove` 是前沿节流而非 burst 合并，且多条重排路径绕过 | P2 | `Services/WidgetLayerService.cs:48-59`、`:979-1029`、`:1324-1369` | 250ms 内后续移动直接丢弃且无 trailing 补发 → 最后位置可能永不失效采样；标题激活重排与 z-order 兜底绕过它（本轮新增修复机制本身不成立） | ⬜ 待修 |
| **DEF-142** | 每个窗格 settle 触发全量可见窗口同步 `SWP_FRAMECHANGED` 扫描（O(N²)） | P2 | `Views/WidgetWindowBase.Collapse.cs:4198` | 12 窗 = 132 次同步 SetWindowPos，收拢→展开两拍翻倍，全在 UI 线程 | ⬜ 待修 |
| **DEF-143** | `DispatcherQueue.TryEnqueue(async () => …)` 是 async void 派发且无 try/catch | P2 | `Views/ContentWidgetWindow.NativeDragDrop.cs:1496-1530` | 异常后 `ScheduleNativeFileDropFallback` 不执行（拖放静默失效）+ 临时副本残留；`grep "async void"` 查不到 | ⬜ 待修 |
| **DEF-144** | 本地备份恢复缺互斥与身份校验（云端已修、本地未跟进） | P2 | `SettingsWindow.Maintenance.cs:475-483`、`DeskBoxDataBackupService.cs:545,1626` | `ConfirmPendingFullRestoreAsync()` 无 expected-identity 参数，`PrepareRestoreAsync` 开头无条件删共享 marker → 「确认的备份」可能≠「实际恢复的备份」；`CloudBackup.cs:296-300` 注释已写明 "Confirmed must equal applied" | ⬜ 待修 |
| **DEF-145** | Todo「今天/本周/本月」智能列表跨日不重算 | P2 | `TodoWidgetViewModel.FilteringAndAppearance.cs:488-519` | 常驻时长期显示昨天分组 | ⬜ 待修 |
| **DEF-146** | 「恢复默认设置」先改内存、丢弃 `SaveAsync` 返回值、失败不回滚 | P2 | `SettingsViewModel.PreferenceCommands.cs:62-101`、`Maintenance.cs:560-587` | 内存与磁盘分叉；正确范式在同轮 `DesktopOrganizationSettingsSection.xaml.cs:182-191` | ⬜ 待修 |
| **DEF-147** | Widget 组成员上移/下移/移除三个 `async void` 可重入、无兜底 | P2 | `SettingsWindow.Navigation.cs:984-1006,1008-1030,1032-1051` | 快速连点并发改组成员顺序；失败静默 | ⬜ 待修 |
| **DEF-148** | 9 处对话框入口裸调 `ShowAsync`，`ShowInfoDialogAsync` 缺 `XamlRoot` null 检查 | P2 | `Maintenance.cs:65,485,560`、`DataTools.cs:154,336-345,418-434`、`StorageAndUpdates.cs:387`、`Navigation.cs:1053`、`CloudBackup.cs:246` | 并发 `ShowAsync` 抛 `InvalidOperationException` 逃出 async void → 错误提示自身失败并掩盖真实失败；同窗口同语义 4 种实现 | ⬜ 待修 |
| **DEF-149** | 作业对象配置失败后留下粘性哨兵，孤儿保护静默失效 | P2 | `Platform/ChildProcessJob.cs:57-67` | `SetInformationJobObject` 失败 `return false` 但不关句柄、不复位 `s_jobHandle` → 后续调用跳过配置把子进程挂进无 `KILL_ON_JOB_CLOSE` 的空 job 并返回 true；`catch → false` 无日志 | ⬜ 待修 |
| **DEF-150** | 门禁 `DoesNotContain` 硬失败契约未接线 | P2 | `scripts/quality/static_gate.py:262-265`（承诺）vs `:381-386`（只 print） | 一整类契约断言永久失效；基线沉积约 700 条 `DoesNotContain violation :: tests/...` 死数据 | ⬜ 待修 |
| **DEF-151** | 门禁当前 FAIL，且基线在红灯态被刷写、计数口径跨轮不可比 | P2 | `scripts/quality/static-baseline.json`、`static_gate.py:397-411` | 实测 `async void 258 > 257`、`empty catch 117 > 116`；基线残留 `"failures": ["async void 257 > 基线 254"]`；`empty_catch 245→116`、`sync_wait 147→193` 方向矛盾；计数域依赖外部 rg（CI 不装） | ✅ 已修（R17 整改批） |
| **DEF-152** | 门禁无 rg 回退分支的 `tests/` 排除永不生效 | P2 | `scripts/quality/static_gate.py:293-296` | `path` 来自 `os.walk(ROOT)` 的绝对路径，`startswith("tests/")` 恒 False → 契约重放把测试自身计入，`Contains` 断言可被测试源码满足而掩盖真实契约破坏 | ⏭ 撤出本轮（Round-18 与 DEF-150 合并立项） |
| **DEF-153** | ARM64 ABI 契约测试默认空过 | P2 | `tests/DeskBox.Tests/Arm64NativeRuntimeGateTests.cs:15-21` | 未设 `DESKBOX_REQUIRE_ARM64_RUNTIME_GATE=1` 时 `return` → xUnit 记为 passed（零断言）；ARM64 唯一自动化证据在 x64 CI 不执行，`passed == total` 无法识别 | ⬜ 待修 |
| **DEF-154** | 更新器安装包 SHA256 二次校验在缺 `--installer-sha256` 时完全跳过 | P2 | `src/DeskBox.Updater/Program.cs:91-97` | `if (!IsNullOrWhiteSpace(...) && !Verify(...))` → 缺参数即 fail-open | ⬜ 待修 |
| **DEF-155** | Quick Access 查询在属性读取失败时仍上报「操作成功 + 状态未知」 | P2 | `native/deskbox-native/src/quick_access.rs:163-182`、`Helpers/QuickAccessNativeBackend.cs` | 部分失败折叠成成功语义，调用方无法区分 | ⬜ 待修 |
| **DEF-156** | 快捷方式读取 260/512 定长缓冲无 `BUFFER_TOO_SMALL` 重试，长路径整体失败 | P2 | `Helpers/ShortcutNativeBackend.cs:541-548,612-619`、`native/deskbox-native/src/shortcut.rs` | 缓冲不足不扩容重试 → 长路径快捷方式判无效 | ⬜ 待修 |

| **DEF-189** | 截图标注栅格化器把 12 个 `[DllImport]` 写在 `Controls/` 域，越过 Platform 边界并使 `ModuleBoundaryContractTests.PlatformInterop_StaysInsideThePlatformDomain` 常红 | P2 | `Controls/ScreenshotAnnotationRasterizer.cs:1337-1400` | 12 个 GDI P/Invoke（`CreateCompatibleDC`/`DeleteDC`/`CreateDIBSection`/`SelectObject`/`DeleteObject`/`GdiFlush`/`SetBkMode`/`SetBkColor`/`SetTextColor`/`CreateFontW`/`TextOutW`/`GetTextExtentPoint32W`）直接声明在 Controls；`tests/DeskBox.Tests/ModuleBoundaryContractTests.cs:463-484` 的 `PlatformInteropExpectedViolations` 未登记该文件 → 测试断言 `New P/Invoke belongs in Platform` 失败。根修应迁入 `Platform/Win32Helper.Text.cs`；浅修是登记清单（会固化越界） | ⬜ 待修（Round-18 决策根修 vs 浅修） |
### 新增缺陷 — P3

| 编号 | 标题 | 位置 | 状态 |
|---|---|---|---|
| **DEF-134** | 退出请求前置段（`MarkCleanExit()`/150s 退避武装）在 `try` 之外；`StartupSessionLedger.TryWrite` 全吞异常 → 当前仅剩 OOM 触发面（`try` 边界缺口，修法两行） | `App.xaml.cs:6726,6736,6738` | ✅ 已修（R17 整改批） |
| **DEF-157** | `SaveCoreAsync` 写临时文件前移走 primary（仅上次读失败触发；旧字节保留 `.unreadable-<ts>` ≤3 份） | `ResilientJsonStore.cs:365-399` | ⬜ 待修 |
| **DEF-158** | 保留设备名消毒对带扩展名无效（`CON.txt` → `CON.txt_`） | `FileService.cs:2020-2027` | ⬜ 待修 |
| **DEF-159** | 大小写重命名 journal 裸换行分隔，路径含换行时 journal 被删、文件滞留 | `FileService.CaseOnlyRename.cs:187-196,253-259` | ⬜ 待修 |
| **DEF-160** | `restore-rollback/` 在「恢复成功、进程死在清理前」后永不回收 | `DeskBoxDataBackupService.cs:2500-2511` | ⬜ 待修 |
| **DEF-161** | `HasUserDocuments` 只认 4 类文件，可能 park 在用数据 | `DeskBoxDataBackupService.cs:2602-2626` | ⬜ 待修 |
| **DEF-162** | `MusicSettingsStore` 静态 `Task.WhenAll` 聚合无界保留历史写入 | `MusicSettingsStore.cs:162-168` | ⬜ 待修 |
| **DEF-163** | 10 分钟总下载超时被归入网络错误，分类依赖异常确切子类型 | `AppUpdateService.cs:399-401,454-478` | ⬜ 待修 |
| **DEF-164** | 取色复制剪贴板写入无 try/catch（违反 DEF-097 范式） | `ScreenshotCaptureWindow.xaml.cs:1455-1456` | ⬜ 待修 |
| **DEF-165** | 截图覆盖窗口 `SetWindowPos` 返回值未检查，配合单例闸可致热键永久失效 | `ScreenshotCaptureWindow.xaml.cs` | ⬜ 待修 |
| **DEF-166** | `LoadFrozenImageAsync` 无 `_isClosed` 守卫，窗口销毁后仍写 XAML | `ScreenshotCaptureWindow.xaml.cs` | ⬜ 待修 |
| **DEF-167** | `BackdropSampleInvalidationRequested` 静态字段持实例方法委托，全仓无清除点 | `WidgetLayerService.cs` | ⬜ 待修 |
| **DEF-168** | `Windows10DwmFlushLoop` 在裸后台线程无 try/catch | `WidgetWindowBase.*.cs` | ⬜ 待修 |
| **DEF-169** | `ActiveDeskBoxDragRegistry.Begin` 的 `Path.GetFullPath` 无 try/catch | `ActiveDeskBoxDragRegistry.cs` | ⬜ 待修 |
| **DEF-170** | `ShowNativeRightButtonDropChoiceAsync` 超时/取消路径不隐藏 Flyout | `ContentWidgetWindow.NativeDragDrop.cs` | ⬜ 待修 |
| **DEF-171** | `ScreenshotCaptureWindow_Closed` 不释放冻结帧缓冲 | `ScreenshotCaptureWindow.xaml.cs` | ⬜ 待修 |
| **DEF-172** | 备份快照列表把 `FailedBody` 当摘要（键语义错配）+ finally 无条件解禁按钮 | `Maintenance.cs:460-472` | ⬜ 待修 |
| **DEF-173** | `BackupRuntime.StopCoreAsync` 把清理失败伪造成「计划内备份失败」 | `BackupRuntime.cs:164-170` | ⬜ 待修 |
| **DEF-174** | 搜索结果右键菜单在 UI 线程同步做 `File.Exists`/PE 头探测 | `SearchPopupWindow.xaml.cs:3813-3815,3823` | ⬜ 待修 |
| **DEF-175** | `PinInformationalSeverityBrushes` 匿名订阅 `ActualThemeChanged` 永不退订（有界） | `Navigation.cs:1399` | ⬜ 待修 |
| **DEF-176** | 发布版残留 TEMP DimDiag 探针（每次选择变化格式化签名串 + 面板扫描） | `FileSurfaceContent.ItemVisuals.cs:2812-2944,2947-2954` | ✅ 已修（Round 02：探针整段删除，`src/` 下零残留） |
| **DEF-177** | 死入口 `TryPrepare`（零调用点）与在用 `TryPrepareDeferred` 语义冲突 | `FileItemDragPackage.cs:170,278-281` | ⬜ 待修 |
| **DEF-178** | retained 选择模式用 `_excludedPaths` 算「全选」指示，与实际选择不符 | `DesktopOrganizationPreviewCard.xaml.cs:404` | ⬜ 待修 |
| **DEF-179** | Recycle Bin 枚举失败码被无条件覆盖为 S_OK | `native/deskbox-native/src/recycle_bin.rs:140` | ⬜ 待修 |
| **DEF-180** | `native/README.md`「空可选字段显式应用」与实现不一致 | `native/README.md:77` ↔ `shortcut.rs` | ⬜ 待修 |
| **DEF-181** | 启动失败通知的阶段名几乎恒为同一值（`startupPhase` 只赋 2 次） | `App.xaml.cs` 启动致命路径 | ⬜ 待修 |
| **DEF-182** | JumpList 待处理参数目录无上限、无孤儿回收 | `App.xaml.cs` / JumpList 相关 | ⬜ 待修 |
| **DEF-183** | 音频会话匹配文本丢弃代理对字符（`normalize_match_text`） | `native/deskbox-native/src/music_volume.rs` | ⬜ 待修 |
| **DEF-184** | 更新器超时后强杀安装器进程树，可能留下半装状态 | `src/DeskBox.Updater/Program.cs` | ⬜ 待修 |
| **DEF-185** | 门禁 `rg()` docstring 与返回值不符、无 rg 回退两套扩展集不同 | `scripts/quality/static_gate.py:40-44,98-109` | ⬜ 待修 |
| **DEF-186** | 截图窗口 `Ctrl+Z`/`Ctrl+C` 加速键不判 `IsTextInputFocused` → 文本编辑中撤销标注/复制图像 | `ScreenshotCaptureWindow.xaml.cs:234-261` vs `:231` | ⬜ 待修 |
| **DEF-187** | `PinSelectionAsync` 失败后钉窗仍留在 `App.ScreenshotPins`（仅进程退出时清理） | `App.Screenshot.cs:22-58` | ⬜ 待修 |
| **DEF-188** | `CreateCompatibleDC` 返回值未检查 → GDI 句柄耗尽时返回全黑帧而非 `null` | `ScreenCaptureService.cs:99,324` | ⬜ 待修 |

### 已知模式新位点（并入既有编号，不新立案）

- **async void 静默失败家族**（全局 `OnUnhandledException` 无条件 `e.Handled = true` 放大）：本轮新增位点 6 处 —— `SettingsWindow.Maintenance.cs:399`（`OpenBackupFolderButton_Click`，本轮新增且未走 `RunGuardedAsync`）、`ContentWidgetWindow.NativeDragDrop.cs:1496`（lambda 内 async void）、`SettingsWindow.Navigation.cs:984/1008/1032`、`Maintenance.cs:65/485/560` 裸 `ShowAsync` 链。
- **错误静默吞家族**：`Maintenance.cs:460-472`、`BackupRuntime.cs:164-170`、`SettingsService.cs:1088`（`catch (IOException) { }`，本轮新增）、`ContentWidgetWindow.NativeDragDrop.cs:1661`（`catch { }`，本轮新增）、`FileSurfaceContent.ItemVisuals.cs:3005`（诊断探针空 catch，本轮新增）。
- **门禁棘轮失真家族**：DEF-150/151/152/185 四条同根，建议 Round-18 合并为「质量门禁有效性」议题一次处理。

### 存量条目状态变更（round-17 复核）

- **本轮不适用**：协议禁止 SubAgent 读取 `docs/quality/**`，所有结论独立得出，无法与历史 DEF 编号逐条比对。历史条目维持原状态；Round-18 由主流程做一次「历史条目 × round-17 新结论」交叉映射（重点：DEF-078/080/092/097/116/123 家族是否被本轮新位点覆盖）。
- **正面结论（独立复核）**：Rust 生产模块（ABI 2 / capability 511 / 十导出）unsafe、句柄所有权、COM 配对、宽字符串、错误码逐条核对**未发现 UB**；`panic = "abort"` 使 panic 跨 C ABI 物理不成立；头文件 / `lib.rs` / C# 三处 ABI 完全一致；13 语言 3220 键与占位符一致；零售离线保证成立。**P0 = 0。**

---

### round-17 整改批（2026-10-27，随轮完成）

- **方案与复核**：`docs/quality/rectify/R17-remediation-plan.md`（v2，含独立复核处置表）＋ 独立复核 `docs/quality/rectify/R17-remediation-plan-review.md`（首轮 NO-GO，5 项阻塞/建议全部处置后 GO）。执行报告：`docs/quality/rectify/R17-remediation-report.md`。
- **本轮修复（4 条）**：
  - **DEF-133（P1）**：`Controls/ScreenshotAnnotationRasterizer.cs:1245-1252` 覆盖度只取绿通道（`coverage[i] = buffer[offset + 1]`）＋ `:1116-1127` 注释纠错；新增用例 `Render_TextPaintsGlyphCoverageInsteadOfAFilledBox`（120×60 白底、`"HH"`、scale 1.0，断言紧包围盒内仍有背景像素且暗像素 < 50%）。**红/绿双向证明**：把实现改回 `max(green, blue)` 时该用例失败（`the text box must keep background pixels between the glyph strokes`），修复版通过。
  - **DEF-134（P3）**：`App.xaml.cs` `MarkCleanExit()` 与 `ArmShutdownExitBackstop(150s)` 移入 `try`，异常不再逃逸出 `async void ExitApplication`。
  - **DEF-135（P2）**：删除 `ShutdownCoreAsync` 末尾 `ShutdownStep.Sync("single-instance", …)` 步骤（互斥体唯一释放点回到 `ShutdownApplicationAsync` 的 `finally`，位于托盘/钉窗/日志落盘之后）；同步 `:6939-6940` 注释与 `docs/architecture/architecture-optimization-progress-20260922.md:527/:1866` 旧设计描述。
  - **DEF-151（P2）**：`SettingsWindow.Maintenance.cs:399` `async void` → `void` + `_ = RunGuardedAsync("open-backup-folder", …)`（`async void` 计数 258→257）；三处新增空 catch 补说明注释（`SettingsService.cs:1088`、`ContentWidgetWindow.NativeDragDrop.cs:1661`、`FileSurfaceContent.ItemVisuals.cs:3005`，空 catch 117→113）；`static_gate.py` 加「FAIL 时拒绝写基线 + 基线不存 `failures` 运行态」门闩；删除 `static-baseline.json` 尾部 `failures` 死键。
- **本轮撤出（2 条）**：DEF-152（修好 `tests/` 排除后 `contract_misses` 15→110，95 条需三分类，且与「拒绝写基线」门闩互锁）；DEF-150（`DoesNotContain` 接线会让切片作用域假阳性立刻 FAIL，需结构性改造）。二者与 DEF-185 合并为 Round-18「质量门禁有效性」议题。
- **本轮新立案（1 条）**：DEF-189（P2，截图栅格化器 P/Invoke 越域，见上表）。
- **门禁四件套**：构建 `dotnet build .\src\DeskBox\DeskBox.csproj -c Debug --no-restore -p:Platform=x64` → **0 错误 / 20 警告**（存量），产物 `src\DeskBox\bin\x64\Debug\net10.0-windows10.0.22621.0\DeskBox.dll` 14,234,624 B / 04:54:27；定向回归 `ScreenshotAnnotationRasterizerTests` **9/9**；x64 全量回归 **6202 通过 / 11 失败 / 1 跳过 / 6214**（11 条全部为环境或存量：5× `DesktopOrganizationTests` 直写真实 `%LOCALAPPDATA%\DeskBox` 被拒、1× 注册表 HKCU 被拒、1× WinRT 无打包上下文、3× 缩略图通道不可用、1× DEF-189）；`static_gate.py` **PASS**（`async void 257 = 基线 257`、`empty catch 113 < 116`、`sync_wait 193`、`reflection 3`、契约重放 5837/失联 15/新增 0）。留档：`.tmp-test/static-gate-r17-after.json`。
- **未执行**：新实例启动验证（外部实例 `E:\DeskBox-dev\app5\DeskBox.exe` 持单实例锁，本轮未杀进程）。
## round-18 十路全量代码缺陷深度审查（2026-10-27，10 路并行 + Lead 独立复核）

> 纯静态审查：未运行应用、未执行测试、未修改任何源码（本节为审查结果录入）。
> 10 份分段报告 + 总报告 + Lead 复核记录见 `docs/quality/rounds/round-18/`（`S1-…`…`S10-…`、`ROUND-18-总报告.md`、`LEAD-VERIFICATION.md`）。
> 本轮协议明令禁止 SubAgent 读取 `docs/quality/**` 历史结论，全部条目从代码独立得出；与历史 `DEF-xxx` 的交叉比对在下一轮做。
> 规模：10 段 100% 模式扫描，深读约 95,000 行。**P0=0，P1=3（Lead 复核后 1），P2=33（复核后 35），P3=53，合计 89 条**；观察项 100+。
> 编号规则：`R18-S{n}-nn`（分段 + 序号），不与历史 `DEF-xxx` 混用。
> Lead 复核改动：**R18-S2-01 P1→P2**（两个调用方都有 `catch(Exception)` 兜底 + 下次 `Stop()` 作废迟到线程）、**R18-S6-01 P1→P2**（`App.xaml.cs:6991` 无条件 `Handled=true`，不崩只静默）、**R18-S2-07 P2→P3**（`GetInvalidFileNameChars` 含 `\` `/`，无法多级回溯）、**R18-S8-01 P2→P3**（分段自查：写盘走无 `_isDisposed` 守卫的纯转发）。唯一 P1 = **R18-S1-001**（已用线上 `stable.json` 实证缺 `arm64Sha256`）。

### 新增缺陷（89 条）

#### S1

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S1-001** | ARM64 上「检查更新」恒定失败：`arm64Sha256` 缺失时用空串覆盖 `sha256` | P1 | ✅ 已修（R18-B，加诊断日志；复核后 P2） |
| **R18-S1-002** | `_lastDownloadedSha256` 取自调用方原始 manifest，与下载核心实际校验的架构哈希不同源 | P2 | ✅ 已修（R18-B） |
| **R18-S1-003** | `HasUserDocuments` 漏判 `quick-capture.json` / `desktop-organization-history.json`，可能永久删除只含快记数据的目录 | P2 | ✅ 已修（R18-B） |
| **R18-S1-004** | `ArchiveZipService.WriteZip` 缺少「排除自身归档」，多选父目录+子项时压缩必然失败（TAR 分支有该保护） | P3 | ⬜ 待修 |
| **R18-S1-005** | `ExecuteTransferPlanAsync` 的 `allowShellElevation` 分支静默丢弃 `onItemError` / `skippedItems`，违反同方法内写明的契约 | P3 | ⬜ 待修 |
| **R18-S1-006** | `FolderWatcherService` 两个 debounce 定时器回调直接 `Invoke` 事件，无异常隔离 | P3 | ⬜ 待修 |
| **R18-S1-007** | `DesktopAutoOrganizationWatcher.BeginEnabledCycle` 每次启用泄漏一个 linked `CancellationTokenSource` | P3 | ⬜ 待修 |

#### S2

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S2-01** | `ReservedHotkeyHookService.TryStartAsync` 超时分支是死代码，超时后抛异常且跳过 `Stop()`，全局钩子留在"装了但不路由"状态 | P1 | ⬜ 待修 |
| **R18-S2-02** | `HookHealthWatchdog` 的恢复退避最多只能升一级，稳态在 90s/180s 之间振荡，8 分钟上限不可达 | P2 | ⬜ 待修 |
| **R18-S2-03** | `MusicSessionService` 在同一 app 存在 ≥2 个 SMTC 会话时把 SessionId 塌缩成 ordinal 0，多会话选择器指向错误会话 | P2 | ⬜ 待修 |
| **R18-S2-04** | `SearchEngineService` 在后台线程直接枚举 UI 线程可变 `List<WidgetConfig>`，并发增删 widget 时抛 `InvalidOperationException` | P2 | ⬜ 待修 |
| **R18-S2-05** | `MusicVolumeService` 的 `ISimpleAudioVolume` / `IAudioSessionControl2` RCW 从不释放 | P3 | ⬜ 待修 |
| **R18-S2-06** | `TodoWidgetStore.DeleteForWidgetAsync` 绕过路径级信号量，删除后仍可能被并发写者重建 | P3 | ⬜ 待修 |
| **R18-S2-07** | `TodoWidgetStore.SanitizeWidgetId` 放行 `"."` 与 `".."`，`Path.Combine` 可逃出 widgets 目录 | P3 | ⬜ 待修 |
| **R18-S2-08** | `SearchEngineService.Dispose()` 先 Dispose 再被事件源读取 `.Token`，`ObjectDisposedException` 冒泡进保存路径 | P3 | ⬜ 待修 |
| **R18-S2-09** | `HotkeyRecorderHookService.Stop()` 在钩子线程尚未登记 threadId 时永久泄漏消息泵线程，录制器此后无法启动 | P3 | ⬜ 待修 |
| **R18-S2-10** | `NativeAppNotificationService.Register` 无并发保护，双重订阅导致一次点击触发两次激活 | P3 | ⬜ 待修 |
| **R18-S2-11** | `SearchSettingsCoordinator.StopCoreAsync` 在 `finally` 里 `_enableGate.Dispose()`，与 `SetEnabledAsync` 的 `Release()` 竞争 | P3 | ⬜ 待修 |
| **R18-S2-12** | `MusicSessionService.AttachCurrentSession` 无同步，三个事件可被重复订阅 | P3 | ⬜ 待修 |

#### S3

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S3-01** | z-order 延迟调度体是 async void，且忽略 `TryEnqueue` 返回值 | P2 | ⬜ 待修 |
| **R18-S3-02** | `_widgetsRaisedFromTray` 被直接赋值，绕过 `SetWidgetsRaisedFromTray` 的状态通知 | P2 | ⬜ 待修 |
| **R18-S3-03** | 组变更回滚快照漏捕屏幕绑定三字段，回滚后成员屏幕绑定停留在组的值 | P2 | ⬜ 待修 |
| **R18-S3-04** | `InvalidateBackdropSampleAfterMove` 是领先沿合并，注释描述的却是尾沿合并 | P2 | ⬜ 待修 |
| **R18-S3-05** | `RaiseVisibleWidgetsTemporarily` / `ActivateAllVisibleWidgetsFromTitle` 的抬升循环无保护，租约与回落定时器在循环之后 | P2 | ⬜ 待修 |
| **R18-S3-06** | `ApplyTrayAnimationGroupOffset` 的逐窗偏移设置无 try/catch，失败会留下屏幕外的残留偏移 | P3 | ⬜ 待修 |
| **R18-S3-07** | `WidgetGroupTransientState.IsEmpty` 恒为 false，空状态清理分支不可达 | P3 | ✅ 已修（R18-B） |
| **R18-S3-08** | `SwitchContentWidgetGroupMemberInPlaceAsync` 在 `finally` 里 `await loadingDelay`，非 OCE 异常会顶替原始异常 | P3 | ⬜ 待修 |
| **R18-S3-09** | `RegisterBoundsTransition` 的容量闸门是死代码 | P3 | ⬜ 待修 |
| **R18-S3-10** | `TryCommitBatch` 失败时放弃 `HDWP` 事务块，逐帧泄漏 | P3 | ⬜ 待修 |
| **R18-S3-11** | `CloseAll` 未清 `WidgetLayerService.BackdropSampleInvalidationRequested` 静态回调 | P3 | ⬜ 待修 |
| **R18-S3-12** | 组拖拽驻留计时器从未 Dispose，`CloseAll` 也不停 | P3 | ✅ 已修（R18-B） |
| **R18-S3-13** | `RefreshVisibleWidgetDesktopLayers` 在 `_isTogglingWidgetsDesktopLayer` 期间用无上限、无取消的 250ms 自续轮询 | P3 | ⬜ 待修 |
| **R18-S3-14** | `ApplyCapsuleArrangementIfChanged` 在重入或拖拽会话期间静默丢弃请求，一致性自愈的票据被消耗 | P3 | ⬜ 待修 |
| **R18-S3-15** | 组解散路径不清 `_widgetGroupMemberInactiveSince` / `_widgetGroupTransientStates` | P3 | ⬜ 待修 |

#### S4

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S4-01** | 恢复默认设置先改内存后落盘，落盘失败不回滚且丢弃返回值 | P2 | ⬜ 待修 |
| **R18-S4-02** | 重置组件外框覆盖后没有任何宿主刷新，按钮点了界面不变 | P2 | ⬜ 待修 |
| **R18-S4-03** | 材质持久化不按当前 OS 支持集校验，Win10 上可落到不存在的选项 | P2 | ⬜ 待修 |
| **R18-S4-04** | 备份运行时用"丢弃"处理刷新重入，并发保存会静默丢失配置变更 | P2 | ⬜ 待修 |
| **R18-S4-05** | 抑制标志一律"无条件置 false"，21 处不可重入（同仓仅 3 处写法正确） | P3 | ⬜ 待修 |
| **R18-S4-06** | `ResetWidgetChromeOverrides` 的 `mode` 形参完全未被使用 | P3 | ✅ 已修（R18-B） |
| **R18-S4-07** | `FormatBytes` 误用 `string.Format`：culture 实参无效，且本地化串含花括号即抛异常 | P3 | ⬜ 待修 |
| **R18-S4-08** | `Dispose` 只退订了 10 个事件，19 个订阅残留（含外观提交链路） | P3 | ⬜ 待修 |
| **R18-S4-09** | Todo 的"今天/逾期"派生状态没有跨日触发器 | P2 | ⬜ 待修 |

#### S5

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S5-01** | 搜索弹窗重命名对话框的 `ShowAsync` 在异常边界之外，可致进程崩溃 | P2 | ⬜ 待修 |
| **R18-S5-02** | 引导流程的错误对话框在 `catch` 块内再次 `await ShowAsync()`，异常二次逃逸 | P2 | ⬜ 待修 |
| **R18-S5-03** | `UpdateFooterState` 无条件重新启用「下一步」，可与完成流程并发导致双次 `Close()` | P2 | ⬜ 待修 |
| **R18-S5-04** | `RootGrid_Drop` 是 `async void` 且 `try` 只有 `finally` 没有 `catch` | P2 | ⬜ 待修 |
| **R18-S5-05** | 右键拖放取消分支在 UI 线程同步递归删除临时目录 | P2 | ⬜ 待修 |
| **R18-S5-06** | 规则转移成功提示被内层刷新覆盖，用户永远看不到反馈 | P3 | ⬜ 待修 |
| **R18-S5-07** | `_isRefreshing` 用裸赋值而非 `try/finally`，一次异常永久锁死设置分区 | P3 | ⬜ 待修 |
| **R18-S5-08** | `AutoOrganizationToggle_Toggled` 无重入保护，快速连点可交错基线与保存结果 | P3 | ⬜ 待修 |
| **R18-S5-09** | 标题重命名编辑器的 `e.Handled` 在 `await` 之后才置位 | P3 | ⬜ 待修 |
| **R18-S5-10** | 「上移/下移堆栈」在历史 key 存在时静默无效，而菜单仍显示可用 | P3 | ⬜ 待修 |

#### S6

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S6-01** |  | P1 | ⬜ 待修 |
| **R18-S6-02** |  | P2 | ⬜ 待修 |
| **R18-S6-03** |  | P2 | ⬜ 待修 |
| **R18-S6-04** |  | P2 | ⬜ 待修 |
| **R18-S6-05** |  | P2 | ⬜ 待修 |
| **R18-S6-06** |  | P2 | ⬜ 待修 |
| **R18-S6-07** |  | P3 | ⬜ 待修 |
| **R18-S6-08** |  | P3 | ⬜ 待修 |
| **R18-S6-09** |  | P3 | ⬜ 待修 |
| **R18-S6-10** |  | P3 | ⬜ 待修 |
| **R18-S6-11** |  | P3 | ⬜ 待修 |
| **R18-S6-12** |  | P3 | ⬜ 待修 |

#### S7

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S7-01** | 模式切换后两套布局同时常驻可视树 | P3 | ⬜ 待修 |
| **R18-S7-02** | 名称展开浮层每次调用新建两支 `SolidColorBrush`，违反共享样式缓存收敛决定 | P3 | ⬜ 待修 |
| **R18-S7-03** | 列表模式重命名编辑框的 `Math.Clamp` 上下界可能倒置并抛 `ArgumentException` | P3 | ⬜ 待修 |
| **R18-S7-04** | 多选命令栏永久折叠，5 个 `async void` 处理器与忙碌态写入成为不可达代码 | P3 | ⬜ 待修 |

#### S8

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S8-01** | 最后一次写盘发生在不被等待的 `async void` 生命周期钩子里，进程退出即可能丢失 | P3 | ⬜ 待修 |
| **R18-S8-02** | `TodoWidgetContent` 两个只写字段：`_selectionPreviewItems` 与 `_copyTapGeneration` | P3 | ⬜ 待修 |
| **R18-S8-03** | `WidgetGroupTitleSwitcher` 的悬停切换 `CancellationTokenSource` 从不 `Dispose` | P3 | ✅ 已修（R18-B） |
| **R18-S8-04** | 附件拖放失败时把原始异常消息直接呈现给用户 | P3 | ⬜ 待修 |
| **R18-S8-05** | 笔记「取消」不取消在飞的自动保存，被丢弃的草稿仍会落盘 | P3 | ⬜ 待修 |
| **R18-S8-06** | `AppUpdateManifest` 的集合属性对显式 JSON `null` 无防护，`HasReleaseNotes` 会 NRE | P3 | ✅ 已修（R18-B） |

#### S9

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S9-01** | 系统音量读取失败仍上报成功，音乐控件把 0% 当成真实读数 | P2 | ⬜ 待修 |
| **R18-S9-02** | 关闭序列的 `abortFollowingStepsOnTimeout` 跳过其后**全部**收尾步骤，未落盘设置与日志尾部丢失 | P2 | ⬜ 待修 |
| **R18-S9-03** | 回收站恢复与快速访问固定：`InvokeVerb` 投递成功即上报「已恢复/已固定」 | P2 | ⬜ 待修 |
| **R18-S9-04** | 回收站精确恢复在长路径父目录下不可用，QUERY 还会静默退化为「0 匹配」 | P2 | ⬜ 待修 |
| **R18-S9-05** | 快捷方式读取：任一字段的访问器失败都会让整次读取变成 `OPERATION_FAILED` | P2 | ⬜ 待修 |
| **R18-S9-06** | 用 `SHELL_NAMESPACE_TARGET` 写出的 IDList 型快捷方式无法被读回 | P2 | ⬜ 待修 |
| **R18-S9-07** | 回收站扫描跳过坏项后仍把 `enumerate_hresult` 覆写为 `S_OK` | P3 | ⬜ 待修 |
| **R18-S9-08** | `deskbox_recycle_bin_v1`（能力位 `1<<8`）在生产代码中没有任何调用者，与文档/能力声明不符 | P3 | ⬜ 待修 |

#### S10

| 编号 | 标题 | 级别 | 状态 |
|---|---|---|---|
| **R18-S10-01** | 缩略图缓存按 decode 宽度互相驱逐（P2 / 高） | P2 | ✅ 已修（R18-B） |
| **R18-S10-02** | `DEVMODE` 结构截断，刷新率恒回落默认 60 Hz（P2 / 中） | P2 | ⬜ 待修 |
| **R18-S10-03** | `SHGetFileInfo` 兜底路径丢弃 `rejectPaddedShortcutIcon`（P3 / 确定） | P3 | ⬜ 待修 |
| **R18-S10-04** | 本地启动观察标记在失败分支不完成，15 秒后必发误导日志（P3 / 确定） | P3 | ⬜ 待修 |
| **R18-S10-05** | Move-only 源在成功导入后收到 `DROPEFFECT_NONE` 回执（P3 / 中） | P3 | ⬜ 待修 |
| **R18-S10-06** | 延迟拖放完成回调拿不到 dataObject（与同步路径契约不对称）+ 死变量（P3 / 确定） | P3 | ⚠️ 部分（R18-B：死变量显式化，行为未变） |


### round-18 整改批（R18-B 已完成 2026-10-27，方案见 `docs/quality/rectify/R18-remediation-plan.md` §实施记录）

| 批次 | 内容 | 条目 | 状态 |
|---|---|---|---|
| B1 | 更新链路：站点清单检查项 + 缺失字段诊断日志 + `_lastDownloadedSha256` 同源 | R18-S1-001（代码侧）、R18-S1-002 | ✅ 已修（B1.1 仅诊断日志，行为不变） |
| B2 | 数据保全：`HasUserDocuments` 改为按 `ShouldIncludeInBackup` 枚举全部文件（根因修复） | R18-S1-003 | ✅ 已修 |
| B3 | 反序列化健壮性：`AppUpdateManifest` 集合属性 null 归一 + 2 条单测 | R18-S8-06 | ✅ 已修 |
| B4 | 资源泄漏与卸载丢保存：Glance `OnUnloaded` await flush、悬停切换 CTS Dispose、组拖拽计时器停用 | R18-S6-07、S8-03、S3-12 | ✅ 已修 |
| B5 | 死代码与契约漂移清理：未用形参、恒未赋值变量、恒 false 判据 | R18-S4-06、S10-06、S3-07 | ✅ 已修（S10-06 仅显式化） |
| B6 | 待决策（不动代码）：异步停用契约、快照字段集、屏幕坐标原点、`FireAndForget` 统一入口、`WaitAsync` 死分支、`DEVMODE` 截断 | S8-01、S3-03、S6-05、S3-01/05、S2-01、S10-02 | 🧭 待决策 |

### round-18 新增高价值候选（复核后应优先纳入后续批次）

| 编号 | 一句话 | 建议批次 |
|---|---|---|
| R18-S9-02 | 关闭序列 `abortFollowingStepsOnTimeout` 超时即跳过其后全部步骤，连带跳过 `settings-flush`/`log-drain`/`service-container` → 关闭瞬间设置与日志尾部丢失 | B7（P2 高） |
| R18-S9-01 | `music_volume` 读系统音量失败仍返回 `STATUS_OK/S_OK`，控件把「不可读」显示成 0% | B7 |
| R18-S9-03 | `recycle_bin`/`quick_access` 把 `InvokeVerb` 投递成功当作状态已改变，恢复/固定失败仍报成功 | B8（需实机） |
| R18-S9-08 | 零售 AOT 无「精确回收站恢复」生产调用者，与 `native/README.md`/AGENTS.md 声明相悖 | B8（文档或接线二选一） |
| R18-S10-01 | 缩略图缓存 `pathPrefix` 不含 decode 段，同路径不同宽度互相驱逐 → 缓存命中率退化 | ✅ 已修（R18-B6） |
| R18-S10-02 | `DEVMODE` 托管结构 188 vs 原生 220 字节，`dmSize` 据此传入 → 刷新率检测失效 | B9（需实机） |
| R18-S2-04 | `SearchEngineService` 后台线程枚举 UI 线程可变 `List<WidgetConfig>` | B7 |
| R18-S2-08 | `SearchEngineService.Dispose()` 后事件源仍读 `.Token` | B7 |
| R18-S3-03 | 组变更回滚快照漏捕屏幕绑定三字段 | B10（决策后） |

### round-18 修复批 R18-B 记录（2026-10-27）

> 执行者：Lead；依据 `docs/quality/rectify/R18-remediation-plan.md`（v2，已吸收独立复核 7 项阻断 + 6 项建议）。
> 约束：用户要求「仅静态审查、不执行运行测试」，本批未启动 GUI 实例；构建 + 单测属门禁验证，不属于运行测试。

| 编号 | 修复落点 | 关键代码 | 验证 |
|---|---|---|---|
| R18-S1-002 | `src/DeskBox/Services/AppUpdateService.cs` | `_lastDownloadedSha256 = SelectInstallerForArchitecture(manifest, CurrentInstallerArchitectureSuffix)?.Sha256 ?? manifest.Sha256` —— 记账与下载校验同源，补齐 `arm64Sha256` 后不再重下-重删 | 定点 562 通过 |
| R18-S1-003 | `src/DeskBox/Services/DeskBoxDataBackupService.cs` | `HasUserDocuments` 改为枚举目录全部文件 + `ShouldIncludeInBackup(相对路径,'/')`；枚举异常保守返回 `true` | 定点 562 通过 |
| R18-S8-06 | `src/DeskBox/Models/AppUpdateManifest.cs` | `Summary`/`ReleaseNotes` 改支撑字段 + `set => value ?? []` | 新增 `AppUpdateManifestNullToleranceTests`（2 条）通过 |
| R18-S6-07 | `src/DeskBox/Views/SettingsSections/GlanceWidgetSettingsSection.xaml.cs` | `OnUnloaded` → `async void` + `await RunGuardedAsync("on-unloaded", FlushPendingSavesAsync)` | 定点通过 |
| R18-S8-03 | `src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml.cs` | 新增 `CancelAndDisposeHoverSwitch()`，三处调用统一先 Cancel 再 Dispose、字段先置空 | 定点通过 |
| R18-S3-12 | `src/DeskBox/Services/WidgetManager.cs` | `CloseAll` 停用并置空 `_groupDragDwellTimer` | 定点通过 |
| R18-S4-06 | `src/DeskBox/ViewModels/SettingsViewModel.AppearanceOptions.cs` + `tests/DeskBox.Tests/SettingsViewModelChromeModeTests.cs` | 删除未读形参 `string? mode = null`，两处测试调用点同步 | 3 条 ChromeMode 测试通过 |
| R18-S3-07 | `src/DeskBox/Services/WidgetManager.Groups.cs` | 删除恒 `false` 的 `IsEmpty`，判据收敛为 `state is null` | 定点通过 |
| R18-S10-01 | `src/DeskBox/Helpers/IconHelper.cs` | `RemoveStaleThumbnailEntries` 增加 `decodePixelWidth` 并追加 `:decode={width}` 后缀匹配 | 定点通过 |
| R18-S10-06 | `src/DeskBox/Helpers/NativeDropTarget.cs` | 删除恒未赋值局部变量，`ResolveCompletionEffect(..., createdShellApplicationLinks: false)` 显式传常量 | 定点通过 |

**评级/结论修正（复核推翻分段结论，需在下一轮继承）**

| 条目 | 分段结论 | 复核结论 |
|---|---|---|
| R18-S1-001 | P1「ARM64 检查更新恒失败」 | **P2**：站点清单判失败后 `CheckForUpdatesAsync` 会回退 GitHub 发布检查（`AppUpdateService.cs:209-224`），ARM64 用户仍可拿到更新；代码侧只加诊断日志，不改行为 |
| R18-S1-002 | P2 | **P1 潜在路径**：一旦站点补齐 `arm64Sha256`，原始 manifest 记账值必然与下载校验值不一致 → 无限重下-重删 |
| R18-S10-05 | P3「`ResolveCompletionEffect` 永不返回 Move」 | **误报**：`NativeDropEffectPolicy` 注释明确说明返回 MOVE 会让拖拽源删除源文件（check-then-disappear 竞态），COPY 是故意设计 |
| R18-S10-06 | P3「死变量 + 延迟路径拿不到 dataObject」 | **部分成立**：变量确实被 `:705` 读取（恒 `false`），非未读死代码；延迟路径 `DataObject:0` 是 OLE 规范行为，不是缺陷 |

**本轮环境基线（供下一轮对照，勿误判为回归）**

- 全量测试：6204 通过 / 11 失败 / 1 跳过 / 6216。11 条失败均与本批改动无关：8 条沙箱令牌拒绝写 `%LOCALAPPDATA%\DeskBox\data` 与 `HKCU\Software\DeskBox.Tests`；3 条 `QuickCaptureServiceTests` 缩略图断言（`BitmapDecoder` 在受限令牌下返回空）；1 条 `ModuleBoundaryContractTests.PlatformInterop_StaysInsideThePlatformDomain` 指向工作区**未跟踪**文件 `src/DeskBox/Controls/ScreenshotAnnotationRasterizer.cs`（12 处 P/Invoke，`scripts/quality/static-baseline.json` 未更新）→ 已列入 R19 必读项。
- 测试需把 `TEMP`/`TMP` 指向工作区内目录（如 `E:\DeskBox\.artifacts\test-temp`），否则测试宿主无法写用户 Temp，会出现 1600+ 条 `UnauthorizedAccessException` 假失败。

---

## Round 19（2026-10-27，十路专项静态审查）

- 输入：`docs/quality/rounds/round-19/` 十份分报告 + `ROUND-19-总报告.md`
- 立案 **115** 条：P0 0 / P1 5→**1**（复核改判 4 条）/ P2 27→**34** / P3 78→**80**（S3 五条原文未标级别，按证据补标 3×P2 + 2×P3）
- 状态：全部「待修」，本轮未修改任何源码；Lead 已逐条复核 P1 与关键 P2（见 `docs/quality/rounds/round-19/LEAD-VERIFICATION.md`）
- 编号规则：`R19-S{段号}-{序号}`；段号与分报告一致（S1 数据安全与更新链路、S2 Widget 管理与窗口会话、S3 Views 与应用主壳、S4 ViewModels、S5 Controls 根层与内容宿主、S6 WidgetContents、S7 Services 与 Helpers、S8 持久化与序列化、S9 原生层、S10 契约/Helpers/同步）

| ID | 级别 | 位点 | 一句话结论 | 状态 |
|---|---|---|---|---|
| `R19-S1-01` | P1 | src/DeskBox/Services/AppUpdateService.cs:409-411（建立 10 分钟总超时）、:439-450（用 linkedToken 读/写正文）、:464-488（四个 catch ... | (见报告) | 待修 |
| `R19-S1-02` | P2 | src/DeskBox/Services/DeskBoxDataBackupService.cs:2988（SingleOrDefault）、:3017（catch 过滤器）、:2768（调用点）、同根已修位点 :187... | (见报告) | 待修 |
| `R19-S1-03` | P2 | src/DeskBox/Services/FolderWatcherService.cs:954-955（重连 tick 调用 StartAsync）、:245（StartAsync 内部 Stop()）、:623-63... | (见报告) | 待修 |
| `R19-S1-04` | P2 | src/DeskBox/Services/FolderWatcherService.cs:980-1003（QueueChange，尤其 :995-998）、:1002（每条事件都重启防抖计时器）、:1023-1027（... | (见报告) | 待修 |
| `R19-S1-05` | P2 | src/DeskBox/Services/ArchiveZipService.cs:86-94（zip 的 FileMode.CreateNew + ZipArchive(Create)）、:135-140（tar 同样... | (见报告) | 待修 |
| `R19-S1-06` | P2 | src/DeskBox/Services/CloudBackupService.cs:720-747（ApplyRetentionAsync，关键 :739 的 Skip(Math.Max(1, options.Rete... | (见报告) | 待修 |
| `R19-S1-07` | P3 | src/DeskBox/Services/FileTransferSessionRegistry.cs:93-98（GroupBy(SourcePath, OrdinalIgnoreCase).Select(group ... | (见报告) | 待修 |
| `R19-S1-08` | P3 | src/DeskBox/Services/DeskBoxDataBackupService.cs:545（DeletePendingRestoreCore()）、:549（ExtractAndValidateRestor... | (见报告) | 待修 |
| `R19-S1-09` | P3 | src/DeskBox/Services/FolderWatcherService.cs:1054（FolderChanged?.Invoke(batch)）、:1071-1074（FolderIconChanged?.... | (见报告) | 待修 |
| `R19-S1-10` | P3 | src/DeskBox/Services/AppUpdateService.cs:356（DownloadCompleted?.Invoke(result)）、:1039（CheckCompleted?.Invoke(r... | (见报告) | 待修 |
| `R19-S1-11` | P3 | src/DeskBox/Services/PersistenceFileMutations.cs:1（namespace DeskBox.Core.Persistence;）与文件物理路径 src/DeskBox/Ser... | (见报告) | 待修 |
| `R19-S1-12` | P3 | src/DeskBox/Services/DeskBoxDataBackupService.cs:32-37 | (见报告) | 待修 |
| `R19-S2-01` | P1 | src/DeskBox/Services/WidgetManager.cs:2274（配合 :2237-2285 的整个 try/finally） | (见报告) | 待修 |
| `R19-S2-02` | P2 | src/DeskBox/Services/SettingsService.cs:1320-1384（WriteSettingsTempFileAsync 的 lock (_lock) + JsonSerializer.S... | (见报告) | 待修 |
| `R19-S2-03` | P3 | src/DeskBox/Services/WidgetManager.cs:206（声明）、:2758（CloseAll 批量 Add，永不移除）、:3206（CloseFailedCreatedWindow 的 Add... | (见报告) | 待修 |
| `R19-S2-04` | P3 | src/DeskBox/Services/WidgetManager.Groups.cs:3514-3544 TransferCapsuleIdentity | (见报告) | 待修 |
| `R19-S2-05` | P3 | src/DeskBox/Services/WidgetManager.cs:2358-2361 | (见报告) | 待修 |
| `R19-S2-06` | P3 | src/DeskBox/Services/WidgetManager.cs:3276-3289（shouldCaptureAnchor） | (见报告) | 待修 |
| `R19-S2-07` | P3 | src/DeskBox/Services/WidgetManager.Groups.cs:3220-3224 | (见报告) | 待修 |
| `R19-S2-08` | P3 | src/DeskBox/Services/WidgetSessionManager.cs:81-86（MarkDesktopResting）、:99-104（MarkHidden） | (见报告) | 待修 |
| `R19-S2-09` | P3 | src/DeskBox/Services/SearchEngineService.cs:46-54（OnQuickCaptureChanged）、:424-462（EnsureDeskBoxContentSnapshot... | (见报告) | 待修 |
| `R19-S2-10` | P3 | src/DeskBox/Services/SettingsService.cs:1104-1116（SaveAsync 通知语义）、:1415-1473（SaveDebounced） | (见报告) | 待修 |
| `R19-S3-01` | P? |  | 未处理异常在生命线建立后被完全静默吞没，用户无任何可见反馈 | 待修 |
| `R19-S3-02` | P? |  | `ShowInfoDialogAsync` 缺 XamlRoot 守卫、无 catch，且 33 处调用绕过仓库为修崩溃而写的 `ShowDialogWhenFreeAsync` | 待修 |
| `R19-S3-03` | P? |  | 恢复默认设置失败时部分生效、不持久化、且无任何用户反馈 | 待修 |
| `R19-S3-04` | P? |  | `_overlayDragCuePollTimer` 从不解除 Tick 订阅也不置空，违反本文件自己的定时器释放契约 | 待修 |
| `R19-S3-05` | P? |  | async void 事件处理器漏斗：多处转调路径无任何异常出口 | 待修 |
| `R19-S4-01` | P2 |  | Todo 的跨日/逾期派生状态没有任何刷新触发点 | 待修 |
| `R19-S4-02` | P2 | src/DeskBox/ViewModels/TodoWidgetViewModel.cs:773-783（早退）与 :810-829（被跳过的插入块）。 | `ApplyExternalStoreChange` 的早退 `return` 吞掉重复任务的"下一次出现" | 待修 |
| `R19-S4-03` | P2 |  | 通知动作后的 `RefreshAsync` 直接等于整表重载，静默清空详情页/创建草稿 | 待修 |
| `R19-S4-04` | P2 | src/DeskBox/ViewModels/TodoWidgetViewModel.EditingAndUndo.cs:261-297。 | `UndoLastActionAsync` 是整表还原而非逆操作，丢弃快照之后的新增项 | 待修 |
| `R19-S4-05` | P3 | src/DeskBox/ViewModels/SettingsViewModel.SettingsSync.cs:66-69（置位）与 :146-150（复位）。 | 抑制标志 `_isApplyingSettingsSnapshot` 复位不对称（当前被兄弟标志掩盖） | 待修 |
| `R19-S4-06` | P3 | src/DeskBox/ViewModels/TodoItemViewModel.cs:391 — public string MarkerGlyph => HasRedMarker ? "\uE915" : "\uE9... | `MarkerGlyph` 两个分支返回同一个字形 | 待修 |
| `R19-S4-07` | P3 | src/DeskBox/ViewModels/TodoWidgetViewModel.cs:836-839 — else { OnPropertyChanged(nameof(IsInitialized)); } | 用 raise `IsInitialized` 当"重绘信号" | 待修 |
| `R19-S4-08` | P3 | src/DeskBox/ViewModels/TodoWidgetViewModel.cs:798-807。 | `ApplyExternalStoreChange` 对 `IsCompleted` 的重复写入 | 待修 |
| `R19-S4-09` | P3 | src/DeskBox/ViewModels/WidgetViewModel.Operations.cs:1022-1028。 | `DeleteDroppedPathsForBinAsync` 的失败项记录错位 | 待修 |
| `R19-S4-10` | P3 |  | QuickCapture 的 bounds/锁定落盘使用默认 `notifySubscribers: true` | 待修 |
| `R19-S4-11` | P3 |  | 多处 `DispatcherQueue.TryEnqueue(...)` 返回值被忽略 | 待修 |
| `R19-S4-12` | P3 |  | Weather/Music VM 在 `Dispose()` 中处置**注入**的 service（所有权倒置） | 待修 |
| `R19-S4-13` | P3 | src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.ItemSync.cs:81-166。 | `QuickCaptureWidgetViewModel.SyncVisibleItems` 增量插入分支未登记索引 | 待修 |
| `R19-S4-14` | P3 | src/DeskBox/ViewModels/TodoWidgetViewModel.ItemOperations.cs:146（Items.Insert(Math.Clamp(currentIndex + 1, 0, ... | `SetCompletedAsync` 插入下一 occurrence 前未归一化排序号 | 待修 |
| `R19-S4-15` | P3 | src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.ViewScheduling.cs:88-108 — _ = Task.Run(async () => { try {... | `CurrentViewSaveTimer_Tick` 用 `Task.Run` 做落盘 | 待修 |
| `R19-S4-16` | P3 |  | `*ItemsSource` 每次 get 返回新数组，使 `SynchronizeVisibleItems` 的增量同步白做 | 待修 |
| `R19-S5-01` | P2 | src/DeskBox/Controls/WidgetShellContentHost.cs:226-244（catch 块）；对照 WidgetShellContentHost.cs:202-213（成功路径）、Wid... | 内容宿主初始提交失败时回滚漏清壳内容 | 待修 |
| `R19-S5-02` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:370（字段）、5627-5631（??= DispatcherQueue.CreateTimer() + Tick -=/+= + St... | 标题栏点击折叠定时器不随卸载释放、不记账 | 待修 |
| `R19-S5-03` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:4759（唯一调用点 ApplyActionButtonSurface(false);）、4791-4798（方法体三元）、5341-53... | `ApplyActionButtonSurface` 参数恒为 false，overlay 分支为死代码并产生无谓分配 | 待修 |
| `R19-S5-04` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:3483-3485 | 两个 `static readonly` 画刷字段零引用，且属跨实例共享可变画刷反模式 | 待修 |
| `R19-S5-05` | P3 | src/DeskBox/Controls/WidgetGroupTitleSwitcher.Interaction.cs:931-937；元素定义 src/DeskBox/Controls/WidgetGroupTitl... | `CapsuleChrome` 永久不可见，却每次都被赋主题画刷 | 待修 |
| `R19-S5-06` | P3 | src/DeskBox/Controls/WidgetGroupTitleSwitcher.Interaction.cs:924-952（UpdateInteractionChrome），字段 :27，清理 :417/:... | `_interactionChromeStoryboard` 缺少 `Completed` 收口 | 待修 |
| `R19-S5-07` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:5203-5215（定义），调用点 4953、5018、5070 | `QueueCompactActionRegionRefreshAfterExit` 无 `IsLoaded` / 代际守卫 | 待修 |
| `R19-S5-08` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:3255 | `ApplyCompactLiveState` 中恒假三元分支 | 待修 |
| `R19-S5-09` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:5711-5734（OverlayDragHandle_PointerMoved，判定用字面量 25）；具名常量 WidgetShell.... | 拖拽手柄移动判定硬编码阈值，绕过具名常量 | 待修 |
| `R19-S5-10` | P3 | src/DeskBox/Controls/WidgetShell.xaml.cs:5095 | 高频悬停路径用 `App.Log` 打印控件标题文本 | 待修 |
| `R19-S5-11` | P3 | src/DeskBox/Controls/WidgetGroupTitleSwitcher.Tabs.cs:616（ObserveDragHoverSwitch 内 DispatcherQueueTimer timer ... | 两处 `DispatcherQueueTimer` 未做 `Tick` 解除与生命周期记账 | 待修 |
| `R19-S5-12` | P3 | src/DeskBox/Controls/WidgetGroupTitleSwitcher.Tabs.cs:45-70（((SolidColorBrush)resources[key]).Color = color;），... | `SetTabForegroundColors` 依赖硬类型转换与索引器，契约脆弱 | 待修 |
| `R19-S5-13` | P3 | src/DeskBox/Controls/WidgetShellContentHost.cs:354-362（DisposeContent）；对照 :202（提交时对 outgoing 调 OnDeactivated）、... | `DisposeContent` 对过渡中的 outgoing 内容不调用 `OnDeactivated` | 待修 |
| `R19-S5-14` | P3 | src/DeskBox/Controls/AttachmentTileStrip.xaml.cs（AttachmentTile_Loaded、AttachmentTile_DataContextChanged，均 pri... | `AttachmentTileStrip` 两个 `async void` 无异常兜底 | 待修 |
| `R19-S5-15` | P3 | src/DeskBox/Controls/WidgetFeedbackPresenter.xaml.cs（AnimateOut 内 storyboard.Completed += (_,_) => FinishHide(... | `WidgetFeedbackPresenter` 淡出动画 `Completed` 不解除且无代际守卫 | 待修 |
| `R19-S6-01` | P2 |  | [P2] 内容层没有 `Dispose` 期 flush 契约：详情/笔记草稿的收尾只能靠 `async void` 抢跑 | 待修 |
| `R19-S6-02` | P2 |  | [P2] 笔记保存失败在"切换条目 / 切换筛选"路径上完全静默 | 待修 |
| `R19-S6-03` | P2 |  | [P2] 自动保存的 `SemaphoreSlim.WaitAsync()` 无取消无超时，而 UI 路径 `await` 该门 → 一次卡住的存储写会连带挂死交互 | 待修 |
| `R19-S6-04` | P2 |  | [P2] 附件缩略图预览的两个 `async void` 无 try/catch，缩略图链路只有 `finally` 没有 `catch` | 待修 |
| `R19-S6-05` | P2 |  | [P2] 拖放失败时把原始异常 `Message` 直接显示给用户，违反本层既有的"异常文本只进日志"约定 | 待修 |
| `R19-S6-06` | P2 |  | [P2] 剪切态永不失效：无 `Clipboard.ContentChanged` 监听、无所有权校验，剪切标记与真实剪贴板会长期脱节 | 待修 |
| `R19-S6-07` | P3 | src/DeskBox/Controls/WidgetContents/TodoWidgetContent.DragDrop.cs:985-996（BuildDroppedTodoTitle 末行 return stri... | [P3] 拖入附件生成的待办标题硬编码英文 `"Attachment"`，绕过 13 语言资源 | 待修 |
| `R19-S6-08` | P3 |  | [P3] 两个拖放 `async void` 只有 `finally` 没有 `catch` | 待修 |
| `R19-S6-09` | P3 |  | [P3] `_lifetimeCancellation.Dispose()` 之后仍有成员读取 `.Token` | 待修 |
| `R19-S6-10` | P3 |  | [P3] Glance / Weather 内容层订阅 `ViewModel.PropertyChanged` 永不退订，且 leaf 不实现 `IDisposable`，Adapter 也不触碰 leaf | 待修 |
| `R19-S6-11` | P3 | src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs:2212-2217 | [P3] 列表模式重命名编辑器：`Math.Clamp` 的下界（80）高于 `contentWidth` 的自有下限（60） | 待修 |
| `R19-S6-12` | P3 | src/DeskBox/Controls/WidgetContents/WidgetContentAdapterBase.cs（protected void ReleaseView() 只清 _view 引用）；View... | [P3] `WidgetContentAdapterBase.ReleaseView()` 是死代码：全仓零调用点，`_view` 缓存永不释放 | 待修 |
| `R19-S6-13` | P3 |  | [P3] `SpacePreviewBroker` 被 `static readonly` 共享，但类注释声称"每个 broker 实例拥有自己的粘性后端" | 待修 |
| `R19-S6-14` | P3 |  | [P3] 内容层 `catch` 后把异常"记日志即结束"，缺少统一的可观测失败通道（静默失败面） | 待修 |
| `R19-S7-01` | P2 | src/DeskBox/Helpers/ShellContextMenuProxy.cs:249-278（Prewarm，关键行 :255 取 gate、:267 释放 gate、:274 DisposeServer()... | (见报告) | 待修 |
| `R19-S7-02` | P3 | src/DeskBox/Helpers/ShellContextMenuProxy.cs:845-867（SendCommandAsync，取消分支 :858-866）。 | (见报告) | 待修 |
| `R19-S7-03` | P3 | src/DeskBox/Helpers/ShellContextMenuProxy.cs:975-976（ParseProtocolLine 的 case "warm":）与 :261-263（Prewarm 只接受 W... | (见报告) | 待修 |
| `R19-S7-04` | P3 | src/DeskBox/Helpers/ShellContextMenuProxy.cs:135-152（ShowAsync 的 pending/cancel 登记）与 :199-215（CancelOpenMenu 发... | (见报告) | 待修 |
| `R19-S7-05` | P3 | src/DeskBox/Helpers/ShellContextMenuProxy.cs:897-902（ReadProtocolLineAsync 把子进程 stdout 的每一行原样写入 App.Log）。 | (见报告) | 待修 |
| `R19-S7-06` | P3 | src/DeskBox/Helpers/ShellContextMenuProxy.cs:911-941（ProxyServer.Dispose）、:804-832（ProcessId/HasExited 的 catch... | (见报告) | 待修 |
| `R19-S7-07` | P3 | src/DeskBox/Helpers/ShortcutNativeBackend.cs:1017（CaptureDiagnosticState），其中 :1029 调 ReadPeArchitecture(module... | (见报告) | 待修 |
| `R19-S7-08` | P3 | src/DeskBox/Helpers/ShortcutNativeBackend.cs:1017 起（CaptureDiagnosticState）使用常量 AbiVersion（声明于 :147，internal c... | (见报告) | 待修 |
| `R19-S7-09` | P3 | src/DeskBox/Services/CompositorClockBoostCoordinator.cs:83：_ = Win32Helper.TrySetHighResolutionTimer(enabled);... | (见报告) | 待修 |
| `R19-S7-10` | P2 | src/DeskBox/Platform/Win32Helper.cs:2631-2658（[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]... | (见报告) | 待修 |
| `R19-S8-01` | P1 |  | (见报告) | 待修 |
| `R19-S8-02` | P1 |  | (见报告) | 待修 |
| `R19-S8-03` | P2 |  | (见报告) | 待修 |
| `R19-S8-04` | P2 |  | (见报告) | 待修 |
| `R19-S8-05` | P2 |  | (见报告) | 待修 |
| `R19-S8-06` | P3 |  | (见报告) | 待修 |
| `R19-S8-07` | P3 |  | (见报告) | 待修 |
| `R19-S8-08` | P3 |  | (见报告) | 待修 |
| `R19-S8-09` | P3 |  | (见报告) | 待修 |
| `R19-S8-10` | P3 |  | (见报告) | 待修 |
| `R19-S8-11` | P3 |  | (见报告) | 待修 |
| `R19-S9-01` | P2 | src/DeskBox/Platform/JumpListNativeMethods.cs:12-15；消费方 src/DeskBox/Services/JumpListService.cs:28-29 | `SetCurrentProcessExplicitAppUserModelID` 被声明为 `bool`，实为 `HRESULT`；调用方还无条件打印成功 | 待修 |
| `R19-S9-02` | P2 | native/deskbox-thumbnail-proxy/src/main.rs:1630-1633（构造），使用于 :1652-1656 | 右键菜单 `lpDirectory` 用 UTF-8 字节填充，而该字段是 ANSI（系统 ACP） | 待修 |
| `R19-S9-03` | P3 | native/deskbox-native/src/shortcut.rs:637-760（五个 capture_field(...)? 串行） | `read_stored_raw` 任一字段失败即整次读取作废 | 待修 |
| `R19-S9-04` | P3 | native/deskbox-native/src/shortcut.rs:761-823（return Err(DESKBOX_NATIVE_S_FALSE)）；消费于 :503-626 execute_shortcu... | `read_effective_diagnostic` 用成功码 `S_FALSE` 表示失败，该码被原样上报为失败原因 | 待修 |
| `R19-S9-05` | P3 | native/deskbox-native/src/shortcut.rs:444 | `IPersistFile::Save` 用 `!= S_OK` 判定成功，把 `S_FALSE` 当失败 | 待修 |
| `R19-S9-06` | P3 | native/deskbox-native/src/shortcut.rs:839 | 截断判定启发式在"恰好 capacity−1 个字符"时误报 | 待修 |
| `R19-S9-07` | P3 | native/deskbox-thumbnail-proxy/src/main.rs:1754、:1841 | 宿主"重命名"菜单项用 UTF-8 字节长度做 256 上限判断，长 CJK 标签被静默丢弃 | 待修 |
| `R19-S9-08` | P3 | native/deskbox-native/src/lib.rs:640-657（is_valid_music_input_string 允许 length_chars == 0 + 空指针）、:812-843（vali... | ABI 与 C# 包装层都接受空音乐身份串；空身份会落到"唯一会话"回退，可能命中无关应用 | 待修 |
| `R19-S9-09` | P3 | native/deskbox-native/src/music_volume.rs:413-448（matching_kind 的规则优先级）与 find_session 中的 return Ok(Some(...))（... | `matching_kind` 的优先级只在单个会话内生效，首个命中即返回，弱匹配可压过强匹配 | 待修 |
| `R19-S9-10` | P3 | native/deskbox-thumbnail-proxy/src/main.rs:333（install_menu_mouse_hook），MouseHookGuard 的 Drop（PostThreadMessag... | 鼠标钩子线程在 2 秒握手超时后被遗弃，可能留下永久 WH_MOUSE_LL 钩子 | 待修 |
| `R19-S9-11` | P3 | src/DeskBox/Platform/ComVtable.cs（Slot<T> 实现） | `ComVtable.Slot<T>` 每次调用都新建委托，COM 热路径上产生持续分配 | 待修 |
| `R19-S10-01` | P1 |  | (见报告) | 待修 |
| `R19-S10-02` | P2 |  | (见报告) | 待修 |
| `R19-S10-03` | P2 |  | (见报告) | 待修 |
| `R19-S10-04` | P2 |  | (见报告) | 待修 |
| `R19-S10-05` | P3 |  | (见报告) | 待修 |
| `R19-S10-06` | P3 |  | (见报告) | 待修 |
| `R19-S10-07` | P3 |  | (见报告) | 待修 |
| `R19-S10-08` | P3 |  | (见报告) | 待修 |
| `R19-S10-09` | P3 |  | (见报告) | 待修 |
| `R19-S10-10` | P3 |  | (见报告) | 待修 |
| `R19-S10-11` | P3 |  | (见报告) | 待修 |

**评级/结论修正（Lead 复核，需在下一轮继承）**

| 条目 | 分段结论 | 复核结论 |
|---|---|---|
| R19-S1-01 | P1「10 分钟总超时只取消 linked CTS，裸 `OperationCanceledException` 不匹配任何 catch 过滤器」 | **P2**：未观察异常被 `App` 的 `Handled = true` 全局吞掉（R18 C-3），实际表现为静默失败 + `.tmp` 残留，而非崩溃；"无提示"仍成立 |
| R19-S8-01 | P1「`widgets` 数组含 `null` 元素 → 归一化链空引用 → 整份设置回默认并覆盖原文件」 | **P2**：触发需"外部损坏但可解析"的本地文件，应用自身不会写出 `null` 元素；后果不可逆，修复优先级仍高 |
| R19-S8-02 | P1「`Normalize` 在反序列化回调内抛异常 → 整份 Todo 被隔离」 | **P2**：同上，且 `.corrupt-*` 隔离 + `.bak` 回退提供了恢复通道（S8-03 指出隔离只留 2 份） |
| R19-S3-01…05 | 原文未标注级别 | **补标**：S3-01/02/03 = P2，S3-04/05 = P3（依据各条 §2 影响段与证据强度） |
| R19-S10-01 | P1 | **维持 P1**：远端快照删除不可逆；判定路径（`CloudBackupService.cs:346-371`、`:596-616`、`:720-747`）已逐行复核与报告一致 |
| R19-S2-01 | P1「`WidgetManager` 删除路径丢弃 `SaveAsync` 返回值」 | **P2（且报告表述需更正）**：`:2274` 是 `await _settingsService.SaveAsync()`，该方法签名是 `Task SaveAsync(bool notifySubscribers = true)`（`SettingsService.cs:1104-1116`），**没有 bool 返回值**；失败被内部吞掉（`SaveToFileOnlyAsync()` 的 bool 只用于清 `_hasPendingSave`）。真实缺陷 = "写盘失败对调用方不可见 + store 删除先于写盘"；`_hasPendingSave` 保持 1 使后续保存会重试，影响面有限，且用户本意即删除该 Widget → P2 下沿 |
| R19-S5-01 | P2 | **维持 P2**：`WidgetShellContentHost.cs:226-244` 回滚分支确实漏 `_clearContent()`，但"已释放内容仍在视觉树"的可见窗口取决于失败瞬间的呈现路径，影响面按 P2 下沿 |

**本轮环境基线（供下一轮对照，勿误判为回归）**

- 本轮**未运行** build/test（十路均为纯静态审查）。基线沿用 R18 收尾值：全量测试 6204 通过 / 11 失败 / 1 跳过 / 6216；11 条失败为沙箱令牌写 `%LOCALAPPDATA%`/`HKCU` 与未跟踪文件 `src/DeskBox/Controls/ScreenshotAnnotationRasterizer.cs` 的契约基线噪声，与任何修复无关。

## R19 整改批（2026-10-27，11 条已修 + 6 条复核改判）

**已修（代码已落地，构建 0 错误 / 22 既有警告；`cargo check` 通过）**

| 条目 | 级别 | 改动位置 | 做法 |
|---|---|---|---|
| `R19-S2-01` | P2 | `src/DeskBox/Services/WidgetManager.cs`（`DeleteWidgetAsync`） | 用既有 `SettingsService.SaveCheckedAsync()` 先落盘，失败记日志并 `return`（不再先删 store）；`persistFailed` 在 `try` 前声明，失败时 `finally` 保留 `_deletedWidgetIds` 墓碑，避免下次启动复活 |
| `R19-S10-01` | P1 | `src/DeskBox/Services/CloudBackupService.cs` | 保留清理改为三分支（取消/未校验/正常）；`ApplyRetentionAsync` 只统计并删除**本机自有**快照（`IsOwnedSnapshotName` 按 8 位设备后缀），并始终保留"上一次已验证可用"的自有快照；未校验时整体跳过清理 |
| `R19-S8-01` | P2 | `src/DeskBox/Services/SettingsService.cs` | 新增 `PreserveSettingsFileBeforeDefaults()`：加载失败回默认前把 `settings.json` 复制为 `settings.recovered-{时间戳}.json` |
| `R19-S1-02` | P2 | `src/DeskBox/Services/DeskBoxDataBackupService.cs` | 归档内重复 `manifest.json` 不再抛 `InvalidOperationException`（`SingleOrDefault` → 取首条 + 日志） |
| `R19-S1-03` | P2 | `src/DeskBox/Services/FolderWatcherService.cs` | `_preserveReconnectAttempt` 让重连退避不再被 `Stop()` 清零（指数退避恢复生效） |
| `R19-S1-04` | P2 | `src/DeskBox/Services/FolderWatcherService.cs` | 变更缓冲超 64 条时先 `Clear()` 再置全量重载标志 |
| `R19-S1-05` | P2 | `src/DeskBox/Services/ArchiveZipService.cs` | 归档改为 tmp + `File.Move` 原子落盘，失败删临时文件 |
| `R19-S5-01` | P2 | `src/DeskBox/Controls/WidgetShellContentHost.cs` | `CommitPreparedContent` catch 中 `outgoingContent is null` 时补 `_clearContent()` |
| `R19-S7-10` | P2 | `src/DeskBox/Platform/Win32Helper.cs` | `DEVMODE` 补齐尾部 8 个成员 → 220 字节（`dmSize` 随之正确） |
| `R19-S9-01` | P2 | `src/DeskBox/Platform/JumpListNativeMethods.cs`、`Services/JumpListService.cs` | AUMID 入口改 `int` 返回 HRESULT，调用方按 `hr < 0` 记失败 |
| `R19-S9-02` | P2 | `native/deskbox-thumbnail-proxy/src/main.rs`、`Cargo.toml` | 新增 `encode_ansi_path()`（`WideCharToMultiByte(CP_ACP)`），`lpDirectory` 不再塞 UTF-8 字节 |

**复核改判（详细依据见 `docs/quality/rounds/round-19/LEAD-VERIFICATION.md` §14–§22）**

| 条目 | 改判 | 依据 |
|---|---|---|
| `R19-S2-01` | P1 → **P2** | `SettingsService.SaveAsync` 返回 `Task`（无 bool），"丢弃返回值"不成立；真实缺陷为"写盘失败对调用方不可见 + 先删 store" |
| `R19-S8-01` | P1 → **P2** | `ResilientJsonStore` 对可重试读失败保留原文件、对反序列化失败隔离（`:84-94`/`:99`/`:114`） |
| `R19-S8-02` | P1 → **撤销** | `TodoWidgetStore.cs:314-326` 已过滤 null 并 GroupBy 去重 |
| `R19-S3-03` | P2 → **P3** | `App.xaml.cs:501` 订阅 `SettingsService.PersistenceFailed`，写盘失败已有 5 分钟节流的用户可见警告（`:4215-4238`） |
| `R19-S7-10` | 维持 P2，**机理更正** | 手算证明 `dmDisplayFrequency` 偏移 184..188 本就正确；真实缺陷是结构在 188 截断、`dmSize` 少报 32 字节 |
| `R19-S1-03` | 维持 P2（**与复核员分歧**） | 复核员称"退避本就封顶"；实测 `Stop()` 在 `:631` 无条件清零 `_reconnectAttempt`，且 `StartAsync` 在 `:245` 调用 `Stop()` |

**因本次语义变更同步更新的测试契约**

- `tests/DeskBox.Tests/CloudBackupTransportTests.cs` `RunBackupNow_PrunesRemoteBeyondRetention`：种子快照改用真实 8 位设备后缀，新增"他设备快照必须保留"的断言，期望文件数 3 → 4。
- `tests/DeskBox.Tests/TodoOrphanCleanupContractTests.cs` 首条用例更名 `RemoveWidgetAsync_PersistsRemovalBeforeDeletingTodoStore`：契约由"先删 store 再判剩余"改为"先落盘、再删 store"，仍保留"禁用 feature 前必须检查剩余 Todo"的断言。
- `tests/DeskBox.Tests/ModuleBoundaryContractTests.cs` 破坏性文件变更清单：新增 `ArchiveZipService.cs = 2`、`SettingsService.cs` 2 → 3（附 R19 说明）。---

## Round 20（2026-10-27，十路专项静态审查；S1–S8 已到货，S9/S10 待补）

审查方式与 R18/R19 一致：十路 SubAgent 并行静态审查（纯代码阅读），Lead 对 P0/P1 全部与 P2 抽样逐条复核。分段报告在 `docs/quality/rounds/round-20/`，复核记录 `LEAD-VERIFICATION.md`，总报告 `ROUND-20-总报告.md`。

**S1–S8 合计 122 条：P0 0 / P1 1 / P2 38 / P3 83；经 Lead 复核后 P1 归零（`R20-S6-01` 降 P2）。**
| 编号 | 级别 | 段 | 一句话结论 |
|---|---|---|---|
| `R20-S1-01` | P2 | 1 | 复核确认（延续 R19-S1-01 / DEF-163）：10 分钟总下载超时以裸 `OperationCanceledException` 逃出，更新下载静默失败 | 已修
| `R20-S1-02` | P2 | 1 | 新立：scoped 恢复的域替换是"先删后拷"，取消点位于拷贝循环内部，重试预算耗尽后连恢复载荷一起删除 | 已修
| `R20-S1-03` | P2 | 1 | 新立：`TryRecoverStrandedRollback` 的门控与调用点使 `restore-rollback/` 副本成为永久孤儿（对用户等价于数据丢失） |
| `R20-S1-04` | P2 | 1 | 新立：WebDAV `ResolveUri` 保留 `..` 段，`RemotePath` 未校验 → retention 删除可落到配置目录之外 | 已修
| `R20-S1-05` | P3 | 1 | 复核确认（延续 R19-S1-09 / R18-S1-006）：`FolderWatcherService` 两个 debounce tick 裸 `Invoke`，且事件负载已先清空 |
| `R20-S1-06` | P3 | 1 | 复核确认（延续 R19-S1-08 / DEF-144）：`Prepare*RestoreAsync` 在解包校验新归档**之前**无条件销毁上一个待确认恢复 |
| `R20-S1-07` | P3 | 1 | 复核确认（延续 R18-S1-005）：`allowShellElevation` 分支丢弃 `onItemError` 与 `skippedItems` |
| `R20-S1-08` | P3 | 1 | 复核确认（延续 DEF-139）：JSON store 提交路径无 `WriteThrough`/`Flush(true)`，断电后"已提交"的主文件可能为零长度或半写 |
| `R20-S1-09` | P3 | 1 | 复核确认（延续 R18-S1-004）：`ArchiveZipService.WriteZip` 缺少"排除自身归档"，与 `WriteTar` 不对称 |
| `R20-S1-10` | P3 | 1 | 新立：`CloudBackupService.DownloadSnapshotAsync` 直接以最终名落盘，无 tmp+rename、无长度回验 |
| `R20-S1-11` | P3 | 1 | 新立：自动/手动"立即备份"在数据目录为空时返回 `Skipped` 且无原因，与 `ExportBackupAsync` 的抛错语义不一致 |
| `R20-S1-12` | P3 | 1 | 复核确认（延续 R19-S1-10）：`AppUpdateService` 三处事件裸 `Invoke`，且状态已先行变更 |
| `R20-S2-01` | P2 | 2 | 交互深度被静默清零，导致显示拓扑门永久关闭与看门狗 epoch 失效 | 已修
| `R20-S2-02` | P2 | 2 | 删除路径的用户数据销毁仍早于持久化提交 |
| `R20-S2-03` | P2 | 2 | `RequestCompactState` 用过渡态比较丢弃反向请求，持久态与窗口实际态分叉 |
| `R20-S2-04` | P2 | 2 | 复合保存在 layout 半失败时仍提交 settings.json，违反「false = 什么都没落盘」契约 |
| `R20-S2-05` | P2 | 2 | 折叠动画提交失败后宣告完成且不重试几何，展开方向无补偿 |
| `R20-S2-06` | P3 | 2 | `OnCompactVisualStateChanged` 早于 `_compactState` 赋值，同态分支重复回调 |
| `R20-S2-07` | P3 | 2 | `MoveWindowWithoutPersisting` 用单槽位字段承载逐帧回调，排队路径可丢弃先前回调 |
| `R20-S2-08` | P3 | 2 | `CleanupWidgetCollapse` 不清 `_compactExpansionReadyCallbacks` |
| `R20-S2-09` | P3 | 2 | `WidgetLayoutStore.SaveAsync` 公开且不检查 `CanWrite` |
| `R20-S2-10` | P3 | 2 | FeatureWidgets 两处清理路径在锁外改 `Settings.Widgets` 且不删 per-widget store |
| `R20-S2-11` | P3 | 2 | `HideAndCloseFeatureWidgetAsync` 名为 Async 实为 void 且只关第一个窗口；三条启停路径绕过更新锁 |
| `R20-S2-12` | P3 | 2 | `CloseFeatureWidgetInstance` 非 UI 线程丢弃 Task 并空 catch |
| `R20-S2-13` | P3 | 2 | `InteractionActiveChanged` 的匿名订阅永不退订 |
| `R20-S3-01` | P2 | 3 | P2 · 未处理异常在启动生命线建立后被完全静默吞没 |
| `R20-S3-02` | P2 | 3 | P2 · `ShowInfoDialogAsync` 缺 `XamlRoot` 守卫、无 catch、不排队，35 处 `ShowAsync` 只有 3 处走协调器 | 已修
| `R20-S3-03` | P2 | 3 | P2 · 本地备份恢复无重入互斥，且「确认」与「实际应用」之间无身份绑定 | 已修
| `R20-S3-04` | P3 | 3 | P3 · `SettingsSections/` 内两处裸 `ShowAsync`（一处无 `XamlRoot` 判空、一处整句在 `try` 外） |
| `R20-S3-05` | P3 | 3 | P3 · post-await 生命周期守卫 `_isClosed` 在 `SettingsWindow` 各 partial 间系统性不一致 |
| `R20-S3-06` | P3 | 3 | P3 · 13 处把原始 `ex.Message` 拼进本地化对话框正文 |
| `R20-S3-07` | P3 | 3 | P3 · `ShowGlanceSection` 命中导航早退，切换 glance widget 后设置列表不刷新 |
| `R20-S3-08` | P3 | 3 | P3 · 启动收尾 `_ = Task.Run(...)` 无 `try/catch`，四步收尾失败只经 GC 记一行 |
| `R20-S3-09` | P3 | 3 | P3 · storm-guard 延迟启动 timer 不入字段、匿名 Tick 永不解绑，关闭竞态下对已 Dispose 的 watcher 调 `Start()` |
| `R20-S3-10` | P3 | 3 | P3 · 备份快照列表把 `FailedBody` 当摘要 + finally 无条件解禁按钮 |
| `R20-S3-11` | P3 | 3 | P3 · 搜索结果右键菜单在 UI 线程同步做 `File.Exists` 与 PE 探测 |
| `R20-S3-12` | P3 | 3 | P3 · 启动失败通知的阶段名几乎恒为同一值 |
| `R20-S4-01` | P2 | 4 | [P2] Todo 变更命令忽略 `SaveAsync` 返回值，"成功"语义与实际落盘脱钩 |
| `R20-S4-02` | P2 | 4 | [P2] 复核确认（延续 R19-S4-01）：Todo 日界/逾期派生状态无任何刷新触发点 |
| `R20-S4-03` | P2 | 4 | [P2] 复核确认（延续 R19-S4-02）：`ApplyExternalStoreChange` 早退 `return` 吞掉下一次重复项插入 |
| `R20-S4-04` | P2 | 4 | [P2] 复核确认（延续 R19-S4-03）：`RefreshAsync` 与 `InitializeAsync` 完全同体，通知动作触发整表重载 |
| `R20-S4-05` | P2 | 4 | [P2] 复核确认（延续 R19-S4-04）：`UndoLastActionAsync` 以整表快照恢复 |
| `R20-S4-06` | P3 | 4 | [P3] 复核确认（延续 R19-S4-05）：`_isApplyingSettingsSnapshot` 无保存/恢复，且快照应用可同步递归 |
| `R20-S4-07` | P3 | 4 | [P3] 复核确认（延续 R19-S4-06）：`MarkerGlyph` 两个分支返回同一字形 |
| `R20-S4-08` | P3 | 4 | [P3] 复核确认（延续 R19-S4-07）：用 raise `IsInitialized` 当作"重绘信号" |
| `R20-S4-09` | P3 | 4 | [P3] 复核确认（延续 R19-S4-08）：`ApplyExternalStoreChange` 对完成态重复写入 |
| `R20-S4-10` | P3 | 4 | [P3] 复核确认（延续 R19-S4-09）：`DeleteDroppedPathsForBinAsync` 把整批失败都记在 `targets[0]` 上 |
| `R20-S4-11` | P3 | 4 | [P3] 复核确认（延续 R19-S4-15）：`CurrentViewSaveTimer_Tick` 用 `Task.Run` 落盘且无写入序号 |
| `R20-S4-12` | P3 | 4 | [P3] 复核确认（延续 R19-S4-16）：`*ItemsSource` 每次 get 返回新数组，增量同步白做 |
| `R20-S4-13` | P3 | 4 | [P3] 复核确认（延续 R19-S4-14）：插入下一 occurrence / 外部插入后不 `NormalizeSortOrders` |
| `R20-S4-14` | P3 | 4 | [P3] 新发现：SearchPopup 失败分支缺代数守卫，过期失败会覆盖新结果的状态文本 |
| `R20-S4-15` | P3 | 4 | [P3] 新发现：`CancelCurrentSearch` 立即 `Dispose()` CTS，而在飞操作仍持有其 token |
| `R20-S4-16` | P3 | 4 | [P3] 新发现：日界计时器用 wall-clock tick 差算间隔，DST 春季跳变日会晚 1 小时 |
| `R20-S4-17` | P3 | 4 | [P3] 新发现：`ClearSearch()` 与 `OnPopupHidden()` 的选择态清理不对称 |
| `R20-S4-18` | P3 | 4 | [P3] 新发现：文件栈预览扫描在线程池上枚举"活的"设置图，且取消路径不重置 loading |
| `R20-S4-19` | P3 | 4 | [P3] 新发现：线程封送约定在同一层内存在三套，语言变更 handler 缺守卫 |
| `R20-S5-01` | P2 | 5 | P2　路由 Drop 抢打"完成戳"，回执只有一处出口 → 落点在非文件表面时拖放静默丢失 |
| `R20-S5-02` | P2 | 5 | P2　`CompleteTransition` 抛异常后 host 永久卡在"有活动过渡"，只有关窗口能恢复 |
| `R20-S5-03` | P3 | 5 | P3　两处 storyboard 未回写基值，`Stop()` 后视觉跳变/永久残留 |
| `R20-S5-04` | P3 | 5 | P3　紧凑层键盘焦点只刷新操作按钮，不刷新重排手柄；且每次 Tab 触发一次淡出淡入 |
| `R20-S5-05` | P3 | 5 | P3　`OverlayDragHandle_PointerPressed` 丢弃 `CapturePointer` 返回值，捕获失败即状态机卡死 |
| `R20-S5-06` | P3 | 5 | P3　`InstallNativeFileDropBridge` 异常路径无法释放已挂满回调的 `NativeDropTarget` |
| `R20-S5-07` | P3 | 5 | P3　`AnimateOpacity` 用动画进行中的当前值判等，中途改目标会被误判"已到位" |
| `R20-S5-08` | P3 | 5 | P3　`CollapsedChromeLayer_SizeChanged` 的 650ms 走马灯延迟被随后入队的 300ms 覆盖 |
| `R20-S5-09` | P3 | 5 | P3　走马灯宽度探针不复制 `FlowDirection`/`Language`，RTL 下测量偏差 |
| `R20-S5-10` | P3 | 5 | P3　`OverlayIdentityHost` 整棵子树永久不可见（死 UI），`OverlayTitle` DP 全链路空转 |
| `R20-S5-11` | P3 | 5 | P3　`IsPointOverGroupTitleBar` 裸 `catch { return false; }` 吞掉一切异常 |
| `R20-S5-12` | P3 | 5 | P3　`SetContent` 的入队尺寸通知无 `IsLoaded`/代际守卫，会向"换人后"的 content 重发旧尺寸 |
| `R20-S5-13` | P3 | 5 | P3　`UpdateCompactActionRegionWidth` 在布局路径上做两次 LINQ 分配 |
| `R20-S5-14` | P3 | 5 | P3　`Localized` 的失败诊断只写 `Debug.WriteLine`，发布版完全静默；`Track` 为全表线性扫描 |
| `R20-S6-01` | P1 | 6 | 缩略图加载三层均无异常边界，`async void` 逃逸到 UI 线程 → 进程终止 |
| `R20-S6-02` | P2 | 6 | 缓存复用钩子 `PrepareForReuse()` 是 `void` 空默认，QuickCapture/Todo 未覆写 → 组内切换无同步 flush 点 |
| `R20-S6-03` | P2 | 6 | 弹层打开期间切换 stack，延迟打开请求被静默丢弃 |
| `R20-S6-04` | P2 | 6 | 弹层宿主窗口被系统关闭后内容层不知情，弹层永久不可用 |
| `R20-S6-05` | P2 | 6 | 原始异常文本直呈用户 |
| `R20-S6-06` | P2 | 6 | 保存闸门 `WaitAsync()` 无取消、无超时 | 已修
| `R20-S6-07` | P2 | 6 | 弹层隐藏不归还前台，关闭后主表面键盘导航失效 |
| `R20-S6-08` | P3 | 6 | Todo 笔记「取消」不取消在飞保存，被丢弃的草稿仍会落盘 |
| `R20-S6-09` | P3 | 6 | `_stackPopoverPopupClosing` 恒为 false 的死判据 |
| `R20-S6-10` | P3 | 6 | close-button 交互租约在 `ReleaseStackPopover()` 路径上不释放 |
| `R20-S6-11` | P3 | 6 | `BeginStackPopoverTitleRename` 无异常边界 |
| `R20-S6-12` | P3 | 6 | `_lifetimeCancellation.Dispose()` 之后仍可能读 `.Token` |
| `R20-S6-13` | P3 | 6 | Glance / Weather 内容层订阅 `ViewModel.PropertyChanged` 永不退订 |
| `R20-S6-14` | P3 | 6 | 剪切态无剪贴板失效钩子（纯视觉陈旧） |
| `R20-S6-15` | P3 | 6 | `Math.Clamp(availableWidth, 80, contentWidth)` 上下界可倒置 |
| `R20-S6-16` | P2（待确认） | 6 | Todo 步骤写路径丢弃 `SaveAsync()` 返回值 |
| `R20-S6-17` | P3 | 6 | 附件名回退硬编码英文 `"Attachment"` |
| `R20-S6-18` | P3 | 6 | `WidgetContentAdapterBase.ReleaseView()` 死代码 |
| `R20-S6-19` | P3 | 6 | `SpacePreviewBroker` 为 `static readonly`，与类注释的生命周期描述矛盾 |
| `R20-S7-01` | P2 | 7 | 置信度：高 · 去重：新立案（对应 R19 §7 遗留「`SettingsService` 的反向锁序」，**结论修正**见下） |
| `R20-S7-02` | P2 | 7 | 置信度：高 · 去重：新立案（与 R19-S10-02 同族、不同位点） |
| `R20-S7-03` | P2 | 7 | 置信度：高 · 去重：**复核确认（延续 R19-S10-02）** |
| `R20-S7-04` | P2 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-05` | P2 | 7 | 置信度：中 · 去重：新立案（关联 R19 C-2′ 共性问题） |
| `R20-S7-06` | P2 | 7 | 置信度：高 · 去重：**复核确认（延续 R19-S2-10）** |
| `R20-S7-07` | P2 | 7 | 置信度：中 · 去重：新立案 |
| `R20-S7-08` | P2 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-09` | P3 | 7 | 置信度：高 · 去重：**复核确认（延续 R19-S2-02）** |
| `R20-S7-10` | P3 | 7 | 置信度：高 · 去重：**复核确认（延续 R19-S2-09）** |
| `R20-S7-11` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-12` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-13` | P3 | 7 | 置信度：中 · 去重：新立案 |
| `R20-S7-14` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-15` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-16` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-17` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-18` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-19` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-20` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S7-21` | P3 | 7 | 置信度：高 · 去重：新立案 |
| `R20-S8-01` | P2 | 8 | `settings.json` 含 `null` widget 元素仍在 layout 合并前炸穿，整份设置回默认（复核确认，延续 R19-S8-01；证据升级） | 已修
| `R20-S8-02` | P2 | 8 | `DesktopOrganizationHistoryStore.Normalize` 对 `items:[null]` 无防护，异常发生在 `ResilientJsonStore` 回调之外 → 整份设置回默认（新） | 已修
| `R20-S8-03` | P2 | 8 | `DesktopOrganizationRecoveryJournal.Items` 显式 `null` → 撤销事务 NRE，桌面整理不可撤销（新；R19-S8-06 同族） | 已修
| `R20-S8-04` | P3 | 8 | 多个 `JsonSerializerContext` 缺 `UseStringEnumConverter`，落盘枚举为整数（新） |
| `R20-S8-05` | P2 | 8 | `ViewMode` / `WidgetSortMode` 无降级转换器，未知值使整份 layout 被隔离（复核确认，延续 R19-S8-04） |
| `R20-S8-06` | P2 | 8 | `MsnWeatherResponse` 裸 `string` + `DescriptionToWmoCode(string)` 不接 `null` → 天气刷新静默失败（复核确认 R19-S8-06 的 MSN 部分，附可达链） |
| `R20-S8-07` | P3 | 8 | `WidgetLayoutSettingsSlice.CopyFrom` 逐属性赋值导致集合引用共享（复核确认，延续 R19-S8-07） |
| `R20-S8-08` | P3 | 8 | `WidgetConfig` 的三个集合属性对显式 `null` 无归一，且 `NormalizeStructure` 不覆盖（复核确认，延续 R19-S8-08 的 `FileAddedAtByPath` 部分） |
| `R20-S8-09` | P3 | 8 | `schemaVersion` 探测硬编码 camelCase 且大小写敏感，PascalCase 文件会被当作 v0 重放全部 15 步迁移（新） |
| `R20-S8-10` | P3 | 8 | settings 写盘 `stream.Flush()` 缺 `flushToDisk: true`，与 layout 写入不一致（复核确认 DEF-139 同族，settings 侧位点） |
| `R20-S8-11` | P3 | 8 | `WriteSettingsTempFileAsync` 注释"11 wire keys"与实际 15 个 layout 属性不符（新，可维护性） |
| `R20-S8-12` | P3 | 8 | `MsnWeatherResponse`/`WeatherData` 的并行数组契约无长度/一致性校验（新，契约不变量） |
| `R20-S9-01` | P2 | 9 | `InitializeProcThreadAttributeList` 的 `PSIZE_T` 被声明为 `ref int`（栈越界写） | 已修
| `R20-S9-02` | P2 | 9 | 上下文菜单服务器路径以 TAB/LF 作带内分隔符，注释前提错误 |
| `R20-S9-03` | P2 | 9 | `ShowAsync` 的 pending-request 接力把新菜单的结果返回给旧调用的调用方 |
| `R20-S9-04` | P3 | 9 | AOT 冒烟未覆盖 `WriteShellNamespaceShortcut` 与 `ReadEffectiveDiagnostic` |
| `R20-S9-05` | P3 | 9 | `ComVtable.Slot<T>` 无空指针校验，且每次调用新建委托 |
| `R20-S9-06` | P3 | 9 | `HdropDataObjectNativeMethods.GlobalFree` 声明为 `void`，丢弃真实返回类型 |
| `R20-S9-07` | P3 | 9 | 拖放描述写入器把剪贴板格式 ID 截断到 `ushort` 且不校验 0 |
| `R20-S9-08` | P3 | 9 | `Prewarm` 在释放门锁之后才 `DisposeServer()`，可误杀并发菜单轮 |
| `R20-S9-09` | P3 | 9 | `StorageMigrationNativeMethods.Extended()` 对相对路径产出非法 `\\?\relative` |
| `R20-S9-10` | P3 | 9 | AUMID 整改残留：HRESULT 型 API 仍带 `SetLastError = true` | 已修
| `R20-S9-11` | P3 | 9 | `read_stored_raw` 五字段串行 `?`，任一失败整次读取作废（第二个生产消费者已确认） |
| `R20-S9-12` | P3 | 9 | `read_effective_diagnostic` 用成功码 `S_FALSE` 表示失败 |
| `R20-S9-13` | P3 | 9 | `IPersistFile::Save` 用 `== S_OK` 而非 `>= 0` 判定 |
| `R20-S9-14` | P3 | 9 | `capture_field` 的截断判定在"恰好 259 字符"时误报 |
| `R20-S9-15` | P3 | 9 | 自定义菜单标签长度上限按 UTF-8 字节数校验 |
| `R20-S9-16` | P3 | 9 | 音乐音量空身份字符串落到"唯一会话"回退 |
| `R20-S9-17` | P3 | 9 | `find_session` 弱匹配可压过强匹配 |
| `R20-S9-18` | P3 | 9 | `MouseHookGuard::Drop` 超时后遗弃线程，可能残留低级鼠标钩子 |
| `R20-S10-01` | P2 | 10 | 更新器超时强杀安装器后，安装目录停在"新旧混合 + 旧清单"状态且无回滚 · P2 |
| `R20-S10-02` | P2 | 10 | 更新器在缺少 `--installer-sha256` 时静默跳过完整性校验（fail-open） · P2 | 已修
| `R20-S10-03` | P3 | 10 | 用户主动取消 UAC 被归类为 `failed` 而非 `cancelled` · P3 |
| `R20-S10-04` | P3 | 10 | 更新器自定义退出码 3/4/8/9 与 Inno Setup 官方退出码语义重叠 · P3 |
| `R20-S10-05` | P3 | 10 | 更新器参数解析：未识别开关会吞掉下一个实参；缺值静默丢弃 · P3 |
| `R20-S10-06` | P3 | 10 | 等待父进程 120s 超时后，若父进程仍存活则无任何日志地继续安装 · P3 |
| `R20-S10-07` | P3 | 10 | 卸载器的"期望路径"守卫恒真（同一表达式自我比较），只剩叶名校验 · P3 |
| `R20-S10-08` | P3 | 10 | 本地化契约对"重复键"与"重复占位符"存在两条静默通道 · P3 |
| `R20-S10-09` | P3 | 10 | `UserFacingFailureSurfaces_DoNotExposeRawExceptionMessages` 是可被重排绕过的精确空格文本否定断言 · P3 |
| `R20-S10-10` | P3 | 10 | AOT 冒烟门禁契约漏登记 3 个 runner，且只钉脚本变量不钉 summary 实际写出值 · P3 |
| `R20-S10-11` | P3 | 10 | ARM64 ABI 门禁测试在非 ARM64 环境零断言通过 · P3（建议由 P2 复议降级） |
| `R20-S10-12` | P3 | 10 | `AGENTS.md` 把 `publish-aot-retail.ps1` 说成零售包产出者，与实际唯一入口不符 · P3 |
| `R20-S10-13` | P3 | 10 | 三份发布摘要的校验强度不一致：唯一携带 `auditFindings` 的 AOT 审计摘要只校验"文件存在" · P3 |

### R20 整改批（2026-10-27，首批 4 条已修）

| 编号 | 级别 | 修法 | 文件 | 状态 |
| --- | --- | --- | --- | --- |
| `R20-S9-01` | P2 | `InitializeProcThreadAttributeList` 的 `PSIZE_T` 由 `ref int` 改为 `ref IntPtr`，调用处改用 `IntPtr attributeListSize` | `src/DeskBox/Platform/ShellUnelevatedLaunch.cs:91/325`、`DragDropPermissionNativeMethods.cs:49` | 已修 |
| `R20-S9-10` | P3 | 去掉 `SetCurrentProcessExplicitAppUserModelID` 上的 `SetLastError = true`（HRESULT 入口不设 last error） | `src/DeskBox/Platform/JumpListNativeMethods.cs:16` | 已修 |
| `R20-S10-02` | P2 | 下载时把摘要写入 `<installer>.sha256`；安装前解析摘要，缺失即拒绝安装；更新器缺 `--installer-sha256` 即拒绝执行 | `src/DeskBox/Services/AppUpdateService.cs`（sidecar 读写 + 安装前 fail-closed）、`src/DeskBox.Updater/Program.cs:91` | 已修 |
| `R20-S3-03` | P2 | 视图加 `_isRestoring` 单入口守卫；`DeskBoxRestorePreparation` 增 `ArchivePath`/`PreparedAtUtc`；`ConfirmPendingFullRestoreAsync` 比对身份，不符即拒绝并记日志 | `src/DeskBox/Views/SettingsWindow.Maintenance.cs:221-320`、`SettingsWindow.xaml.cs:74`、`src/DeskBox/Services/DeskBoxDataBackupService.cs:1628/3812` | 已修 |

- 构建：`dotnet build .\src\DeskBox\DeskBox.csproj -p:Platform=x64` → 0 错误 / 20 警告；`DeskBox.Updater` → 0 错误 / 0 警告。
- 定向测试：`--filter "FullyQualifiedName~DataBackup|~Update|~Restore"` → **447 通过 / 1 失败 / 448**，唯一失败是既有环境基线（`DesktopOrganizationTests` 需要真实 shell 令牌写 `%LOCALAPPDATA%`）。
- 方案修订：`docs/quality/rectify/R20-remediation-plan.md` §6（v2，按 `R20-remediation-plan-review.md` 的 10 条阻断逐条改写修法）。
### R20 整改批（2026-10-27，7 条已修 + 4 条复核改判/撤回）

| 编号 | 原级别 | 处置 | 位点 |
| --- | --- | --- | --- |
| R20-S9-01 | P2 | 已修 | `src/DeskBox/Platform/ShellUnelevatedLaunch.cs:91`（`PSIZE_T` 由 `ref int` 改 `ref IntPtr`，调用处 `IntPtr attributeListSize`）；`Platform/DragDropPermissionNativeMethods.cs:49` 同族声明同步改正 |
| R20-S9-10 | P3 | 已修 | `src/DeskBox/Platform/JumpListNativeMethods.cs:16`（HRESULT 入口去掉 `SetLastError`） |
| R20-S10-02 | P2 | 已修 | `src/DeskBox/Services/AppUpdateService.cs`（下载后写 `<installer>.sha256`、安装前解析摘要并 fail-closed、非空即无条件校验）；`src/DeskBox.Updater/Program.cs:91`（缺 `--installer-sha256` 即拒绝执行） |
| R20-S3-03 | P2 | 已修 | `Views/SettingsWindow.xaml.cs:74` + `Views/SettingsWindow.Maintenance.cs:221`（`_isRestoring` 单入口守卫）；`Services/DeskBoxDataBackupService.cs:1628`（`ConfirmPendingFullRestoreAsync` 增 `expectedArchivePath`/`expectedPreparedAtUtc` 身份比对）+ `:3812`（`DeskBoxRestorePreparation` 携带身份） |
| R20-S1-02 | P2 | 已修 | `Services/DeskBoxDataBackupService.cs:1482-1522`（逐文件 `<dest>.restore-tmp` → `File.Move(overwrite: true)`，全部成功后才删除快照中不存在的 live 文件） |
| R20-S8-01 | P2 | 已修 | `Services/SettingsService.cs:2821-2831`（`settings.Widgets ??= []` + `RemoveAll(widget => widget is null)`） |
| R20-S6-06 | P2 | 已修 | `Controls/WidgetContents/TodoWidgetContent.DetailNotesAndSteps.cs:17-21/274-285`（`_notesSaveGate.WaitAsync()` 改 10 秒上限，超时记日志并跳过本次保存） |
| R20-S5-02 | P2 | **撤回** | 既有契约测试 `WidgetShellContentHostTests.CompletePresenterFailure_RemainsRollbackCapable` 明确要求抛出 + 回滚；两处调用点均 catch 后 `Rollback()`，无永久卡死路径 |
| R20-S7-05 | P2 | 降 P3 | `ResilientJsonStore.cs:92/228-247/366` 已有「读失败先保留主文件再覆盖」+ 反序列化失败隔离；残余仅为可观测性 |
| R20-S7-06 | P2 | 降 P3 | 写盘失败有独立通道 `PersistenceFailed`（`SettingsService.cs:483/1216/1308/1332`）+ App 侧 5 分钟节流提示；`SettingsChanged` 语义是「内存已变，请刷新」 |
| R20-S4-01 | P2 | 降 P3 | `TodoWidgetViewModel.SaveAsync()` 已返回 `Task<bool>` 且失败触发 `SaveFailed` 横幅；命令返回 false 会与「内存权威、下次保存补齐」设计冲突 |

- 门禁：`DeskBox` 0 错误 / 20 警告（基线一致）；`DeskBox.Updater` 0 错误；定向测试 182/1（唯一失败为未跟踪文件基线）。
- 因修复同步上调的契约清单：`ModuleBoundaryContractTests` 破坏性文件操作 `DeskBoxDataBackupService.cs` 12 → 13；`SettingsSliceOwnershipContractTests` 门面直通 `SettingsService.cs` 623 → 625。
### R20 整改批·第三批（2026-10-27，2 条已修 + 2 条复核降级/撤回）

| 编号 | 原级别 | 处置 | 位点与修法 |
| --- | --- | --- | --- |
| R20-S1-01 | P2 | 已修 | `Services/AppUpdateService.cs`：`DownloadUpdateCoreAsync` 新增 `catch (OperationCanceledException)`，把 `OverallDownloadTimeout` 取消 linked token 产生的裸 OCE 归类为 `Network` 并清理 `.tmp`（原实现该异常不匹配任何过滤器，直接逃出方法，而调用方只有 try/finally） |
| R20-S1-04 | P2 | 已修 | `Services/WebDavBackupTransport.cs:169-186`：`ResolveUri` 逐段拒绝 `.`/`..` 后再转义（原实现 `new Uri(baseUri, escaped)` 会把点段归一化掉，保留清理的 Delete 可能落到配置目录之外） |
| R20-S1-03 | P2 | 降 P3 | `ApplyPendingRestoreAsync` 开头无条件调 `TryRecoverStrandedRollback()`（`:1284`，启动链 `App.xaml.cs:1235` 每次都走），门控 `HasUserDocuments` 在 `DataDirectory` 不存在时返回 false（`:2668-2671`）→ 恢复路径可达；「默认 settings.json 触发门控」不成立（该步骤早于设置加载） |
| R20-S8-06 | P2 | 撤回 | 声称的 `Cap` null → `WeatherCodeMapper` `Trim()` NRE 不存在：唯一 `Trim()`（`Helpers/WeatherCodeMapper.cs:138`）前有 `IsNullOrWhiteSpace` 守卫（`:132-135`），`MsnDescriptionOrIconToWmoCode`（`:208`）回落到 icon 映射 |

- 门禁：`DeskBox` 0 错误 / 20 警告；定向（Update/WebDav/DataBackup/CloudBackup）**326 通过 / 0 失败**；全量 **6204 / 11 / 1 / 6216**，与基线逐条一致。
## R20 整改批（2026-10-27，第三批：6 条已修 + 2 条复核撤回）

| ID | 级别 | 处置 | 位点与要点 |
|---|---|---|---|
| `R20-S1-01` | P2 → 已修 | 下载总超时归类网络失败并清理临时文件 | `src/DeskBox/Services/AppUpdateService.cs` `DownloadUpdateCoreAsync` 新增 `catch (OperationCanceledException)`：`OverallDownloadTimeout` 取消的是 linked token，`ReadAsync`/`WriteAsync` 抛裸 OCE 不匹配 `when (cancellationToken.IsCancellationRequested)`，逃出方法后调用方只有 `try/finally` → 静默失败 + `.tmp` 残留。 | 已修
| `R20-S1-04` | P2 → 已修 | WebDAV 路径先逐段拒绝 `.`/`..` 再转义 | `src/DeskBox/Services/WebDavBackupTransport.cs:169-186`；原实现 `new Uri(base, escaped)` 会归一化点段，保留清理的 `Delete` 可落到配置目录之外。 | 已修
| `R20-S8-02` | P2 → 已修 | 历史记录元素级 null 过滤 | `src/DeskBox/Services/DesktopOrganizationHistoryStore.cs:173-186`：`entry.Items` 在 `foreach` 前 `RemoveAll(item => item is null)`（`Normalize` 在 resilient-store 回调之外，null 元素会逃出隔离协议并让设置整体回默认）。 | 已修
| `R20-S8-03` | P2 → 已修 | 恢复日志在反序列化回调内归一 | `src/DeskBox/Services/DesktopOrganizationRecoveryStore.cs`：新增 `NormalizeJournal`，`Items`/`CreatedWidgetIds` 为 null 时 `?? []` 并滤 null 元素。 | 已修
| `R20-S2-01` | P2 → 已修 | 交互深度清零时补发 `InteractionActiveChanged` | `src/DeskBox/Services/WidgetSessionManager.cs` `MarkDesktopResting`/`MarkHidden`：原实现清零不通知，泄漏看门狗（`WidgetManager.ZOrder.cs:509-518`）永不停止、`Tick` 因 `IsInteractionActive == false` 每轮直接 return。**机理更正**：报告所称「DisplayTopologyGate 的 UserInteraction 永不复位」不成立（该事件唯一订阅者是看门狗）。 | 已修
| `R20-S3-02` | P2 → 已修 | 信息对话框统一走协调器 | `src/DeskBox/Views/SettingsWindow.DataTools.cs` `ShowInfoDialogAsync` 改为 `ShowDialogWhenFreeAsync(() => …)`（复用 `SettingsWindow.DialogCoordination.cs:18-64` 的 XamlRoot 判空 + 重试 + `InvalidOperationException`/`COMException 0x80000019` 兜底），覆盖 30+ 个异步续延调用点。 | 已修
| `R20-S1-03` | P2 → P3（撤回修复） | 恢复残留目录门控可达性成立 | `ApplyPendingRestoreAsync` 开头**无条件**调 `TryRecoverStrandedRollback()`（`DeskBoxDataBackupService.cs:1284`），启动链每次经 `RunOptionalStartupStepAsync("apply-pending-restore")`；`HasUserDocuments` 在目录不存在时直接 false → 门控不阻断。 |
| `R20-S8-06` | P2 → 撤回（伪缺陷） | 天气 `Cap` null 不导致 NRE | `src/DeskBox/Helpers/WeatherCodeMapper.cs` 唯一 `Trim()` 在 `:138`，前置 `:132-135` 已 `if (string.IsNullOrWhiteSpace(description)) return -1;` → 只降级不抛。 |

**门禁**：`DeskBox` 构建 0 错误 / 20 既有警告；全量测试 **6204 通过 / 11 失败 / 1 跳过 / 6216**，11 条失败与 R18/R19 基线逐条一致（沙箱 `%LOCALAPPDATA%`/`HKCU` + 未跟踪文件 `ScreenshotAnnotationRasterizer.cs`）。定向（Update/WebDav/DataBackup/CloudBackup）326 通过 0 失败；`FeatureWidgetsSettingsCoordinatorTests` 并行下单条 flaky，单跑 6/6 通过。
### R20 整改批（第五批，2026-10-27，2 条已修 + 1 条记录不修）

| 编号 | 级别 | 处置 | 说明 |
|---|---|---|---|
| `R20-V5-01` | P2 | 已修 | `WidgetManager.RemoveWidgetAsync` → `Task<bool>`，`ContentWidgetWindow.Commands.cs:1007-1030` 失败时弹 `Widget.DeleteWidgetFailed` 反馈并复位 pending；修复"删除静默失败、重启复活" | 已修
| `R20-V5-03` | P2 | 已修 | `CloudBackupService` 新增 `OwnedDeviceSuffix`（身份缺失为 null）+ `IsOwnedSnapshotName` 用其判定；身份缺失时不再认领 `nodevice` 快照，避免多台设备互删备份 | 已修
| `R20-V5-02` | P2 | 记录不修 | 换机后旧设备快照永不删除导致云端增长；先实现的"180 天清理"被契约测试否决，判定删除对端唯一备份风险更高，保留"仅删本机快照"并在 `ApplyRetentionAsync` 注释说明取舍 |

### round-21（Round 21 全量循环审查，2026-10-27）

- 审查方式：10 路 SubAgent 并行纯静态审查（不运行应用、不构建、不改源码）；S1–S8 报告已到，S9（原生层与 Platform）/S10（构建发布与本地化）待补。
- 立案 82 条：**P0 0 / P1 1 / P2 21 / P3 60**；另记已排除候选 90+、观察项若干（见各段报告 §4/§5）。
- 总报告：`docs/quality/rounds/round-21/ROUND-21-总报告.md`
- 本轮汇总期间已修 3 条：`R21-S4-01`（P1，多选块拖拽顺序，抽出 `ApplyBlockReorder` + 2 个回归测试，`FileItemMultiDragTests` 106 通过）、`R21-S8-02`（`quick-capture.json`/glance preferences 版本号 `Math.Max` 保序）、`R21-S3-02`（恢复默认设置改 `Task<bool>` + 失败弹窗）。
- 结构性主题：C-1″ 取消语义分层未贯通、C-3 `Handled = true` 吞异常、C-4「成功」语义被下层污染、C-5 记账漏项、C-6 只读不变量依赖「绑定成功」。

| ID | 级别 | 位点 | 一句话结论 | 状态 |
| --- | --- | --- | --- | --- |
| `R21-S1-01` | P2 | (见报告) | **取消跨卷目录移动时把未完成的移动当作"已完成结果"上报，3 个消费者不复核 → UI 计数、撤销收据、列表移除三处状态错误** | 待修 |
| `R21-S1-02` | P3 | `src/DeskBox/Services/FileService.cs:1841-1859`（`completedPaths` 填充在 `1841-1848`，抛异常在 `1856-1859`） | **`DeleteEntriesWithShell` 部分成功后抛 `Win32Exception`，已完成删除的路径集合随异常丢失** | 待修 |
| `R21-S1-03` | P3 | `src/DeskBox/Services/DeskBoxDataBackupService.cs:545`、`:643` | **`PrepareRestoreAsync` / `PrepareScopedRestoreAsync` 在校验新归档之前就删除已排队的恢复（R20-S1-06 未修，原位复核）** | 待修 |
| `R21-S1-04` | P3 | (见报告) | **`ArchiveZipService.WriteZip` 缺少"排除自身归档"逻辑，与 `WriteTar` 不对称（R20-S1-09 未修）** | 待修 |
| `R21-S1-05` | P3 | `src/DeskBox/Services/ArchiveZipService.cs:227-270`（`ExtractAllAsync`）、`:325-370`（`ExtractEntry`） | **`ArchiveZipService.ExtractAllAsync` 无条目数/总字节上限，且失败时残留部分解压目录** | 待修 |
| `R21-S1-06` | P3 | `src/DeskBox.Updater/Program.cs:695-730`，关键行 `:721-726` | **更新器 `UpdateOptions.Parse` 把任何未知 token 当作"带值开关"，会吞掉紧随其后的已知开关** | 待修 |
| `R21-S1-07` | P3 | `src/DeskBox/Services/AppUpdateService.cs:685-730` | **`PrepareDetachedUpdaterHelper` 先递归删除所有旧 helper 目录，再复制新 helper；复制失败即本次更新无法安装** | 待修 |
| `R21-S2-01` | P2 | (见报告) | **标题**：`PersistCompletedWidgetResize` 抛出后缩放清理链整体跳过，`_compactInteractionDepth` 永不清零 → Smart 折叠对该窗口永久失效 | 待修 |
| `R21-S2-02` | P2 | (见报告) | **标题**：`ResolveCompactWorkArea` 的 `DisplayArea.GetFromPoint` 无异常兜底，是折叠/展开/缩放全部热路径的唯一 choke point | 待修 |
| `R21-S2-03` | P2 | (见报告) | **标题**：`RenameWidgetCoreAsync` 磁盘改名成功、settings 落盘失败时只记日志 → 下次启动 widget 指向不存在的文件夹 | 待修 |
| `R21-S2-04` | P2 | (见报告) | **标题**：胶囊排列在退化工作区（0 尺寸）下 `arranged[id]` 抛 `KeyNotFoundException`，且 `ApplyCapsuleArrangementIfChanged` 无 catch | 待修 |
| `R21-S2-05` | P3 | (见报告) | **标题**：`WaitForCompactExpansionWarmupGateAsync` 的 `SemaphoreSlim` 取消竞态可泄漏**进程级静态闸**许可，导致全进程胶囊预热退化 | 待修 |
| `R21-S2-06` | P3 | (见报告) | **标题**：`UiDispatch.RunOrDefer(Action)` 被传入 `async` lambda，形成无 try/catch 的 `async void` 派发（2 处），破坏「延迟动作按序重放」契约 | 待修 |
| `R21-S2-07` | P3 | (见报告) | **标题**：`RemoveWidget(string)` 丢弃 `RemoveWidgetAsync` 的 Task，删除失败（含不可逆的回收站/移出桌面动作）完全静默 | 待修 |
| `R21-S2-08` | P3 | (见报告) | **标题**：`GetOrphanManagedStorageFolders` 的目录枚举无 try/catch，卷掉线时异常直抛设置页 | 待修 |
| `R21-S2-09` | P3 | (见报告) | **标题**：`WidgetLayoutStore.SaveAsync` 仍是 `public` 且不检查 `CanWrite`（**R20-S2-09 未修，残留**） | 待修 |
| `R21-S2-10` | P3 | (见报告) | **标题**：`RepairLegacyContentFeatureFileShells` / `DeduplicateFeatureWidgets` 在组闸外改 `Settings.Widgets` / `DeletedWidgetIds`，且不删 per-widget … | 待修 |
| `R21-S2-11` | P3 | (见报告) | **标题**：`HideAndCloseFeatureWidgetAsync` 名 Async 实 void；`CloseFeatureWidgetInstance` 丢弃 Task + 空 catch（**R20-S2-11 / -12 未修，残留**） | 待修 |
| `R21-S2-12` | P3 | (见报告) | **标题**：`NormalizeWidgetBounds` 的 `DisplayArea.GetFromRect` 无 try/catch，且被 `CreateContentWidgetFromConfigAsync` 在 try 块之外调用 | 待修 |
| `R21-S2-13` | P3 | (见报告) | **标题**：`PinAllWidgetSurfacesToScreenAsync` 直接遍历含墓碑的 `Settings.Widgets` / `Settings.WidgetGroups`，给已删除 widget 写屏幕绑定 | 待修 |
| `R21-S2-14` | P3 | (见报告) | **标题**：`ExecuteTrayVisibilityOperationAsync` 的 `_trayVisibilityOperationGate.WaitAsync()` 无 CancellationToken、无超时 | 待修 |
| `R21-S2-15` | P3 | (见报告) | **标题**：`TryReorderCapsuleBarMember` 重排后不刷新 `session.MemberSlots`，槽位几何与 `MemberOrder` 索引脱钩 | 待修 |
| `R21-S2-16` | P3 | (见报告) | **标题**：`FindConfig` 不过滤 `IsDeleted`，被 40+ 处调用，构成墓碑泄漏的长期陷阱 | 待修 |
| `R21-S3-01` | P2 | (见报告) | **标题**：引导页功能开关的持久化链可被永久毒化，"完成"与"跳过"双双永久失效 | 待修 |
| `R21-S3-02` | P2 | (见报告) | **标题**："恢复默认设置"失败后静默无反馈，内存与磁盘分叉，下次启动悄悄回滚 | 已修 |
| `R21-S3-03` | P2 | (见报告) | **标题**：确认类/破坏性对话框绕过 `ShowDialogWhenFreeAsync`，`XamlRoot` 被占用时按钮表现为"点了没反应" | 待修 |
| `R21-S3-04` | P3 | `src/DeskBox/Views/SearchPopupWindow.xaml.cs:4011` | **标题**：仅修改大小写的重命名被静默跳过 | 待修 |
| `R21-S3-05` | P3 | `src/DeskBox/Views/SearchPopupWindow.xaml.cs:1344-1351` | **标题**：保存的弹窗边界只要与工作区"有交集"就原样应用，可开出几乎不可见的窗口 | 待修 |
| `R21-S3-06` | P3 | (见报告) | **标题**：6 处 `DispatcherQueue.TryEnqueue(async () => …)` 的 async-void lambda 无 try，异常直达全局吞异常，其中右键拖放落地流程会停在中途 | 待修 |
| `R21-S3-07` | P3 | `src/DeskBox/Views/ScreenshotCaptureWindow.xaml.cs:1826-1832` | **标题**：截图导出在 `Close()` 之后仍由 `finally` 操作已关闭窗口的控件 | 待修 |
| `R21-S3-08` | P3 | (见报告) | **标题**：协调器 3 秒放弃 + 设置窗"隐藏而非关闭"策略，使更新安装确认可能永远等不到空闲 `XamlRoot` | 待修 |
| `R21-S3-09` | P3 | (见报告) | **标题**：虚拟拖放临时目录的递归删除有 7 处在 UI 线程同步执行（其中 2 处经 `ContinueWith` 默认调度器绕回 UI 线程），与同文件自己写明的"递归删除必须离开 dispatcher turn"契约冲突 | 待修 |
| `R21-S4-01` | P1 | (见报告) | **标题**：多选块移动在"向前拖动"时把块打散并落到错误位置，且结果被持久化 | 已修 |
| `R21-S4-02` | P2 | (见报告) | **标题**：开始一次拖拽（即使随即取消）即永久把控件切到"手动排序"并落盘 | 待修 |
| `R21-S4-03` | P3 | (见报告) | **标题**：拖入回收站的删除路径不刷新手动顺序快照，且批量失败只报告第一个路径 | 待修 |
| `R21-S4-04` | P3 | (见报告) | **标题**：`CloneTodoItem` 漏克隆 `TodoItem.DeviceId`，删除+撤销会把所有条目的设备来源抹成"本机" | 待修 |
| `R21-S4-05` | P3 | (见报告) | **标题**：`SettingsViewModel.Dispose()` 不退订 App 级单例的两个更新事件，每次开设置窗口泄漏一个 VM | 待修 |
| `R21-S4-06` | P3 | (见报告) | **标题**：`SortItems` 仍是 O(n²)（每元素一次 `Items.IndexOf`），同一根因在栈投影里还有一处 | 待修 |
| `R21-S4-07` | P3 | (见报告) | **标题**：剪贴板重试循环末尾的 `throw lastException` 不可达，`lastException` 是只写死变量，最终失败无日志 | 待修 |
| `R21-S4-08` | P3 | (见报告) | **标题**：`SetSortMode` 用 `Items.Clear()` + 重填切换排序，销毁 ListView 选中与滚动位置，且与同文件明确声明的设计原则相反 | 待修 |
| `R21-S4-09` | P3 | (见报告) | **标题**：删除附件时忽略 `SaveAsync()` 的失败结果，仍删除受管文件 → 元数据与磁盘不一致 | 待修 |
| `R21-S4-10` | P3 | (见报告) | **标题**：到期日预设的"本周/下周一"日期数学被复制三份，任一处修正都需同步三处 | 待修 |
| `R21-S4-11` | P3 | (见报告) | **标题**：`TodoItemViewModel` 构造函数把 `Recurrence` 实例同时交给模型与 VM，与自身注释和 setter 行为矛盾，store 归一化后二者静默分叉 | 待修 |
| `R21-S4-12` | P3 | (见报告) | **标题**：音乐进度计时器在首次 SMTC 时间轴同步前用 `default(DateTimeOffset)` 外推，进度条直接跳到满格 | 待修 |
| `R21-S4-13` | P3 | (见报告) | **标题**：`MusicAccentBrush` / `PlayPauseButtonBackgroundBrush` 每次读属性都新建 `SolidColorBrush` | 待修 |
| `R21-S4-14` | P3 | (见报告) | **标题**：系统音量设置被拒绝后 UI 保留未生效的值，无回滚 | 待修 |
| `R21-S4-15` | P3 | (见报告) | **标题**：`SettingsViewModel` 构造函数发起两个无人观察的异步任务，异常将完全静默 | 待修 |
| `R21-S4-16` | P3 | (见报告) | **标题**：Todo 的筛选计数与"剩余 N 项"绕过 `LocalizationService.Format`，与其它计数文案不一致 | 待修 |
| `R21-S5-01` | P2 | (见报告) | **`async` lambda 直接交给 `DispatcherQueue.TryEnqueue`，等价 `async void` 且无任何兜底** | 待修 |
| `R21-S5-02` | P3 | (见报告) | **`_suppressNextTitleBarClickCollapse` 是一次性抑制标志，在"双击第二击后转为拖拽 / 指针捕获丢失"路径上不会被清除，导致下一次合法的标题栏单击被吞掉** | 待修 |
| `R21-S5-03` | P3 | (见报告) | **`SetOverlayChromeVisible(bool isVisible, bool animateButtons = true)` 的 `animateButtons` 参数在默认值 `true` 下完全无效，6 个调用点里 5 个都在传默认值** | 待修 |
| `R21-S5-04` | P3 | (见报告) | **`WidgetShellContentHost` 的内容生命周期回调在"未激活窗口"与"过渡中 outgoing 内容"两个分支上不配对** | 待修 |
| `R21-S5-05` | P3 | (见报告) | **`WidgetShell.xaml.cs` 有两处裸 `catch` 直接吞掉异常并返回 `false`，无日志、无注释，且都落在标题栏交互路径上** | 待修 |
| `R21-S5-06` | P3 | (见报告) | **`CompleteNativeFileDropFallbackAsync` 只捕获 `OperationCanceledException`，其余异常从被丢弃的 `Task` 逃逸为未观察任务异常** | 待修 |
| `R21-S5-07` | P3 | (见报告) | **`Localized.PruneDeadTargets()` 全仓零调用（死 API），而 `UntrackTree` 只有 1 个调用点——弱引用台账的显式回收契约只被一个窗口遵守** | 待修 |
| `R21-S5-08` | P3 | (见报告) | **"瞬时 UI 计时器"的性能台账契约没有收口设施，43 处 `CreateTimer()` 只有 10 处记账，且不记账的位点里有两个连 `Tick` 都不解绑** | 待修 |
| `R21-S6-01` | P2 | (见报告) | **QuickCapture 明细保存闸门仍是无界等待，R20-S6-06 只修了 Todo 一半** | 待修 |
| `R21-S6-02` | P2 | (见报告) | **Todo 笔记闸门超时后「完成」按钮完全无响应（R20-S6-06 修复引入的新分支）** | 待修 |
| `R21-S6-03` | P2 | (见报告) | **弹层关闭按钮的交互租约在 `ReleaseStackPopover` 路径不归还（R20-S6-10 半修）** | 待修 |
| `R21-S6-04` | P3 | (见报告) | **`SelectStackMembers` 在弹层已打开时静默失效（R20-S6-03 同族）** | 待修 |
| `R21-S6-05` | P3 | (见报告) | **撤销按钮丢弃 `UndoLastActionAsync` 返回值，无条件提示「已撤销」** | 待修 |
| `R21-S6-06` | P3 | (见报告) | **DimDiag 临时诊断探针留在生产热路径** | 已修（Round 02 删除；审计见 `doc/audit-20261029-093210/round-02-总报告.md`） |
| `R21-S6-07` | P3 | (见报告) | **`CreateMultiItemFlyout` 的 `anchor` 参数是死参数** | 待修 |
| `R21-S6-08` | P3 | (见报告) | **`PickCustomDueDateAsync` 是假异步，且缩进错误** | 待修 |
| `R21-S6-09` | P3 | (见报告) | **`StackSurface_DragOver` 在校验 sender 之前就 `e.Handled = true`** | 待修 |
| `R21-S6-10` | P3 | (见报告) | **`TodoTab_Drop` 缺 catch，与同文件其它 Drop 处理器不一致** | 待修 |
| `R21-S6-11` | P3 | (见报告) | **Glance / Weather 订阅 `ViewModel.PropertyChanged` 从不退订（R20-S6-13 仍在）** | 待修 |
| `R21-S7-01` | P2 | (见报告) | **`Migration_11_To_12` 未守卫 `settings.Widgets`，`null` 值使迁移链永久停在 v11** | 待修 |
| `R21-S7-02` | P2 | (见报告) | **`Migration_9_To_10` 的"durable checkpoint"承诺不成立：`WaitForPendingPersist()` 在写入失败时也正常返回，旧音乐字段永久丢失** | 待修 |
| `R21-S7-03` | P2 | (见报告) | **`settings.json` / `widget-layout.json` 的临时文件从不 `flushToDisk`，掉电后可能得到零长度或截断的正式文件** | 待修 |
| `R21-S7-04` | P2 | (见报告) | **`ResilientJsonStore.s_unreadablePrimaries` 只在"保存时"清除，一次瞬时读失败会让该存储**永久**失去 `.bak` 冗余** | 待修 |
| `R21-S7-05` | P2 | (见报告) | **`QuickCaptureSettingsCoordinator` 的 `Changed`/`DiagnosticsChanged` 裸广播：一个订阅者抛异常会截断其后所有订阅者，并把异常抛回设置写入方** | 待修 |
| `R21-S7-06` | P2 | (见报告) | **Jump List 的 AUMID 只写到 `{userprograms}` 快捷方式；机器级安装下 Jump List 永不出现，且日志显示"成功"** | 待修 |
| `R21-S7-07` | P3 | (见报告) | **`TodoWidgetStore.SanitizeWidgetId` 放过 `..`，且空结果回退到随机 Guid 会让删除目标与写入目标错位** | 待修 |
| `R21-S7-08` | P3 | (见报告) | **`QuickCaptureStore.Normalize` 无条件把 `Version` 压回 4，高版本文档被静默降级覆盖** | 待修 |
| `R21-S7-09` | P3 | (见报告) | **`WeatherCacheStore.Normalize` 丢弃高版本缓存后，`UpdateAsync` 立即以 v1 覆盖** | 待修 |
| `R21-S7-10` | P3 | (见报告) | **Jump List 的"切换 Widget"入口绕过 `ToggleWidgetsFromTrayAsync` 的串行状态机：TOCTOU + 无 parity 折叠** | 待修 |
| `R21-S7-11` | P3 | (见报告) | **`QuickCaptureSettingsCoordinator.StopCoreAsync` 的 wait-then-release 屏障后立即 `Dispose()`，排队中的 waiter 会在已释放的信号量上 `Release()`** | 待修 |
| `R21-S7-12` | P3 | (见报告) | **`JumpListService.ConfigureAsync` 先 `Clear()` 再逐项 `AddItem`（异常被逐项吞掉），全部失败时仍 `SaveAsync()` 一个空 Jump List** | 待修 |
| `R21-S8-01` | P2 | (见报告) | **标题**：新版本 `settings.json` 反序列化失败时，「新版本只读」不变量不可达，旧构建以默认值覆盖更新版本的 profile | 待修 |
| `R21-S8-02` | P2 | (见报告) | **标题**：`quick-capture.json` 与 glance preferences 无条件把 `Version` 回写为当前值，降级后静默丢弃更新版本的字段 | 已修 |
| `R21-S8-03` | P3 | (见报告) | **标题**：`shortcut-native-abi-v2.md` 的「当前」能力掩码与导出清单仍停在阶段 3C 早期，与代码/头文件不一致 | 待修 |

### round-21 追加（S9 原生层与 Platform、S10 构建发布与本地化，2026-10-27）

- S9 立案 5 条（P2 1 / P3 2 / P4 2）：Rust 层无可触发内存安全或 ABI 错位缺陷，全部落在托管↔原生壳边界。
- S10 立案 8 条（P2 1 / P3 7）：四条硬契约（离线自足、清单安全、锁一致性、13 语言一致）复核通过，问题集中在发布门禁机制缺口。
- Round 21 合计 **95 条：P0 0 / P1 1 / P2 23 / P3 69 / P4 2**。

| ID | 级别 | 位点 | 一句话结论 | 状态 |
| --- | --- | --- | --- | --- |
| `R21-S9-01` | P2 | `src/DeskBox/Helpers/NativeDropTarget.cs:2213-2225`（`ReadVirtualFileDescriptors` 的容量夹紧）；越界读发生在 `:2234-2238`。 | **触发**：拖放源提供 `FileGroupDescriptorW`（`CFSTR_FILEDESCRIPTORW`）时用 **0 字节 HGLOBAL**：`GlobalSize`（`OleDropTargetNativeMethods.cs` 的 `GlobalSiz… | 待修 |
| `R21-S9-02` | P3 | `src/DeskBox/Helpers/NativeDropTarget.cs:856`（`Marshal.AddRef`）↔ `:894`（`finally` 中的 `Marshal.Release`）；丢弃点在 `src/DeskBox/Platform/UiDisp… | **触发**：shell 命名空间 / AppsFolder 拖放（`shellApplicationDrop \|\| shellNamespaceDrop`）走 `:844-912` 的延迟分支。`:856` 先 `AddRef`，然后把"提取 → `WritePerf… | 待修 |
| `R21-S9-03` | P3 | `src/DeskBox/Platform/Win32Helper.cs:3445-3455`，拼串在 `:3447`：`"/select,\"{path}\""`。 | **触发**：`path` 以目录分隔符结尾。可达路径之一：用户把备份目录配成 `D:\DeskBoxBackup\`，`src/DeskBox/Views/SettingsWindow.Maintenance.cs:424-426` 把它原样交给 `ShowInExplo… | 待修 |
| `R21-S9-04` | P4 | `src/DeskBox/Platform/ShellUnelevatedLaunch.cs:325-333`。 | **触发**：第一次探测调用的返回值被丢弃： | 待修 |
| `R21-S9-05` | P4 | `docs/architecture/shortcut-native-abi-v2.md:11-13`。 | **触发**：文档写"当前完整模块能力掩码为 63"，而实际模块是**能力 511、十个导出**：`native/README.md:7-8`、`:21-22`；`native/deskbox-native/src/lib.rs:1092-1416` 的十个导出；`docs… | 待修 |
| `R21-S10-01` | P2 | (见报告) | **位置** | 待修 |
| `R21-S10-02` | P3 | (见报告) | **位置** | 待修 |
| `R21-S10-03` | P3 | (见报告) | **位置** | 待修 |
| `R21-S10-04` | P3 | (见报告) | **位置** | 待修 |
| `R21-S10-05` | P3 | (见报告) | **位置** | 待修 |
| `R21-S10-06` | P3 | (见报告) | **位置** | 待修 |
| `R21-S10-07` | P3 | (见报告) | **位置** | 待修 |
| `R21-S10-08` | P3 | (见报告) | **位置** | 待修 |

### round-21 修复批 R21-B（2026-10-27，累计 15 条已修 + 3 条契约测试修复）

| 编号 | 级别 | 状态 | 落点 |
| --- | --- | --- | --- |
| R21-S4-01 | P2 | 已修 | `WidgetViewModel.SortingAndWatchers.cs` 抽出 `internal static ApplyBlockReorder(...)`，多选块拖拽保持连续；新增 2 条单测（`FileItemMultiDragTests` 106/106） |
| R21-S8-02 | P2 | 已修 | QuickCapture/Glance 版本号与计数守卫改为 `Math.Max(...)` 单次求值 |
| R21-S3-02 | P2 | 已修 | `RestoreDefaultPreferencesAsync` → `Task<bool>`；视图侧 try/catch + `SaveFailed` 提示 |
| R21-S7-01 | P2 | 已修 | `SettingsMigrationService.cs:397` 补 `settings.Widgets` 空守卫（v11 `"widgets": null` 曾把迁移永久钉在 v11） |
| R21-S7-02 | P3 | 已修 | `MusicSettingsStore` 增 `_persistFailed` + `WaitForPendingPersist()`；迁移在写失败时保留 v9 检查点 |
| R21-S7-03 | P3 | 已修 | `SettingsService.cs:1425/1453` `Flush(flushToDisk: true)` |
| R21-S1-02 | P2 | 已修 | `DeskBoxDataBackupService` 新增 `DeletePreviousPendingRestore`，不再误删新 payload |
| R21-S9-01 | P2 | 已修 | `NativeDropTarget.cs:2213` 0 字节 HGLOBAL 时也夹紧声明计数，消除最多 2.42 MB 越界读 |
| R21-S10-01 | P2 | 已修 | `publish-aot-retail.ps1` restore 加 `-p:RestoreLockedMode=true` |
| R21-S2-02 | P2 | 已修 | （前批）组拖拽计时器与交互泄漏看门狗 |
| R21-S5-01 | P3 | 已修 | （前批）宿主 `TryEnqueue(async …)` 无 try 的包装 |
| R21-S8-01 | P3 | 待裁决 | 方案 §2.1 A′（`Utf8JsonReader` 只读 `schemaVersion`） |
| R21-S1-01 / S4-02 / S3-01 / S2-01 | P2 | 未落地 | 见 `R21-remediation-plan.md` §7.4 |

**契约测试修复（改代码不改基线）**：抽出 `TryReadPendingRestoreMarker()` 让 `JsonSerializer` 调用点回到 12/3；`SettingsMigrationService` 切片访问回到 58（`FacadePassthroughAccess_OnlyShrinks` 恢复绿）。

**本轮门禁基线**：全量 **失败 11 / 通过 6206 / 跳过 0 / 总计 6218**，11 条失败与 R19/R20 基线逐条同源（沙箱 ACL 8、未跟踪文件基线漂移 1、受限令牌缩略图 3→实为 3 条中 2 条归此类、HKCU 1）。
### round-21 修复批 R21-C（2026-10-27，B1/B2 剩余四条）

| 编号 | 级别 | 状态 | 落点与要点 |
| --- | --- | --- | --- |
| R21-S1-01 | P2 | 已修 | `FileSurfaceContent.xaml.cs` 取消分支 + `WidgetViewModel.Operations.cs:542` 均改为只认 `FileService.IsCompletedShellMove` 为真的条目；不改 `FileTransferResult` 契约 |
| R21-S4-02 | P2 | 已修 | `FileSurfaceContent` 记住拖拽前排序模式，手势结束且未移动任何条目时恢复；取消拖拽不再永久关闭自动排序 |
| R21-S3-01 | P2 | 已修 | `OnboardingWindow.TaskFlow.cs` 兜底 `SaveAsync` 独立 try/catch，任务链不再 faulted |
| R21-S2-01 | P2 | 已修（修法二次修正） | `CommitInteractiveResizeBounds` 内部 catch 持久化异常并记日志；首版 `try/finally` 形状被 `Windows10WidgetMotionContractTests` 拦下，保持调用点文本不变 |

**契约测试教训**：`Windows10WidgetMotionContractTests` 按源码文本断言缩进与出现次数，任何改变调用点形状的重构都会红；同类修法应优先改「被调方内部行为」而非「调用点结构」。

**门禁**：构建 0 错误 / 20 警告；全量 **11 失败 / 6206 通过 / 0 跳过 / 6218**（与基线逐条同源）。累计 R21 已修 19 条；仅剩 `R21-S8-01` 待裁决。
### round-21 修复批 R21-D（2026-10-27）

| 编号 | 级别 | 状态 | 落点与要点 |
| --- | --- | --- | --- |
| R21-S8-01 | P2 | 已修（A′） | `SettingsService.TryReadRawSchemaVersion` 在加载器**之前**读原始 `schemaVersion`；类型绑定失败时 `_loadedDiskSchemaVersion` 不再塌陷为 0，「新版本 profile 只读」不变量恢复可达；新增回归测试 `LoadAsync_NewerSchemaThatFailsToDeserializeStillStaysReadOnly` |

**R21 累计已修 20 条**；方案 §1 内条目全部落地。门禁：构建 0 错误 / 20 警告；全量 11 失败 / 6207 通过 / 6219（11 条为环境基线）。
### round-21 修复批 R21-E（2026-10-27，复审驱动 13 条）

复审来源：`docs/quality/rounds/round-21/verification/V6..V10-*.md`。判定分布：真修好 6 条（S10-01/S7-02/S7-03/S4-01/S3-02/S8-02）、部分修好 6 条（S1-01/S1-02/S7-01/S8-01/S9-01/S4-02/S3-01）、误修未落地 2 条（S5-01/S3-06）、台账虚标 1 条（S2-02）。

| 条目 | 处置 | 落点 |
| --- | --- | --- |
| V8-01/V8-02/V8-05/V8-04 | 已修 | 整叠分支去掉多余 `SetSortMode(Manual)`；`CommitSurfaceReorder` 用 try/finally；新增 `RestoreSortModeAfterCancelledReorder`（含降序）；`SetEnabled` 移入内层 try |
| V7-01/V7-03 | 已修 | `MemberIds`/`Surfaces` 显式 null 归一；加载异常只在旧版本时清零 `_loadedDiskSchemaVersion` |
| V6-02/V6-03/V6-04/V6-05 | 已修 | 撤销收据只认落盘条目；count 读取在尺寸守卫之后；marker 先写后删 + `DeletePreviousPendingRestore(keep, previous)`；新增 `IsCompletedShellTransfer` |
| V9-01/V9-04 | 已修 | `MoveItemForReorder` 插入索引语义；`ApplyBlockReorder` 阶段 2 补 `currentIndex < 0` 守卫 |
| V9-02 | 已修 | 6 处 `TryEnqueue(async …)` 补 try/catch（右键拖放失败同时清挂起插入 + 临时文件） |
| V9-03 | 已修 | `ResolveCompactWorkArea` 三级回退（Nearest → Primary → 1920x1080），覆盖 7 个几何调用点 |
| V10-02 | 已修 | 两处注释字面 `async void` 改词，静态门禁 `async_void_count` 不再被注释污染 |
| R21-S2-02 | 台账更正 | 原标注的「看门狗」属 R18-B4；真实缺陷是 `WidgetWindowBase.Collapse.cs:3108` `GetFromPoint(...).WorkArea` 裸调用，**本批已按 V9-03 修复**，状态改「已修（V9-03）」 |
| R21-S5-01 / R21-S3-06 | 状态改「已修（V9-02）」 | 原标已修但 6 处调用点未落地，本批补齐 |
| R21-S4-02 / R21-S1-01 / R21-S1-02 / R21-S7-01 | 状态改「已修（复审后补强）」 | 见方案 §7.9 表 |
| V6-01 | 台账更正 | 备份条目真 ID 是 R21-S1-03；`FileService.cs:1856-1859` 的 P3 另立 `R21-S1-04`，状态保持「待修」 |
| V6-06 / V7-06 | 待补测试 | 四条修复与 `_persistFailed` 均无行为回归测试，列入 Round 22 整改批 |
| V7-02 | 待清理 | `_settingsSchemaNewerThanBuild` 仅日志使用，死字段 |
| V10-01 | 记录 | `MusicSettingsStore.cs:149` 新增同步等待使 `sync_wait_count` 193/193 零余量 |
| V10-03 | 环境 | 未跟踪 `src/DeskBox/Controls/ScreenshotAnnotationRasterizer.cs`（12 处 P/Invoke）使 `PlatformInterop_StaysInsideThePlatformDomain` 红，非 R21 引入 |

**门禁**：构建 0 错误 / 20 既有警告；定向 422/422（备份+整理+文件服务+拖放+VM+多选拖拽+窗口运动）、177/177（排序+引导+文件面+VM+运动契约）、208/208（设置+迁移+音乐设置+切片）。

**未决（需用户裁决）**：① `async_void_count` 258 vs 基线 257 差 1；② `sync_wait_count` 零余量；③ `packages.aot.lock.json` 仍记 2.4.0 而 csproj 为 2.5.1（零售发布 NU1004）。

### round-22（Round 22 全量循环审查，2026-10-27；按文件簇十段）

- 审查方式：10 路 SubAgent 并行纯静态审查（禁构建/禁运行/禁改源码/禁读 `docs/quality/**`），按**文件簇**分段，把 R18–R21 累计未逐行读的大文件一次吃透。S3/S6 首派失效（与 12:16 那批同样的静默死亡），15:11 重派后于 15:26/15:29 交付，**十段齐全**。
- 立案 **134 条**：P0 0 / P1 1 / P2 38 / P3 89 / P4 6（Lead 改判 `R22-S1-01` P2→P3 后为 P2 37 / P3 90）；合计逐行深读约 11.4 万行。
- 总报告：`docs/quality/rounds/round-22/ROUND-22-总报告.md`；Lead 复核：`docs/quality/rounds/round-22/LEAD-VERIFICATION.md`（8 条高价值条目 + 1 条结转项）。
- **本轮为审查轮，未产生任何源码改动**（最近一次代码改动是 R21 第五批，12:41–12:56）。
- 结构性主题：C-1 非交互路径的静默降级；C-2 状态标志缺少单一复位点；C-3 异常兜底依赖 `async void` 逃逸；C-4 顺序/排序双基准（`_stackOrder` 无单一权威）；C-5 契约与文档漂移。

| ID | 级别 | 位点 | 一句话结论 | 状态 |
|---|---|---|---|---|
| `R22-S7-01` | **P1** | `Services/WidgetLayerService.cs:955` | 标题点击抬升路径缺少 #553「前台已属本带即跳过」守卫（兄弟方法 `:899-905` 有），`TrySetForegroundWindow` 无非阻塞保护 → 可再次挂死 UI 线程 | 待修（Lead 维持 P1） |
| `R22-S1-01` | P2 → **P3** | `App.xaml.cs:4576-4582` | `OnLanguageChanged` 缺 `HasThreadAccess` 重投 → 托盘文字停留旧语言（显示层，Lead 改判） | 待修 |
| `R22-S1-02` | P2 | `App.Tray.cs:1516-1521` | `OpenManagedStorageFromTray` 无异常守卫，经两条 `async void` 逃逸后被 `OnUnhandledException` 吞掉 → 点了没反应 | 待修 |
| `R22-S1-03` | P2 | `App.xaml.cs:6967-6984` | 非 abort 的 `Bounded` 超时被计为成功 → `settings-flush` 超时仍 `TrySetResult(true)`，互斥体可能提前释放（不产生文件损坏） | 待修（Lead 复核） |
| `R22-S2-01` | P2 | `FileSurfaceContent.StackPopover.cs:454-497` | 「待显示栈键」永久滞留 → 该栈浮层再也打不开（切别的栈可恢复，不升 P1） | 待修（Lead 复核） |
| `R22-S2-02` | P2 | `FileSurfaceContent*.cs` | `_stackProjectionTransitionPending` 在异常路径上永久置位 → 拖放插入点永久失效 | 待修 |
| `R22-S2-03` | P2 | `FileSurfaceContent.ItemVisuals.cs:2388-2391` | 取得 deferral 后用未保护的 `Path.GetFullPath` → deferral 永不 Complete，OLE 事务悬挂 | 待修（Lead 更正影响描述） |
| `R22-S2-04` | P2 | `FileSurfaceContent*.cs` | `AbandonPendingBoxSelectionPress` 把浮层框选结算到主视图 | 待修 |
| `R22-S2-05` | P2 | `FileSurfaceContent*.cs` | 粘贴被取消时剪切态与「已移出」通知双双跳过 | 待修 |
| `R22-S2-06` | P2 | `FileSurfaceContent*.cs` | `TryEnqueue(async …)` 无异常兜底 | 待修 |
| `R22-S3-01` | P2（分支下 P1） | `Controls/WidgetShell.xaml.cs:4805-5159` vs `:4043-4050`/`:5841-5852` | `StoryboardSlot` 停止后是否回落基值，同一文件两套互斥假设（动画基值/动画值混用） | 待修 + 定级待裁决 |
| `R22-S4-01` | P2 | `Controls/ScreenshotAnnotationRasterizer.cs` | 导出光栅化在 UI 线程同步执行（4K 选区 ~100MB） | 待修 |
| `R22-S4-02` | P2 | 截图导出文本层 | GDI 文本超 8192px 静默丢弃 | 待修 |
| `R22-S5-01` | P2 | `Services/QuickCaptureService.cs:2087-2108` | 降低最近条目上限会不可逆丢弃超限记录及其图片 | 待修 |
| `R22-S5-02` | P2 | 便签缩略图缓存 | 缓存以文件名主干为键 → 跨条目串图 | 待修 |
| `R22-S5-03` | P2 | `Services/DeskBoxDataBackupService.cs:2494` | 恢复期整篇重写暂存 JSON：丢未知字段 + 枚举降级为数字（`CreateDataJsonOptions` 无 `UseStringEnumConverter`） | 待修 |
| `R22-S5-04` | P2 | 备份快照清单 | 枚举自定义目录下全部 `*.zip`，删除仅校验目录与后缀 → 可删用户自己的 zip | 待修 |
| `R22-S5-05` | P2 | scoped replace | 逐文件覆盖且失败不回滚已应用域 → 可留半恢复态 | 待修 |
| `R22-S5-06` | P2 | todo 重映射 | 在保存目标库之前就删除暂存源目录 | 待修 |
| `R22-S5-07` | P2 | 恢复替换主文件 | 不轮转 `.bak` → 回退源仍是恢复前内容 | 待修 |
| `R22-S6-01` | P2 | `Services/SettingsService.cs:2907` 等 6 处 | 顶层集合无守卫解引用，`"widgets": null` 触发 NRE → 被 `:1027` 捕获后整份设置回落默认（同构数据在 `WidgetLayoutStore` 有 `??=` 保护） | 待修 |
| `R22-S6-03` | P2 | `Services/MusicSettingsStore.cs` | 缓存与写链是实例级，而该 store 至少 3 处独立实例化 → 读-改-写互相覆盖 | 待修 |
| `R22-S6-05` | P2 | `Services/SettingsMigrationService.cs:830/843` | 9→10 检查点仅 `File.Exists` 判定；`WaitForPendingPersist()` 在 `SettingsService.cs:922` 持锁路径上同步阻塞 = UI 线程持锁等待；`MigrateAsync` 无调用方 | 待修 |
| `R22-S7-02` | P2 | `Services/WidgetManager.cs:2202/2204` | 删除路径忽略组摘除结果、`config is null` 时跳过组清理 → 可持久化悬空 memberId，下次启动 `Normalize` 丢弃 2 成员组 | 待修 |
| `R22-S7-03` | P2 | `WidgetManager*.cs` | 组摘除回滚快照不覆盖拓扑布局服务 | 待修 |
| `R22-S7-04` | P2 | `WidgetManager.Groups.cs:918/955` | 合并回滚用深克隆整表替换，破坏引用同一性（与摘除路径的引用保持式回滚语义相反） | 待修 |
| `R22-S7-05` | P2 | 屏幕绑定写入 | 含完全相同的 if/else 死分支；成员绑定原地改写不发通知 | 待修 |
| `R22-S8-01` | P2 | `Services/FileService.cs:1831-1841` | 无窗口句柄删除同时带 `FofSilent/FofNoErrorUi/FofNoConfirmation` → 回收站不可用时静默永久删除（Lead 取证：Rust 层不接管删除，零售包同样适用） | 待修 |
| `R22-S8-02` | P2 | `Services/FileService.cs:2135` | ≥260 字符路径 `PathExists` 把探测失败当可用，冲突消解返回已存在且不可访问的名字；`app.manifest` 无 `longPathAware` | 待修 |
| `R22-S8-03` | P2 | `ViewModels/WidgetViewModel.Operations.cs:999-1002` | 零句柄时「拖到回收站」静默返回 0 条删除，无失败无提示 | 待修 |
| `R22-S9-01` | P2 | `/select` 参数拼接 | 反斜杠结尾路径被 `CommandLineToArgvW` 误解 → 资源管理器定位静默失败 | 待修 |
| `R22-S9-02` | P2 | UNC 回退路径 | 同类引号缺陷 | 待修 |
| `R22-S9-03` | P2 | 进程属性列表 | `DeleteProcThreadAttributeList` 对未初始化缓冲区无条件调用 | 待修 |
| `R22-S10-01` | P2 | `ViewModels/WidgetViewModel.ItemHydration.cs:743-751` | 计数水合循环用 `return` 代替 `continue` → 一项消失即终止整代（Lead 已复核） | 待修 |
| `R22-S10-02` | P2 | `ViewModels/WidgetViewModel.Operations.cs:232-313` | 拖入批量异常后排序模式永久停留 Manual（恢复逻辑在 `try/finally` 之外） | 待修 |
| `R22-S10-03` | P2 | `ViewModels/WidgetViewModel.Stacks.cs:1551-1577` | `MoveStackUp/Down` 与 `CanMoveStackUp/Down` 顺序基准不一致 → 菜单可点但不动 | 待修 |
| `R22-S10-04` | P2 | `_stackOrder` | 只增不减，`Item:` 死键永久累积 | 待修 |
| `R22-S10-05` | P2 | `SetSortMode` | 不重置 `_stackOrder` | 待修 |
| `R22-S10-06` | P2 | `FileStackGroupByDateAdded` | 读写两侧被静默改写为 Kind | 待修 |

（P3 89 条、P4 6 条见各段报告 §2；本轮未逐条录入表格，R22 整改方案落地时按批次回填。）

**R21 三条未决项结项（本轮实测）**

| 项 | 结论 |
|---|---|
| `async_void_count` 258 vs 基线 257 | **已结项**：R21 第五批把 `GlanceWidgetSettingsSection.xaml.cs` 的 `OnUnloaded` 改回非 async（注释同步改词），`python scripts/quality/static_gate.py` **PASS** |
| `sync_wait_count` 193/193 零余量 | 维持记录：`MusicSettingsStore.cs:149` 的 `GetAwaiter().GetResult()` 为唯一增量，属已知债务 |
| `packages.aot.lock.json` 记 2.4.0 → 零售发布 NU1004（R-1） | **已不成立**：两份 AOT 锁现均为 `[2.5.1, )`/`2.5.1`，与 `DeskBox.csproj:181` 一致（见 `LEAD-VERIFICATION.md` 结转项复核） |
