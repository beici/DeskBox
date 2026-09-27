# R11-B 界面交互与工程面专项审查报告（round-11）

> 审查基线 commit `8a8a5bde`（分支 `wip/fix-bug`）/ 纯静态审查，未运行任何构建或测试。

---

## 1. 审查范围与方法

### 1.1 范围与代码量

| 区域 | 规模 |
|---|---|
| `src/DeskBox/Views/`（含 `SettingsSections/`） | 63 个 .cs + 13 个 .xaml |
| `src/DeskBox/Controls/`（含 `WidgetContents/`） | 117 个文件 |
| `src/DeskBox/ViewModels/` | 73 个 .cs |
| `src/DeskBox/Strings/` | 12 个 JSON，en-US 基准 2896 键 |
| `src/DeskBox.Updater/` | Program.cs（DEF-113 位点定点复核） |
| `native/` | Rust 契约漂移检查（见 §1.4） |
| `tests/DeskBox.Tests/` | 404 个 .cs（find 全树，只审测试质量） |
| `scripts/` + `installer/` | 59 脚本 + 11 个 .iss（本轮新增全读 4 个） |
| round-10 基线以来变更 | `e3ad11ad..8a8a5bde` 共 14 文件（+690/−13）：代码面 5 文件（TodoRecurrenceService / SearchPopupViewModel / TodoItemViewModel / TodoWidgetViewModel.DetailAndAttachments）+ 测试 2 文件，其余为台账与 rectify 文档；**代码 diff 全量逐行审读** |

### 1.2 与 round-09/10 的差异化声明

round-09/10 已精读并连续复核的面（WidgetToolDialogWindow、Glance/Weather 订阅链、SettingsWindow Closed 链、QuickCaptureSurfaceContent 核心区、FileSurfaceContent 订阅生命周期区、cleanup 脚本、Migration.iss、Uninstall.iss 删除守卫区）本轮仅做定点/差异复核，不再整段重读。**本轮深读预算投向此前未覆盖或仅扫描的面**：

- **全读（本轮首次）**：
  - `Controls/WidgetContents/MusicWidgetContent.xaml.cs`（1569 行，历轮 B 报告从未涉及）——订阅/生命周期/动画/布局全路径；
  - `Controls/WidgetContents/FileSurfaceContent.StackPopover.cs`（3087 行，最大未覆盖分部）——弹出宿主生命周期、Rendering 揭示协议、订阅退订清单、重排/拖放协议；
  - `scripts/publish-aot-retail.ps1`（571 行全文，round-09 仅读 1-120）——路径安全、PE 机校验、禁载清单、manifest 生成、告警白名单、源稳定指纹；
  - `installer/DeskBox.iss`（304 行）+ 与 `DeskBox.arm64.iss` 归一化 diff（架构、URL、文案差异全部符合预期）；
  - `installer/DeskBox.Installation.iss`（354 行）——升级检测/目录锁定/路径域进程终止；
  - `installer/DeskBox.Dependencies.iss`（403 行）+ arm64 版本架构检测比对。
- **抽样精读**：`MarkdownDocumentView.cs` / `MarkdownSourceEditor.xaml.cs`（订阅与计时器面）、`MusicWidgetViewModel.Playback.cs`（全读）、`MusicSessionService.cs` Try* 族、`SettingsSections/` 四个未读分部（Appearance/CapsuleMode/Music/FileWidget）、`SearchPopupWindow.xaml.cs:3160-3200`（选中行高亮重试环）。
- **存量维持条目**：round-10 基线以来 diff 不含相关文件（见 §1.1 变更清单），维持结论直接沿用并做抽样确认。

### 1.3 机械校验（python 只读脚本，未写任何仓库文件）

