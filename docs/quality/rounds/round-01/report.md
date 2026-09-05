# DeskBox 迭代 R1 轮报告

- 轮次：R1（2026-08-30）
- 分支：`wip/fix-bug`（基于 1.4.8 / 7b60ce6）
- 范围原则：保守迭代——仅处理三大已知问题中可在源码层定位根因的部分 + 回归门禁修复，全部为最小侵入改动，无架构重构。

## 一、本轮修复清单（按风险等级）

| 编号 | 问题 | 处理 | 交付 |
|---|---|---|---|
| DEF-001 | 显示桌面后部分格子不显示（P0） | 新增事件驱动自愈机制 | issues/DEF-001-*.md |
| DEF-002 | 胶囊悬停自动展开偶现无响应（P1） | ToolTip 豁免阻塞面判定 | issues/DEF-002-*.md |
| DEF-003 | 展开/收起动画逐帧开销（P1） | 部分修复（每帧分配消除）；Win11 合成器动画列 R2 | issues/DEF-003-*.md |
| DEF-004 | 内存 600MB / DWM 1–2GB 关联（P1） | 分析完成；R2 以运行时基线数据驱动修复 | issues/DEF-004-*.md |
| DEF-005 | Internet 快捷方式测试环境耦合（P3） | 测试密闭化 | issues/DEF-005-*.md |

## 二、代码改动总览

详见 `changes.md`。要点：

1. **修复 A（DEF-001）**：`SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART)` 去抖 700ms 后核验常驻格子——iconic 则 `SW_SHOWNOACTIVATE` 恢复并重新 attach 桌面层，DWM cloak 则解除；跳过托盘有意 cloak 与不可见格子；受 `KeepWidgetsVisibleOnShowDesktop` 门控。新增 `WidgetShowDesktopSelfHealService` 与 `WidgetManager.ShowDesktop.cs` 分部。
2. **修复 B1（DEF-002）**：`HasBlockingFlyoutOpen()` 豁免 ToolTip Popup，打破「悬停 → 提示弹出 → 展开被抑制」自锁环；契约测试同步钉住新语义。
3. **修复 B2（DEF-003 部分）**：动画帧路径 `Values.ToArray()` → 复用缓冲，消除每帧堆分配。
4. **修复 C（DEF-005）**：`Steam.url` 夹具改通用 URL，测试全环境密闭。

## 三、审查结果（7 维强制审查：全部通过）

| 维度 | 结论与证据 |
|---|---|
| 资源管理 | WinEvent 钩子在 `Dispose` 注销；委托实例由服务字段保活；计时器 Tick 解绑；静态缓冲有界。无新增未释放句柄。 |
| 异常安全 | dwmapi/user32 调用带 `DllNotFoundException` 等降级 catch；自愈核验整体 try/catch；初始化失败仅记日志不阻断启动。 |
| 线程安全 | 钩子在 UI 线程注册（out-of-context 事件回到 UI 线程消息循环）；核验与动画缓冲均单线程；`FlushPendingBoundsMoves` 唯一调用点已核实无重入。 |
| 窗口交互 | 自愈仅触碰「本应用认为可见 + 非托盘有意 cloak + iconic/cloak 实证」的窗口；`SW_SHOWNOACTIVATE` 不抢焦点；DesktopPinned/动态两模式、双宿主均走既有原语。 |
| 性能影响 | 事件驱动无轮询；核验 O(格子数) 且每次风暴仅一次；帧路径反而减少分配。 |
| 兼容性 | `DWMWA_CLOAK` Win8+，dwmapi 缺失时降级 -1；Win10 兼容底线不受影响；`WINEVENT_SKIPOWNPROCESS` 防自触发循环。 |
| 逻辑一致性 | 复用 `MoveToDesktopBottom`/`ShouldAttachRestingWindowToDesktop` 既有原语与日志风格；源码形状契约测试同步更新并保留防回归断言。 |

## 四、回归测试结论

- 命令：`dotnet test tests/DeskBox.Tests/DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64`
- 结果：**2998/2998 全部通过**（修复前本机 2997/2998，唯一失败即 DEF-005，已密闭化修复）。
- 场景复现（显示桌面、悬停 ToolTip 场景）需在目标机按各单问题文档「验证方案」章节执行；`[ShowDesktop]` 日志标记已内置。

## 五、性能基线对比

- 本轮为基线建立轮：`docs/quality/performance-baseline.md` 已定义五项指标与测量流程；目标机数据采集列入 R2 第一项（本轮环境为无头/开发态，无法产生生产代表性数据，不伪造数字）。
- 环境侧完成：安装 .NET SDK 10.0.303（用户级）与 rustup 1.96.0 工具链，`-p:DeskBoxShellThumbnailProxy=false` 仅用于无 Rust 代理的快速编译，正式回归测试已含真实 Rust native 构建。

## 六、遗留问题与下一轮建议

1. **R2 首项**：目标机采集性能基线（布局编辑 50 次场景 × 5 指标），驱动 DEF-004 修复与 DEF-003 候选 1（Win11 合成器动画）。
2. DEF-001 的 shell 侧行为（iconic vs cloak 的实际分布）依赖 `[ShowDesktop]` 日志在目标机取证，据此可把「attach 失败重试」从备选转为正式项。
3. 观察清单：`WidgetSurfaceSnapshotCache` 死代码确认与清理、`HardwareAdaptiveAnimationService` 未使用字段与帧跳档位联动（合并进 DEF-003 候选 2）。
4. 子代理产出（悬停展开深挖报告）关键结论已经主流程逐一读源码复核后采纳；其候选 A（probe 内带时滞释放 suppression）与候选 B（真实新进入语义）因涉及动画状态机时滞设计，列入 R2 评估。
