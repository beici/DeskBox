# DeskBox 审查与修复循环 — 总览

> 审查者 / 修复者：单一代理（唯一有权改代码、构建、跑测试、提交的人）。
> 分支：`wip/fix-bug`。每个问题一份 `docs/audit/round-<N>-<短名>.md`，本文件每轮更新。

## 当前状态

| 轮次 | 靶（范围） | 发现 | 定级 | 处置 | 提交 |
| --- | --- | --- | --- | --- | --- |
| 1 | `WidgetManager.ShowDesktop` 自愈 + `WidgetLayerService` 桌面层 attach 日志 | 稳态日志风暴；attach 失败行错误码恒为 0 | P2 / P2 | 已修 | `9dda3be4` `f789df27` |
| 2 | 数据面：`ResilientJsonStore` 读失败处理 + `QuickCaptureService` 附件清理 | 读不到被当成"内容坏了"（隔离健康文件并用旧备份覆盖）；加载失败后把附件当孤儿递归删除 | P1 / P1 | 已修 | `ecd3e8d6` `4dab3ef8` |

第 1 轮结束时：P0 = 0，P1 = 0（两条 P1 候选经真机实验证伪，见 §1.3），P2 = 2（均已修）。
第 2 轮结束时：P0 = 0，P1 = 2（均已修），P2 = 0。

## 第 1 轮：Show Desktop 自愈链

### 1.1 问题一 — 稳态下每 1.5 秒刷一行日志（P2，已修）

**人话摘要**：用户按 Win+D 或点任务栏右下角后把桌面留在最前面时，程序每 1.5 秒往日志里写一行"桌面已抬起，格子都在原位"。什么都没发生，日志却一直长；5 MB 轮转窗口很快被这一行挤满，真出事时的证据就被冲掉了。现在一次"桌面在前台"只写一行。

**根因**：`VerifyRestingWidgetsAfterShellMinimize` 由 `WidgetShowDesktopSelfHealService` 的 1500 ms 看门狗驱动。方法在"桌面已抬起 + 每个格子都在原位"这一稳态分支里直接 `App.Log`，把"状态"当成"事件"记录 —— 状态持续多久就写多少行。

**证据**（本机 `%LOCALAPPDATA%\DeskBox\DeskBox.log`，修复前构建）：
- `05:13:59.560` → `05:14:28.355` 连续 24 行，间隔恒为 1.5 s；
- `06:18:09.046` → `06:18:26.436` 连续 14 行；
- 同一台机器上，桌面处于前台的时间越长，行数线性增长。

**改法**：新增 `_showDesktopSteadyStateLogged`，让"稳态结论"每次 Show Desktop 只落一行；只要该趟真的恢复了/抬升了/解除了遮挡（`restored/uncloaked/raised > 0`）或桌面不再被抬起，就复位标记，下一趟重新记账。

**验证**：修复后构建（`DESKBOX_VERBOSE_LOG=1`）在桌面前台状态停留 5 分钟以上，全日志只有 2 行该文案（`06:39:12.751`、`06:41:23.783`），而修复前同状态 30 秒就有 24 行。详见 `round-01-show-desktop-steady-log.md`。

### 1.2 问题二 — attach 失败日志的错误码恒为 0（P2，已修）

**人话摘要**：格子挂到桌面层的动作失败时，日志写 `error=0`，等于没写 —— 谁也没法从现场判断是"没权限"还是"句柄无效"。现在错误码在系统调用返回的当场就被抓住，并且字段名从 `defView=` 改成 `desktopLayerOwner=`，因为实际挂的 owner 是桌面层根窗口，不是图标视图。

**根因**：`Marshal.GetLastWin32Error()` 在失败判定之后才调用，中间隔着一次 `GetWindowLongPtr` 读回；后者自己也是 `SetLastError=true` 的 P/Invoke，把 `SetWindowLongPtr` 的错误码覆盖掉了。历史上 252 条现场记录全部是 `error=0`，正是这个覆盖造成的。

**证据**：日志中 `[WidgetLayer] DesktopPinned owner attach failed` 共 252 条（`04:20` 前后），`error=` 全为 0，`actual=0x0`。

