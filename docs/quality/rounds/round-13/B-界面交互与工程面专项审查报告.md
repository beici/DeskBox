# R13-B 界面交互与工程面专项审查报告（round-13）

> 审查基线 commit `7387efeb`（分支 `wip/fix-bug`）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 与 round-09~12 的差异化声明

round-09~12 已精读并连续复核的面（WidgetToolDialogWindow、Glance/Weather/Todo 订阅链、SettingsWindow Closed 链、QuickCaptureSurfaceContent 核心区、FileSurfaceContent 的 KeyboardNavigation/ScrollBars/TransferState/RenderWindow/StackPopoverRename/Navigation/Opening/ImportProgress/StackAnimations/ShortcutDrop/SelectionAndMenus/StackPopover、MusicWidgetContent、WidgetShellContentHost、SettingsSections 8 分部、installer 全部 .iss、publish-aot-retail.ps1、cleanup 脚本等）本轮仅做定点/差异复核。**本轮把深读预算投向从未覆盖或从未逐行读过的盲区**：

- **全读（本轮首次）**：
  - `Controls/WidgetContents/FileSurfaceContent.IconSizing.cs`（153 行）与 `FileSurfaceContent.TextScaling.cs`（146 行）——FileSurfaceContent 14 个非 AOT 分部中唯二从未被历轮 B 报告点名的两个（至此该类全部分部至少点名一次）；
  - `Views/ContentWidgetWindow.ContentSwitching.cs`（433 行，组内容缓存/Prepare-Commit-Rollback 过渡协议的窗口侧半边）、`.TrayAnimations.cs`（212 行）、`.File.cs`（58 行）——ContentWidgetWindow 分部中从未覆盖的三个；
  - `Views/WidgetWindowBase.Backdrop.cs`（753 行，材质签名复用/控制器复用/交互降级/刷新计时器，历轮从未涉及）、`.InputSuppression.cs`（89 行）、`.CoordinatedMove.cs`（95 行）；
  - `Views/DesktopOrganizationWindow.xaml.cs`（321 行，全仓唯一同时挂两个 Closed 处理器 + 最小尺寸子类钩子的窗口，历轮从未点名）；
  - `Views/OnboardingWindow.IntroAnimations.cs`（343 行，代际护栏 + 超时回退的开场动画协议）；
  - `Controls/SearchResultRowControl.xaml.cs`（196 行，搜索结果行自持视觉）；
  - `Services/WidgetManager.Memory.cs`（117 行）；
  - `scripts/quality/static_gate.py`（306 行，历轮机械门禁的载体本身首次审读）、`scripts/start-debug.ps1`（125 行）、`scripts/Test-DeskBoxDirectReleaseConsistency.ps1`（57 行）。
- **分段精读**：`Services/FolderWatcherService.cs` StartAsync/Stop/ReconnectTimer_Tick 全路径（DEF-123 修复逐行核验 + 全部调用图推演）；`Services/LocalizationService.cs` CurrentCultureName/ResolveDefaultLanguage 核心区（DEF-120 核验 + 全仓 culture 赋值点反查）；`Controls/WidgetShell.xaml.cs` 构造/Unloaded/Suspend-Resume 生命周期区（370-489）、StoryboardSlot（42-115）、Attach/DetachHostedContentEvents（1585-1607）、粒子动画协议（3427-3600）、指针进入/退出 + EnsureStoryboards（4117-4217）；`Views/SearchPopupWindow.xaml.cs` 构造/订阅区（40-199）与 Closed 清理链（4508-4548）；`Controls/WidgetContents/FileSurfaceContent.xaml.cs` 内联重命名协议（1627-1815）与拖出会话收尾协议（1257-1480）；`Services/WidgetManager.CapsuleArrangement.cs` 头部 250 行（排列签名/拖拽会话协议，历轮唯一未点名的 WidgetManager 分部）。
- **round-12 修复批复核（本轮重点）**：`af25fd28`（DEF-120~124）全量 diff + 当前树逐路径核验，含 12 语言 JSON 新键的**插入位置与内容逐文件比对**、FolderWatcherService 守卫的**全调用图交错推演**（详见 §3）。
- **tests 抽查**（正确性方向）：`LocalizationResourceContractTests`（261 行全文——parity/占位符/日期字母/动态键/失败面五个用例的断言方向逐一核对）、`LocalizationServiceLanguageTests` 用例清单、static-baseline.json 基线一致性（async_void=249 与当前树 grep 实测 249 吻合）。
- **机械反查**：`string.Join("、"` 全仓 0 残留（DEF-121 修复完整性）；`CurrentUICulture =`/`CurrentCulture =` 赋值点全仓 0 处（DEF-120 记忆化前提）；`SetIsTranslationEnabled(true)` 14 处全量盘点（ANI-06 家族面积核实，R12 只记录了 5 处）。

