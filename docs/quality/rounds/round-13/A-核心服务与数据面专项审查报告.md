# R13-A 核心服务与数据面专项审查报告（round-13）

> 审查基线 commit `7387efeb`（wip/fix-bug，工作树干净）/ 纯静态审查，未运行任何构建或测试
> 审查代理：R13-A「核心服务与数据面」；产出供主流程逐条核验

---

## 1. 审查范围与方法

### 1.1 差异化声明

- **基线漂移极小**：R11 基线（`8a8a5bde`）以来仅两个代码提交——`cfdf8059`（R11 修复批）与 `af25fd28`（R12 修复批，DEF-120~124）。除这两批修复 diff 外，全部源文件与 round-09~12 四轮深读时的版本逐字节一致，前轮全文审读结论可直接继承。
- **本轮主攻方向**（按任务指令）：① 以挑剔视角逐行验证 round-12 修复批（尤其 DEF-123 两处守卫的正确性与完备性）；② 补齐仍未深读的盲区：FolderWatcherService 整读复核、EverythingSearchService 全文、WidgetManager 非 ZOrder 分部、TodoWidgetViewModel.DetailAndAttachments 全文、零提及 Services 文件盘点。
- **不再重复深读**：SettingsService、FileService 家族、Cloud/WebDav/Updater、MusicVolume、ResilientJsonStore、TodoRecurrence、QuickCaptureService、DirectStartup 族、DesktopOrganization 引擎 10 文件、LocalizationService 全文——round-09~12 已全文主审，本轮仅做台账锚点定点复核。

### 1.2 覆盖率声明

**全文逐行读（本轮新覆盖或修复批复核）**：

- `Services/FolderWatcherService.cs`（1,075 行整读——R11 全读 + R12 两批修复后的第一轮完整复核，重点核对 DEF-117/123 修复后的守卫链语义）
- round-12 修复批 diff（commit `af25fd28`，18 文件）逐行复核：`FolderWatcherService.cs`（DEF-123）、`FileSurfaceContent.ImportProgress.cs`（DEF-124）、`LocalizationService.cs`（DEF-120）、`DesktopOrganizationSettingsSection.xaml.cs` + `DesktopOrganizationTaskView.xaml.cs`（DEF-121）、`FileSurfaceContent.ShortcutDrop.cs` + 12 语言键（DEF-122）
- `Services/WidgetManager.cs` 主文件（2,912 行全文）+ `WidgetManager.Storage.cs`（1,670 行全文，含托管存储迁移事务/回滚/重试全路径）
- `Services/EverythingSearchService.cs`（688 行全文，R9 后首次重读，重点核对 DEF-044/045/103 修复在位性）
- `ViewModels/TodoWidgetViewModel.DetailAndAttachments.cs`（489 行全文——Todo VM 家族五个分部至此全部深读过一轮）
- 零提及 Services 文件深读：`ManagedStorageDesktopShortcutService.cs`（382）、`ShortcutLaunchPolicy.cs`（247）
- `ViewModels/WidgetViewModel.SortingAndWatchers.cs` 监视器接线段（:200-420，ConfigureFolderWatchersAsync / ProcessFolderChangedAsync 异常面）
- `Services/FileService.PathResolution.cs`（189 行重读，核 TryResolveExistingPathForTraversal 异常面）
- `Services/LocalizationService.cs` 默认语言解析段（:255-310）

**模式扫描 + 命中处精读**：

- **WidgetManager 其余非 ZOrder 分部 13 个文件**（Groups 2,771 / FeatureWidgets 1,241 / CapsuleArrangement 1,002 / TrayAnimation 573 / QuickLook 284 / CoordinatedMove 250 / Surfaces 232 / BulkAppearance 206 / SurfaceContent 185 / GroupDragPerformance 134 / ShowDesktop 120 / Memory 117 / Diagnostics 87）：按 `async void` / `.Wait(`/`.Result`/`GetAwaiter().GetResult()` / `Task.Run` / `File.Delete`/`Directory.Delete` / `File.Write*` / 文化敏感 Parse/ToString / `new Thread`/`new Timer` / `Process.*` / `AllocHGlobal` / 裸 `?.Invoke` / `Widgets.*` 锁外写横向扫描——**除 FeatureWidgets 的 DEF-070 既有位点与 Groups:2285 单个裸事件外全部零命中**；Groups.cs 另抽读事务保存/组切换段（:490-620）确认质量
- **零提及 Services 文件全量盘点**：文件名在 `docs/quality/` 全目录零提及者 100 个，剔除 AOT `*Fixture` 测试装置后按上述模式全扫，唯一命中（ManagedStorageDesktopShortcutService 的 `File.Delete`）经全文精读确认为「存储元数据所有权校验后删自家快捷方式」的防御性位点，不立案；其余零命中
- 台账锚点 grep 定点复核（§3）

