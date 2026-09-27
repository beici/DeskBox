# R11 整改方案（round-11 双路审查闭环批次）

> 输入：`docs/quality/rounds/round-11/全量代码缺陷审查总报告.md`（DEF-117~119）。
> 原则：最小侵入、零架构变更、探测语义零变化、不触碰 12 语言键集与 z-order 红线。
> 基线：`wip/fix-bug` @ `8a8a5bde`，工作树仅含 round-11 文档产出。

## 1. 逐项处置

### W1 ｜ DEF-117 ｜ FolderWatcherService 重连/双失败路径 UI 线程同步探测（P3）

- **现状（主流程核验）**：`FolderWatcherService.cs` 三处不一致——①`StartAsync` 双失败分支（:265-271）裸用同步 `ProbeFolderAccess`（:288-310，`Directory.Exists` + `EnumerateFileSystemEntries().Take(1)`，离线 SMB 阻塞数秒），而同方法主路径（:233）已用 `ProbeFolderAccessAsync`（:312-315 = `Task.Run(ProbeFolderAccess)`）；②`ReconnectTimer_Tick`（UI 线程 async void，:844 起）在 `await ProbeFolderAccessAsync` 前同步执行 `FileService.TryResolveExistingPathForTraversal`（:861-866，逐段属性查询）；③`StartAsync` 入口（:211-216）同款同步解析。
- **修法**：
  1. 双失败分支：`SetHealth(ProbeFolderAccess(folderPath) == FolderWatcherHealth.AccessDenied ? …)` 改为 `await ProbeFolderAccessAsync(folderPath)` 后**镜像 :234-240 守卫样式复检 `_isDisposed || startGeneration != _watchGeneration`** 再 `SetHealth`/`BeginReconnect`（封住新挂起点的陈旧流窗口）；探测分类语义零变化；
  2. `ReconnectTimer_Tick`：把 `TryResolveExistingPathForTraversal` 解析包进 `Task.Run`，**失败回退语义钉死为 `path`**（junction 重定向下探测目标不得漂移）：`probePath = await Task.Run(() => TryResolve(requestedPath, out var refreshedPath) ? refreshedPath : path);`；
  3. `StartAsync` 入口解析（:211-216）同样包 `Task.Run`：`folderPath = await Task.Run(() => FileService.TryResolveExistingPathForTraversal(folderPath, out string traversalPath) ? traversalPath : folderPath);`——注释保留；解析在 `Stop()` 与代际捕获之前、不触碰任何服务状态，仅移动执行线程；重连 tick 经此全链路无 UI 线程 FS IO。
- **验证**：构建 0 错误 + 全量回归兜底 + static gate（零 async void 新增——改写均在既有 async 方法体内）。

### W2 ｜ DEF-118 ｜ DragDropPermissionService Process 数组未 Dispose（P3）

- **现状（主流程核验）**：`GetExplorerTokenSnapshot`（:804-828）`Process.GetProcessesByName("explorer").FirstOrDefault()`——选中实例与数组其余元素均无 Dispose；同仓 `QuickLookPreviewService.cs:191-199` 已确立 foreach+using 规范。
- **修法**：`Process[] explorers = Process.GetProcessesByName("explorer");` 提取数组，内层 try/finally `foreach (Process process in explorers) process.Dispose();` 全量释放（**比原报告建议的 `using var` 首实例更彻底**——其余元素各自持进程句柄，仅释放首实例仍遗弃孤儿句柄）；既有 `OpenProcess`/`CloseHandle`/快照构建逻辑逐字不动。方法体已有外层 try/catch 兜底，结构不变。
- **验证**：构建 + 全量回归（诊断路径无既有用例覆盖，静态走查留档）。

### W3 ｜ DEF-119 ｜ 安装器 .NET 检测漏用户级布局（P3）

- **现状（主流程核验）**：`IsDotNet10RuntimeInstalled`（x64:105-107 / arm64:105-107）仅探 `{autopf}\dotnet` 与 `{pf}\dotnet`；`IsDotNet10RuntimeInstalledAt(BasePath)` 探测 `AddBackslash(BasePath) + 'dotnet\dotnet.exe'` 并执行 `--list-runtimes` 版本判定（`IsCompatibleDotNetRuntimeVersion` 排除 preview/RC，无需改动）。
- **修法**：两个 .iss 同步增补一行探测点 `IsDotNet10RuntimeInstalledAt(ExpandConstant('{localappdata}') + '\Microsoft')`（覆盖 .NET 安装器未提权运行/dotnet-install 脚本/VS 附带组件的默认落位 `%LOCALAPPDATA%\Microsoft\dotnet`），并更新函数头注释说明三布局与提权上下文边界（管理模式下 `{localappdata}` 解析为运行 Setup 账户的 profile；零售 AOT 不走此检测）；**PATH 兜底留痕不进本批**（需 Exec 环境查找，超出两行预算，触发条件：用户报告 PATH-only 安装仍误判）。
- **验证**：无构建/测试面；Inno PascalScript 语法走查（`//` 行注释与字符串拼接均为文件内既有惯例）+ 两变体 diff 对称性核验。

## 2. 不进本批（延后维持，与总报告 §5 一致）

DEF-078 seek 位点（`MusicWidgetContent.xaml.cs:432`）、孤儿键、观察项 10 条（A 面 O-15~O-19 + B 面 5 条）、DEF-119 PATH 兜底。

## 3. 门禁

1. 停止仓库路径 DeskBox 实例 → Debug x64 构建 0 错误；
2. `python scripts/quality/static_gate.py` PASS（本批零 async void/同步等待/空 catch 新增）；
3. x64 全量回归全绿（基线 4297/4297）；
4. 启动规范 Debug 实例核验；
5. 台账/TODO 收口（DEF-117~119 → ✅ 已修复）。

## 4. 风险与回滚

- 2 源文件 + 2 安装脚本，每处 ≤12 行；无签名/序列化/ABI/依赖变更；探测语义零变化。
- 回滚：单 commit revert。