- **12 语言键位 parity**：en-US 2896 键为基准逐语言 flat 比对 → **0 缺失、0 多余**（2896 × 12 = 34752 键全对齐）；
- **占位符 arity**：各语言与 en-US 同键 `{n}` 索引集合比对 → **0 失配**；
- **.NET 非法日期格式字母**（DEF-039 模式收紧判定）→ **0 复发**；
- **代码引用键存在性**：src 下全部 .cs/.xaml 点分字符串字面量提取，命中 en-US 键 2302 个 → **精确缺失 0**（含动态键族所需前缀外键全部存在）；
- **Format 调用点 arity**（复跑 round-10 方法并扩展三种形态：①`Format("Key",…)` 直接字面量——顶层逗号计数（修正键名段误计入实参的 off-by-one）；②`Format(T("Key"),…)` T 包裹单键；③`Format(T(条件表达式),…)` 异构 arity 三连扫描）→ **0 失配**。DEF-116 修复后全仓仅剩 1 处三元 T 包裹 Format 位点且两分支占位符数一致；round-10 报告的 2 个位点（`TodoItemViewModel.Collapse` 死实参 / `SearchPopupViewModel.PartialResults` 多余实参）已消除；
- **本地化值孤立花括号**（非 `{n}` 的 `{`/`}`）→ **0 命中**；
- **XAML StaticResource/ThemeResource**：全树唯一引用键 197 个，其中 2 个为注释文本正则误报（"ThemeResource consumer"），真实 195 键中 52 个仓外定义、逐一核对全部为 WinUI/XamlControlsResources 平台资源 → **无失效引用**；
- **XAML 事件处理器绑定**：Views/Controls 全部 .xaml 按 40+ 事件名抽取共 **709 处 → 0 处缺失处理器**；
- **孤儿键**：通用字面量 + 前缀拼接排除扫描 → **172 候选**（round-09 报 443 / round-10 报 573，方法差异所致），簇分布一致（`Onboarding.Scene` 36、`Onboarding.Task` 35、`Onboarding.Step6` 21 等）——维持既有观察；
- **模式族扫描**：`TryEnqueue(async` 范围内 12 处全部落在 DEF-078 挂账位点族内，无新增；culture 敏感 Parse（`DateTimeOffset.Parse` 等）范围 4 处全部为 DEF-080 挂账位点（WeatherWidgetViewModel.DataProcessing.cs:531/544/557/570），无新位点；`Assert.ThrowsAsync` 无漏 await；测试 `Task.Delay` 密度与 round-10 扫描持平（无新增窗口）。

### 1.4 native 契约核验结果（正面结论）

- `lib.rs` 恰好 10 个 `#[unsafe(no_mangle)]` 导出；`DESKBOX_NATIVE_ABI_VERSION = 2`（lib.rs:15）；
- panic 边界：`native/Cargo.toml` dev/release 双 profile `panic = "abort"`，无 `catch_unwind`；
- `77f2b4b4..8a8a5bde`（round-08 基线以来）`native/` 仅 `README.md` 4 行变动——round-09/10 的结构布局抽查结论（`DeskBoxRecycleBinRequestV1`/`NativeUtf16String` 逐字段一致）继续有效，**零漂移**。

---

## 2. 已知模式新位点（并入既有编号，不新立案）

1. **DEF-078 家族 +1：音乐进度条拖放提交链（async void 裸逃逸）**。`src/DeskBox/Controls/WidgetContents/MusicWidgetContent.xaml.cs:432-444` `ProgressHost_PointerReleased`（async void，无 try/catch）→ `ViewModels/MusicWidgetViewModel.Playback.cs:393-421` `CommitSeekAsync`（无 try/catch）→ `Services/MusicSessionService.cs:263-267` `TrySeekAsync` 对解析到的会话对象**裸 await** `session.TryChangePlaybackPositionAsync(...)`（`GetSessionAsync` 只做缓存解析、不捕异常，:345-354）。媒体会话在「解析成功 → Try 调用」毫秒级窗口内消亡（用户关播放器/切歌）时 COM 异常直穿 async void → 全局兜底仅记日志 → 用户拖动进度条释放后**无任何反应**。与台账已挂账的「音乐四紧凑按钮与展开视图按钮（MusicSessionService Try* 裸调）」同根因同文件族（Try* 族 :225-267 六个方法全部裸调，对照 `GetSessionOptions` 有防御），进度条 seek 入口此前未被点名；本条目补入 DEF-078 挂账清单，修法同族（Try*Async 包一层与 GetSessionOptions 同式防御，单点覆盖全部六个方法）。置信度：高（同族机制已由 DEF-078 主线亲验）。

