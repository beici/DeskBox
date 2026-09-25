# R8-AB 优化修复方案（基于 A/B 双轮审查报告）

> 输入：A 轮 `docs/quality/rounds/round-08/全量代码缺陷审查总报告.md`（DEF-085~107，23 条）＋ B 轮 `docs/quality/rounds/round-08-fresh/从零重审总报告.md`（F 编号 61 条）＋ 《双轮比对验证报告.md》仲裁结论。
> 原则：最小侵入；不触碰 z-order 核心约定与 [重要勿删] 手册核心段（文档失同步仅做事实性勘误）；每条修复须可静态/回归验证；文件集互斥分批；每批后构建＋回归门禁。
> 处置分三类：**【修】**本批执行；**【延后】**明确理由与触发条件；**【不修】**说明依据（设计取舍/观察项）。
> 验证门：每波次后 `dotnet build`（x64）＋ `dotnet test`（x64，全量回归）；终门禁含停仓库实例→构建→回归→启动新实例核验。

---

## 一、处置总表

### A. P1（2 条，全部【修】，最高优先）

| 编号 | 修法要点 | 涉及文件 | 验证 |
|---|---|---|---|
| FCFG-01 搜索「保存到随记」旁路写 | 弃 `new QuickCaptureStore()` 直写；改经 `QuickCaptureService` 公共 API（优先复用 `AddItemWithAttachmentsAsync(path, copyToManagedStorage:false)`；若 Title/Body/SourceKind 语义不等价，则在 QuickCaptureService 新增小公共方法 `AddExternalLinkedFileItemAsync(string path)`：内部走服务既有 `_gate`＋`EnsureLoadedCoreAsync`＋`_data` 变更＋`SaveAsync`，语义与现实现逐字段一致）。`SearchResultActionService` 构造注入 `QuickCaptureService`（App.xaml.cs:4881 构造点同步改，App 已持有该服务属性 :154） | `Services/SearchResultActionService.cs`、`Services/QuickCaptureService.cs`、`App.xaml.cs` | 新增回归用例：旁路写入后模拟宿主保存，断言条目不丢；既有随记用例全绿 |
| FCFG-02 搜索「附加到 Todo」旁路写 | 弃 Load/Save 直写；改 `TodoWidgetStore.MutateAsync`（:100，门控内原子变更）；写后复用 DEF-043 中继链通知常驻 VM 合并：`TodoReminderService` 新增公开 `NotifyExternalStoreChanged(widgetId, changedItem, insertedItem)`（内部复用既有 **PublishStoreChanged** 的隔离＋dispatcher 投递实现 :482-505）；`SearchResultActionService` 构造注入 `TodoReminderService`，写后调用之 | `Services/SearchResultActionService.cs`、`Services/TodoReminderService.cs`、`App.xaml.cs` | 新增回归用例：外部附加后触发 VM 保存，断言外部条目存活；既有 Todo 用例全绿 |

### B. P2（18 条独立项，全部【修】，其中 FARC-03 为「硬化＋延后实测」）

