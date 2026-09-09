# F6 批次 B+C 整改报告（随记数据完整性 + 稳定性强化）

> 整改日期：2026-09-01 ｜ 基线：`d2e7e87`（批次 A 之后）｜ 分支：`wip/fix-bug` ｜ 方式：Linux 静态迭代，**本批未经编译验证，Windows 侧门禁见 `pending-windows-gate.md`**。
> 批次 B 提交：`f25cb77`（DEF-011/012/013）；批次 C 提交：`3117668`（DEF-018/019/020/021/023/024/025/026）+ DEF-022 后续提交。

## 批次 B（随记数据）—— 一句话版

| 编号 | 问题 | 通俗解释 | 修法 |
|---|---|---|---|
| DEF-012 | 删除图像随记后 4.2 秒内撤销，图片已被回收 | 你删了条带图的随记，系统在这 4.2 秒的「后悔时间」里把图片文件也当垃圾清了；点撤销 → 条目回来了但图没了，且无法再修 | 把「正在等待撤销决定的条目」的图片路径记进一个 10 秒保护清单，垃圾回收会绕开它们；撤销或超时后保护自动解除，原有清理语义一点没变 |
| DEF-011 | 编辑旧随记会被「默认格式」悄悄改写 | 一条 Markdown 记录点开编辑保存后变成纯文本（`#`、图片引用全变原文），反着来也会中招，且没有后悔按钮 | 编辑时保留这条记录自己的格式；默认格式只给新建的记录用 |
| DEF-013 | 配色校验只在保存时做，切主题后失效 | 自定义深色字 × 「跟随主题」浅色底：保存时通过了可读性检查，切个主题后字就看不见了 | 每次应用配色（含切主题）都复检实际对比度，跌破阈值自动把背景退回「跟随主题」并记日志 |

### DEF-012 实现细节（根因链）

`QuickCaptureService` 删除条目 = 从内存列表移除 + 落盘；图像 GC（`CleanupUnusedImageCacheCore`）以「当前 Items+RecentItems 是否引用」为唯一判据。撤销快照里的条目不在任何列表 → 其图片在撤销窗口内被任何一次 GC 物理删除。修复：新增 `_undoWindowImagePaths`（路径→登记时间），`GetReferencedImagePathsCore` 把它并入引用集，三处删除出口（单条/最近/批量）登记、`RestoreDeletedItemAsync` 解除、每次 GC 顺带清理过期项（10 秒 = 撤销窗 4.2s × 2.4 裕量）。**等价性**：正常路径（无删除-撤销）行为逐字节不变；过期项按原语义可回收。

### DEF-011 实现细节

三个编辑入口（`OpenDetail`/`BeginDetailEditing`/格式变更订阅）全部改为 `_detailItem?.ContentFormat ?? 默认`；新建路径（`StartDetailCreation`）保持默认格式不变。**等价性**：只读展示路径原本就用 `item.ContentFormat`，行为零变化；`QuickCaptureService.UpdateItemDetailsWithResultAsync` 的 `effectiveFormat` 现在总是等于记录原格式 → 持久化值不变。

## 批次 C（稳定性）—— 一句话版

| 编号 | 问题 | 通俗解释 | 修法 |
|---|---|---|---|
| DEF-019 | 一个主题订阅者抛异常，后面的订阅者全收不到通知 | 主题广播是「多米诺」：第 1 个倒下（抛异常），后面 9 个全不亮，一半界面还是旧主题 | 照抄本地化服务的成熟模式：先抄一份订阅者名单，逐个调用且逐个兜异常，谁炸了记日志不影响别人 |
| DEF-020 | 启动失败被整体吞掉，应用半启动常驻无提示 | 启动流程 200 行包在一个大 try 里，任何一步炸了都只是日志里一行字，用户看着「应用开着但格子没恢复」一脸懵 | 加阶段标记（设置服务阶段 / 格子恢复阶段），失败时尽力弹系统通知（toast → 托盘气泡兜底），12 语言文案齐全；通知本身失败也兜住 |
| DEF-021 | 后台任务异常完全不可见 | fire-and-forget 任务的异常要等垃圾回收时才冒出来，且没人记录 | 启动时注册两个全局兜底（只记日志留痕 + 标记已观察，不改变进程行为） |
| DEF-023 | 拖文件瞬间卡 UI，网络盘/慢盘能卡几秒 | 拖拽启动在 UI 线程同步等 Windows 存储接口（3 个连续阻塞调用） | 改成「先承诺拖拽、内容延迟供货」：真正的文件解析挪到拖放目标索要内容时（线程池），与仓库既有 QuickCapture 拖拽同模式；3 个阻塞调用连同死掉的同步入口一起删除 |
| DEF-024 | 快速开关「自动整理」偶发崩溃 | 开关切换时销毁令牌（CTS），而后台线程还在读它 → ObjectDisposedException | 改为「只取消不销毁再换新」，最终 Dispose 保持原样 |
| DEF-025 | 天气数据并发刷新会互相污染 | 缓存实例被直接发给调用方改写字段（地名/过期标记），并发时数据撕裂 | 缓存实例变「只读权威副本」，每次返回浅拷贝（顶层展示字段逐次独立，嵌套预报数据只读共享） |
| DEF-026 | 胶囊太多 + 屏幕太小时整条溢出屏幕 | 每个胶囊最小 1px 仍放不下时，旧逻辑静默溢出且补偿只修一边 | 先压间距再截尾部（放不下的胶囊回退自由摆放），双向越界改居中补偿；+2 行为用例锁死 |
| DEF-018 | 胶囊常驻氛围动画 20Hz 空转耗电（台账立案时成立） | **当前树复核：三处计时器已是死代码**——live 呼吸已被合成器动画（`StartCompactLiveIndeterminate` 含 Opacity 关键帧）覆盖、底部辉光已被移除（`ApplySpectrum` 直接折叠）、边框呼吸被合成器 `StartEdgeGlowPulse` 取代，三个 Start 方法零调用 | 按零行为变化原则整体删除（约 150 行）+ 更新引用其分节标记的契约测试 |