### 1.2 机械校验（python 只读脚本，stdin 直读，未写任何仓库文件）

- **12 语言键位 parity**：en-US **2897** 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2897 × 12 = 35764 键全对齐；R12 批新增键 `Widget.CreateShortcutSuffix` 后键集）；
- **占位符 arity**（各语言与 en-US 同键 `{n}` 索引集合）→ **0 失配**；
- **.NET 非法日期格式字母**（DEF-039 收紧判定：仅对 en-US 值呈纯日期格式的键做全语言扫描，含西里尔 `г/М/д/Ч`）→ **0 违规**；
- **本地化值孤立花括号**（非 `{n}` 的 `{`/`}`）→ **0 命中**；
- **Format 调用点 arity**（两形态：`Format("Key",…)` 与 `Format(T("Key"),…)`，顶层逗号=实参计数；三元 T 多键分支 3 处逐一手工核对 `DesktopOrganizationTaskView.Presentation.cs:168`、`SettingsWindow.CloudBackup.cs:184`、`SettingsViewModel.GroupNavigation.cs:481`）→ 扫描 **230 处** + 3 处三元 → **0 失配**（DEF-116/122 修复保持；首轮扫描器曾因「键名后首个分隔逗号误计入实参」报 229 假阳性，修正计数口径后归零——该 off-by-one 与 round-10 首扫踩过的是同一坑，留档防复发）；
- **XAML StaticResource/ThemeResource**：36 个 .xaml，197 个唯一引用键，54 个仓外定义——逐一核对全部为 WinUI/XamlControlsResources 平台资源（`AccentButtonStyle`、`TextFillColorPrimaryBrush`、`SystemColor*` 等），另 2 个为正则误报（注释文本 "consumer"、`x:` 前缀截断），**无失效引用**；
- **XAML 事件处理器绑定**：37 类事件名抽取共 **757 处 → 0 处缺失处理器**；
- **native 契约**：`git diff 77f2b4b4..HEAD -- native/` 仅 `README.md` 4 行（round-10/11 已核）→ **ABI 零漂移**（十导出/ABI 2/掩码 511/panic=abort 结论沿用）；
- **scripts/installer 漂移**：`git diff 77f2b4b4..HEAD -- scripts/ installer/` 仅 4 文件（DEF-119 修复的 2 个 Dependencies.iss + static_gate 两文件），冒烟脚本群与 publish 脚本自 round-08 基线零内容变更；
- **模式族计数**：`TryEnqueue(async` 25 处（与 R12 持平）；`async void` 249 处（与 static-baseline.json 基线 249 持平）。

### 1.3 覆盖率声明

全读约 3.3k 行 + 分段精读约 4.5k 行 + 结构扫描/抽样约 3k 行。仍未见逐行全读：`WidgetShell.xaml.cs` 正文其余约 4.2k 行、`FileSurfaceContent.xaml.cs` 正文其余约 2.9k 行（已由历轮分区覆盖 + 本轮拖拽/重命名两协议补读）、`SearchPopupWindow.xaml.cs` 中段（600-2300，历轮已覆盖订阅/计时器/多选/动作/Closed 六区）、`WidgetManager.CapsuleArrangement.cs` 其余约 750 行、`App.xaml.cs`、`run-aot-managed-ui-smoke.ps1`（6967 行）、`publish-aot-audit.ps1`（10498 行，R12 起持续留档的观察项）。基线 `7d201074..7387efeb` 代码 diff 仅 `af25fd28` 的 6 源文件 + 12 语言 JSON，其余历轮复核结论按「文件零变更」直接沿用。