**未覆盖及原因**：

- round-06~12 已全文主审的持久化/文件安全大文件（SettingsService、FileService 主体、DeskBoxDataBackupService、CloudBackupService、AppUpdateService、QuickCaptureService、DesktopOrganization 引擎）：基线无漂移，仅锚点复核
- 12 语言 JSON：R12 整改批刚做过 2,897 键 parity 门禁，本轮仅复核新增单键的 12 语言对齐（§3）
- `WidgetManager.ZOrder.cs` / `WidgetLayerService.cs`：round-06~09 反复主审面，基线无漂移，未触碰

---

## 2. 已知模式新位点

（已知机制在新位点出现，一行一条，不展开）

| 已知模式 | 新位点 | 说明 |
|---|---|---|
| THR-06 家族（跨线程裸读写状态字段） | `Services/FolderWatcherService.cs:247`（StartAsync 主路径 `_lastError = null` 锁外写）、`:391`（`TryStartQueryWatcherAsync` catch 内 `_lastError = ex.Message` 锁外写） | R11 已列同文件家族位点（:99,167,226,361,438,491,556,698,781），本轮整读补录两个未列行号；仅诊断用途，水位同族 |
| DEF-092 家族（死代码残留） | `Services/WidgetManager.cs:1974-1977` `public void RemoveWidget(string widgetId)` | 全仓（src + tests）零调用点；且方法体为 `_ = RemoveWidgetAsync(widgetId)` 无守卫 fire-and-forget——若未来被「顺手接线」会同时复活 DEF-078 家族的静默失效面。3 行包装器，随卫生批删除或注明保留意图 |

---

## 3. 存量复核（范围内挂账条目现状）

### 3.1 round-12 修复批复核（本轮重点，逐行当前树验证）