| 编号 | 修法要点 | 涉及文件 | 验证 |
|---|---|---|---|
| DEF-086 settings.json 缺新架构档案只读防护 | 加载路径记录磁盘档案 SchemaVersion；>Current 时置「settings 只读」态（对齐 `WidgetLayoutStore.CanWrite` 语义）；`SaveToFileOnlyAsync` 设置写入前检查：只读则记 `PersistenceFailed`（原因注明 schema N newer than build）＋日志＋返回 false，不覆写 | `Services/SettingsService.cs` | 新增用例：伪造高版本 settings.json → 保存被拒且文件未动；低版本/无文件路径不受影响 |
| DEF-087 钩子同步握手 UI 阻塞 | 三个热键服务新增 `RefreshRegistrationAsync`（内部走既有 `TryStartAsync`/`WaitAsync` 异步握手）；`App.xaml.cs` 生命周期恢复链（:1628-1630）改 await 三者（宿主方法改 async＋SafeFireAndForget 语义保持）；`OnboardingWindow.Hotkey.cs:94`、`SettingsWindow.HotkeyAndAppearance.cs:361` 若存在同型异步变体则一并切换，否则维持并注释 | `Services/GlobalHotkeyService.cs`、`Services/DesktopDoubleClickActivationService.cs`、`Services/SearchHotkeyService.cs`、`Services/ReservedHotkeyHookService.cs`、`App.xaml.cs`、`Views/OnboardingWindow.Hotkey.cs`、`Views/SettingsWindow.HotkeyAndAppearance.cs` | 既有钩子契约/生命周期用例全绿；静态检查恢复链无 `.Wait(/.GetResult()` |
| DEF-089/FANI-01 共享 Composition 关键帧模板重灌 | 修法按代码现实二选一（修复 subagent 现场定夺并在报告记录）：①关键帧不随启动变化 → 关键帧插入移入模板首建（GetScalar/GetVector3 构造时种子化或 `_keyframesSeeded` 幂等守卫），启动点只 Start；②关键帧确随启动变化 → 这些调用点改为每次新建动画实例（不入缓存字典），避免重灌 | `Services/WidgetCompositionResources.cs`、`Controls/WidgetShell.xaml.cs`（:2135-2201 等）、`Services/WidgetTrayAnimationController.cs` | 既有动画契约用例全绿；静态确认无启动路径重复 InsertKeyFrame |
| DEF-098/FQC-03 设置页配色硬编码暗色基线 | 生效色解析改主题感知：按 `ActualTheme`（或元素主题）在明/暗两组基线常量间选择，替换 `SettingsWindow.QuickCaptureColors.cs:87-113,123-129` 的固定 0xF5F5F5/0x282828；背景按钮显示值随之主题化；对比度校验基准与格子入口（`QuickCaptureSurfaceContent.xaml.cs:325-336`）同源 | `Views/SettingsWindow.QuickCaptureColors.cs`（必要时抽共享解析到 `QuickCaptureClipboardColorSettings`） | 既有配色对比度用例全绿＋新增明/暗两主题解析用例 |
| FWIN-01 cloak 掩码 `==1` 与意图错位 | `WidgetManager.ShowDesktop.cs:82` 判定改「任意已 cloak 态」：`TryGetDwmCloakState(hwnd) > 0`（-1 失败值除外）；`Win32Helper.TryGetDwmCloakState` 文档注释更正为完整掩码语义（0 可见 / 1 APP / 2 SHELL / 4 INHERITED） | `Services/WidgetManager.ShowDesktop.cs`、`Platform/Win32Helper.cs` | 契约测试：掩码判定函数对 0/1/2/4/-1 的行为断言 |
| FQC-01 混合多选删除单 bool 误传 | `DeleteSelectedQuickCaptureItemsAsync` 不再传 `All(IsRecent)` 单 bool；`QuickCaptureWidgetViewModel.DeleteItemsAsync` 增加能区分 recent/record 两集合的重载（或改为传 `IReadOnlyList<bool>`/分组调用两次），逐条按真实归属删除与计数 | `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs`、`ViewModels/QuickCaptureWidgetViewModel.Operations.cs`、`Services/QuickCaptureService.cs` | 新增用例：混合选择删除后 recent 与记录各自正确；计数准确 |
| FQC-02 附件新建卡死＋重试重复 | 根因：先 `AddItemWithAttachmentsAsync` 入库、后 `EditItemDetailsWithResultAsync` 空 body 校验失败返回 false。修法：校验前置——进入「仅附件无正文」分支时不再二次走会失败的 Edit 校验；`AddItemWithAttachmentsAsync` 成功即视为已保存（对齐服务侧 :1312-1316 语义：附件即内容），直接 `created = attached.ToModel()` 收尾；或 Edit 调用传占位空格后清理。修复 subagent 按服务校验现实选择最小等价方案，并保证「取消/失败不再产生孤儿条目或重复条目」 | `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs`、（如需）`Services/QuickCaptureService.cs` | 新增用例：仅附件新建→完成成功且单条目；重复完成不产生第二条 |
| FQC-04 主题翻转不刷记录配色 | `QuickCaptureSurfaceContent_ActualThemeChanged`（:3304-3319）补调 `ApplyClipboardItemColors()`；同时按 ：3170-3182 注释思路核对记录画刷解析是否经由元素主题（`ResolveThemedResource`），App 级误源一并修正 | `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs` | 新增用例：翻转主题后记录前景/背景画刷键变化；既有配色用例全绿 |
| FCFG-04 PasswordVault.Add 已存在即抛 | `SetSecretAsync` 改「Retrieve（try/catch not-found）→ 存在则 Remove → Add」；保留参数校验；异常仍向上传播真实失败 | `Services/PasswordVaultCredentialStore.cs` | 单测（内存桩补齐「已存在→更新」路径）；云端凭据用例全绿 |
| FTHR-01 热键字段撕裂写 | `SettingsService` 仿 `UpdateWidget`（:1298）新增锁内多字段变更 API（如 `UpdateGlobalHotkeySettings(kind, modifiers, key)`：`lock(_lock)` 内三连赋值＋既有通知＋SaveDebounced）；`GlobalHotkeyService.cs:253-255` 改调之 | `Services/SettingsService.cs`、`Services/GlobalHotkeyService.cs` | 单测：锁内三字段原子更新断言；既有热键用例全绿 |
| FEXC-01 搜索动作 fire-and-forget 静默 | `App.xaml.cs:5129-5136` 两处 `_ = Handle...` 改 `SafeFireAndForget`（App:745 既有助手，含日志）；确认两 handler 内部补 try/catch＋失败反馈（tooltip/toast 同搜索动作其他失败路径） | `App.xaml.cs` | 静态：无裸丢弃；既有搜索用例全绿 |
| FEXC-02 附件打开 async void 无 catch | `DetailAttachmentStrip_OpenRequested` 包 try/catch＋日志＋失败反馈（File.Exists 预检保留，TOCTOU 由 catch 兜底） | `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs` | 既有附件用例全绿 |
| FEXC-03 Todo SaveAsync 无捕获 | `SaveAsync`（`FilteringAndAppearance.cs:71-90`）体 内 try/catch＋日志＋（失败时）用户可见反馈；调用点丢弃语义改 `SafeFireAndForget` 或等价 | `ViewModels/TodoWidgetViewModel.FilteringAndAppearance.cs`、调用点文件 | 新增用例：Store 抛异常时 VM 状态一致且有日志；既有 Todo 用例全绿 |
| FEVT-01 AppearancePreviewChanged 广播无隔离 | `SettingsService.cs:1281,1292` 仿同文件 `NotifySettingsChangedSafely`（:1222-1242）改 GetInvocationList 逐 handler 隔离＋日志（单订阅者异常不再截断其余 widget/搜索弹窗刷新） | `Services/SettingsService.cs` | 既有设置广播用例全绿；静态确认 raise 点无裸 `?.Invoke` |
| FARC-03 ProtectedCursor 反射 AOT 风险 | 【硬化】三处助手（`WidgetShell.xaml.cs:5468`、`ContentWidgetWindow.WindowInteraction.cs:337`、`SearchPopupWindow.xaml.cs:859`）：property 为 null 时一次性日志（防刷屏）＋注释标注 AOT 裁剪风险与实测前置。【延后】替换方案（AOT 安全写法）待 `scripts/run-aot-*.ps1` 实测确认失效后再立项 | 上述三文件 | 静态：null 分支有日志；JIT 回归全绿 |
| FLAY-01 胶囊排列顺序与注释相反 | `WidgetCapsuleArrangementCalculator` 重排为文档语义：①总和含间距已 fits → 原样返回（现有早退保留）；②溢出 → 先 spacing=0 试 fits（尺寸优先）；③仍溢出 → 按 `effectiveMinimum` 地板等比压尺寸＋尾截断（保持「caller-visible sizes 总和 = 恰好放下」不变量）；同步更新 `WidgetCapsuleArrangementCalculatorTests`。【延后】96/36 物理常量 DPI 化（视觉行为改动，需 GUI 验证窗口） | `Services/WidgetCapsuleArrangementCalculator.cs`、`tests/DeskBox.Tests/WidgetCapsuleArrangementCalculatorTests.cs` | 单测：中等过载带 spacing=0 且尺寸未被压；极端过载尾截断；既有排列用例全绿 |
| DEF-085 边距对话框×自动折叠取消恢复竞态 | 打开时快照 `(dialogInitialRect, RestsCollapsed 状态, Config.CompactPlacement 深拷贝)`；取消恢复分支（:593-619）改为：若**当前**处于折叠态（`RestsCollapsed`/ViewState 收起）而打开时是展开态 → 不套回展开矩形，改为恢复快照的胶囊 placement（`CaptureCompactPlacement` 路径）或保守 no-op（折叠自愈已复位）＋日志；仍展开 → 维持现行为。修复 subagent 须先通读 `Collapse.cs:553-569,4295-4321,4753-4771` 自动折叠路径与 DEF-065 守卫再落最小补丁 | `Views/WidgetWindowBase.TitleAppearance.cs`（必要时 `Collapse.cs` 只读参考） | 新增纯策略用例（如可抽判定函数）；既有边距/折叠用例全绿 |

