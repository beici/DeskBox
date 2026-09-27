# R12-A 核心服务与数据面专项审查报告（round-12）

> 审查基线 commit `7d201074`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R12-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 零审查历史文件盘点（本轮差异化策略）

以「文件名在 `docs/quality/` 全目录（含 8 轮专项报告与台账）中的提及次数」对 `src/DeskBox/Services`（316 文件）与 `src/DeskBox/ViewModels`（~90 文件）做全量交叉盘点，得到 160 个文件名零提及的文件。再按「前序报告存在不带 `.cs` 后缀的裸名提及」修正归属后，真实的零审查历史集合集中在：**DesktopOrganization / DesktopAutoOrganization 引擎家族（10 文件，约 1.8k 行）、FileService 两个未点名分部、LocalizationService 全文、Todo/Weather 两大 ViewModel 家族的未深读分部、以及一批搜索/文件栈/分组小服务**。AOT `Aot*Fixture` 测试装置（7 个）与纯菜单构造器不计入深读预算。

### 1.2 覆盖率声明

**全文逐行读（本轮新覆盖，此前从未被任何轮次深读）**：

- DesktopOrganization 引擎家族 10 文件全文：`DesktopOrganizationScanner.cs`（227）、`DesktopOrganizationPlanner.cs`（229）、`DesktopOrganizationClassifier.cs`（120）、`DesktopOrganizationRuleResolver.cs`（157）、`DesktopOrganizationTransfer.cs`（50）、`DesktopOrganizationPreviewReconciliation.cs`（27）、`DesktopAutoOrganizationStateMachine.cs`（366）、`DesktopAutoOrganizationActivityTracker.cs`（111）、`DesktopAutoOrganizationBaseline.cs`（152）、`DesktopAutoOrganizationSuppressionRegistry.cs`（182）
- `FileService.PathResolution.cs`（189，**FileService 家族唯一从未被点名的分部**）+ `FileService.OpenItem.cs`（187）、`FileService.CaseOnlyRename.cs`（144，两文件仅被前轮报告的文件清单列名、未实际审读）
- `LocalizationService.cs` 全文（534——R9 仅锚定 EXC-06 双检锁位点）
- TodoWidgetViewModel 家族：`TodoWidgetViewModel.cs`（846）、`ItemOperations.cs`（630）、`EditingAndUndo.cs`（294）、`FilteringAndAppearance.cs`（760）全文；`DetailAndAttachments.cs` 附件删除段维持 R10 证据
- WeatherWidgetViewModel 家族：`WeatherWidgetViewModel.cs`（988）、`RefreshAndLayout.cs`（545）全文 + `WeatherWidgetContentAdapter` / `WeatherWidgetContentProvider` 生命周期链
- 零历史小服务：`EverythingInstallationDetector.cs`（307）、`SearchResultRanker.cs`（174）、`SearchResultCollectionReconciler.cs`（147）、`VirtualDropFileNameResolver.cs`（150）、`WidgetStackGroupingService.cs`（299）、`FileWidgetSession.cs`（37）、`FileOpenRequestGate.cs`（84）、`FileOpenTrace.cs`（50）、`FileOpenPickerService.cs`（52）、`FolderPickerService.cs`（36）
- 零历史 ViewModel：`WidgetViewModel.SortingAndWatchers.cs`（800）、`WidgetViewModel.ItemMutationBatch.cs`（170）、`WidgetViewModel.AddedAt.cs`（148）、`TodoStepViewModel.cs`（69）、`WidgetStackItem.cs`（191）、`MusicWidgetViewModel.Lifecycle.cs`（128）、`GlanceWidgetContextMenuBuilder.cs`（126）、`WidgetGroupSwitchRequestCoordinator.cs`（165）

**grep 定向扫描 + 命中处精读**：

- 其余零历史文件（160 集合全体）：按 `async void` / `.Result`/`.Wait(`/`GetAwaiter().GetResult()` / `Task.Run` / `Parse`+文化敏感 `ToString` / `File.Write*`/`File.Delete` / `AllocHGlobal`/`GCHandle` / `new Timer`/`new Thread` / `Process.*` / `lock` 模式横向扫描。命中位点（`SettingsViewModel.FileStackOptions.cs:473` Task.Run 预览、`GlanceWidgetContextMenuBuilder.RunAsync`、诊断时间戳格式化等）逐一精读，均无立案项。**零历史集合中 `async void` 与同步等待命中数为 0**。
- `WidgetManager.Storage.cs` 的 4 处 `Directory.Delete`/`File.Delete` 位点上下文（:785/:1285/:1329/:1507）逐处核对（均为仅删空目录 / 身份校验后的快捷方式 / 仅删空根，fail-safe）；`WidgetManager.FeatureWidgets.cs` 的 DEF-070 家族位点（:707-710/:759-760/:890-904）确认仍在原位（维持，不重复立案）
- `SettingsService.cs` 防抖/预览段（:1255-1385）重读；`ServiceRegistry.cs` 全文（48 行）
- round-11 修复批（commit `cfdf8059`）diff 逐行复核 + 修复后调用面上下文全文核对（见 §3）