| 条目 | 结论 | 证据 |
|---|---|---|
| **DEF-120（默认语言 T() 逐次注册表读取）** | ✅ 修复正确（附一条语义备注，见 O-25） | `LocalizationService.cs:274` `private static readonly Lazy<string> ResolvedDefaultLanguageInstance = new(ResolveDefaultLanguageCore)`；`:276-278` `ResolveDefaultLanguage()` 变纯取值；原方法体整段移入 `ResolveDefaultLanguageCore`（:281 起），注册表读取 + 12 语言白名单 + CurrentUICulture 回退逻辑逐行未变。Lazy 线程安全，避开 EXC-06 族 DCL 模式。语义备注：Lazy 同时冻结了 OS UI 语言回退分支（原实现每次求值可感知会话中改系统显示语言），影响面仅「语言=跟随系统 × 会话中改 OS 显示语言」，见 §5 O-25 |
| **DEF-121（列表分隔符硬编码「、」）** | ✅ 修复正确 | 三个站点全部替换：`DesktopOrganizationSettingsSection.xaml.cs:844,865`（原 `Join("、",…)` ×2）+ `DesktopOrganizationTaskView.xaml.cs:300`；两类各新增静态属性 `ListSeparator`（SettingsSection:977-980、TaskView:256-258）均走 `IsChinese ? "、" : ", "`，与 SettingsViewModel 既有范式一致 |
| **DEF-122（快捷方式后缀硬编码英文）** | ✅ 修复正确 | `FileSurfaceContent.ShortcutDrop.cs:96` `GetShortcutDisplayName(source, string localizedSuffix)` 参数化；:107-110 空名兜底 `localizedSuffix.TrimStart(' ', '-')`、正常路径 `name + localizedSuffix`；:146 调用点传 `T("Widget.CreateShortcutSuffix")`。12 语言键逐一核对全部存在且以 `" - "` 前缀起头（de: ` - Verknüpfung.lnk`、zh-CN: ` - 快捷方式.lnk` 等 12/12），TrimStart 兜底还原出「快捷方式.lnk」形态的裸名词，语义自洽 |
| **DEF-123（round-11 修复引入的重入竞态）** | ⚠️ 主目标达成、守卫链自洽，但**成功分支存在同族残留**（本轮立案 A-01） | 逐行验证：`StartAsync` 入口快照（:213-217 lock 内取 `entryGeneration`）→ 解析 await（:226-229 包 Task.Run）→ await 后校验（:231-237 `_isDisposed \|\| entryGeneration != _watchGeneration`）→ `Stop()`（:239，锁内 `_watchGeneration++`）→ `startGeneration` 快照（:248-252）→ 探测 await 后校验（:255-261）→ WatchedPath 认领前再校验（:272-277）。守卫链语义经完整推演自洽：入口快照先于一切 await（快照序 = 调用序），任何先行调用者的快照必然被后继调用者的 `Stop()` 递增作废，「最后通过校验者胜」。`ReconnectTimer_Tick` 顶部快照（:879-885）+ 解析 await 后校验（:911-917）+ 探测 await 后校验（:920-926）两处均落实——R12 整改方案 W1（tick 探测 await 残留窗口）确认闭合。**但 `await TryStartQueryWatcherAsync` 之后的成功分支（:302-312）无任何代际复核**，与失败分支（:289-295 有守卫）不对称，机制详见 §4 A-01 |
| **DEF-124（封送 lambda 无守卫）** | ✅ 修复正确（附一条残余备注，见 O-24） | `FileSurfaceContent.ImportProgress.cs:238-250`：`try { await CompleteTrackedImportAsync(completionState); } finally { completion.TrySetResult(true); }`——await 抛出时等待方必然被释放；`TrySetResult` 幂等无双重置风险。残余：`TryEnqueue` 返回值未检查（:238），enqueue 失败路径仍会挂起等待方——后果受进程退出限定，不构成同水位缺陷，见 §5 O-24 |

### 3.2 round-11 修复批再证（基线无漂移 + 本轮整读）

| 条目 | 结论 | 证据 |
|---|---|---|
| DEF-117（FolderWatcher UI 线程同步探测） | ✅ 在位 | 入口/重连解析均包 Task.Run（:226-229、:903-909）；`ProbeFolderAccessAsync`（:342-345）为同步体 Task.Run 包装；双失败分支改异步探测（:288） |
| DEF-118（Process 数组全量 Dispose） | ✅ 在位（未触碰，基线无漂移） | `DragDropPermissionService.cs:804-836` 与 R12 复核版本一致 |
| DEF-119（安装器用户级 .NET 探测点） | ✅ 在位（未触碰） | `installer/DeskBox.Dependencies.iss` 与 arm64 变体与 R12 复核版本一致 |

