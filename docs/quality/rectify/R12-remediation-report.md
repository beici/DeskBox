# R12 整改报告（round-12 闭环批次，2026-09-27）

> 输入：`rounds/round-12/全量代码缺陷审查总报告.md`（DEF-120~124，含 round-11 回归 DEF-123）。
> 方案：`R12-remediation-plan.md`（独立完善性审查**首审不通过**——W1 tick 探测 await 残留窗口 + 台账预写流程问题；按指令修订后复审**通过**，另采纳 5 条建议）。
> 门禁总览：Debug x64 构建 **0 错误**（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**（12 语言 2897 键对齐，其余五项与基线持平）；x64 全量回归 **4297/4297**；新实例 **PID 30924** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-123 ① | `Services/FolderWatcherService.cs`（StartAsync） | 入口（await 前）锁内快照 `entryGeneration`；解析 `await Task.Run` 之后、`Stop()` 之前校验 `_isDisposed \|\| entryGeneration != _watchGeneration` 即 return——恢复「后到者赢」，任何插队 Stop（ConfigureFolderWatchersAsync:223 / 后到 StartAsync）使代际前移 |
| DEF-123 ② | 同文件（ReconnectTimer_Tick） | tick 顶部快照 entryGeneration；**解析 await 后**与 **`await ProbeFolderAccessAsync` 后、availability 判定前**两处均校验（首审只覆盖解析窗口，probe 窗口可复现 DEF-123——审查阻断项 1 修正）；被取代即 return，重连链由接管层自愈（StartAsync 失败走 BeginReconnect） |
| DEF-124 | `Controls/WidgetContents/FileSurfaceContent.ImportProgress.cs:238-252` | 封送 lambda 改 `try { await … } finally { completion.TrySetResult(true); }`——等待方必然释放，异常传播语义与现状一致（async void 全局兜底） |
| DEF-120 | `Services/LocalizationService.cs` | `ResolveDefaultLanguage` 改 `Lazy<string>`（`ResolvedDefaultLanguageInstance = new(ResolveDefaultLanguageCore)`）——线程安全发布（避开 EXC-06 家族 DCL 形态），仅 System 分支触发求值；`T()`/`CurrentCultureName` 对外语义零变化 |
| DEF-121 | `Views/SettingsSections/DesktopOrganizationSettingsSection.xaml.cs` + `Controls/DesktopOrganizationTaskView.xaml.cs` | 两类各加 `ListSeparator` 静态属性（`IsChinese ? "、" : ", "`，经 `App.Current.LocalizationService`，与 SettingsViewModel 既有范式一致）；三站点 `string.Join("、",…)` → `string.Join(ListSeparator,…)`（审查核正：两类无 `_localizationService` 字段） |
| DEF-122 | `Controls/WidgetContents/FileSurfaceContent.ShortcutDrop.cs` + 12 语言 JSON | 新键 `Widget.CreateShortcutSuffix` ×12（en ` - Shortcut.lnk`、zh-CN ` - 快捷方式.lnk`、zh-TW ` - 捷徑.lnk`、ja ` - ショートカット.lnk`、de ` - Verknüpfung.lnk`、fr ` - Raccourci.lnk`、es ` - Acceso directo.lnk`、pt-BR ` - Atalho.lnk`、ru ` - ярлык.lnk`、ar ` - اختصار.lnk`、hi ` - शॉर्टकट.lnk`、bn ` - শর্টকাট.lnk`）；后缀作为参数传入 static `GetShortcutDisplayName`；空名兜底改 `localizedSuffix.TrimStart(' ', '-')`（同键覆盖，en→`Shortcut.lnk`、zh→`快捷方式.lnk`） |

净变化：6 源文件 + 12 语言 JSON（键集 2896→2897）；无签名/序列化/ABI/依赖变更。

## 2. 门禁证据

1. 停实例（PID 19732）→ 构建 0 错误（22 警告 ≤ 基线 24）。
2. x64 全量回归 → **4297/4297 通过、0 失败**（2m05s）。
3. `static_gate.py --json rectify/r12-static-gate.json` → **PASS**：[1] 12 语言键/占位符一致性 PASS（2897×12）；[2] async void 249=基线；[3] 剪贴板 8/8 配对；[4] 同步等待 147/空 catch 245/反射 6 全部=基线；[5] 契约重放新增 0。
4. 新实例 PID 30924 @ 规范 Debug 路径。

## 3. 流程记录

- 首审不通过的两项阻断均属实质问题：①W1 只闭合了 tick 的第一个 await 窗口，probe 窗口可原样复现 DEF-123（审查给出两处校验的修正指令）；②台账曾预写未发生的审查/门禁结论（已改为待实施占位、落地后回填——该纪律固化为后续轮次约定：整改批小节在落地与门禁完成前只允许「待实施」占位）。
- 延后维持（与总报告 §5 一致）：新位点 5 组（并入既有编号挂账）、观察项 9 条（O-20~O-23 + B 面 5 条）。
