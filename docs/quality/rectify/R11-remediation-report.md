# R11 整改报告（round-11 闭环批次，2026-09-27）

> 输入：`rounds/round-11/全量代码缺陷审查总报告.md`（DEF-117~119）。
> 方案：`R11-remediation-plan.md`（独立完善性审查首审 **GO**，5 条建议全部采纳：tick 回退语义钉死 `path`、双失败分支代际守卫镜像、`{localappdata}` 提权上下文注释、文件计数更正、验证措辞对齐）。
> 门禁总览：Debug x64 构建 **0 错误**（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**（五项与基线持平：async void 249 / 剪贴板 8 配对 / 同步等待 147 / 空 catch 245 / 契约重放新增 0）；x64 全量回归 **4297/4297**；新实例 **PID 19732** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-117 ① | `Services/FolderWatcherService.cs`（StartAsync 入口） | `TryResolveExistingPathForTraversal` 解析包 `Task.Run`（纯 FS IO 无线程亲和；解析在 `Stop()` 与代际捕获之前，不触碰服务状态） |
| DEF-117 ② | 同文件（StartAsync 双失败分支） | 同步 `ProbeFolderAccess` → `await ProbeFolderAccessAsync`，并镜像 :234-240 守卫样式在挂起点后复检 `_isDisposed \|\| startGeneration != _watchGeneration` 再 `SetHealth`/`BeginReconnect`（封住陈旧流窗口） |
| DEF-117 ③ | 同文件（ReconnectTimer_Tick） | 解析包 `Task.Run`，**失败回退语义钉死为 `path`**（junction 重定向下探测目标不漂移）；UI 线程重连 tick 全链路零 FS IO |
| DEF-118 | `Services/DragDropPermissionService.cs`（GetExplorerTokenSnapshot） | `Process[]` 提取 + 内层 try/finally `foreach … Dispose()` **全量释放**（比原报告 `using var` 首实例建议更彻底，消除其余元素孤儿句柄）；`OpenProcess`/`CloseHandle`/快照逻辑逐字不动 |
| DEF-119 | `installer/DeskBox.Dependencies.iss` + `.arm64.iss` | `IsDotNet10RuntimeInstalled` 增补 `IsDotNet10RuntimeInstalledAt(ExpandConstant('{localappdata}') + '\Microsoft')` 探测点，函数头注释写明用户级布局与提权上下文边界（管理模式下解析为运行 Setup 账户的 profile；零售 AOT 不走此检测）；PATH 兜底留痕不进本批 |

净变化：2 源文件 + 2 安装脚本；无签名/序列化/ABI/依赖变更；探测语义零变化。

## 2. 门禁证据

1. 停实例（PID 29740）→ 构建 0 错误（22 警告 ≤ 基线 24）。
2. x64 全量回归 → **4297/4297 通过、0 失败**（2m05s）。
3. `static_gate.py --json rectify/r11-static-gate.json` → **PASS**（五项与基线持平）。
4. 新实例 PID 19732 @ 规范 Debug 路径。
5. .iss 无构建/测试面：语法走查（`//` 注释与字符串拼接为文件内既有惯例）+ 双变体 diff 对称核验（审查核验 `InstallerUninstallContractTests`/`AotStage7C1ContractTests` 断言字符串不受触碰）。

## 3. 延后维持（与总报告 §5 一致）

DEF-078 seek 位点（`MusicWidgetContent.xaml.cs:432` → `TrySeekAsync` 裸调）、孤儿键（本轮 172 候选）、观察项 10 条（A 面 O-15~O-19 + B 面 5 条）、DEF-119 PATH 兜底（触发条件：用户报告 PATH-only 安装仍误判）。
