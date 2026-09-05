# 收敛式深度审查报告（F6/F7 迭代轮终态）

> 审查日期：2026-09-01 ｜ 审查对象：`4bc81af..1df469e`（F6 批次 A/B/C/D + F7 卫生批次的全部改动）｜ 方式：主流程 + 3 个后台深挖 subagent 并行（对齐 `rectify/R6-P1-independent-review.md` 方法论）｜ 收敛判据：一整轮全量审查无新增 P0/P1/P2 即收敛，硬上限 4 轮。

## 审查覆盖面（第 1 轮，3 面并行）

| 面 | 覆盖内容 | 报告 |
|---|---|---|
| 面 A：修复的相邻代码面 | 撤销窗保留集（13 处 GC 调用点 × 保留集语义 × 10s/5s 窗口关系）、对比度回退分支递归安全、死宿主删除后订阅完整性、WeatherService 浅拷贝的调用方使用模式 | [round1-faceA-adjacent-surfaces.md](round1-faceA-adjacent-surfaces.md) |
| 面 B：round-06 总报告自报覆盖率限制区 | SearchPopup 业务段全路径精读（拖拽/复制/状态机/剪贴板/生命周期）、全仓 220 处 async void 抽检、85 处 null-forgiving 抽检、OnboardingWindow 五个分部的事件订阅/退订矩阵与 storyboard 生命周期 | [round1-faceB-coverage-gaps.md](round1-faceB-coverage-gaps.md) |
| 面 C：新代码与既有线程模型交互 + 冻结契约 | Rust FFI 十导出零漂移验证（native/ 零改动）、看门狗三计时器交织时序、ForceReset 重入安全、DEF-022 钩子线程共存窗口、DEF-020 fallback 路径 | [round1-faceC-contract-interaction.md](round1-faceC-contract-interaction.md) |

## 结论：**第 1 轮即收敛** ✅

三个审查面共产出 **5 条新发现，全部为 P3 卫生类**——无新增 P0/P1/P2，满足收敛判据，按约定不启动第 2 轮（剩余轮次预算保留给未来迭代）。

## 新发现清单（全部 P3，已立案）

| 编号 | 位置 | 问题 | 处置 |
|---|---|---|---|
| QC-16 | `QuickCaptureSurfaceContent.xaml.cs:235-236` | 对比度回退递归调用无异常保护（理论路径：递归中抛异常则材质刷新中断） | 挂账 P3，随下次卫生批次（建议 try/finally） |
| F7-B1 | `WidgetWindowBase.Collapse.cs:2367-2383` | Compact 壳控件 4 个 async void 处理器委托至未保护的 OnCompact*RequestedAsync | 挂账 P3 |
| F7-B2 | `ContentWidgetWindow.xaml.cs:517` | OnCompactPrimaryActionRequestedAsync 无异常边界 | 挂账 P3 |
| F7-B3 | `OnboardingWindow.xaml.cs:484` | storyboard Completed 闭包未显式退订（GC 可回收，最佳实践问题） | 挂账 P3（可选改进） |
| F7-B4 | `SettingsWindow.Maintenance.cs:18/112` | 两个维护按钮无异常边界（模式不统一） | 挂账 P3 |
| F7-B5 | 全仓 174 处 | async void 事件处理器保护不统一的架构卫生观察 | 纳入审查 checklist |

另有 2 条非立案级观察项（面 C）：OBS-C-1（ShowPopup 公开 async void 形态，内部已有完整保护）、OBS-C-2（启动失败通知 toast→托盘降级为已知 design trade-off）。

## 已核验为安全/有效的关键面（防误报摘要）

- **撤销窗保留集**：13 处 GC 调用点全部经 `_gate` 锁 + `GetReferencedImagePathsCore` 共享路径并入保留集；10s 保留窗 > 5s 撤销 toast 展示窗，过期自清正确。
- **对比度回退**：递归仅触发一次（回退后 `backgroundCustom=false` 短路），回退后无需重刷材质面（材质面只对自定义背景生效）。
- **死宿主删除**：生产源码零残留引用；`QuickCaptureSurfaceContent` 订阅/退订完全对称（构造 ↔ Dispose）。
- **WeatherService 浅拷贝**：三出口全部独立实例；ViewModel 只读嵌套 payload（`ApplyWeatherData`/`PopulateDailyForecast` 均为只读消费），三实例污染隔离测试锁死。
- **Rust 冻结契约**：`git diff 4bc81af..HEAD -- native/` 为空集；十导出签名未变；无新增 DllImport/LibraryImport。
- **看门狗交互**：三组计时器独立 Start/Stop 对称；ForceReset 后与正常 End 路径串行于 UI 线程无真重入；watchdog 触发后 restore monitor 正常接管。
- **异步握手**：generation 防复活使任意时刻最多一个钩子线程处于安装态，无「双线程共存超一个周期」可能。
- **null-forgiving 抽检**：`_data!`（27 处均守卫后）、`handler = null!`、AOT smoke `Module!` 等均有前置守卫。

## 与台账/挂账清单的去重确认

本轮 5 条新发现均不在既有台账（DEF-008~029、R6 卫生批次 P3×45、F7 挂账清单）中；面 A/B 报告逐条对照确认无重复立案。挂账条目（MEM-01/02、ANI-03~06、WIN-08/09、LAY-05/06/08、QC-06/07/10~14、EVT-02、CFG-03/04/05/09、THR-06/07、EXC-04/05/06、ARC-02~05）维持挂账状态，作为下一轮迭代输入。