### C. P3【修】——速赢/机械修复（24 条，按文件集分组实施）

| 组 | 条目 | 修法要点 |
|---|---|---|
| C-死代码删除 | DEF-092（两死方法）、DEF-099（WidgetRemoved）、DEF-100（SearchRequested 链）、FWIN-05（WithAlpha）、FWIN-06（BringAllVisibleWidgetsToFront＋其源码文本测试改写）、FQC-05/06/07/08/09/10（随记六死链：保存到文件格子链/图片导出链/遗留输入管线/Recent 状态簇/ActivationHelper/配色编辑器三参重载）、FEVT-04（IFeatureLifecycleEvents 若零引用则删，否则注释锚定） | 删除前逐一重验零调用（含测试/XAML 绑定）；涉及 Strings 键的（FQC-05 五键、FQC-06 一键、FQC-08 五键）**12 语言文件同删保齐**；死抽象若与 pluginization 路线相关改注释锚定 |
| C-持久化 | DEF-102（QuickCaptureStore 补 per-path 门控，镜像 TodoWidgetStore `s_pathGates`）、FMEM-01（TodoWidgetStore 门字典加界：计数 >64 时清理 `CurrentCount==初始值` 的未持有条目【注释说明临时路径良性竞态】）、FCFG-05（MusicSettingsStore 写链化：`Task _writeChain` 串行追加防旧覆新）、FCFG-06/FEXC-09（SearchHistoryService 迁移 ResilientJsonStore：Load/Save 走 `ResilientJsonStore.LoadAsync/SaveAsync(storePath, json)`）、FCFG-07（迁移管线：原 JSON 无 `schemaVersion` 属性 → 按 0 处理使 Migration_0_To_1 生效；补/调单测；若实现面风险超预期，修复者回报后降级为延后）、FCFG-08（Migration_9_To_10 落盘完成后再推进检查点：等待写任务完成或改同步写） | 均为既有模式镜像；ResilientJsonStore API 已确认（`SaveAsync(storePath, string)`） |
| C-异常/线程卫生 | DEF-104（重复 manifest 条目 → 计数判别抛带 key 的 InvalidDataException）、DEF-105/FEXC-06（WebDAV href 用 `Uri.TryCreate` 预检，失败跳过＋日志）、DEF-106（`item.Id null` → 抛带归因 key 的 InvalidDataException）、FEXC-04（通知激活丢弃改 SafeFireAndForget）、FMEM-02（托盘旧 Icon 替换前 Dispose：`App.Tray.cs:1022`＋`AppBranding.cs:36`）、FEXC-05（Glance 两 async void 补 catch＋日志；**含 F06-FEVT-06** 同位点 NativeCalendarView_PointerWheelChanged）、FEXC-07（KeyboardHookProc 顶层 try/catch：吞并记日志、保证返回 CallNextHookEx 路径）、FEXC-08（后台任务 catch 放宽到 Exception＋日志）、FEVT-02（静态事件 raise 包 try/catch＋日志）、FEVT-05（App 中继 raise 包 try/catch＋日志）、DEF-088（watchdog 去 `using var`：显式创建＋finally Dispose）、FTHR-03（CTS 竞态：注册期快照 token＋dispose 前先 Cancel）、FTHR-04（DisplayTiming 静态字典加锁）、FTHR-05（FolderWatcher 字段改 Interlocked/volatile＋置换加锁）、FTHR-06（PerformanceLogger 计数器 Interlocked）、FTHR-07（HistoryStore.SaveChecked 经 Task.Run 中转，对齐 RecoveryStore）、FTHR-08/DEF-103（Everything `_isDisposed` volatile＋Wait 超时补日志）、FTHR-09（钩子族跨线程字段 volatile）、FANI-03（FrameBudget 每帧 LINQ 改手写循环）、FANI-04（帧回调异常注销注册＋一次性日志）、FARC-05（ARM64 安装器选择前克隆 manifest 条目，缓存保持原始） | 每条独立小补丁；不改变行为语义（除异常可见性与删除死分支） |
| C-布局/窗口/文档 | FLAY-03（初始补位级联偏移后再钳制，防推出工作区）、DEF-094（capture-lost 分支 `WidgetWindowBase.Interaction.cs:783` 补 `:501-506` 同款 `wasEngaged` 门）、DEF-095（`TitleAppearance.cs:805-817` `ReferenceEquals` 改 RectInt32 值相等，并确认原死分支激活后行为正确——subject==live 时走 `RefreshCompactPlacementAfterBoundsMove`）、DEF-091（WidgetShellSettingsSlice 注释改为地板除语义）、DEF-090 相关注释（cap 分支注释与实现矛盾处更正；阶梯删除延后）、DEF-093（[重要勿删] 三处事实性勘误：QuickReveal 50/200 双档、热键 LL 描述、第三层级模式补记——仅勘误不触碰核心约定）、DEF-107/FARC-04（sync-protocol 契约文档头状态更新为「已入库未接线」＋备份排除补显式说明/测试）、FARC-01（search-core ABI 文档与 native/README 指向已删模块的命令勘误）、FARC-02（current_architecture.md 补 Search/Glance 清单） | 文档类不动代码逻辑；DEF-093 勘误保持既有 z-order 结论不动 |
| C-随记存储 | FQC-13（attachments/{itemId} 残留：在删除路径的 GC 扫描中纳入 attachments 目录清理，与 images/thumbnails 同式、有界＋日志）、FQC-14（新增契约测试钉住 撤销 toast 5s < 图像保护 10s 的常量关系）、DEF-097（多选复制 `QuickCaptureSurfaceContent.xaml.cs:2764-2771` 包 try/catch＋日志＋失败反馈，同 FEXC-02 模式） | 存储清理有界；不触碰撤销保留集主链 |