**改法**：`SetWindowLongPtr` 返回后立即取错误码，存入局部变量；失败行改用该变量，并把 `defView=` 更名为 `desktopLayerOwner=`。

**验证**：构建通过；本机 smoke 窗口内 attach 全部成功（`[WidgetLayer] Desktop owner attached hwnd=... bottom=False`），未自然复现失败路径，故该行只做代码级验证。

### 1.3 两条被证伪的候选（不再追）

- **"高完整性实例持锁时，中完整性实例会因 `UnauthorizedAccessException` 静默退出"** —— 假。本机实测：中完整性进程启动第二个实例，成功打开高完整性实例持有的互斥体并激活主实例，退出码 0，日志 `[Activation] Secondary instance kind=bare ... tokenElevated=True integrity=Medium(0x2000)`。
- **"explorer 重启会销毁被 owner 挂靠的格子窗口，格子消失且可见性被写成 false"** —— 假。本机实测（`E:\DeskBox-dev\er-test.ps1`）：杀掉 explorer 后新 `Progman 0x30496` 起来，13 个格子 HWND 全部存活、仍可见，owner 被系统清成 `0x0`（不重挂，但窗口不消失），`widget-layout.json` 的 `isVisible` 未被改写。owner 丢失本身无用户可感知后果：格子带 `WS_EX_TOOLWINDOW`，不进 Alt+Tab。

## 第 2 轮：数据面（读失败与附件清理）

### 2.1 问题一 — 读不到的文件被当成"内容坏了"，健康主文件被旧备份覆盖（P1，已修）

**人话摘要**：用户数据文件（待办、布局、快速记录、剪贴板历史）偶尔会因为杀软扫描、同步盘、索引器或另一进程短暂占用而"这一次没读到"。旧代码把"没读到"和"内容坏了"当成同一件事：把好的主文件改名成 `.corrupt-*` 隔离起来，再把上一次的 `.bak` 拷回去覆盖。结果是用户最后一次保存的内容凭空回退，而且除了 `settings.json` 之外没有任何提示。现在"没读到"就原地不动、只用备份只读地顶上，绝不改写磁盘上的文件。

**根因**：`src/DeskBox/Services/ResilientJsonStore.cs` 的 `LoadWithResultAsync` 用同一个 `catch (Exception)` 包住"读文件"和"反序列化"，两条完全不同的失败被送进同一条"隔离 + 用备份覆盖"的分支。

**证据**：
- 代码：`ResilientJsonStore.cs:44-114`（读+解析同 catch → `QuarantineCorruptFile` → `RestorePrimaryAsync` 覆盖主文件）。
- 单测红灯（修复前）：`LoadAsync_LockedPrimary_KeepsFileAndDoesNotQuarantine` 报 `Expected: "{"value":"newer"}" Actual: "{"value":"older"}"` —— 主文件被旧备份覆盖的直接证据。

**改法**：把"读"和"解析"拆开。读主文件走短重试（40 ms / 80 ms，只吸收 `IOException` / `UnauthorizedAccessException`）；只有**反序列化失败**才隔离。读仍失败时：文件原地保留、备份只读使用（不写回主文件）、结果报告 `Source=Backup`，让调用方知道磁盘上的文档没有被替换。

**验证**：
- 新增单测用**字节区间锁**（`FileShare.ReadWrite | FileShare.Delete` + `Lock(0,1)`）让"读"失败而"改名"仍然可行，因此能真正区分新旧行为；旧代码红、新代码绿。
- 筛选跑（`ResilientJsonStoreTests` + `QuickCaptureServiceTests`，77 个用例）：修复前失败 5 → 修复后失败 3，剩下 3 个是本机环境性缩略图失败（见 §环境事实）。
- 真机 smoke（`E:\DeskBox-dev\smoke-store2.ps1`，独占锁住 `quick-capture.json` 后启动部署版）：日志出现 `Primary store could not be read (left in place)` 与 `Backup loaded without replacing the unreadable primary store.`；主文件 SHA-256 前后一致，无 `.corrupt-*` 产生。

详见 `round-02-store-read-failure.md`。

### 2.2 问题二 — 文档没读到时，附件目录被当成孤儿递归删除（P1，已修）