**未覆盖及原因**：

- `SettingsService.cs`（3,738 行）/ `FileService.cs`（3,767 行）主体、`DeskBoxDataBackupService`、`CloudBackupService`、`AppUpdateService`、`MusicVolumeService`、`ResilientJsonStore`、`TodoRecurrenceService`、`QuickCaptureService`、`FolderWatcherService` 非 DEF-117 面：round-06~11 已全文主审，本轮仅做台账锚点定位复核
- `WidgetManager` 各分部 / `WidgetLayerService`：round-06~09 反复主审面，本轮仅扫描锁外写点与删除位点
- 12 语言 JSON（归 B 代理）；`ManagedStorageMigrationDialog` 等 UI 对话框（归 B 面）
- Models 60 文件：R10 已扫 AOT 契约，本轮无迹象变化，未重扫

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| O-9 家族（CurrentCulture 比较器与仓库 Ordinal 惯例不一致，纯展示排序） | `Services/SearchResultRanker.cs:63` | 合并去重后的并列结果按 `StringComparer.CurrentCultureIgnoreCase` 排 Title——仅影响展示次序，无机器解析消费方 |
| O-9 家族（同上） | `Services/WidgetStackGroupingService.cs:280` | FileStack 按名排序 `OrderBy(Name, CurrentCultureIgnoreCase)`——同水位，展示序 |
| DEF-080 家族（current-culture 时间格式化进用户可见文本，无解析消费方） | `ViewModels/SettingsViewModel.RuntimeDiagnostics.cs:68` | 生命周期时间戳 `ToLocalTime().ToString("g")`——ar-SA 下诊断页显示 Hijri 历/阿拉伯-印度数字，纯展示 |
| DEF-080 家族（同上，显式传 CurrentCulture） | `ViewModels/SettingsViewModel.QuickCaptureDiagnostics.cs:105` | `ToString("HH:mm", CultureInfo.CurrentCulture)`——同水位，纯展示 |

---

## 3. 存量复核（范围内挂账条目现状）

### 3.1 round-11 修复批复核（本轮重点，逐行当前树验证）

| 条目 | 结论 | 证据 |
|---|---|---|
| **DEF-117（FolderWatcher UI 线程同步探测）** | ✅ 修复正确 | `FolderWatcherService.cs:213-216`：`StartAsync` 入口路径解析包 `Task.Run`，且位于代际捕获（:227-231）之前，解析期间的并发 Start 不会污染代际判定；`:267-274`：双失败分支改 `await ProbeFolderAccessAsync` 并在其后补 `_isDisposed || startGeneration != _watchGeneration` 代际守卫（lock 内判定）；`:874-882`：`ReconnectTimer_Tick` 的解析入 `Task.Run`，失败回退钉死为最后物理路径 `path`（与修复前语义一致，注释已留痕）。`ProbeFolderAccess` 同步体（:297-319）语义未动，`ProbeFolderAccessAsync`（:321-324）即其 Task.Run 包装——与报告建议的最小修法一致，探测结果与健康分类语义不变 |
| **DEF-118（Process 数组全量 Dispose）** | ✅ 修复正确 | `DragDropPermissionService.cs:804-836`：`Process[] explorers` 全量在外层 `finally` 逐元素 `Dispose()`；早退路径（`not-running`/`OpenProcess` 失败）均被外层 finally 覆盖；裸句柄 `processHandle` 由内层 `try/finally CloseHandle` 负责——返回值在 Dispose 之前已构造完成，无 use-after-dispose。比原报告建议的 `using var` 首实例方案更彻底 |
| **DEF-119（安装器用户级 .NET 探测点）** | ✅ 修复正确 | `installer/DeskBox.Dependencies.iss:109-113` 与 `arm64.iss` 同位点：`IsDotNet10RuntimeInstalledAt(ExpandConstant('{localappdata}') + '\Microsoft')`；`IsDotNet10RuntimeInstalledAt`（:67-72）拼 `dotnet\dotnet.exe` 并跑 `--list-runtimes`，与既有两个探测点同一条检测管线；提权上下文下 `{localappdata}` 解析为运行 Setup 的账户配置这一已知局限已在注释中留痕 |