### D.【延后】（7 项，理由与触发条件）

| 条目 | 理由 | 触发条件 |
|---|---|---|
| FLAY-02 拖动吸附跨屏快照失真 | 修复需重解析节奏改动（逐帧或跨屏事件重算），涉及拖动手感，需 GUI 实测验证 | 下一运行窗口（GUI 复验批次） |
| DEF-096 批量边距「静止几何」语义 | 行为语义改动（测量主体切换），需 GUI 对照单格路径验证 | 同上 |
| FWIN-02/03/04 不可达分支/名不符实/force no-op | 三者均涉及「设计意图 vs 现行为」判定（兜底应激活还是删除），需意图确认后二选一，避免误改行为 | 方案补审轮 |
| FQC-11 CurrentView 只写不读 | 「恢复时生效」或「停止落盘」属产品决策 | 产品决策后 |
| FQC-12 Todo 枚举值/ArchivedAt 死项 | 触及持久化模型字段，删除影响存量数据兼容 | 数据迁移方案确立后 |
| FQC-15 剪贴板去重不对称 | 现行为属设计歧义（与首条同文忽略、与旧条同文重复入库），改动影响用户预期 | 产品决策后 |
| DEF-101 约 20 广播源裸 `?.Invoke` 泛化 | F06 观察项已核实现有 15+ 裸广播源的现存订阅者均自防护/线程 marshal，逐点改造面大收益低，与最小侵入原则冲突；其已立案高危实例 FEVT-01 随本批修复 | 统一 SafeBroadcast/Raise 助手立项，或任一该类事件新增订阅者前 |
| FANI-02 自适应阶梯删除 + FARC-03 完整替代 | 热路径删除需谨慎；AOT 替代写法需运行时实测 | AOT 实测窗口（`scripts/run-aot-*.ps1`） |

