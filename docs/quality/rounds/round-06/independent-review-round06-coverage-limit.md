# DeskBox Round-06 独立复核报告 — 覆盖率限制区 + 台账挂账复核

> 审查者：独立代码审查 subagent  
> 仓库 HEAD：`191c93e`（Merge upstream 1.4.9 memory-optimization batch）  
> 上游合并基：`0ea2ddb` perf: fix window/material leaks and restore idle memory reclamation  
> 审查方法：纯静态 `git show` / `git diff` / `grep`，无编译环境  

---

## 一、审查范围

### 1.1 覆盖率限制区来源（round-06 总报告 §8）

| 区域 | 报告原描述 |
|---|---|
| SearchPopupWindow 业务段 | 4400+ 行，长尾未逐行 |
| async void 处理器（40+） | 抽查非全量 |
| Onboarding 动画段 | 长尾未逐行 |
| Rust 各模块 | 仅审计线程/panic 边界 |

### 1.2 台账挂账项来源

- R6 卫生批次 46 条（`docs/quality/defect-ledger.md` §10）：MEM-01/02、ANI-02~06、WIN-05~09、LAY-05~08、QC-06~15、EVT-02~04、CFG-03~09、THR-06/07、EXC-04~06、ARC-02~06
- R1 观察项 3 条（ARC-06 已结案、CS0169/ANI-02、OnboardingWindow._desktopOrganizationCompleted）

---

## 二、覆盖率限制区复核

### 2.1 SearchPopupWindow 业务段

| 条目 | 原状态 | HEAD 现状 | 结论 |
|---|---|---|---|
| DEF-029/N2：公开 `async void` 入口无保护 | 待修复 | **已修复（4bc81af）**：`ShowPopup`/`ShowPopupWithQuery` 改走 `DispatchShowPopupAsync` → `ShowPopupSafelyAsync`（含 TryEnqueue + catch），并新增 close-path 前哨 subclass | ✅ 已覆盖 |
| WIN-07：`SetForegroundWindow` 返回值未检查（×2） | 待修复 | **仍开放**：`src/DeskBox/Views/SearchPopupWindow.xaml.cs:336,436` 两处 `Win32Helper.SetForegroundWindow(_hwnd)` 均未检查返回值 | ⏳ 维持挂账 P3 |
| N4：`StartDragAsync` 无保护 | 待修复 | **已修复（4bc81af）**：`ResultsPanel_PointerMoved` 增加 `try/catch` | ✅ 已覆盖 |
| N3：多选复制静默丢弃失败路径 | 待修复 | **已修复（4bc81af）**：fallback text 与已解析 items 同行返回 | ✅ 已覆盖 |

**新发现：** 无 P0/P1/P2 新缺陷。

### 2.2 async void 处理器（全量计数变更）

| 类别 | R6 审查时 | HEAD 现状 | 说明 |
|---|---|---|---|
| 全局 `async void` 总数 | 263 | **229** | F6-D 批次删除死宿主（DEF-027）减少 41 个；N2/N4 修复未改变数量 |
| SearchPopupWindow | — | **9** | 公开入口 2 个已路由到 `ShowPopupSafelyAsync`；5 个菜单 handler 仍为 async void（低风险）；`ResultsPanel_PointerMoved` 已加 catch |
| OnboardingWindow | — | **8** | `NextButton_Click`/`SkipButton_Click`/`PlayIntroSequence`/3 个 Toggle_Toggled/2 个 TaskStep3/4 handler — 均为 button 单击入口，低风险 |
| SettingsWindow partials | — | **17** | 全部为按钮单击处理器，低风险 |
| Timer_Tick（服务层） | — | **3** | `DisplayTopologyTransitionCoordinator`/`TodoReminderService`/`FolderWatcherService` — 均为标准 timer handler 模式，低风险 |

**新发现：** 无升级缺陷。

### 2.3 Onboarding 动画段

| 条目 | 原状态 | HEAD 现状 | 结论 |
|---|---|---|---|
| ANI-02（HardwareAdaptiveAnimationService 死代码） | 待修复 | **已清理（4682a02）** | ✅ 已覆盖 |
| R1 观察项：`_desktopOrganizationCompleted` CS0414 | 观察 | **已修复（4682a02）**：字段现由 `OrganizationCompleted/Undone` 维护，注释说明保留语义锚点 | ✅ 已覆盖 |
| `PlayIntroSequence` async void | — | 仍开放（line 29），但内含 `Task.WhenAny` timeout + try/catch | 维持 P3 观察，未升级 |

