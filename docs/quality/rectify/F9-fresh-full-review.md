# F9 从零全量审查报告（fresh full review，linux-hermes）

## 审查方式（用户指令：不沿用既有审查结论，从零详细一轮）
- 基线：HEAD=fe1e011（CI 绿）
- 三波 subagent 深审（单发避免 429）+ 主线三波亲审，**所有 P1/P2 候选均经主线亲验后才定级**（交叉验证，5 条候选被证伪）
- 覆盖：Todo/QuickCapture 全链（SA-A，102 工具调用）、Search/Weather/Glance 26 服务+UI（SA-B，68）、Settings/Onboarding/窗口壳层 ~50 文件（SA-W3，91）、主线：更新/热键/Music/JsonStore/各 Store 并发、DesktopOrg 事务、FolderWatcher、显示拓扑、托盘动画、生命周期恢复、WidgetManager 核心、注册表、主题/字体/迁移、Rust 契约（ABI=2 常量在位、extern 导出 12 处）

## 实锤新缺陷（P1×1 + P2×3 + P3×10）

### DEF-043 · P1 · Todo 数据双写者无串行化门控（数据丢失风险）
- 位置：`ViewModels/TodoWidgetViewModel.FilteringAndAppearance.cs:68-75`（UI 写，29 处调用点无门控）× `Services/TodoReminderService.cs:259/321/417`（后台定时器写）
- 根因：两条写路径都是「load 全量 → 内存改 → save 全量」的读改写，无共享 SemaphoreSlim。提醒定时器在 VM 落盘前 load，会把 VM 刚写的完成状态用旧快照覆盖回去（用户勾选被静默回滚）；反向覆盖提醒标记 → 重复提醒。`ResilientJsonStore` 只保单次写原子，不保跨写者一致。对照：GlanceWidgetStore:32、WeatherCacheStore:79-94、QuickCaptureService:38 全有 `_gate`，**唯独 Todo 链没有**。附带：非 1175 IOException 在 VM 路径直接外抛。
- 触发：提醒到点恰好与用户操作重叠（日活跃用户下概率不低）。
- 修复：TodoWidgetStore 内置 `_gate`（SemaphoreSlim(1,1) 包 Load/Save），或服务层复用 VM 同一持久化队列；一处修复两侧受益。

### DEF-044 · P2 · Everything_QueryW 无超时，可挂死整个搜索子系统
- `Services/EverythingSearchService.cs:438`（同步阻塞原生调用）+ `:211` `_nativeGate` 全程持有。Everything 进程无响应时 CT 取消无效，后续所有查询/探测排队 → 搜索永久转圈。修复：`WaitAsync(TimeSpan)` 包装 + 失败 Reset，或 SDK SetTimeout。

### DEF-045 · P2 · 未启用 Everything 时每次击键跑全量安装检测
- `EverythingSearchService.cs:190-196` + `:84-86`：默认配置下 35ms 消抖后每次击键执行注册表双视图扫描 + `Process.GetProcessesByName` + MainModule/令牌枚举（无 TTL，TTL 只在启用路径）。修复：未启用结果也缓存 30s。

### DEF-046 · P2 · 天气风向负值导致 UI 线程数组越界
- `WeatherWidgetViewModel.DataProcessing.cs:626-630`：`(int)Math.Round(d/45) % 8` 负模返回负值直接索引 `keys[]`；调用点 `:284` 无 try。畸形 MSN 响应（负 windDir）→ IndexOutOfRangeException，本次天气渲染中断。修复：`((x % 8) + 8) % 8` + NaN/Infinity 防护。

### P3 挂账（DEF-047~056）
| 编号 | 摘要 | 位置 |
|---|---|---|
| DEF-047 | CTS 取消后同步 Dispose → 在途注册回调 ODE（日志噪音+弃用查询丢失） | SearchPopupViewModel.cs:696-712 |
| DEF-048 | 「转小写比较」注释未实现，大小写敏感匹配退化 | WeatherCodeMapper.cs:98-99 |
| DEF-049 | MSN icon 29→29 非法 WMO 码透传（唯一恒等映射笔误） | WeatherCodeMapper.cs:207 |
| DEF-050 | MSN 日期解析失败仍追加空行（UI 错位） | WeatherService.cs:498-504/559-566 |
| DEF-051 | 远程 JSON 无大小上限（仅超时兜底） | GlanceImageService.cs:614-620、WeatherService.cs:83,413,442 |
| DEF-052 | FileMetaService LRU 软缺陷：失败 null 永久缓存不重试、在途不淘汰 | FileMetaService.cs:236-265/267-304 |
| DEF-053 | DeskBox 内容刷新任务引用竞态（旧 finally 清新引用） | SearchEngineService.cs:485-505/553-559 |
| DEF-054 | Saka/Bangla 历固定起点近似漂移（标注近似或表驱动） | GlanceTraditionalCalendarService.cs:225-258 |
| DEF-055 | 窗口壳层 Closed 后 zombie 回调系统面：SettingsWindow ~20 处 async void 无 _isClosed 复检 + OnboardingWindow.xaml.cs:131 Closed 内直接清理（F7-B5/EVT-03 同族系统面，WinUI 不释放托管树+全局兜底故仅 P3） | SettingsWindow.*.cs、OnboardingWindow.xaml.cs:131-153 |
| DEF-056 | Migration_1_To_2 与 Migration_2_To_3 完全重复段 | SettingsMigrationService.cs:127-176 |

## 证伪清单（候选→亲验否决，不立案）
1. SA-A「过滤器下完成周期任务插入位置错误」P1：`Items.Insert` 作用于主列表（非过滤视图），视觉序由 `CompareVisibleItems`（SortOrder 并列时 UpdatedAt 新者前）重建，语义正确。
2. wave3「ContentWidgetWindow Closed 与 _contentLoadTask 跨线程竞态」P1：`LoadContentAsync:1028` ReferenceEquals 守卫 + WinUI DispatcherQueueSynchronizationContext 保证续体回 UI 线程；残留仅 zombie attach（已并入 DEF-055 面）。
3. wave3「AccentResourceScope 原地改共享笔刷」P1：刷子存于**窗口本地** ResourceDictionary（Apply 自建），SharedBrushCache 笔刷只直接赋给元素属性、不入字典——跨窗口共享前提不成立，原地改色是 WinUI 惯用法。
4. wave3「Migration_7_To_8 未重置 EnableContinuousDecorativeAnimations」P2：该字段仍被 PerformanceSettingsPolicy.cs:289-337 主动读写（主开关），非废弃字段。
5. wave3「SystemFontCatalog 需 ReleaseComObject」P2：ENUMLOGFONTEX 回调指针是原生结构体非 COM 接口，ReleaseComObject 会抛 ArgumentException——建议本身错误。

## 与既有台账去重
- SA-A 的 IQuickCaptureClipboardReader SoftwareBitmap 缺 using = **MEM-02/DEF-031 已知项**（同位置），不重复立案，修复时顺带。
- Onboarding/SettingsWindow async void 面 = F7-B5 已知系统债，本轮以 DEF-055 记录新实例清单。

## 结论
**NO-GO（本轮新发现 P1×1 + P2×3）**——符合「从零换眼」预期：前几轮聚焦内存/生命周期/修复回归，本轮深挖的 Todo 并发、Everything IPC、天气数据处理是首次覆盖面。
- 建议修复优先级：DEF-043（P1 数据丢失风险）> DEF-046（一行修）> DEF-045（一行修）> DEF-044 > P3 批。
- 全部为 C# 侧修复，不触红线（z-order/Rust/XAML/12 语言键均未涉及）。
