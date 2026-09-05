# DEF-004 主进程内存 600MB 与 DWM 内存 1–2GB 的关联定位

- 优先级：P1 ｜ 状态：源码分析完成，待运行时基线量化（R2 起修复）｜ 修复轮次：—

## 一、问题现象

- **复现步骤**：多次编辑格子布局（拖动位置、拖拽调整大小）后，观察任务管理器：DeskBox 主进程内存可达 600MB；同时 DWM 进程内存上涨至 1–2GB。
- **触发条件**：高频/长时的交互式布局编辑；与格子数量、材质（Mica/Acrylic）、单格面积正相关（待量化）。
- **影响范围**：整机图形内存压力，长期运行后可能影响系统整体流畅度。
- **风险等级**：中高。

## 二、根因分析（源码级）

需要区分四类嫌疑，源码层面的当前结论：

1. **布局编辑主路径本身是健康的**：交互式缩放走指针捕获 + 逐帧合并提交（`WidgetWindowBase.Interaction.cs` 的 `ResizeBorder_PointerMovedCore` → `QueueInteractiveResizeBounds`），配置持久化推迟到操作结束一次性落盘；拖拽同理；`DisplayChangeWatcher` 在拖拽期间抑制恢复避免抖动。
2. **托管泄漏面已有治理**：`MemoryCleanupPolicy` + `VisibleIdleMemoryTracker` + `ReleaseLongHiddenWidgetResources` 构成隐藏/空闲释放体系；快照缓存 `WidgetSurfaceSnapshotCache` 是带像素预算的 LRU（且当前无生产调用方，非泄漏源）。
3. **DWM 关联面（重点）**：每个格子窗口持有一个 `MicaController`/`DesktopAcrylicController` 或 Win10 legacy accent blur 背板。逐帧 HWND resize 期间 DWM 必须为每个可见窗口重新分配/扩大合成表面——这正是「编辑布局 → DWM 内存上涨」的机制性解释。背板代码本身有签名复用与控制器 Dispose（`WidgetWindowBase.Backdrop.cs` 的 `CanReuseAppliedBackdrop`/`DisposeAcrylicController`），且交互期已有 `SimplifyBackdropForInteraction()`（Win10 blur 降级）缓解；**Win11 上 Mica/Acrylic 的 DWM 表面高水位无法由应用直接回收，属于 DWM 侧行为**。因此 DWM 1–2GB 大概率是「多窗口 × 大面积 × 逐帧 resize」下 DWM 合成表面的合理但过高的高水位，而非应用侧句柄泄漏；DeskBox 能做的是降低表面创建频率与面积。
4. **尚未排除的疑点（R2 用数据回答）**：图标/缩略图解码缓存是否随重排无上界增长（`IconHelper`、`ShellThumbnailProxy` 路径）；交互路径上的事件订阅是否成对注销。

## 三、优化/修复思路（R2 计划）

1. **先测后改**：用现成 `scripts/measure-deskbox-memory.ps1`、`scripts/measure-scenario-memory.ps1` 在目标机建立「布局编辑 50 次」场景的工作集/私有提交/句柄数/GDI 对象数/DWM 内存五项基线，定位增长曲线归属（托管堆 vs 原生 vs DWM）。
2. 按数据命中点修复，候选方向（依证据启用，不一刀切）：
   - 交互 resize 期间降低 `SetWindowPos` 频率（复用 DEF-003 的帧跳档位），减少 DWM 表面重建次数；
   - 交互期把 Win11 Acrylic 降级为 Solid tint（对齐既有 Win10 `SimplifyBackdropForInteraction` 策略，Win11 扩展同款政策）；
   - 图标/缩略图缓存加像素预算上限（对齐 `WidgetSurfaceSnapshotCache` 的预算模式）。
3. 每轮修复后重跑同场景基线，写回 `docs/quality/performance-baseline.md`。

## 四、拟修改代码模块与功能说明

本轮无代码改动。R2 预计涉及：`WidgetWindowBase.Backdrop.cs`（交互期背板降级扩展到 Win11）、`WidgetCompactAnimationCoordinator`（resize 帧率耦合）、`IconHelper`/`ShellThumbnailProxy`（视证据）。

## 五、风险评估

- 背板交互期降级会带来材质瞬时变化（可接受的视觉折衷，已有 Win10 先例）；
- 缓存预算收紧可能增加重复解码 CPU（需在基线中同时观察 CPU 占用红线）。

## 六、验证方案

- 同场景前后对比五项指标；红线：主进程工作集峰值不高于修复前，DWM 增量下降；功能回归覆盖布局编辑、多显示器拓扑恢复、性能模式三档切换。