2. **孤儿键（维持既有观察，不重复立案）**：本轮方法报 172 候选（R9 443 / R10 573，方法差异），簇分布与两轮一致；`QuickCapture.TextFileNamePrefix/LinkFileNamePrefix` 两键仍含其中。

---

## 3. 存量复核（范围内挂账条目现状）

> round-10 基线（`e3ad11ad`）以来代码 diff 仅 5 文件 + 2 测试文件，下列维持类条目所在文件均不在 diff 内，round-10 复核结论直接沿用；在 diff 内的条目逐一复核如下。

| 编号 | 现状 | 当前树证据 |
|---|---|---|
| **DEF-115（克隆补 StorageMode + 删除引用扫描）** | **✅ 修复复核正确** | `Services/TodoRecurrenceService.cs:86` 克隆初始化器补 `StorageMode = attachment.StorageMode`，与 `CloneTodoItem` 规范对齐。`ViewModels/TodoWidgetViewModel.DetailAndAttachments.cs:393-409` 删除前 `Items.Any(...)` 引用扫描：①扫描发生在 `item.Item.Attachments.RemoveAll` 之后、`File.Delete` 之前，次序正确；②作用域充分——循环克隆与已完成源事项（历史候选）都在同一 VM 的 `Items` 集合内（`FilteringAndAppearance.cs:626-631` 从 `Items` 分组历史），跨格子共享不可能（托管目录按事项隔离：`DetailAndAttachments.cs:417` `_store.AttachmentDirectory/itemId`，仅克隆显式共享 FilePath）；③`OrdinalIgnoreCase` 与 Windows 路径语义一致。整事项删除路径（`EditingAndUndo.cs:112/142`）不删托管文件 → 无悬空风险，仅孤儿文件（台账已记为接受项）。两个新契约用例（`TodoRecurrenceServiceTests.cs:97-138` 存储模式保持、`TodoWidgetViewModelTests.cs:1148-1191` 共享文件先保后删）断言方向正确、无时序依赖。 |
| **DEF-116（Format arity 两位点）** | **✅ 修复复核正确** | `ViewModels/TodoItemViewModel.cs:582` Collapse 分支改 `Format("Todo.RecurrenceHistory.Collapse")`（键无占位符，展示文本不变）；`ViewModels/SearchPopupViewModel.cs:689-698` 两分支各自 Format，`PartialResults` 只传 `{0}`。本轮三形态全量复扫 0 失配（§1.3）。 |
| **DEF-110** | **✅ 修复复核正确（抽样）** | `Services/TodoReminderService.cs:31` `ConcurrentDictionary<string, byte>`、`:96/:124/:138` Clear、`:444` TryAdd 在位。 |
| **DEF-111** | **✅ 修复复核正确（字节级抽样）** | `Services/LocalizationService.cs:1` 单 BOM（`od` 实证 `357 273 277 u`）。 |
| **DEF-112** | **✅ 修复复核正确（抽样）** | `GetRecentNotesAsync`/`GetUpcomingTodosAsync` 全仓 0 残留引用。 |
| **DEF-113** | **✅ 修复复核正确（抽样）** | `DeskBox.Updater/Program.cs:59-65` `if (!RestartApp(...)) { RestartAfterIncompleteUpdate(options, "restart-failed"); }` 与失败路径对称。 |
| **DEF-078（R9/R10 五处收口）** | **维持（修复在位）** | `QuickCaptureSurfaceContent.xaml.cs` 两处 try/catch + `quick-detail-open-error` 反馈在位（round-10 已逐行复核，本基线该文件无新变更）；本轮家族清单 +1（见 §2.1）。 |
| **DEF-055 / DEF-083 / DEF-090 / EVT-02 / MEM-01 / MEM-02 / DEF-048 / 049 / 051 / 052 / 053 / 076 / 079 / 080 / 082 / THR-06 / EXC-06** | **维持** | 相关文件均不在 `e3ad11ad..8a8a5bde` diff 内；round-10 §3 的当前树证据继续有效。抽样确认：`GlanceWidgetSettingsSection.xaml.cs:73-78` OnLoaded async void 模式未变（DEF-078 家族挂账口径）；`WeatherWidgetViewModel.DataProcessing.cs:531-570` 四处 `DateTimeOffset.Parse` 未变（DEF-080 口径）。 |
| **DEF-039 / DEF-072** | **修复保持** | 日期格式字母收紧扫描 0 复发；2302 引用键反向检查 0 缺失（§1.3）。 |
| **遗留观察（R8 #4 extract_widgetmanager.ps1 名单陈旧；R10 §5 全部 7 条）** | **维持** | 相关脚本/文件本基线无变更。 |

