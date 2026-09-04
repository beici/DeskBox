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
| **DEF-050** | 文件格子图标列数被统一槽宽舍入挤掉（用户报告：右侧出现大片空白，曾 4 列现在 3 列） | P2 | 合并 1.4.9 后实机 | FileSurfaceContent.TextScaling.cs ApplyUniformIconCellSize（上游 ItemsWrapGrid 固定 ItemWidth 机制） | 实机回归 | ✅ 已修复 |
| **DEF-050** | MSN 日期解析失败仍追加空行，UI 错位 | P3 | F9 | WeatherService.cs:498-504/559-566 | SA-B | 📌 挂账 |
| **DEF-051** | 远程 JSON 无大小上限（Glance/Weather 数处） | P3 | F9 | GlanceImageService.cs:614-620、WeatherService.cs:83 | SA-B | 📌 挂账 |
| **DEF-052** | FileMetaService LRU 软缺陷：失败 null 永久缓存不重试、在途不淘汰 | P3 | F9 | FileMetaService.cs:236-304 | SA-B | 📌 挂账 |
| **DEF-053** | DeskBox 内容刷新任务引用竞态（旧 finally 清新引用） | P3 | F9 | SearchEngineService.cs:485-505/553-559 | SA-B | 📌 挂账 |
| **DEF-054** | Saka/Bangla 历固定起点近似漂移 | P3 | F9 | GlanceTraditionalCalendarService.cs:225-258 | SA-B | 📌 挂账（标注近似或表驱动）|
| **DEF-055** | 窗口壳层 Closed 后 zombie 回调系统面：SettingsWindow ~20 处 async void 无 _isClosed 复检 + OnboardingWindow.xaml.cs:131 Closed 内同步清理（F7-B5/EVT-03 同族） | P3 | F9 | SettingsWindow.*.cs 全部 | SA-W3+主线证伪降级 | 📌 挂账 |
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