### 3.3 挂账条目锚点复核

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| DEF-048（WeatherCodeMapper 声明小写比较未实现） | 维持 | `Helpers/WeatherCodeMapper.cs:100` `string d = description.Trim()` 无 ToLower |
| DEF-049（MSN icon 29→29 恒等映射） | 维持 | `WeatherCodeMapper.cs:209` `29 => 29` |
| DEF-050（MSN 日期解析失败仍追加空行） | 维持 | `WeatherService.cs:497-504`：`TryParse` 失败时 `dateStr` 保持空串仍 `daily.Time.Add(dateStr)` |
| DEF-051（远程 JSON 无大小上限） | 维持 | 按台账维持（`GlanceImageService.cs` / `WeatherService.cs` 位点，基线无漂移） |
| DEF-052（FileMetaService 失败 null 永久缓存） | 维持 | `FileMetaService.cs:164` `_iconCache.GetOrAdd(key, k => LoadIconAsync(k))` 无失效机制 |
| DEF-053（内容刷新任务引用竞态） | 维持 | `SearchEngineService.cs:515` finally lock 内无条件 `_deskBoxContentRefreshTask = null` |
| DEF-054（Saka/Bangla 历近似漂移） | 维持（R11 全读定性，基线无漂移） | 未重读，沿用 R11 证据 |
| DEF-056（Migration_1_To_2 与 2_To_3 重复段） | 维持 | 未触碰（R9 证据沿用） |
| DEF-070 家族（FeatureWidgets 锁外写点） | 维持 | `WidgetManager.FeatureWidgets.cs:707,710,759-760,890,904` 逐点确认仍在原位（本轮 grep） |
| DEF-076（遗留 settings.json 一次性迁移非原子） | 维持 | `SettingsService.cs:941` `await File.WriteAllTextAsync(_settingsPath, json)` 仍裸写 |
| DEF-078（SMTC `Try*Async` 裸调簇） | 维持 | `MusicSessionService.cs:233-236` 起六处 `Try*Async` 裸调仍在 |
| DEF-079（JIT 音量后端 Volume RCW 不释放） | 维持 | `MusicVolumeService.cs` `AudioSessionHandle.Dispose()` 仅 `ReleaseComObject(Control)` |
| DEF-080（ar-SA 天气解析/缓存错乱） | 维持 | `WeatherService.cs` `ToString("yyyy-MM-dd` 4 处，无 InvariantCulture |
| DEF-101（广播隔离泛化） | 维持 | `EverythingSearchService.cs:619` `ConnectionChanged?.Invoke` 已在 R8 S06 全表（:146），非新位点；Groups:2285 单点同族 |
| EXC-06（CitySearchService DCL 无 volatile） | 维持 | `CitySearchService.cs:54,65` 机制不变 |
| MEM-02（SoftwareBitmap 未确定性释放） | 维持 | `IQuickCaptureClipboardReader.cs:99,124` 两处仍无 using |
| DEF-110（提醒去重集合跨线程） | 已修复（再证在位） | `TodoReminderService.cs:31` `ConcurrentDictionary<string, byte>`，Clear 三处（:96,:124,:138）线程安全 |
| O-20（tick 双 await 后不复核代际） | **已解决（随 DEF-123 修复）** | `FolderWatcherService.cs:911-917,920-926` 两处 await 后均复核 `entryGeneration`——O-20 描述的机制已消除 |
| O-21 / O-22 / O-23（R12 观察项） | 维持 | O-21 守卫-结算间毫秒级 TOCTOU（:267-280）机制未变；O-22/O-23 未触碰 |
| O-1~O-19（R9~R11 观察项） | 维持 | 基线无漂移，未发现可解除或升级的证据 |

### 3.4 证伪留档（避免后续轮次重复怀疑）

- **DEF-123 守卫链「除成功分支外自洽」的完整推演**：对 StartAsync 与 ReconnectTimer_Tick 的并发交错做了全排列推演（双 StartAsync 交错、导航 Stop 与 StartAsync 交错、tick StartAsync 与导航交错），结论：入口快照先于一切 await，保证快照序 = 调用序；`startGeneration` 在本调用 `Stop()` 之后取得，保证探测/查询窗口内被后来者 Stop 时必然失效；「最后通过解析校验者胜」等价于修复前同步语义的「后到者赢」。唯一漏洞即 A-01 所述成功分支结算段。后续轮次勿再怀疑入口/探测/双失败三段守卫本身。
- **`FileService.TryResolveExistingPathForTraversal` 的理论异常逃逸面**：`Path.GetFullPath` 的 catch 过滤器（:82）不含 `SecurityException`/`IOException`，`StartAsync` 亦无 try/catch——但两条调用链终点均有兜底：导航路径 `WidgetViewModel.Navigation.cs:302-318` `catch (Exception)` 记日志并回退重挂 previousPath；tick 路径 `ReconnectTimer_Tick` catch 全捕获（:943-958）。实际不可达为生产缺陷，不立案。
- **EverythingSearchService 门控释放语义再证**：DEF-044 修复的超时竞速（:255-330）在超时/IPC 失败/SDK 缺失/OCE 四分支下的 `_nativeGate` 释放归属逐分支核对，均恰好一次；`Dispose` 忙时跳过 CleanUp 且日志化（:673-681）。本轮全文重读零立案。
- **WidgetManager 非 ZOrder 分部模式扫描零命中**：13 个分部（约 7,200 行）对并发/同步等待/文件删除/进程/原生内存/文化敏感格式化全模式零命中，唯一事件广播点（Groups:2285）已归 DEF-101 既有水位。

---

## 4. 新发现问题清单

