# DeskBox R1 轮代码变更记录（可追溯 / 可回滚）

> 回滚方式：整轮回滚 revert 本轮提交；单问题回滚按下表逐文件 revert。
> 基线：1.4.8（7b60ce6），分支 `wip/fix-bug`。

## 新增文件

| 文件 | 属问题 | 核心内容 |
|---|---|---|
| `src/DeskBox/Services/WidgetShowDesktopSelfHealService.cs` | DEF-001 | `SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, WINEVENT_OUTOFCONTEXT\|WINEVENT_SKIPOWNPROCESS)`；UI 线程回调仅重启 700ms 去抖 `DispatcherQueueTimer`；tick 调核验回调并整体 try/catch；`Dispose` 注销钩子与计时器 |
| `src/DeskBox/Services/WidgetManager.ShowDesktop.cs` | DEF-001 | `VerifyRestingWidgetsAfterShellMinimize(reason)`：五道闸（`_widgetsRaisedFromTray`/`_isTogglingWidgetsDesktopLayer`/`IsWidgetInteractionActive`/`ShouldKeepWidgetsVisibleOnShowDesktop`）→ 遍历 `GetLoadedDesktopWindows()` → 可见窗口逐个判 `IsIconic`（`SW_SHOWNOACTIVATE` 恢复 + `MoveToDesktopBottom` 重挂桌面层）或 `DWMWA_CLOAK==1` 且非 `IsTrayCloakActive`（写 0 解除）；`[ShowDesktop]` 日志 |

## 修改文件

| 文件 | 属问题 | 函数/位置 | 改动点 |
|---|---|---|---|
| `src/DeskBox/Helpers/Win32Helper.cs` | DEF-001 | P/Invoke 区 | +`IsIconic`、`DwmGetWindowAttribute`、`TryGetDwmCloakState()`、`SetWinEventHook`（DllImport，注释说明 LibraryImport 不支持委托参数与保活要求）、`UnhookWinEvent`、常量与 `WinEventProc` 委托 |
| `src/DeskBox/Services/WidgetLayerService.cs` | DEF-001 | `ShouldAttachRestingWindowToDesktop()` 后 | +`internal static bool ShouldKeepWidgetsVisibleOnShowDesktop()`（自愈门控，复用既有判定） |
| `src/DeskBox/Services/WidgetTrayAnimationController.cs` | DEF-001 | `IsPositionTransitionActive` 后 | +`public bool IsCloakedForTrayShow => _isWindowCloakedForTrayShow` |
| `src/DeskBox/Views/WidgetWindowBase.cs` | DEF-001 | `TrayAnimation` 字段后 | +`internal bool IsTrayCloakActive => TrayAnimation.IsCloakedForTrayShow` |
| `src/DeskBox/App.xaml.cs` | DEF-001 | 字段区 / WidgetManager 创建后 / `ShutdownApplicationAsync` | +`_showDesktopSelfHealService` 字段、`InitializeShowDesktopSelfHealWatcher()`（UI 线程注册，回调闭包 `WidgetManager?.VerifyRestingWidgetsAfterShellMinimize`）、关闭时 Dispose |
| `src/DeskBox/Views/WidgetWindowBase.cs` | DEF-002 | `HasBlockingFlyoutOpen()` | 逐 Popup 判定重写 + `IsToolTipPopup()`（`popup.Child is ToolTip` 或包装父级为 ToolTip → 豁免）；其余 Popup 维持阻塞；+`using Microsoft.UI.Xaml.Controls(.Primitives)` |
| `tests/DeskBox.Tests/WidgetCompactTrayVisibilityContractTests.cs` | DEF-002 | `OpenXamlPopups_BlockCapsuleCollapseAcrossHostedContent` | 契约从 `.Count > 0` 改为钉住「仍用 GetOpenPopupsForXamlRoot + 必须存在 IsToolTipPopup 豁免 + 非 ToolTip 必须 return true」 |
| `src/DeskBox/Services/WidgetCompactAnimationCoordinator.cs` | DEF-003 | `FlushPendingBoundsMoves()` / 字段区 | +`PendingBoundsMovesBuffer` 复用缓冲替代 `Values.ToArray()`（注释标注单调用点无重入约束）；`count={moves.Count}` |
| `tests/DeskBox.Tests/FileServiceTests.cs` | DEF-005 | `EnumerateDirectoryAsync_RecognizesInternetShortcutAndHidesExtension` | 夹具 `Steam.url`→`Example.url`、`steam://rungameid/123`→`https://example.org/`，断言同步，附环境耦合原因注释 |
| `AGENTS.md` | 工作流 | 全文 | /init 增补：仓库概览、构建/测试命令、AOT+Rust 约束、架构边界、本地化规则（与缺陷修复无关，先行独立提交说明见提交记录） |

## 验证记录

- Debug 构建（canonical 输出）：0 错误（12 个警告均为既有：CS0108/CS8602/CS8601/CS0169/CS0414，已列入观察清单）。
- x64 测试：2998/2998 通过。
- 运行核验：仓库 Debug 实例 `E:\DeskBox\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.exe` 启动并保持运行；用户的安装版实例 `D:\DeskBox\DeskBox.exe` 按规程未受影响。