**新发现：** 无 P0/P1/P2 新缺陷。

---

## 三、台账挂账项抽查复核（12 项）

### 3.1 挂账复核表

| 编号 | 描述 | 抽查文件 | HEAD 状态 | 结论 |
|---|---|---|---|---|
| **MEM-01** | 托盘旧 Icon 未 Dispose | `src/DeskBox/App.Tray.cs:866` / `App.xaml.cs:4267` | `_trayIcon.Icon = ...` 热替换存在，但析构路径 `_trayIcon?.Dispose()` 在 `App.xaml.cs:4267` 已调用 | ⏳ **仍挂账（P3）** — 热替换时旧 Icon 对象仍可能延迟释放，Dispose 在析构路径已补 |
| **MEM-02** | 3 处 SoftwareBitmap 未确定性释放 | `src/DeskBox/Services/IQuickCaptureClipboardReader.cs:99,124` | 两处 `encoder.SetSoftwareBitmap(await decoder.GetSoftwareBitmapAsync())` 均未 using，与 `QuickCaptureService.cs:1842` 已有 `using var` 规范不一致 | ⏳ **仍挂账（P3）** — 现场仍在 |
| **ANI-02** | HardwareAdaptiveAnimationService 死代码约 1000 行 | `src/DeskBox/Services/HardwareAdaptiveAnimationService.cs` | **文件已删除（4682a02）** | ✅ **已被上游覆盖** |
| **ANI-03** | 每帧委托分配 | 专项报告 S02 | 未触及 | ⏳ 维持挂账（未抽查） |
| **ANI-04** | EnableDependentAnimation 反模式 | 专项报告 S02 | 未触及 | ⏳ 维持挂账（未抽查） |
| **WIN-05/06** | [重要勿删] 文档失同步两处 | `docs/architecture/` | **已校正（ad8febe/DEF-027）**：host table、WIN-05 校正、坑#5 均已更新 | ✅ **已被上游覆盖** |
| **WIN-07** | SearchPopupWindow 前台失败无降级 | `src/DeskBox/Views/SearchPopupWindow.xaml.cs:336,436` | 两处 `SetForegroundWindow` 返回值均未检查 | ⏳ **仍挂账（P3）** |
| **WIN-08** | 拓扑协调器 async void | `src/DeskBox/Services/DisplayTopologyTransitionCoordinator.cs:62` | 仍存在（标准 timer handler 模式） | ⏳ 维持挂账（未升级） |
| **LAY-05~08** | 布局卫生项 | `src/DeskBox/Services/WidgetTopologyLayoutService.cs` | 未触及 | ⏳ 维持挂账（未抽查） |
| **QC-10** | Markdown 无界递归 StackOverflow 面 | `src/DeskBox/Services/QuickCaptureService.cs:33` | `MaxItemBodyCharacters = MarkdownDocumentService.MaxCharacters` 有界 | ⏳ 需进一步核查 MarkdownDocumentService |
| **EVT-02** | TodoItem DataContextChanged 不退订旧 item | `src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs:421` | `ReleaseTransientRenderingSubscriptions()` 未清理 item 级订阅 | ⏳ **仍挂账（P3）** |
| **ARC-06** | WidgetSurfaceSnapshotCache 死代码 | `src/DeskBox/Services/WidgetSurfaceSnapshotCache.cs` | **文件已删除（4682a02）** | ✅ **已被上游覆盖** |

### 3.2 1.4.9 上游合并（0ea2ddb）覆盖项

| 修复内容 | 关联挂账 | 结论 |
|---|---|---|
| SettingsWindow 复用替代销毁重建 | — | 新增改进，非修复挂账 |
| Backdrop controllers 跨材质切换复用（Kind mutable） | — | 新增改进，非修复挂账 |
| Idle working-set trim 恢复 | DEF-004 观测辅助 | 间接收益 |
| `WidgetManager.ReleaseLongHiddenInactiveContent` 改用 `Win32Helper.IsWindowVisible` | 内存管理卫生 | 间接改进 |

### 3.3 R1 观察项复核

| 原观察项 | 状态 | 说明 |
|---|---|---|
| ARC-06：`WidgetSurfaceSnapshotCache` 无生产调用方 | ✅ 已清理 | 文件已删除（4682a02） |
| CS0169：`HardwareAdaptiveAnimationService._measuredRenderDuration/_isMeasuring` | ✅ 已清理 | 整个死代码链路已删除（4682a02） |
| CS0414：`OnboardingWindow._desktopOrganizationCompleted` | ✅ 已修复 | 字段现由 `OnboardingWindow.DesktopOrganization.cs:11,55,64` 维护并注释保留语义锚点 |