---

## 4. 新发现问题清单

### B-01｜安装器 .NET 运行时检测只覆盖机器级 Program Files 布局，用户级 .NET 10 安装被误判缺失
- **优先级**：P3（冗余动作 + 意外 UAC，无数据风险）
- **位置**：`installer/DeskBox.Dependencies.iss:68-108`（`IsDotNet10RuntimeInstalledAt` / `IsDotNet10RuntimeInstalled`）
- **触发条件**：用户机器上的 .NET 10 Runtime 是**用户级安装**——.NET 官方安装器在未提权运行时默认装入 `%LOCALAPPDATA%\Microsoft\dotnet`，`dotnet-install.ps1` 脚本与 Visual Studio 附带组件同样落位该处——之后运行 DeskBox 安装器（无论提权与否）。
- **影响**：检测恒报「.NET 10 缺失」→ 走完整依赖链：重新下载约 55MB 运行时安装器（`DotNetRuntimeUrl`）+ `ShellExec('runas', …)` 触发一次计划外的 UAC 提权 + 在机器级 Program Files 重复安装一份 .NET 10.0.9。应用最终可用（机器级安装后重检通过），但与「下载**缺少的**运行时依赖」的页面文案相悖，且在 unelevated 直装流程中多弹一次 UAC、向机器写入用户未请求的全局状态。
- **根因机制**：检测点仅两个固定布局 `{autopf}\dotnet\dotnet.exe` 与 `{pf}\dotnet\dotnet.exe`（:105-107）；`{autopf}` 随安装作用域切换（admin→`%ProgramFiles%`、current-user→`%LOCALAPPDATA%\Programs`），但两个作用域都不覆盖 .NET 用户级默认目录 `%LOCALAPPDATA%\Microsoft\dotnet`，也不查 `PATH` 上的 `dotnet`。对照同文件 Windows App Runtime 检测走 `Get-AppxPackage`（含 `-AllUsers` 兜底，:117），.NET 侧没有等价的跨布局探测。
- **证据**：
  ```pascal
  function IsDotNet10RuntimeInstalled: Boolean;
  begin
    // {autopf} follows the installer architecture ... Keep {pf} as a
    // compatibility fallback for older Inno Setup installations ...
    Result :=
      IsDotNet10RuntimeInstalledAt(ExpandConstant('{autopf}')) or
      IsDotNet10RuntimeInstalledAt(ExpandConstant('{pf}'));
  end;
  // IsDotNet10RuntimeInstalledAt 只探 AddBackslash(BasePath) + 'dotnet\dotnet.exe'
  ```
- **建议修法（最小侵入）**：在 `IsDotNet10RuntimeInstalled` 增补一个探测点 `ExpandConstant('{localappdata}\Microsoft\dotnet')`（复用现有 `IsDotNet10RuntimeInstalledAt`，两行）；可选加固：追加 `dotnet.exe --list-runtimes` 的 PATH 兜底（一次 `Exec`）。x64/arm64 两个 `.iss` 同步修改。版本兼容判定逻辑（`IsCompatibleDotNetRuntimeVersion` 排除 preview/RC）无需改动。
- **置信度**：高（机制：检测点集合可直接从脚本验证）；触发面中等（取决于用户群中用户级 .NET 安装的占比），故 P3。