### A-01 ｜ FolderWatcherService.StartAsync 成功分支缺代际复核：旧调用的结算可覆盖新调用健康状态并取消其已排程重连（DEF-123 修复不完备残留） ｜ P3

- **位置**：`src/DeskBox/Services/FolderWatcherService.cs:302-312`（`await TryStartQueryWatcherAsync` 之后的 else 分支：`SetHealth(Degraded)` + `lock { _reconnectPath = null; _reconnectAttempt = 0; }`）；对照失败分支的同位守卫 `:289-295`；竞态另一侧 `:285`（query 启动 await，内部代际检查 :376-386 使 stale 调用必然返回 false）
- **触发条件**：同一 `FolderWatcherService` 上两次 `StartAsync` 交错（两次快速导航文件夹格子，或导航与 `ReconnectTimer_Tick:936` 的 `StartAsync` 并发）。需要旧调用的 query 启动延续落在新调用完整结算**之后**——旧目标为大目录/慢存储提供者（`GetFolderFromPathAsync` + `GetItemsAsync` 慢），新目标为本地已删除目录（解析与探测快速失败并 `BeginReconnect` 排程 2s 重连）。
- **影响**：新调用已 `BeginReconnect(newPath)`（`WatchedPath = newPath`、重连计时器已武装）后，旧调用的 else 分支无条件 ①`SetHealth(Degraded)` 覆盖新调用写入的 `Unavailable`/`Watching`；②`_reconnectPath = null; _reconnectAttempt = 0` 清掉新调用的重连登记——计时器到点后 `ReconnectTimer_Tick` 读到空 `_reconnectPath` 直接早退（:887-890），**重连被静默取消**。终态：无任何活动 watcher（旧调用的新生 native watcher 已被新调用的 `Stop()` 释放）、健康显示 Degraded、永不重连，格子内容冻结直至用户重新导航。与 DEF-123 同级同族（P3：错挂/失刷新 + 重新导航自愈），但后果从「错挂旧目录」变为「重连计划被取消的完全失监视」。
- **根因机制**：R12 整改给 StartAsync 的解析窗口（:231-237）与失败分支的探测窗口（:289-295）都补了代际守卫，但成功分支在 `await TryStartQueryWatcherAsync` 返回后**无 await 地**执行 `SetHealth` 与重连状态清零，且无 `startGeneration != _watchGeneration` 复核——stale 调用（其 query 启动内部检查 :376-386 已因代际失配返回 false，`nativeStarted=true` 指向的 watcher 也已被新调用 `Stop()` 释放）恰好落入 `nativeStarted && !queryStarted` → else 分支，把新层的健康/重连状态当作自己的收尾改写。
- **证据**：
```csharp
// FolderWatcherService.cs:284-312（节选）
bool nativeStarted = TryStartLegacyWatcher(folderPath);
bool queryStarted = await TryStartQueryWatcherAsync(folderPath, generation);   // :285 旧调用在此挂起
if (!nativeStarted && !queryStarted)
{
    FolderWatcherHealth probeHealth = await ProbeFolderAccessAsync(folderPath);
    lock (_lock) {
        if (_isDisposed || startGeneration != _watchGeneration) return;        // :289-295 失败分支有守卫
    }
    SetHealth(...); BeginReconnect(folderPath);
}
else
{
    SetHealth(nativeStarted && queryStarted ? Watching : Degraded);           // :304-306 无守卫
    lock (_lock) { _reconnectPath = null; _reconnectAttempt = 0; }            // :307-311 无守卫
}
// 交错序：A(X)=慢 query → B(Y)=快速失败，BeginReconnect(Y) 排程重连 → A 的 query 延续
// （内部 :378 generation != _watchGeneration → false）→ A 走 else：SetHealth(Degraded) 覆盖、
// _reconnectPath=null 取消 B 的重连；计时器到点 tick 读空路径早退 → 永不重连
```
- **建议修法（最小侵入）**：在 `:285` 的 await 返回后、`if` 判定之前补一次与失败分支同款的锁内复核（`if (_isDisposed || startGeneration != _watchGeneration) return;`），或将 else 分支的 `SetHealth`/重连清零也纳入同款守卫；一行守卫即可闭合，语义与其余三个守卫位点一致。
- **置信度**：机制高（守卫位点不对称可直接静态确证，交错面与 DEF-123 已立案的交错面完全相同）；触发时序中（需两次导航/导航×重连 tick 的毫秒级窗口叠加慢 query 延续，真实用户可复现面低于 DEF-123 原案）。