### 3.2 挂账条目锚点复核（本轮定点验证，未全文重读）

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| DEF-048（WeatherCodeMapper 小写比较未实现） | 维持 | `Helpers/WeatherCodeMapper.cs:99` 段无 ToLower；`:209` `29 => 29` 亦在位（DEF-049 同点维持） |
| DEF-052（FileMetaService 失败 null 永久缓存） | 维持 | `FileMetaService.cs:163-164` `_iconCache.GetOrAdd(key, k => LoadIconAsync(k))` 无失效机制 |
| DEF-053（内容刷新任务引用竞态） | 维持 | `SearchEngineService.cs:513-517` finally `lock` 内无条件 `_deskBoxContentRefreshTask = null` |
| DEF-070 家族（FeatureWidgets 锁外写点） | 维持 | `WidgetManager.FeatureWidgets.cs:707-710,759-760,890-904` 仍在原位（R9 已并入既有编号） |
| DEF-076（遗留 settings.json 一次性迁移非原子） | 维持 | `SettingsService.cs:941` `await File.WriteAllTextAsync(_settingsPath, json)` 仍裸写 |
| DEF-079（JIT 音量后端 Volume RCW 不释放） | 维持 | `MusicVolumeService.cs:538-541` `Dispose()` 仅 `ReleaseComObject(Control)` |
| DEF-080（ar-SA 天气解析/缓存错乱） | 维持 | `WeatherService.cs` `ToString("yyyy-MM-dd` 4 处（grep 计数）无 InvariantCulture；本轮家族新位点见 §2 |
| EXC-06（CitySearchService DCL 无 volatile） | 维持 | `CitySearchService.cs:54,65` 机制不变；LocalizationService 同型位点（:313-467）本轮全文复核确认维持 |
| MEM-02（SoftwareBitmap 未确定性释放） | 维持 | `IQuickCaptureClipboardReader.cs:99,124` 两处仍无 using |
| O-1~O-19（R9/R10/R11 观察项） | 维持 | 本轮未发现任何一条可解除或升级的证据；O-17（IconDebounceTimer 锁外发事件，现位点 :993-1010）与 O-8（Stop 锁覆盖不对称，:575-621）本轮全文重读确认机制未变 |

### 3.3 证伪留档（避免后续轮次重复怀疑）

