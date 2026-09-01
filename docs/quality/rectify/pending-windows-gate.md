# Windows 侧门禁待验证项汇总（pending-windows-gate）

> **2026-09-01 更新：GitHub Actions CI（windows-latest）已全绿——Release x64 Build + 3015/3015 测试通过（run 33477323326）。下表中「构建」「测试」两类自动项已闭环，明细见 `ci-verification-report.md`；「人工验证」项仍需实机执行。**
>
> 背景：本轮迭代（F6 批次 A/B/C/D + F7 卫生批次 + 收敛式深度审查）在 Linux 服务器上以纯静态方式进行（代码审查 + 静态门禁脚本），**没有 WinUI3/Windows 编译环境**。所有代码改动都经过 Linux 静态验证（`scripts/quality/static_gate.py`：12 语言键一致、async void/同步等待/空 catch/反射基线对比、剪贴板写配对、契约断言重放），但**全部改动未经编译验证**。
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

## 预期回归基线

| 提交 | 预期用例数 |
|---|---|
| `d2e7e87`（批次 A） | 3018/3018（3011 + 7） |
| `f25cb77`（批次 B） | 3023/3023（+5） |
| `12dbbf2`（批次 C） | 3025/3025（+2 净） |
| `ad8febe`（批次 D） | 死宿主专属契约用例删除后总数略降，**全绿即可** |
| `4682a02`（F7） | ARC-06 测试删除，总数再降，**全绿即可** |

## 批次 A（DEF-014/015/017）

- [ ] 构建 0 错误、警告不高于基线 24 条
- [ ] `WindowInteractionSafetyNetContractTests`（4）+ `WidgetSessionManagerTests` 新增 3 用例通过
- [ ] 可选人工观察：正常使用下日志不应出现 `[TrayBatch] Interaction watchdog`；出现即说明真有泄漏被兜住，请保留日志
- [ ] 可选人工观察：托盘/F7 唤起被前台锁拒绝时出现 `[ZOrder] Content ActivateRaisedFromTrayBatch: SetForegroundWindow FAILED`
- [ ] 可选人工观察：启动日志 `[ShowDesktop] Self-heal watcher started minimizeHook=0x... foregroundHook=0x...` 两值均非 0

## 批次 B（DEF-011/012/013）

- [ ] `QuickCaptureServiceTests` +2、`QuickCaptureDataIntegrityContractTests`（3）通过
- [ ] **人工验证（重点）**：删除一条图像随记 → 4.2s 内点撤销 → 图片完好（DEF-012 核心场景）
- [ ] 人工验证：打开 Markdown 记录编辑 → 保存 → 格式仍为 Markdown（DEF-011）
- [ ] 人工验证：自定义文字色 × 跟随主题背景 → 切换系统主题 → 跌破阈值时背景自动回退（DEF-013）

## 批次 C（DEF-018~026 含 DEF-022）

- [ ] `StabilityHardeningContractTests`（5）、胶囊 +2、天气 +1 用例通过
- [ ] **人工验证（重点）**：从文件格子拖拽文件到资源管理器/桌面（含网络盘路径）→ 拖动无 UI 冻结、落放内容完整（DEF-023 延迟供货路径）
- [ ] 人工验证：胶囊数量多 + 极小分辨率/虚拟机 → 胶囊栏不溢出屏幕（DEF-026）
- [ ] 人工验证：设置页改热键/切双击桌面开关 → 无卡顿（DEF-022 异步握手）；热键功能正常触发
- [ ] 人工验证：锁定/解锁或 Explorer 重启后热键自动恢复（生命周期恢复经 SafeFireAndForget 路径）
- [ ] 人工验证：天气格子并发/连续刷新显示正常（DEF-025）
- [ ] 人工验证：快速开关「自动整理桌面」多次 → 无崩溃（DEF-024）
- [ ] 人工验证：胶囊 live 状态/边框氛围动画视觉表现不变（DEF-018 死代码删除零行为变化确认）
- [ ] 人工验证（低频）：启动失败通知——仅当启动失败时出现「未能完全启动」toast/托盘提示（DEF-020，正常启动无感知）

## 批次 D（DEF-027 死宿主删除）

- [ ] **随记全功能人工回归清单**：`rectify/F6-D-quickcapture-regression-checklist.md` 7 大类 30 项
- [ ] 构建确认无 QuickCaptureWidgetWindow 相关编译错误（应零引用）

## F7 卫生批次

- [ ] ARC-06/ANI-02 删除后构建 0 错误（AOT smoke 无 SmartAnimationAdapter 残留引用）
- [ ] 人工验证：Markdown 工具栏「更多」按钮 tooltip 各语言显示已翻译文本（CFG-08）
- [ ] 人工验证：对比度拒绝对话框关闭按钮显示已翻译「关闭」而非 `Common.Close`（CFG-08）

## 挂账条目观察（非本批修复，运行时留意）

- MEM-01（托盘旧 Icon）/MEM-02（SoftwareBitmap 释放）：下次运行窗口可用任务管理器句柄计数辅助观察
- QC-10（Markdown 无界递归 StackOverflow 面）：如遇超深嵌套 Markdown 记录卡死请保留样本

## 后续批次

（无——本轮迭代全部批次已闭环）

## F8 R2 Round 1（Linux Hermes 批次，2026-09-02）

> 对应提交：`663c593` / `cf0fadb` / `3f2504d` / `4701b39`；CI run 33561624305（Build + Test 全绿）已覆盖编译与回归层验证。以下为 CI 无法覆盖的实机项。

| # | 验证项 | 提交 | 预期表现 | 状态 |
|---|---|---|---|---|
| F8-1 | 传入任意多窗口场景反复触发格子组唤起/收起（DEF-034 相邻回归） | 4701b39 | z-order 重排正常，无可见闪烁/跳位；日志无 `[ZOrder] Window order re-checked` 异常刷屏（该日志仅在锁竞争真实发生时出现，低频属预期） | 待验证 |
| F8-2 | Store 构建（如有）：启动 + 设置页自动启动开关 + Onboarding 步骤 4 开关（DEF-035/036） | 4701b39 | UI 无冻结感；开关状态与系统设置一致；首次点击开关响应正常（缓存未命中路径） | 待验证（非 Store 构建不受影响） |
| F8-3 | 长时间挂机 + 频繁触发后台内存清理调度（DEF-037） | 4701b39 | 日志无 ObjectDisposedException / `[Memory] Background cleanup coordinator failed`；内存平稳 | 待验证 |
| F8-4 | 常规冒烟：启动 → 桌面格子加载 → 随记/文件/天气各一次交互 | 4701b39 | 全部正常（本批未触碰这些路径，仅回归确认） | 待验证 |