**人话摘要**：快速记录的图片附件存在 `attachments/<id>/` 下，每次编辑都会做一次"没被任何条目引用的附件目录"清理。如果启动时文档没读到，内存里就是空集合 —— 于是所有附件都"没被引用"，清理会把它们连同目录整个删掉，而且不可恢复。现在只要这份文档不是从磁盘成功读来的（或本进程还没成功写过），清理就整段跳过并留一行日志。

**根因**：`src/DeskBox/Services/QuickCaptureService.cs:1513` 用 `LoadAsync()` 拿数据，`ResilientJsonLoadSource` 被丢弃，服务无法知道"内存里的空集合"是真空还是读失败。

**证据**：
- `QuickCaptureService.cs:1868-1896` 的清理按"引用集合"递归删目录（`:1885 TryDeleteDirectory`）。
- 修复前单测红灯：`CleanupUnusedImageCacheAsync_KeepsAttachmentsWhenStoreCouldNotBeRead` 报 `attachments must survive a cleanup pass that ran on a document which could not be read`。

**改法**：`QuickCaptureStore` 暴露 `LastLoadSource`；`QuickCaptureService` 维护 `_dataTrusted`（只有 `Primary` / `DefaultMissing` 为真，或**成功保存之后**才置真），孤儿清理在 `_dataTrusted == false` 时提前返回并记一行日志。

**验证**：单测旧代码红、新代码绿；筛选跑同为 5 → 3；真机 smoke 日志出现 `Quick capture store could not be read (source=Backup); attachment cleanup stays disabled until this process writes the document.`，附件目录未被触碰。

详见 `round-02-orphan-attachment-gc.md`。

### 2.3 方法学教训（值得记住）

第一版缺陷一的单测**没有区分力**：用 `FileShare.None` 独占主文件时，同一个共享冲突同时也阻断了"改名隔离"和"用备份覆盖"，所以旧代码也能让文件保持不变，单测修复前后都是绿的。真实危险窗口比最初写的窄：仅当失败**不是**共享冲突（ACL / 杀软过滤 / 瞬时 IO），或锁在"读失败"与"隔离"之间被释放时才会发生。换成字节区间锁之后才真正复现。**教训：验证"会不会破坏数据"的单测，必须让危险的那一步仍然可行。**

### 2.4 留给后续轮的线索（本轮不主张、不定级）

1. **全量恢复失败会把用户数据弃留在 `restore-rollback/<guid>/data`**：两次 `Directory.Move` 都失败时（`DeskBoxDataBackupService.cs:1285` 与 `:1290`），外层 catch 会 `DeletePendingRestoreCore()` 删掉 marker 与 staging，启动流程见不到 marker 就直接走默认数据，全仓库对 `restore-rollback` 只有创建处一处引用 —— 没有任何入口能把数据找回来，用户看到的是"数据全没了"。本机从未做过恢复（无 `restore-rollback` 目录），属潜在缺陷，需先造可复现条件再定级。
2. **scoped 云恢复"先删后拷"**：`DeskBoxDataBackupService.cs:1392-1403` 先删目标域、`:1410-1427` 再拷；暂存域目录缺失时 `:1405-1408` 直接 `continue`，删完不回填。
3. **自动快照按 mtime 裁剪且不校验新包可打开**：`DeskBoxDataBackupService.cs:2943-2957`，时钟回拨/同步盘 mtime 异常时可能删掉唯一可读快照而仍报成功。
4. **用户数据文件不 fsync、无跨进程锁**：`ResilientJsonStore.SaveCoreAsync` 只做 temp + `File.Replace`，不 `Flush(flushToDisk: true)`；首次保存走 `File.Move` 无 `.bak`；跨进程互斥完全不存在（只有进程内 `SemaphoreSlim`）。
5. **自愈看门狗会整段静默**（第 1 轮遗留）：`DESKBOX_VERBOSE_LOG=1` 下实测出现 95 s、44 s 两段完全静默，同期 UI 线程未挂。怀疑 WinUI `DispatcherQueueTimer` 在非前台被节流，需独立探针隔离。

## 环境事实（后续轮直接复用）

