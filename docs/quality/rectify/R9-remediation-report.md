# R9 整改报告（round-09 闭环批次，2026-09-27）

> 输入：`rounds/round-09/全量代码缺陷审查总报告.md`（DEF-110~114 + DEF-078 新位点收口）。
> 方案：`R9-remediation-plan.md`（独立完善性审查 NO-GO → 按指令修订（W1-3 定性改写 + 3 项建议）→ 复审 **GO**）。
> 门禁总览：Debug x64 构建 **0 错误**（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**；x64 全量回归 **4295/4295**；DEF-114 用例 3 连跑稳定；新实例 **PID 31372** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-110 | `Services/TodoReminderService.cs` | `_sessionNotifiedKeys` 改 `ConcurrentDictionary<string, byte>`（`TryAdd(reminderKey, 0)`；三处 `Clear()` 语义不变）+ `using System.Collections.Concurrent;` + 字段处线程归属注释 |
| DEF-113 | `src/DeskBox.Updater/Program.cs:58-69` | 成功路径 `if (!RestartApp(options.AppPath)) { RestartAfterIncompleteUpdate(options, "restart-failed"); }` + `// DEF-113:` 注释；`GetIncompleteUpdateOutcome` 契约不动 |
| DEF-078 位点 ×2 | `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs` | `ItemsList_ItemClick` 与上下文菜单 Edit lambda 两处 `await OpenDetailAfterSavingAsync(item);` 各包 try/catch + `App.Log` + `RaiseFeedback(T("Common.OperationFailedRetry"), Error, "quick-detail-open-error")`（逐字对齐 `Root_Drop` 既有模式）；lambda 失败分支 `return` 不进入编辑态 |
| DEF-111 | `Services/LocalizationService.cs:1` | 文件头 4 连 BOM 归一为单 BOM（`tail -c +10`；修后 `EF BB BF 75 73 69 …`），零内容变更 |
| DEF-112 | `Services/SearchEngineService.cs` | 删除死方法 `GetRecentNotesAsync`（原 :400-440）、`GetUpcomingTodosAsync`（原 :742-791）共 93 行；`TruncateText`（活调用 :580）与本地化键 `Search.Todo.Due`（活调用 :639）核实保留；grep 复验零残留 |
| DEF-114 | `tests/DeskBox.Tests/CloudBackupTransportTests.cs` | `UploadHook` 增加 `Interlocked` 启动计数 + `secondUploadStarted` 信号；断言改为 `Assert.ThrowsAsync<TimeoutException>(() => secondUploadStarted.Task.WaitAsync(250ms))`（有界「期望无第二次启动」，与上传时长无关）+ 保留 `Assert.Single(Files)` |

净变化：6 文件（+107/−101 含文档外代码面），无签名/序列化/ABI/依赖面变更，`packages.lock.json` 无涉。

## 2. 门禁证据

1. **停实例**：仓库路径实例 PID 27104 已停止。
2. **构建**：`dotnet build ./src/DeskBox/DeskBox.csproj -p:Platform=x64` → 0 错误（22 警告，全部既有项）；`DeskBox.Updater.csproj` → 0 警告 0 错误。
3. **静态门禁**：`python scripts/quality/static_gate.py --json rectify/r9-static-gate.json` → **PASS**。
   - Windows 侧首次留档运行；修复脚本两处 Windows 兼容缺陷（rg 输出行按正则解析以兼容盘符冒号路径；contract replay 的 rel 路径归一正斜杠）。
   - 基线 `static-baseline.json` 刷新并附 notes：计数漂移 async void 229→249 / 同步等待 132→147 / 空 catch 225→245 全部来自 1.5.5 上游合并与 R8-AB 批（R9 批六文件零贡献，逐文件核实）；contract_misses 以当次 46 条规范化快照入基线（重放近似匹配产物，真实契约测试由全量回归背书）。
4. **全量回归**：`dotnet test -p:Platform=x64` → **4295/4295 通过、0 失败**（2m10s）。
5. **时序稳定性**：DEF-114 改造用例 3 连跑全绿（751/587/604ms）。
6. **实例**：新实例 PID 31372 @ `E:\DeskBox\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.exe`（规范 Debug 路径）。

## 3. 延后维持（与总报告 §6 一致，留触发条件）

- DEF-070 残余维护路径锁外写点（低频；待与 SafeBroadcast 一并立项）、DEF-101 广播隔离泛化、DEF-096 批量边距语义（需 GUI 对照）、443 孤儿键清理（需先排除反射/动态拼接面）、EXC-06 DCL volatile 族、观察项 18 条（两专项报告 §5）。
