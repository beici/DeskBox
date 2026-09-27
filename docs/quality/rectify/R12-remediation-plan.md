# R12 整改方案（round-12 双路审查闭环批次）

> 输入：`docs/quality/rounds/round-12/全量代码缺陷审查总报告.md`（DEF-120~124，含 round-11 回归 DEF-123）。
> 原则：最小侵入、语义零变化（除缺陷方向本身）、不触碰 z-order 红线。
> 基线：`wip/fix-bug` @ `7d201074`，工作树仅含 round-12 文档产出。

## 1. 逐项处置

### W1 ｜ DEF-123 ｜ round-11 DEF-117 修复引入的 StartAsync 重入竞态（P3，批内最高优先）

- **现状（主流程核验 + 原报告交错序列）**：`FolderWatcherService.StartAsync` 入口解析 `await Task.Run` 让出 UI 线程后，`Stop() → 写 _requestedPath → 读代际` 才执行——离线 UNC 慢解析的旧调用恢复后可 Stop 掉新目录 watcher 并以最新代际通过守卫，watcher 持久错挂旧目录。`Stop()` 在锁内 `_watchGeneration++`（:583），提供守卫基础。
- **修法**：
  1. `StartAsync` 进入时（await 前）`int entryGeneration; lock (_lock) { entryGeneration = _watchGeneration; }`；解析 await 之后、`Stop()` 之前补 `lock (_lock) { if (_isDisposed || entryGeneration != _watchGeneration) { return; } }`——任何插队的 Stop（调用方 ConfigureFolderWatchersAsync:223 或后到 StartAsync）都使代际前移，旧调用在恢复点即退出，恢复「后到者赢」；
  2. `ReconnectTimer_Tick` 同款但**两个 await 后均校验**（完善性审查阻断项 1）：tick 顶部快照 entryGeneration，①解析 await 之后校验（被取代即 return）；②**`await ProbeFolderAccessAsync` 之后、`availability` 判定之前再次校验**——否则取代发生在 probe await 期间且 probe 成功时，:891 `StartAsync` 以新快照通过入口校验、`Stop()` 杀掉新目录 watcher，DEF-123 经 probe 窗口原样复现；probe 失败变体下 :885 `SetHealth` 也会覆盖新 watcher 的 Watching。同一快照服务两处校验；被取代即 return 不破坏重连链（代际前移意味着更新一层的 StartAsync 已接管恢复责任，其失败走 BeginReconnect 重建重连链）；
  3. 既有 `startGeneration` 守卫（:236/:253/:270）原样保留——两层守卫分别覆盖「解析期间被取代」与「探测/查询启动期间被取代」，无冗余冲突；
  4. 残留语义留档（审查建议 5）：入口快照相等的一对 StartAsync（tick-vs-ConfigureFolderWatchers 极窄窗口/双 tick 同目标）退化为「先恢复者赢」——前置条件比原缺陷更苛刻，接受。
- **验证**：构建 + 全量回归（FolderWatcher 无行为级用例，以全量回归兜底）+ 静态走查交错序列（新 UI 调用插队 → 旧调用代际失配 → return）。

### W2 ｜ DEF-124 ｜ CompleteTrackedImportAsync 封送 lambda 挂起面（P3）

- **现状（主流程核验）**：`FileSurfaceContent.ImportProgress.cs:238-243` 非 UI 线程路径 `TryEnqueue(async () => { await CompleteTrackedImportAsync(...); completion.TrySetResult(true); })`——被 await 调用抛异常时 TrySetResult 永不执行，`await completion.Task` 永久挂起。
- **修法**：lambda 体改 `try { await CompleteTrackedImportAsync(completionState); } finally { completion.TrySetResult(true); }`——异常仍沿 async void 语义进全局兜底（与现状一致），但后台等待方必然被释放。
- **验证**：构建 + 全量回归。

### W3 ｜ DEF-120 ｜ T() 热路径注册表重复读（P3）