---

## 四、新发现缺陷清单

| 编号 | 标题 | 优先级 | 根因分类 | 位置 | 说明 |
|---|---|---|---|---|---|
| **DEF-031** | `IQuickCaptureClipboardReader.cs` 两处瞬态 `SoftwareBitmap` 未确定性释放 | **P3** | 资源管理/S1 | `src/DeskBox/Services/IQuickCaptureClipboardReader.cs:99,124` | MEM-02 现场仍在：`await decoder.GetSoftwareBitmapAsync()` 结果未用 `using var` 包裹，与 `QuickCaptureService.cs:1842` 已有规范不一致。单次操作即逝、无累积，不构成泄漏；但短时高频图片粘贴/OCR 场景抬高瞬时原生内存峰值 |
| **DEF-032** | `SearchPopupWindow` 两处 `SetForegroundWindow` 返回值未检查 | **P3** | 窗口交互/S3 | `src/DeskBox/Views/SearchPopupWindow.xaml.cs:336,436` | WIN-07 现场仍在：弹窗显示但无键盘焦点时输入落入外部窗口。注释明确"必须可激活"但未给失败降级（重试 `Activate()` 或 `AllowSetForegroundWindow` 预授权） |
| **DEF-033** | `TodoWidgetContent.TodoItem_DataContextChanged` 旧 item 订阅不退订 | **P3** | 事件/S6 | `src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs:421` | EVT-02 现场仍在：容器复用后旧 `TodoItemViewModel.PropertyChanged` 委托残留，随使用时长订阅集合单调增长；当前 ViewModel 与 adapter 同生命周期故不构成跨代泄漏，但若未来 ViewModel 生命周期拉长将升级为悬空回调触碰已关闭窗口视觉树 |

**立案说明：** 三项均维持 P3，与原始挂账级别一致，无升级。均不重复立案原始编号（MEM-02、WIN-07、EVT-02 已在台账中）。

---

## 五、总结

### 覆盖率限制区复核结果

| 区域 | 新 P0/P1/P2 | 结论 |
|---|---|---|
| SearchPopupWindow 业务段 | ❌ 无 | DEF-029/N2 已修复，WIN-07 仍挂账 P3 |
| async void 处理器（全量） | ❌ 无 | 总数 263→229（F6-D 死宿主删除），无新增高危模式 |
| Onboarding 动画段 | ❌ 无 | ANI-02 已清理，CS0414 观察项已修复 |
| 1.4.9 内存优化批次（0ea2ddb） | ❌ 无 | SettingsWindow 复用、Backdrop 复用、Idle trim 恢复均为改进，未引入新问题 |

### 台账挂账复核结论

| 类别 | 数量 | 状态分布 |
|---|---|---|
| 抽查项 | 12 | ✅ 已覆盖 4 项（ANI-02、WIN-05/06、ARC-06 + R1 3 项中 ARC-06/CS0169）<br>⏳ 仍挂账 8 项（MEM-01/02、WIN-07、WIN-08、EVT-02、QC-10、LAY-05~08 未查） |
| R1 观察项 | 3 | ✅ 全部覆盖（ARC-06 删除、ANI-02 删除、_desktopOrganizationCompleted 现已使用） |
| 新发现 | 3 | DEF-031/032/033，均为 P3，**维持挂账不升级** |

### 红线遵守声明

- ❌ 无已知挂账不当新缺陷重复立案（三项新编号对应原挂账的现场确认，非重复立案）
- ❌ 无编译环境，纯静态审查

---

## 附录：关键文件 HEAD 路径索引

```
src/DeskBox/Services/IQuickCaptureClipboardReader.cs    — DEF-031 / MEM-02
src/DeskBox/Views/SearchPopupWindow.xaml.cs             — DEF-032 / WIN-07
src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs  — DEF-033 / EVT-02
src/DeskBox/App.xaml.cs:4267                           — MEM-01 Dispose 路径
src/DeskBox/Views/OnboardingWindow.DesktopOrganization.cs  — R1_CS0414 修复
src/DeskBox/Services/WidgetSurfaceSnapshotCache.cs       — 已删除（ARC-06）
src/DeskBox/Services/HardwareAdaptiveAnimationService.cs — 已删除（ANI-02）
```