---

## 5. 观察项

| # | 内容 | 位置 | 置信度 |
|---|---|---|---|
| O-24 | DEF-124 修复残余：`CompleteTrackedImportAsync` 非 UI 分支的 `DispatcherQueue.TryEnqueue(async () => …)` 返回值未检查（:238）——enqueue 失败（DispatcherQueue 已关闭，仅发生于应用退出窗口）时 `await completion.Task` 仍会永久挂起等待方。与 DEF-124 已修的 throw 路径同构，但等待方是线程池后台线程（background），进程退出即终止，实际后果被进程生命周期限定。若求完备可在 enqueue 返回 false 时直接 `completion.TrySetResult(true); return;` | `Controls/WidgetContents/FileSurfaceContent.ImportProgress.cs:238-250` | 高（机制）/ 极低（后果，受进程退出限定） |
| O-25 | DEF-120 修复的语义备注：`Lazy<string>` 记忆化把 `ResolveDefaultLanguageCore` 的 OS UI 语言回退分支一并冻结——语言=「跟随系统」的用户在会话中更改 Windows 显示语言后，应用不再跟随（原实现每次求值会重读 `CurrentUICulture`），需重启应用。主路径（安装器写入的注册表值）进程内恒定，备注不构成回退理由；如需兼顾可在 `SetLanguage`/系统语言变更通知处显式失效缓存 | `Services/LocalizationService.cs:274-278` | 高（机制）/ 低（触发面：会话中改系统显示语言） |
| O-26 | `ReconnectTimer_Tick` 的 catch 块（:943-958）无代际复核：await 之后若有真异常逃逸（内部探测/解析均吞异常，实际近乎不可达，残余面为 OOM 级），无条件 `SetHealth(Unavailable)` 可瞬时覆盖新层健康状态；`BeginReconnect` 侧因 `_reconnectPath` 已被新层改写而基本自愈。与 A-01 同族的最后一处未守卫结算点，触发面远窄于 A-01 | `Services/FolderWatcherService.cs:943-958` | 高（机制）/ 极低（触发面） |

---

## 6. 统计

- **新立案**：1 条 —— P0×0，P1×0，P2×0，**P3×1**（A-01：FolderWatcherService.StartAsync 成功分支缺代际复核，DEF-123 修复不完备残留）
- **观察项**：3 条（O-24 ~ O-26）
- **已知模式新位点**：2 行（THR-06 家族 FolderWatcherService 补录 2 行号、DEF-092 家族 WidgetManager.RemoveWidget 死代码）
- **存量复核**：round-12 修复批（DEF-120~124）逐行复核——**4 条修复正确落地，DEF-123 主目标达成但发现成功分支残留（A-01），DEF-124 附 enqueue 残余备注（O-24），DEF-120 附语义备注（O-25）**；round-11 修复批再证在位；挂账条目 20+ 组锚点逐条给当前树证据，**无改判**；**O-20 由 DEF-123 修复解决（本轮唯一观察项解除）**，其余 O-1~O-19/O-21~O-23 维持
- **证伪留档**：4 项（DEF-123 守卫链「最后通过者胜」语义全排列推演、TryResolveExistingPathForTraversal 异常逃逸面被导航边界兜底、EverythingSearchService 门控释放四分支再证、WidgetManager 非 ZOrder 分部模式扫描零命中）
- **零审查历史盘点结论**：100 个零提及 Services 文件（剔除 AOT 测试装置）全模式扫描仅 1 处命中且为防御性位点；深读 4 个（ManagedStorageDesktopShortcutService、ShortcutLaunchPolicy、WidgetManager 主文件 + Storage 分部——后两者亦属非 ZOrder 分部盲区）；TodoWidgetViewModel 五分部至此全部深读过一轮；WidgetManager 非 ZOrder 13 个剩余分部模式扫描零命中
- **总体结论**：连续第八轮 P0/P1 = 0。round-12 修复批整体质量良好，唯一实质发现是 DEF-123 守卫网在 StartAsync 成功分支的最后一处缺口（A-01，与原案同族同级的毫秒级窗口竞态）；核心服务与数据面维持历史最高加固水位。
