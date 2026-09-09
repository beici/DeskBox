# DEF-001 「显示桌面」后部分格子不显示，需打开新窗口才恢复

- 优先级：P0 ｜ 状态：代码修复完成，待生产环境实测确认 ｜ 修复轮次：R1

## 一、问题现象

- **复现步骤**：桌面存在若干 DeskBox 格子 → 打开任意其它应用窗口并保持前台 → 点击任务栏右下角「显示桌面」按钮（或按 Win+D）→ 桌面显示后，部分格子没有正常出现在桌面上。
- **触发条件**：格子此前处于「桌面静置」状态；与其它活动窗口存在 z-order 交互史（如格子曾被拖到前台窗口后方、或经过悬停展开/托盘唤起后回落）的格子更易触发，即「部分格子」的来源——各格子落点路径不同。
- **临时绕过**：打开任意一个非全屏窗口后，格子会重新渲染显示出来。
- **影响范围**：桌面整理核心可见性失效；办公环境下用户会认为格子/文件丢失，属生产环境不可接受缺陷。
- **风险等级**：高。

## 二、根因分析（源码级）

DeskBox 保护格子躲过「显示桌面」的机制是：静置格子将窗口 owner（`GWLP_HWNDPARENT`）挂到 Explorer 桌面图标层（`SHELLDLL_DefView`），使其随桌面带存在而不被 MinimizeAll 波及。实现位于 `src/DeskBox/Services/WidgetLayerService.cs` 的 `TryAttachToDesktopIconLayer()`（约 841 行起）与判定函数 `ShouldAttachRestingWindowToDesktop()`（约 823 行），后者受设置项 `KeepWidgetsVisibleOnShowDesktop`（默认 true，`src/DeskBox/Models/AppSettings.cs:320`）控制。

关键缺口在于**各回落路径的 attach 保护不是无条件的**：

1. `TryPlaceDynamicWindowBehindForeground()`（`WidgetLayerService.cs:473`）先经 `PrepareRestingWindowForRelativePlacement()`（:507）尝试 attach，**失败则显式 `DetachFromDesktopIconLayerIfNeeded()`**，把格子放回普通顶层窗口带。「失败」的常见触发：`FindDesktopIconView()` 瞬时找不到 DefView、Explorer 重启过渡期、`s_startupDesktopLayerAttachmentDeferred` 启动延迟窗口（`WidgetLayerService.cs:851`）。
2. `BringAbovePeerWidgets()`（非 pinned 分支，:326）对动态层级格子无条件 detach。
3. 一旦格子以「未 attach 的普通顶层窗口」身份静置，shell 的「显示桌面」最 小化/伪装（DWM cloak）风暴会连同应用窗口一起把它收起。而**全工程不存在任何自愈路径**：
   - `AppLifecycleRecoveryWatcher`（`src/DeskBox/Services/AppLifecycleRecoveryWatcher.cs`）只覆盖 TaskbarCreated、会话锁/解锁、显示切换；其恢复动作 `OnLifecycleRecoveryRequested` → `RecoverExternalStateAsync`（`App.xaml.cs:2490`）仅刷新文件格子数据，**不触碰窗口可见性**。
   - 回落监视器（`WidgetManager.ZOrder.cs` 的 200ms 监视器）仅在 F7/托盘唤起会话期间运行。
4. 「打开任意非全屏窗口才恢复」与上述缺口吻合：前台变化会触发新的 z-order 整理/重放路径，顺带让格子重新可见；这是副作用而非恢复机制。

结论：**根因是「保护失败 × 无自愈」的组合**。无法在静态代码层面 100% 断定 shell 对未 attach 工具窗口最终施加的是 iconic 还是 cloak（两种在 Win10/11 不同版本都存在），故修复对两者都做兜底（见下），并以 `[ShowDesktop]` 日志在实测中留下证据链。

## 三、优化/修复思路

**选定方案（最小侵入、事件驱动自愈）**：

- 用 `SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART)`（`WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`）监听 shell 级最小化事件——「显示桌面」的本质是对所有应用窗口的最小化风暴，事件必然来自其它进程，因此不需要 hook 自身进程事件。
- 事件去抖 700ms（等风暴平息）后在 UI 线程核验一次：遍历 DeskBox 认为可见的格子窗口，发现 `IsIconic` → `ShowWindow(SW_SHOWNOACTIVATE)` 恢复并经 `MoveToDesktopBottom()` 重新 attach；发现 DWM `DWMWA_CLOAK==1` 且非本应用托盘动画所为 → 写 0 解除伪装。
- 检查全程幂等：所有格子正常时零动作；被托盘动画有意 cloak 的格子（`IsCloakedForTrayShow`）明确跳过，绝不撤销用户主动的隐藏。