---

## 2. 已知模式新位点（并入既有编号，不新立案）

1. **ANI-06 家族（SetIsTranslationEnabled(true) 从不复位）+9 位点**：R12 只记录了 5 处（Navigation ×3、StackAnimations ×1、SearchPopup:2375）；本轮 14 处全量盘点发现其余 9 处同属该家族且未入账——`Controls/WidgetShell.xaml.cs:2198`、`:2283`（紧凑过渡目标元素）、`:3080`（CompactLiveProgress，壳生命周期内常驻）、`:3469`（particle.Shape，随 StopParticles/画布清空回收）、`Controls/WidgetContents/MusicWidgetContent.xaml.cs:1409`（频谱画布）、`Helpers/DetailPageTransitionHelper.cs:23,57,86`（随记详情页三个过渡元素）、`Views/SearchPopupWindow.xaml.cs:2224`（RecommendedAppsPanel）。全部为元素生命周期结束后翻译属性残留，与挂账口径同机制（当前无可见破损，维持 P3 挂账、不升级）。
2. **DEF-080 家族（culture 敏感时间戳，纯显示级）+4 位点**：`ViewModels/TodoItemViewModel.cs:113`（`CreatedAt.ToLocalTime().ToString("yyyy/M/d HH:mm")` 嵌入 `Todo.Detail.Created`）、`:674-675`（`FormatDueDateTime` 两分支）、`:681`（`FormatDueTime`）——Todo 详情/到期/贪睡文本在 ar-SA 区域下呈阿拉伯-印度数字。与 R12 记录的 OrganizationHistoryEntry/QuickCaptureItemViewModel 等位点同水位（显示级、无解析消费方），并入 DEF-080 观察面。

---

## 3. 存量复核（round-12 修复批 + 范围内挂账条目现状）