- **`FileService.PathResolution.cs` 链接目标为驱动器根时的「盘符相对路径」疑点**：初判存在缺陷——链接（junction/symlink）指向 `D:\` 时，递归 `TryResolvePathSegments("D:\")` 返回的 `current` 若被 `NormalizeResolvedPath` 去掉尾分隔符变成 `D:`，外层循环的 `Path.Combine("D:", "sub")` 会产出盘符相对路径 `D:sub`（`Path.Combine` 对裸盘符不插分隔符，已用 .NET API 实测确认输出 `D:sub`），后续 `File.GetAttributes`/`GetFullPath` 将按进程在该盘的当前目录解析（IFileDialog 等会改写它），得到错误的物理路径。**证伪**：.NET 的 `Path.TrimEndingDirectorySeparator` 契约为「Trims one trailing directory separator **beyond the root** of the path」（dotnet/runtime 源码 XML doc 原文），根路径（`D:\`、`\\server\share\`）的尾分隔符被保留 → 递归返回 `D:\` → `Path.Combine("D:\", "sub") = "D:\sub"`，链路正确。另核：迁移重叠检查消费方 `TryResolvePathIdentity`（`FileService.cs:2429-2443`）对存在路径再经 `Win32Helper.TryGetFinalPath` 二次取真身路径，双保险。**不立案**。
- **`EverythingInstallationDetector.Detect`（:71-97）**：`Process.GetProcessesByName("Everything")` 逐元素 `using`，句柄探测 `OpenProcess/OpenProcessToken/GetTokenInformation` 全部 `try/finally CloseHandle`，注册表键全部 `using`。无泄漏位点，不立案。
- **`WidgetGroupSwitchRequestCoordinator`**：Cancel/Dispose 回调刻意移出 `_gate`（注释留痕）、`Cancel` 捕获 ODE、`Dispose` 用 `Interlocked.Exchange` 幂等——并发纪律为正面范式，不立案。
- **`FileService.CaseOnlyRename`**：两跳移动 + 失败回滚 + 回滚失败聚合异常；`ThrowIfExactDestinationEntryExists` 的 Ordinal 语义在大小写不敏感文件系统上只可能拒绝同名冲突、不可能误伤 case-only 重命名本身。不立案。

---

## 4. 新发现问题清单

### A-01 ｜ LocalizationService 默认「跟随系统」语言下，每次 T() 本地化取词都同步读一次注册表 ｜ P3

- **位置**：`src/DeskBox/Services/LocalizationService.cs:159-174`（`T()` 每次调用进入 `CurrentCultureName` switch）、`:37-49`（`CurrentCultureName` getter）、`:265-297`（`ResolveDefaultLanguage()` 内 `Registry.GetValue`，:269-272）；默认值 `src/DeskBox/Models/CoreSettingsSlice.cs:21`（`Language = "System"`）
- **触发条件**：语言设置为「跟随系统」（**出厂默认值**，未显式选语言的全部安装均命中）时，任意一次本地化取词。`T()` 在仓库有 1,200+ 处调用（grep `.T("` 粗计 1,273 处），覆盖全部 ViewModel 展示属性与控件文案，包括列表项级属性（`TodoItemViewModel`、`QuickCaptureItemViewModel` 的逐条目属性求值、过滤标签文案 `FormatFilterText` 等）。
- **影响**：每次取词额外执行一次 `Registry.GetValue(HKCU\Software\DeskBox, InstallLanguage)`——Win32 侧需打开键句柄 + 查询 + 关闭，典型微秒级；单次无感，但在列表虚拟化/整页绑定求值/搜索弹窗等一次交互数百至数千次 `T()` 的路径上累积为可测量的额外开销，且属无记忆化的重复系统调用（该注册表值仅在安装/修复时写入，进程生命周期内恒定）。IsEnglish/IsChinese 等派生属性（:51-53）同样逐次触发。功能正确性不受影响（读值恒定），属热路径性能缺陷。
- **根因机制**：`CurrentCultureName` 是「每次访问即重算」的派生属性；安装器语言特性接入时（DEF-062 同期的安装语言落注册表机制）未对 `ResolveDefaultLanguage()` 的结果做任何记忆化，而它的调用频率被 `T()` 的取词频率放大。
- **证据**：
```csharp
// LocalizationService.cs:159-161（每次 T() 都走 CurrentCultureName）
public string T(string key)
{
    var table = CurrentCultureName switch { ... };
// :37-48 —— language == LanguageSystem 时逐次进入 ResolveDefaultLanguage
public string CurrentCultureName
{
    get {
        string language = LanguageSetting;
        if (language == LanguageSystem) { language = ResolveDefaultLanguage(); }
        return language; }
}
// :269-272 —— 每次打开/查询/关闭注册表键，无缓存
var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\DeskBox",
    "InstallLanguage", null) as string;
// Models/CoreSettingsSlice.cs:21 —— 默认即走该分支
public string Language { get; set; } = "System";
```
- **建议修法（最小侵入）**：`ResolveDefaultLanguage()` 结果做静态记忆化（该值仅在安装器写入、进程内不变，可直接 `Lazy<string>`/静态字段缓存一次）；若担心极少数场景注册表值中途变化，可按 `LanguageSetting` 键缓存并在 `SetLanguage` 时失效。`T()`/`CurrentCultureName` 的对外语义零变化。
- **置信度**：高（调用链、默认值、无缓存均可静态确证；单次注册表查询成本为常识量级，未运行基准）。

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-20 | `ReconnectTimer_Tick`（async void，UI 线程）在两次 await（`Task.Run` 路径解析 + `ProbeFolderAccessAsync`）之后不复核 `_reconnectPath`/代际：若 tick 在途期间用户把格子切到别的文件夹（`Stop()` 已 gen++、清 `_reconnectPath`），而旧离线路径恰好在同一窗口恢复在线，tick 继续走 `await StartAsync(旧路径)` → 再次 `Stop()` 杀掉新文件夹刚建的 watcher 并改为监视旧目录，格子显示与监视目标错位（下次导航自愈）。该竞态机制先于 round-11 修复存在（修复前的探测 await 已打开同样窗口），DEF-117 的 `Task.Run` 化仅把窗口从「探测时长」扩为「解析+探测时长」，非修复引入的回归；触发需「离线路径重连 tick 在途 × 旧路径恰好恢复 × 用户同时切换」三重巧合 | `FolderWatcherService.cs:851-913`（await 后仅 :864 的 `_isDisposed` 前置检查，:891 无二次校验） | 高（机制）/ 低（触发面） |
| O-21 | `StartAsync` 双失败分支的代际守卫（:268-274）与随后的 `SetHealth`/`BeginReconnect`（:276-279）之间、以及 :245 处 `BeginReconnect`（:234-240 守卫之后）存在毫秒级 TOCTOU：并发 Start/Stop 在 lock 释放后推进代际时，stale 路径仍可能登记重连。`BeginReconnect` 本身持锁写、`ReconnectTimer_Tick` 消费时以 `_reconnectPath` 为准，后果限于一次多余重连探测。属 round-11 新增守卫留下的残余窗口（修复前此处完全无守卫），不构成修复回归 | `FolderWatcherService.cs:267-280` | 高（机制）/ 无（实际后果） |
| O-22 | `WeatherWidgetViewModel.Dispose()` 直接 `_weatherService.Dispose()`（:764）。生产路径 `WeatherWidgetContentProvider.CreateDetachedContent`（:20-28）不传服务、由 `WeatherWidgetContentAdapter`（:35）`weatherService ?? new WeatherService()` 逐格子新建——所有权私有，Dispose 正确。但 `ServiceRegistry.cs:42` 同时注册了单例 `WeatherService`（全仓无任何 `GetRequiredService<WeatherService>` 消费方，属 ARC-04「DI 注册未接线」同族）；若未来有人把 DI 单例接进 adapter，首个关闭的天气格子将杀掉共享实例、其余格子刷新抛 ObjectDisposedException。属「所有权陷阱」标记，非现行为缺陷 | `ViewModels/WeatherWidgetViewModel.cs:764`、`Controls/WidgetContents/WeatherWidgetContentAdapter.cs:35`、`Services/ServiceRegistry.cs:42` | 高（机制）/ 无（当前触发面） |
| O-23 | `WidgetStackItem.PreviewOne => Members[0]`（:31）与 PreviewTwo~Four（:33-37，空集合时 `Math.Min` 下探到 `Members.Count - 1 = -1`）对空 Members 直接越界。当前 `WidgetStackGroupingService` 的全部建组路径（自定义规则 `Count>0` 过滤、Other 仅在 `unmatched.Count > 0` 时建组、Loose 单元素）不可能产出空组，实际不可达；若未来新增建组入口需先保证非空。防御性备注 | `ViewModels/WidgetStackItem.cs:31-37` | 高（机制）/ 无（当前触发面） |

---

## 6. 统计

- **新立案**：1 条 —— P0×0，P1×0，P2×0，**P3×1**（A-01 默认语言下 T() 逐次注册表读取）
- **观察项**：4 条（O-20 ~ O-23）
- **已知模式新位点**：4 行（O-9 家族×2、DEF-080 家族×2）
- **存量复核**：round-11 修复批（DEF-117/118/119）逐行复核**全部正确落地、无回归**；挂账条目 9 组锚点逐条给当前树证据，**无改判**；O-1~O-19 维持
- **证伪留档**：1 项（PathResolution 驱动器根链接的盘符相对路径疑点，经 dotnet/runtime 源码契约与 .NET API 实测双重证伪）+ 3 项并发/生命周期正面核验（EverythingInstallationDetector、WidgetGroupSwitchRequestCoordinator、CaseOnlyRename）
- **零审查历史盘点结论**：160 个文件名零提及文件中，本轮全文深读约 5,900 行（DesktopOrganization 引擎家族、FileService.PathResolution、LocalizationService、Todo/Weather ViewModel 主家族、搜索/文件栈小服务群），模式扫描覆盖其余全体；**async void 与同步等待在零历史集合中零命中**。DesktopOrganization 自动整理引擎（移动用户文件的核心数据面）经 10 文件全文审读：状态机全转移持锁 + 代际校验、抑制注册表指纹校验 + 2 分钟过期、基线「不完整捕获拒替换」、扫描器 junction/隐藏/占位/临时四重排除、批量上限排序确定性——工程化水位为本轮所见最高，零立案。
- **总体结论**：连续第七轮 P0/P1 = 0；本轮新立案仅 1 条性能卫生级位点。核心服务与数据面在 round-11 修复后维持历史最高加固水位。