### E.【不修】（0 条）

两轮报告经《双轮比对验证报告》仲裁后无判「误报/不成立」的立案；观察项均不立案。

---

## 二、实施批次与文件集互斥（2 并发约束）

| 波次 | 修复组 | 涉及文件（互斥声明） |
|---|---|---|
| W1-1 | ①P1 双条（FCFG-01/02＋DEF-102＋FMEM-01＋FQC-13）②持久化组（DEF-086、FEVT-01、FEXC-08、FCFG-04/05/06/07/08） | ①`SearchResultActionService/QuickCaptureService/TodoWidgetStore/TodoReminderService/QuickCaptureStore/App.xaml.cs(构造点/中继)/tests` ②`SettingsService(只读守卫＋AppearancePreviewChanged 隔离＋RequestAppearancePreview catch)/PasswordVaultCredentialStore/MusicSettingsStore/SettingsMigrationService/SearchHistoryService` |
| W1-2 | ③窗口/钩子/随记 UI（DEF-087、FEXC-01/02/03/07、FQC-01/02/04、DEF-097、DEF-098、FTHR-01 调用点、FARC-03 两处、DEF-094）④布局/动画/备份/更新（DEF-085、DEF-095、FLAY-01、FLAY-03、DEF-089、FANI-03/04、FARC-03 一处、FWIN-01、FARC-05、DEF-104/105/106） | ③`App.xaml.cs(恢复链/搜索动作丢弃)/三个热键服务/ReservedHotkeyHookService(含 KeyboardHookProc 顶层 catch)/QuickCaptureSurfaceContent/TodoWidgetViewModel.FilteringAndAppearance/SettingsWindow.QuickCaptureColors/ContentWidgetWindow.WindowInteraction/SearchPopupWindow/WidgetWindowBase.Interaction` ④`TitleAppearance/Collapse(只读参考)/WidgetCompositionResources/WidgetShell/WidgetTrayBatchAnimationDriver/WidgetCompactAnimationCoordinator/WidgetCapsuleArrangementCalculator+tests/WidgetManager.cs(FLAY-03)/ShowDesktop/Win32Helper/DeskBoxDataBackupService/WebDavBackupTransport/AppUpdateService` |
| Gate-1 | 主流程：停仓库 DeskBox.exe → `dotnet build`（x64）→ `dotnet test`（x64 全量）→ 修复失败项 | — |
| W2-1 | ⑤死代码删除组 ⑥异常/线程卫生组 | ⑤`WidgetManager.cs/ZOrder.cs/ContentWidgetWindow.xaml.cs/SearchWidgetContent*/QuickCaptureService(死链段)/QuickCaptureSurfaceContent(死 UI 段)/QuickCaptureClipboardColorEditor/QuickCaptureClipboardActivationHelper/Contracts/IFeatureLifecycleEvents/Strings×12/tests` ⑥`NativeFileDragOut/App.Tray.cs/AppBranding.cs/GlanceWidgetContent/FolderWatcherService/DesktopAutoOrganizationWatcher/WidgetAnimationDisplayTiming/PerformanceLogger/DesktopOrganizationHistoryStore/EverythingSearchService/HookHealthWatchdog/DesktopDoubleClickActivationService(volatile 字段)/钩子族 volatile 字段` |
| W2-2 | ⑦布局/文档组 ⑧随记存储组 | ⑦`TitleAppearance(FLAY-03 如未在④)/WidgetShellSettingsSlice/Collapse(注释)/[重要勿删]手册/sync-protocol 契约文档/search-core ABI 文档/native/README/current_architecture.md/WidgetManager.cs(FLAY-03 如未在④)` ⑧`QuickCaptureService(GC 段)/tests(FQC-14)` |
| Gate-2 | 终门禁：构建→x64 回归→12 语言键齐校验→启动新实例核验路径 | — |