- 构建：**必须用 `C:\Users\scrip\.dotnet\dotnet.exe`**（PATH 上的 `dotnet` 是没 SDK 的宿主，会报 `No .NET SDKs were found`）。命令 `dotnet build .\src\DeskBox\DeskBox.csproj --configuration Debug --no-restore -p:Platform=x64`；产物 `src\DeskBox\bin\x64\Debug\net10.0-windows10.0.22621.0\`。**构建成功的唯一证明是 `DeskBox.dll` 写入时间晚于构建开始**（exe 是固定桩）。构建输出不要用 `Select-Object -First` 截断（会提前关闭管道，`dotnet` 以 -1 退出且不写 DLL），改为 `*> 日志文件` 再筛。
- 真机 smoke 必须把构建产物 `robocopy` 到 `E:\DeskBox-dev\app` 再启动：仓库内 `bin` 目录带 Low 完整性标签，直接运行会让托盘注册被 UIPI 拒绝。**robocopy 前必须先停掉正在运行的 DeskBox**（DLL 被占用时 robocopy 会按默认重试策略无限等待）。
- 本机 UAC 已关闭，explorer 与 DeskBox 都是 High（`0x3000`）；我的 shell 是 Medium。需要 High 上下文的操作（模拟 Win+D、启动 High 实例、跑 testhost）一律走**计划任务**：`Register-ScheduledTask` + `New-ScheduledTaskPrincipal -RunLevel Highest -LogonType Interactive`。`schtasks.exe` 在本机被拦截（进程直接死亡），不要用。
- 结束 High 进程：`Get-CimInstance Win32_Process -Filter "ProcessId=<pid>" | Invoke-CimMethod -MethodName Terminate`（`taskkill` 会 Access denied）。
- 单测：`dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore -p:Platform=x64`，**testhost 跑在 Low 完整性/AppContainer 里**，写不了 `%TEMP%` 下的普通目录（会报 `UnauthorizedAccessException: Access to the path 'C:\Users\scrip\AppData\Local\Temp\DeskBox.Tests\...' is denied`，表现为整个 fixture 在构造函数里全红）。跑法：把 `TEMP`/`TMP` 指到 `E:\DeskBox-dev\testtmp`，该目录需 `icacls <dir> /grant '*S-1-1-0:(OI)(CI)F'` + `icacls <dir> /setintegritylevel '(OI)(CI)Low'`，并由 High 计划任务执行（脚本 `E:\DeskBox-dev\run-tests.ps1`）。
- 全量单测基线（第 2 轮实测，含修复）：总计 5812 / 失败 10 / 通过 5799 / 跳过 3，28 分 28 秒。**只认差值**；明确要求全绿的子集用 `--filter`。那 10 个固定环境性失败是：`DesktopOrganizationTests.Transaction_*`（5 个，写 `%LOCALAPPDATA%\DeskBox\data\desktop-organization-recovery.json.<guid>.tmp` 被拒）、`QuickCaptureServiceTests` 缩略图三连（`:942`、`:961`、`:1024`）、`StartupRegistryAccessTests.DeferredAccessReadsAndRemovesTheSameUserEntry` 与 `FileDragSafetyTests.ErroneousMoveCompletion_KeepsStorageItemsFallbackSources`（HKCU 注册表写入被拒）。
- 探针：`E:\DeskBox-dev\zprobe6\zprobe.exe`（`dump` / `sim` / `seq <ms>` / `minall` / `toggle` / `kids`），`E:\DeskBox-dev\tokprobe\...\tokprobe.exe <pid>`。
- 桌面层在本机的真实形态：`SHELLDLL_DefView` 是 `Progman` 的子窗口；格子以 owner 方式挂在 `Progman` 上；`CEF-OSC-WIDGET`（NVIDIA 覆盖层）是常驻非 topmost 覆盖窗口，会让 `IsDesktopLayerRaised()` 在本机恒为 false —— 判断"桌面是否抬起"不要依赖它，用 `IsShellDesktopForeground()` 一类的前台判定，或逐窗口 `IsDesktopLayerAboveWindow()`。
- git 小坑：PowerShell 里 `git stash pop stash@{0}` 会被当成哈希表字面量（报 `unknown switch 'e'`），必须写 `git stash pop 'stash@{0}'`；多行提交信息写文件后 `git commit -F <file>`；`docs/audit/` 在 `.gitignore` 里，提交要 `git add -f`。
