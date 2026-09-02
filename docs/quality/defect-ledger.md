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