> 冲突协调规则：`SettingsService.cs`（②⑥有交叉——FEVT-01/FEXC-08 已随 W1-1② 完成，W2-1⑥ 不再触碰该文件）、`App.xaml.cs`（①③交叉——①仅构造点/中继、③恢复链/丢弃点，异波串行）、`QuickCaptureService.cs`（①⑤⑧交叉——①功能性修、⑤死链删除、⑧GC 段，异波串行）、`WidgetManager.cs`（④⑤交叉——④FLAY-03、⑤死方法/死事件，异波串行）、`ReservedHotkeyHookService.cs`（③内含 DEF-087 与 FEXC-07 同文件，同组完成）——同文件多组修复按波次顺序串行执行。

## 三、方案完善性自检清单（供审查 subagent 核对）

1. A/B 两轮全部立案（A23＋B61，去重后独立项 71）是否**每条**都有处置（修/延后/不修）与理由？
2. 每个【修】条目是否给出：修法要点、涉及文件、验证方式、回滚边界（整条补丁可独立 revert）？
3. 批次文件集是否互斥（同一文件不出现于并发两组）？
4. 是否定义了构建/回归/语言键三重门禁与 AGENTS.md 重启工作流？
5. 延后项是否理由充分且触发条件明确？
6. P1 修法是否消除「旁路写」根因（而非仅加重试）？是否复用既有规范模式（服务缓存/门控/中继）而非发明新机制？