本基线相对 round-12 收口（`7d201074`）代码变更仅 `af25fd28` 的 6 源文件 + 12 语言 JSON（git 双向核实），下列逐条为当前树证据；其余挂账条目所在文件零变更，round-12 复核结论直接沿用。

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-122（快捷方式后缀本地化，新键 ×12）** | **✅ 修复复核正确** | ①**键存在与位置**：12 语言 `Widget.CreateShortcutSuffix` 全部紧邻 `Widget.CreateShortcut` 之后插入（en-US/zh-CN/ja-JP/de-DE/pt-BR 在 :2650，fr-FR/es-ES/ru-RU/ar-SA/hi-IN/bn-BD 在 :2677，zh-TW 在 :2679），插入点一致、无错位；②**内容**：12 值全部为「前导空格 + 连字符 + 空格 + 本地化名词 + `.lnk`」（zh-CN ` - 快捷方式.lnk`、ja-JP ` - ショートカット.lnk`、ar-SA ` - اختصار.lnk` 等），与 R12 方案 W5 预告值逐一相符，且均与各语言 Explorer 原生命名一致；③**调用点**：`FileSurfaceContent.ShortcutDrop.cs:146` `GetShortcutDisplayName(source, T("Widget.CreateShortcutSuffix"))`——后缀作参数传入静态助手（private static 与实例 T() 的边界按 R12 方案核正意见处理）；④**空名兜底**：`:107-109` `localizedSuffix.TrimStart(' ', '-')` 对全部 12 值正确剥出裸名词（所有值前缀恰为 `' '`+`'-'` 各一）；⑤Format 调用点 arity 复扫 0 失配（§1.2），键集 2897×12 对齐。 |
| **DEF-123（FolderWatcher 重入竞态守卫）** | **✅ 修复复核正确（含已留档接受残余的再确认）** | `FolderWatcherService.cs:213-217` 入口快照 `entryGeneration`（首个 await 之前）、`:231-237` 解析 await 之后、`Stop()` 之前校验；`ReconnectTimer_Tick` `:879-885` 顶部快照、`:911-917` 解析 await 后、`:920-926` probe await 后双校验。**全调用图推演**：`StartAsync` 全仓仅 3 个调用点——`ConfigureFolderWatchersAsync`（WidgetViewModel.SortingAndWatchers.cs:246/252）先 `Stop()`（:223-224，`FolderWatcherService.cs:583` 锁内 `_watchGeneration++`）再调用，任何旧调用在恢复点必因代际前移退出；`ReconnectTimer_Tick:936` 的 StartAsync 自带同款入口快照。检查通过段（:231→:239）与 tick 两次校验后段均在 UI 线程同步执行、无让出点，无新窗口。**R12 整改方案 W1-4 已留档的接受残余**（`rectify/R12-remediation-plan.md:16`：「入口快照相等的一对 StartAsync（tick-vs-ConfigureFolderWatchers 极窄窗口）退化为先恢复者赢」）经本轮独立推演**确认属实且已正确定性**——tick 校验依赖「更新一层已 bump 代际」，而更新的 StartAsync 在自身解析 await 期间尚未 Stop，此窗口内 tick 若先完成解析+探测且旧路径恰已恢复 Watching，可抢占并令新目录调用代际失配退出；前置条件比原缺陷更苛刻（tick 恰落在新调用解析窗口内 + 旧路径恰好恢复可用），按台账「同机制同位点不重复立案」不另立，维持 R12 已接受口径。 |
| **DEF-124（导入完成封送 try/finally）** | **✅ 修复复核正确** | `FileSurfaceContent.ImportProgress.cs:238-251`：`TryEnqueue(async …)` 内 `try { await CompleteTrackedImportAsync(…); } finally { completion.TrySetResult(true); }`——`CancelAndResetTrackedImport`（:310，内部 `Cancel()` :367 可抛 AggregateException 的路径）异常时等待方必然释放；异常沿 async lambda 语义进全局兜底（与修复前一致，调用方对该结果仅作「UI 段已完成」信号）。无回归。 |
| **DEF-120（默认语言 Lazy 记忆化）** | **✅ 修复复核正确（前提经全仓反查成立）** | `LocalizationService.cs:274` `private static readonly Lazy<string> ResolvedDefaultLanguageInstance = new(ResolveDefaultLanguageCore);`（默认 ExecutionAndPublication，线程安全，避开 EXC-06 家族 DCL）；`ResolveDefaultLanguage()`（:276-279）薄委托；原方法体原样改名 `ResolveDefaultLanguageCore`（:281 起）。**前提核实**：全仓 grep `CurrentUICulture =`/`CurrentCulture =` 赋值点 **0 处**——进程内 OS UI culture 恒定，记忆化语义安全；注册表值仅安装器写入的前提与 `ResolveDefaultLanguageCore` 注释一致。残余语义（System 兜底路径下运行中变更 OS 显示语言不再被拾取、需重启）属修复注释已明示的进程级取舍，不立案（见 §5.3）。 |
| **DEF-121（ListSeparator ×3 站点）** | **✅ 修复复核正确且无残留** | 两类各新增 `private static string ListSeparator => App.Current.LocalizationService.IsChinese ? "、" : ", "`（DesktopOrganizationTaskView.xaml.cs:257-259、DesktopOrganizationSettingsSection.xaml.cs:976-978），与 SettingsViewModel 既有范式逐字对齐；三个 Join 站点（TaskView:300、SettingsSection:844、:865）全部改用。**全仓 grep `string.Join("、"` = 0 残留**。 |
| **DEF-097 / DEF-078 / ANI-06 / DEF-080 / O-9 家族** | **维持（本轮补位点见 §2）** | 相关位点文件除 6 个修复文件外零变更；static_gate 剪贴板配对/契约重放机制经本轮审读（§1.1）确认在位。 |
| **测试时序纪律（DEF-067/114 类）** | **修复保持** | `LocalizationResourceContractTests` 五用例断言方向全部正确（parity 用 `Assert.Equal` 键集全等、日期字母白名单 `yYmMdDhHsStTfFgK` 有意排除 z/Z、动态键逐一 ContainsKey、打包 resw 键集全等）；无时间窗型弱断言复发。 |