- **现状（主流程核验）**：`ResolveDefaultLanguage()`（LocalizationService.cs:265-297）每次读 `Registry.GetValue(HKCU\Software\DeskBox, InstallLanguage)`，System 分支（出厂默认）被 1,200+ T() 调用放大。
- **修法**：`private static readonly Lazy<string> ResolvedDefaultLanguage = new(ResolveDefaultLanguageCore);`——把现方法体改名为 `ResolveDefaultLanguageCore`，`ResolveDefaultLanguage()` 变为 `=> ResolvedDefaultLanguage.Value;`。`Lazy<>` 默认线程安全发布（避开 EXC-06 家族的 DCL 无 volatile 模式）；仅 System 分支触发求值；注册表值进程内恒定（仅安装/修复写入），语义零变化。
- **验证**：构建 + 全量回归（本地化相关契约测试全绿）。

### W4 ｜ DEF-121 ｜ 顿号硬编码 ×3（P3）

- **现状（主流程核验）**：`DesktopOrganizationSettingsSection.xaml.cs:844,865` 与 `DesktopOrganizationTaskView.xaml.cs:295` 硬编码 `string.Join("、", …)`；同仓范式为 `_localizationService.IsChinese ? "、" : ", "`（SettingsViewModel.Performance.cs:279 对照）。
- **修法**：三个站点按既有静态访问模式改写为该范式（审查核正：两类均无 `_localizationService` 字段，静态 `T()` 均经 `App.Current.LocalizationService`——SettingsSection :972、TaskView :253；TaskView 站点位于 static 方法，同样可用）。展示文本在中文下零变化、非中文语言由「、」变为「, 」（缺陷方向本身）。
- **验证**：构建 + 全量回归。

### W5 ｜ DEF-122 ｜ 快捷方式后缀硬编码（P3）

- **现状（主流程核验）**：`FileSurfaceContent.ShortcutDrop.cs:107-109` 硬编码 `" - Shortcut.lnk"`；同文件已用 `Widget.CreateShortcut` 键族。
- **修法**：新增键 `Widget.CreateShortcutSuffix` ×12 语言（en-US ` - Shortcut.lnk`、zh-CN ` - 快捷方式.lnk`、zh-TW ` - 捷徑.lnk`、ja-JP ` - ショートカット.lnk`、de-DE ` - Verknüpfung.lnk`、fr-FR ` - Raccourci.lnk`、es-ES ` - Acceso directo.lnk`、pt-BR ` - Atalho.lnk`、ru-RU ` - ярлык.lnk`、ar-SA ` - اختصار.lnk`、hi-IN ` - शॉर्टकट.lnk`、bn-BD ` - শর্টকাট.lnk`，各组内位置紧邻 `Widget.CreateShortcut`），调用点改本地化后缀（审查核正：`GetShortcutDisplayName` 为 private static :96 而 T() 为实例方法——后缀作为参数传入静态助手）。:108 空名兜底 `"Shortcut.lnk"` 一并覆盖：改 `T("Widget.CreateShortcutSuffix").TrimStart(' ', '-')`（en「Shortcut.lnk」/zh「快捷方式.lnk」），单一键覆盖两形态。键集 2896→2897：static_gate 的 check_strings 以键数最多语言动态取基准（无测试钉死 2896——grep 实证），12 语言同步增补即全对齐（gate 输出键数 2897×12 对齐）。
- **验证**：构建 + 全量回归 + static_gate（[1] 键 parity PASS，gate 输出键数 2897×12 对齐，JSON 留档）。

## 2. 不进本批（延后维持，与总报告 §5 一致）

新位点 5 组（DEF-097 CopyPathsToClipboard、DEF-078 QuickLook 导航 TryEnqueue、ANI-06 ×5、DEF-080 ×7、O-9 ×2——均并入既有编号挂账）、观察项 9 条。

## 3. 门禁

1. 停止仓库路径 DeskBox 实例 → Debug x64 构建 0 错误；
2. `python scripts/quality/static_gate.py` PASS（零 async void/同步等待/空 catch 新增；键集 2897）；
3. x64 全量回归全绿（基线 4297/4297）；
4. 启动规范 Debug 实例核验；
5. 台账/TODO 收口（DEF-120~124 → ✅ 已修复；static_gate 输出键数 2897×12 对齐核对，JSON 留档 `rectify/r12-static-gate.json`）。

## 4. 风险与回滚

- 6 源文件 + 12 语言 JSON；W1/W2 行为变化仅缺陷方向；W3 记忆化语义零变化；W4 非中文展示文本变化（缺陷方向）；W5 非英文展示文本变化（缺陷方向）。
- 回滚：单 commit revert。
