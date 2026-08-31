# Windows 侧门禁待验证项汇总（pending-windows-gate）

> 背景：本轮迭代在 Linux 服务器上以纯静态方式进行（代码审查 + 静态门禁脚本），**没有 WinUI3/Windows 编译环境**。所有代码改动都经过 Linux 静态验证（`scripts/quality/static_gate.py`：12 语言键一致、async void/同步等待/空 catch/反射基线对比、剪贴板写配对、契约断言重放），但**全部改动未经编译验证**。
> 请在每次推送后于 Windows 机按 `AGENTS.md`（见任务附录）执行构建与回归；本文件汇总每批待验证点，验证通过后请标记 ✅ 并注明结果。

## 门禁命令（每次推送后执行）

```powershell
Get-Process DeskBox | Where-Object { $_.Path -like 'E:\DeskBox*' } | Stop-Process -Force
C:\Users\scrip\Tools\dotnet10\dotnet.exe build .\src\DeskBox\DeskBox.csproj -c Debug --no-restore -p:Platform=x64
C:\Users\scrip\Tools\dotnet10\dotnet.exe test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore -p:Platform=x64
# 规范构建 + 重启（人工测试实例）
C:\Users\scrip\Tools\dotnet10\dotnet.exe build .\src\DeskBox\DeskBox.csproj -c Debug --no-restore
Start-Process E:\DeskBox\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.exe
# 身份核实：%LOCALAPPDATA%\DeskBox\DeskBox.log 的 [Build] buildTime 行；基线 x64 3011/3011
```

## 批次 A（DEF-014/015/017）—— 待验证

- [ ] **构建**：Debug x64 0 错误，且警告数不高于基线 24 条（本批不引入新类型，预期零新增警告；若有 CS 编译错误或新警告请反馈）
- [ ] **回归**：x64 全量 ≥ 3011/3011（本批新增 7 用例：`WindowInteractionSafetyNetContractTests` 4 条 + `WidgetSessionManagerTests` 追加 3 条，预期 3018/3018）
- [ ] **DEF-014 人工观察（可选）**：正常使用下日志不应出现 `[TrayBatch] Interaction watchdog`；如出现请保留日志——那说明真有交互深度泄漏被兜住了
- [ ] **DEF-015 人工观察（可选）**：托盘/F7 唤起格子时如前台被拒，日志出现 `[ZOrder] Content ActivateRaisedFromTrayBatch: SetForegroundWindow FAILED`（低频场景，难主动复现，无日志即正常）
- [ ] **DEF-017 人工观察（可选）**：启动日志 `[ShowDesktop] Self-heal watcher started minimizeHook=0x... foregroundHook=0x...`，两个值都非 0；正常环境不应出现 `hook registration FAILED`

## 后续批次（随推送更新）

（批次 B/C/D/F7 推送后追加）