---

## 4. 新发现问题清单

### B-01｜SearchPopupWindow 的关闭观察器子类钩子从不移除，`_isPopupCloseWatcherInstalled` 为只写字段——全仓唯一无对称卸载的 comctl32 子类化
- **优先级**：P3（代码卫生 + 潜在悬空 thunk 面；当前流程无实际危害，故 P3）
- **位置**：`src/DeskBox/Views/SearchPopupWindow.xaml.cs:79-80`（字段声明）、`:168-176`（构造期 `SetWindowSubclass` 安装）、`:4508-4542`（`OnWindowClosed` 全量清理链——缺 `RemoveWindowSubclass`）
- **触发条件**：搜索弹窗销毁（服务 dispose 或应用退出）时；以及任何未来改动使「托管窗口对象先于 HWND 销毁被 GC」的卸载顺序变更。
- **影响**：当前实现中弹窗子类过程（`PopupCloseWatcherSubclassProc`，仅诊断日志）由窗口实例的 readonly 委托字段扎根，销毁期回调安全，comctl32 在窗口销毁时随之丢弃子类链——故无即时危害。但：①本仓其余 **9 处**子类化全部有对称卸载（`AppLifecycleRecoveryWatcher.cs:251`、`DisplayAreaWatcherService.cs:266`、`GlobalHotkeyService.cs:106`、`SearchHotkeyService.cs:413`、`WidgetDisplayChangeWatcher.cs:177`、`ReleaseNotesWindow.xaml.cs:424`、`StackPopoverHostWindow.cs:298`、`WidgetWindowBase.Grouping.cs:162`、`DesktopOrganizationWindow.xaml.cs:229-241` 且后者还在 WM_NC_DESTROY 兜底），本窗口是唯一破例；②`_isPopupCloseWatcherInstalled` 写后永不读（只写死状态），安装失败的返回值被记入日志却无任何降级或复检路径；③若卸载顺序未来变化（对象先亡、HWND 后毁），残留子类的函数指针 thunk 将指向已回收的委托，属崩溃级隐患。
- **根因机制**：弹窗采取「永不主动关闭、只隐藏」模型（:70-75 注释），Closed 链以「订阅退订 + 计时器停止」为主体力行，子类卸载这一 Win32 侧对称项在清单外。
- **证据**：
  ```csharp
  // SearchPopupWindow.xaml.cs:168-173（安装；返回值仅存只写字段）
  _popupCloseWatcherProc = PopupCloseWatcherSubclassProc;
  _isPopupCloseWatcherInstalled = Win32Helper.SetWindowSubclass(
      _hwnd, _popupCloseWatcherProc, PopupCloseWatcherSubclassId, UIntPtr.Zero);
  // OnWindowClosed :4513-4541：12 组退订 + 2 计时器 + 4 ItemsSource + 控制器
  // Dispose + _viewModel.Dispose() —— 无 RemoveWindowSubclass
  ```
- **建议修法（最小侵入）**：`OnWindowClosed` 顶部（退订组之前）补 `if (_isPopupCloseWatcherInstalled) { _ = Win32Helper.RemoveWindowSubclass(_hwnd, _popupCloseWatcherProc, PopupCloseWatcherSubclassId); _isPopupCloseWatcherInstalled = false; }`——与 `DesktopOrganizationWindow.RemoveMinimumSizeHook` 同款三行；顺带使该字段恢复可读。
- **置信度**：事实高（grep 全仓可直接复核「唯一无卸载」与只写字段）；危害低（机制如上推演，当前无触发面）。

---

## 5. 观察项（不够立案标准）