### DEF-022（热键钩子生命周期异步化）—— 本批内完成

| 文件 | 改动 |
|---|---|
| `ReservedHotkeyHookService.cs` | 新增 `TryStartAsync`（握手 `await ready.Task.WaitAsync(1.5s)`，钩子线程创建仍锁内原子、`SetWindowsHookEx` 仍在专属钩子线程——线程亲和约束保持）；新增 `StopAsync` 薄包装；同步 `TryStart`/`Stop` 原样保留（设置页/引导页录制钩子仍用） |
| `DesktopDoubleClickActivationService.cs` | 新增 `TryStartAsync` + `RefreshRegistrationAsync` + `TrySetEnabledAsync`（设置开关不再卡 UI 最长 2 秒） |
| `GlobalHotkeyService.cs` | 新增 `RefreshRegistrationAsync`/`TryApplyActivationAsync`/`TryApplyGestureAsync` 返回 `HotkeyApplyResult`（回滚逻辑与同步版逐行为等价）；同步 API 全部保留 |
| `App.xaml.cs` | 启动钩子注册与生命周期恢复（解锁/Explorer 重启）改经 `SafeFireAndForget` 异步执行，UI 线程不再被钩子握手阻塞 |
| `SettingsWindow`/`OnboardingWindow` | 热键设置与录制改走 async 变体（await 后回到 UI 线程再弹对话框，**无 ConfigureAwait(false)**） |
| `App.AotHotkeySmoke.cs` | smoke 契约接线到 async API |
| `GlobalHotkeySafetyContractTests.cs` | 生命周期恢复契约更新为异步接线断言 |

**等价性论证**：①钩子线程模型不变——`SetWindowsHookEx` 永远在专属消息泵线程执行，`TryStartAsync` 只把「等它装好」从同步 `Wait` 改为异步 `await`；②`Stop` 的 generation 防复活机制原样保留，`TryStartAsync` 前置 `Stop()` 与同步版相同，双钩子瞬态共存窗口不变；③同步 API 保留给非 UI 场景（设置页录制钩子、AOT smoke 的 Search 冲突测试），无行为漂移。

## Linux 静态门禁（两批均全 PASS）

| 检查 | 批次 B | 批次 C |
|---|---|---|
| 12 语言键一致 | 2559 键 × 12（DEF-020 新增 4 键同步 12 语言） | 同左 |
| async void | 263 = 基线 | 263 = 基线（DEF-023 特意保持事件处理器同步） |
| 同步等待 | 134 = 基线 | **131（净 -3：删掉三个 GetResult）** |
| 剪贴板配对 / 空 catch / 反射 | 全 = 基线 | 全 = 基线 |
| 契约断言重放 | 新增 7 条全命中 | 新增累计 16 条全命中 |

自审清单：UI 线程亲和（await 后触碰 XAML 的调用链无 ConfigureAwait(false)；服务内部纯等待保留）✓ ｜ 无新增 async void ✓ ｜ 锁内无 await（TryStartAsync 的锁只包线程创建，await 在锁外）✓ ｜ 订阅/退订对称 ✓ ｜ AOT 反射零新增 ✓ ｜ Rust 契约零触碰 ✓ ｜ z-order 生命周期零触碰 ✓

## 新增测试清单

| 文件 | 用例 |
|---|---|
| `QuickCaptureServiceTests.cs` | +2：撤销窗内 GC 不删图、恢复后引用保护移交 |
| `QuickCaptureDataIntegrityContractTests.cs`（新） | 3 条源码契约（保格式 ×3 入口 / 保留集三出口+恢复 / 主题切换复检回退） |
| `WeatherResilienceTests.cs` | +1：三次调用三实例、调用方修改不污染缓存 |
| `WidgetCapsuleArrangementCalculatorTests.cs` | +2：极小工作区截断有界（横/竖） |
| `StabilityHardeningContractTests.cs`（新） | 5 条源码契约（广播隔离 / 兜底注册 / 阶段标记 / 双通道通知 / CTS 不 Dispose） |
| `GlobalHotkeySafetyContractTests.cs` | 生命周期恢复契约对齐异步接线 |

预期 Windows 回归：3011 + 14 = **3025/3025**。

## 回滚方式

批次 B：`git revert f25cb77` 或按文件单项还原。批次 C：`git revert 3117668` + DEF-022 后续提交；DEF-022 各文件可独立回滚（`ReservedHotkeyHookService`/`DesktopDoubleClickActivationService` 新增方法独立成块，删除即回到同步版）。