**备选方案（评估后未采用）**：

- A. 在每次回落路径把 attach 失败改为重试循环：会拉长回落关键路径、与既有「回落必须一次完成边界建立」的约束（`docs/architecture/[重要勿删]widget_zorder_lifecycle.md` §4.4）冲突，风险更高。
- B. `WH_SHELL` shell 钩子：需系统级钩子注册，侵入面与权限面更大，收益与本方案相同。
- C. 周期轮询核验（每 N 秒）：无事件也能兜底，但常态空转，与「Resource saver」性能模式相悖。

方案风险：低。自愈动作只在「本应用认为可见 + 非有意 cloak + iconic/cloaked 实证」三重条件下触发，最坏情况是恢复一个本应隐藏的格子，而该情况已被三重条件排除。

## 四、拟修改代码模块与功能说明（已实施）

| 文件 | 改动 |
|---|---|
| `src/DeskBox/Helpers/Win32Helper.cs` | 新增 P/Invoke：`IsIconic`、`DwmGetWindowAttribute`、`SetWinEventHook`/`UnhookWinEvent`（DllImport + 委托保活注释）、常量 `EVENT_SYSTEM_MINIMIZESTART`/`WINEVENT_OUTOFCONTEXT`/`WINEVENT_SKIPOWNPROCESS`、封装 `TryGetDwmCloakState()` |
| `src/DeskBox/Services/WidgetShowDesktopSelfHealService.cs`（新增） | WinEvent 钩子注册/注销、700ms 去抖 DispatcherQueueTimer、触发核验回调；UI 线程注册保证回调线程安全 |
| `src/DeskBox/Services/WidgetManager.ShowDesktop.cs`（新增分部） | `VerifyRestingWidgetsAfterShellMinimize(reason)`：五道闸（唤起会话/toggle 进行中/交互深度/设置开关）+ 逐窗口 iconic/cloak 核验与恢复，`[ShowDesktop]` 日志留痕 |
| `src/DeskBox/Services/WidgetLayerService.cs` | 暴露内部判定 `ShouldKeepWidgetsVisibleOnShowDesktop()` 供自愈门控 |
| `src/DeskBox/Services/WidgetTrayAnimationController.cs` | 暴露 `IsCloakedForTrayShow` 只读属性 |
| `src/DeskBox/Views/WidgetWindowBase.cs` | 暴露 `IsTrayCloakActive`（聚合托盘 cloak 状态） |
| `src/DeskBox/App.xaml.cs` | WidgetManager 创建后初始化自愈服务；应用关闭时 Dispose |

## 五、风险评估

- **副作用**：理论上可能把「shell 恰好最小化的工具窗」恢复——但本应用格子无用户最小化路径，该场景即缺陷本身。
- **兼容性**：`DWMWA_CLOAK`（Win8+）、`SetWinEventHook`（全版本支持）；dwmapi 不可用时 cloak 分支静默降级为 -1（不恢复），iconic 分支不受影响；Windows 10 兼容底线不受影响。DesktopPinned 模式下 `ShouldAttachRestingWindowToDesktop()` 恒真、`MoveToDesktopBottom()` 内部自带 pinned 分支，两宿主 × 两模式四象限均安全。
- **最坏情况**：钩子失效 → 退化为现状（无回归）；`SW_SHOWNOACTIVATE` 不抢前台，不影响用户焦点。

## 六、验证方案

1. **自动化回归**：x64 全量测试 2998/2998 通过（本修复未破坏任何既有契约；`dotnet test ... -p:Platform=x64`）。
2. **场景复现（目标机）**：
   - 格子 + 2~3 个应用窗口 → 任务栏「显示桌面」→ 全部格子应可见；日志出现 `[ShowDesktop] Self-heal completed ... restored=N`（若发生兜底）或 `Self-heal verified, nothing to restore`。
   - Win+D 与按钮两种触发各验一次；重复 toggle 恢复后再验一次。
   - F7 唤起会话期间触发显示桌面 → 自愈应跳过（`reason` 五道闸日志）。
   - 托盘隐藏全部格子后触发显示桌面 → 不得复现格子（`IsTrayCloakActive`/不可见跳过）。
3. **性能红线**：`[ShowDesktop]` 核验仅在最小化风暴后触发一次，事件回调仅做去抖；无新增轮询。