1. **`WidgetManager.CapsuleArrangement.ApplyCapsuleArrangementIfChanged` 的重入丢弃**（`WidgetManager.CapsuleArrangement.cs:48-51`）：`_isApplyingCapsuleArrangement` 或拖拽会话期间到达的变更被静默丢弃；由于方法全程 UI 线程同步、签名在入口捕获，apply 期间若经同步回调链（窗口移动触发的事件）再次到达变更，该次变更被吞后要等**下一次**任意触发才被签名失配补上。自愈型「延迟」而非「丢失」，未构造出具体可达回调链，不立案。
2. **`publish-aot-audit.ps1`（10498 行）持续未全读**：R12 起留档的观察项维持；本轮补充核验其自 round-08 基线零内容变更（git diff），完整审读建议仍随下次 AOT 管线变更专项进行。
3. **DEF-120 记忆化的进程级取舍**：System 兜底路径（无 InstallLanguage 注册表值）下，运行中变更 OS 显示语言不再被拾取（修复前每次 T() 重读 `CurrentUICulture` 可拾取），需重启生效；修复注释已明示「constant for the lifetime of the process」。触发面极窄（OS 语言热切换 + 无安装语言值 + 跟随系统档），维持不立案。
4. **round-09~12 各轮观察项全部维持**：`Exclusion.None` 防御缺口、Updater `RestartApp` 不确认存活、cleanup 脚本不清理空目录、`Localized.cs:88` 缩进、`WidgetToolDialogWindow` Enter 假设、`StackPopover.cs:2280` 同行双语句、依赖包下载无哈希校验、音量滑条同值回写、`WaitForDeskBoxDependencies` 固定轮询、SearchPopup 入场守卫理论超窗——相关文件本基线零变更（除 DEF-122 修复触碰的 ShortcutDrop，其观察无涉）。
5. **孤儿键**：本轮未重扫（方法依赖候选集，R9~R12 四轮分别报 443/573/172/未扫，簇分布一致）；`QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 维持遗留观察。
6. **静态门禁基线一致性**：`static-baseline.json` async_void_count=249 与当前树 grep 实测一致；`static_gate.py` 的 check_strings 以「键数最多语言」动态取基准（非硬编码 en-US）——parity 完整时等价，仅当某语言意外多键时基准会漂移到多键侧并把其他语言报缺键（方向仍是 FAIL，不漏报），纯设计取向记录。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：1（B-01 搜索弹窗子类钩子无对称卸载）
- **总立案数**：1（连续第八轮 P0/P1 = 0；立案数 5 → 1，回到 R10 水位）
- **已知模式新位点**：2 组并入既有编号（ANI-06 +9 位点——R12 记录覆盖率为 5/14；DEF-080 显示级 +4 位点 TodoItemViewModel）
- **存量复核**：round-12 修复批 DEF-120~124 **逐条复核全部正确落地、无回归**（含 12 语言新键位置/内容/兜底逐文件比对、FolderWatcher 守卫全调用图推演、Lazy 记忆化前提全仓反查、ListSeparator 零残留反查）；R12 已留档接受的 tick-vs-StartAsync「先恢复者赢」残余经独立推演确认属实，按不重复立案原则维持原口径；无改判、无已修复项回退。
- **正面结论**：12 语言 2897 键 parity / 占位符 arity / 日期格式字母 / 孤立花括号 / Format 调用点 arity（230 处 + 3 三元 0 失配）五项机械校验全绿；XAML 197 资源键 0 失效、757 事件绑定 0 缺失处理器；Rust ABI 零漂移（native/ 自 round-08 仅 README 4 行）；scripts/installer 自 round-08 零漂移（仅 DEF-119 与 static_gate 已知变更）；`WidgetWindowBase.Backdrop` 材质签名/控制器复用与刷新计时器代际协议、`ContentWidgetWindow.ContentSwitching` Prepare-Commit-Rollback 单次性协议、`DesktopOrganizationWindow` 双 Closed 链与子类卸载、`OnboardingWindow.IntroAnimations` 代际护栏 + 超时回退、`FileSurfaceContent` 内联重命名与拖出收尾协议、`SearchResultRowControl` 自持视觉、`WidgetShell` 生命周期区（StoryboardSlot/粒子代际/计时器清理）全文审读零立案；`static_gate.py` 门禁逻辑与基线一致性核验通过。