---

## 5. 观察项（不够立案标准）

1. **`FileSurfaceContent.StackPopover.cs:2280` 两条语句挤在同一行**（`ResetBoxSelectionState();        if (_stackPopoverHostWindow is { } releasingHost)`）——合并残留格式瑕疵，纯外观，与 round-10 观察项 6（`Localized.cs:88` 缩进）同类。置信度：高。
2. **依赖安装包下载无哈希校验**（`DeskBox.Dependencies.iss:156-199`）：HTTPS 直连 `builds.dotnet.microsoft.com` / `aka.ms` 固定端点 + Inno 下载页，无签名/哈希验证——与 DEF-084①（更新器直发渠道 hash 校验取舍）同类基础设施取舍，不重复立案。
3. **音乐音量滑条对外部音量变化的同值回写**：`MusicWidgetContent.xaml:1292` `Value="{Binding SystemVolume, Mode=TwoWay}"` + `ValueChanged` 处理器（`:1158-1168`）——音量面板打开期间，系统音量被外部（如键盘媒体键）改变 → VM 更新 → 滑条回写 → `SetSystemVolumeAsync(e.NewValue)` 把刚变的值原样写回音量服务。同值幂等回写，无可感知副作用，机制上不会成环（`_pendingSystemVolume` 排空设计正确）。仅记录。
4. **`WaitForDeskBoxDependencies` 固定 10×1s 轮询**（`DeskBox.Dependencies.iss:138-154`）：安装器 UI 上下文中的有界等待，注释已说明意图（运行时发布延迟），最坏 10s 后按 `DependencyVerificationFailed` 收口，无挂死方向。
5. **round-10 观察项 1-7 维持**（`DesktopOrganization.Exclusion.None` 防御性缺口、测试时钟窗口 2 条、Updater RestartApp 不确认存活、cleanup 脚本不清理空目录、`Localized.cs` 缩进、WidgetToolDialogWindow Enter 假设）——本基线相关文件零变更。

---

## 6. 统计

- **P0**：0
- **P1**：0
- **P2**：0
- **P3**：1（B-01）
- **总立案数**：1
- **已知模式新位点**：2（DEF-078 家族 +1 音乐进度条 seek 链；孤儿键观察维持）
- **存量复核**：round-10 修复批（DEF-115/116，5 代码文件 + 2 测试文件）经 diff 级逐行复核**全部正确落地、无回归**；DEF-115 引用扫描的作用域充分性（历史候选同在 `Items`、跨格子共享不可能、删改次序正确）为本轮新增论证；DEF-110/111/112/113 抽样在位；维持类条目文件均不在基线 diff 内，round-10 结论直接沿用；无「已修复项回退」。
- **正面结论**：12 语言 2896 键 parity / 占位符 arity / 日期格式字母 / 2302 引用键 0 缺失 / Format 调用点三形态 arity 全绿 / 孤立花括号 0——六项机械校验全绿；XAML 资源引用 0 失效、709 处事件绑定 0 缺失处理器；Rust ABI 十导出 / ABI 2 / 双 profile panic=abort，`native/` 自 round-08 仅 README 4 行，零漂移；`scripts/` 自 round-08 仅 static_gate 两文件（R9 批已知），`installer/` 零漂移；`publish-aot-retail.ps1` 全文审读防护链完整（路径逃逸拒绝、PE 机校验、禁载 JIT 产物清单、告警白名单、发布期源稳定指纹）；Music/StackPopover 两大未覆盖面订阅-退订纪律良好（Rendering 揭示协议可取消、宿主窗口 Destroy 全退订、pointer handler 字段化移除）。本轮范围内连续第六轮 P0/P1 = 0，立案数收敛趋势延续（2 → 1）。
