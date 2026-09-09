# VERIFY-1 验证版：悬停展开动画优化 + 帧率四档可选（30/60/90/120）

- 归属：遗留问题清单批次（项①）｜ 关联：DEF-003 ｜ 验证方式：代码层面审查 + 自动化回归；运行时人工复验由用户执行

## 一、需求与实现对照

| 要求 | 实现 |
|---|---|
| 主控制面板「常规」选项卡新增动画帧率选择，四档 30/60/90/120 | `SettingsWindow.xaml` 动画组新增 SettingsCard（`Settings.Animation.FrameRate.Title`），ComboBox 四档（`AvailableWidgetFrameRateOptions`，SettingsOption.Value 为 int 30/60/90/120，`SelectedValuePath="Value"` 双向绑定 `SelectedWidgetFrameRate`） |
| 帧率设置与动画核心逻辑联动、切换实时生效 | 设置写入 `AppSettings.WidgetAnimationFrameRate`（SaveDebounced）；每次胶囊动画启动（`Collapse.cs` BeginWidgetCollapse）读取该值（`NormalizeFrameRate` 归一到四档，非法回退 60），故下一次动画即生效，无需重启 |
| 帧调度与渲染逻辑优化、消除卡顿 | 两处 warm-up 增强（见下）+ 既有候选 1（合成器动画）+ 帧跳 ladder 保持 |

### 交付细节

1. **档位→节奏解析**（`WidgetCompactFrameSkipPolicy.ResolveSkipForFrameRate`）：实际节奏 = 刷新率/目标 四舍五入，恒 ≤ 目标。165Hz 屏：120→skip2（≈82fps，最接近 120 的可整除节奏）、90→skip2、60→skip3（55fps）、30→skip6（27fps）。60/30 档同时映射到自适应 ladder 的 Sixty/ThirtyFpsLevel。
2. **cap 模式旁路自适应升级**：`RecordCollapseAnimationTickCadence` 在 `_compactFrameRateCapActive` 时不再 escalate——用户选定的速率即最终速率，会话不静默降档（原 ladder 逻辑在无 cap 时保持不变）。
3. **冷启动首展优化**（审计 B 设计、主流程实施）：①warm-up 门控拒绝分支在视觉树未预热时仍执行预算化预热切片（`PrimeCompactExpansionVisualTree("warmup-gate-priming")`，4ms/48 节点预算）；②内存 epoch 重预热对可见窗口升级 urgent 队列。两者共同把此前一次性 dropped=31/60 的冷启动首展成本前移出动画窗口。

## 二、代码验证要点结论

- **定时器精度**：帧时钟仍为 CompositionTarget.Rendering（Win11）/DwmFlush 对齐（Win10），未变；cap 只改「第 N 个 tick 才落 HWND 边界」，tick 源精度不受影响。✅
- **重绘区域控制**：被跳过的 tick 完全不调 SetWindowPos/SetCompactTransitionProgress，无部分重绘风险；结束帧（progress≥1）不受 cap 限制，保证终态精确落位。✅
- **帧插值**：progress 为时间基（elapsed/duration），低节奏下每次落位仍插值到正确 eased 进度，无跳变伪影；结束时 `CompleteBoundsTransition` 覆盖终态。✅
- **档位边界**：`NormalizeFrameRate` 把任意存储值归一 {30,60,90,120}（其它→60）；`ResolveSkipForFrameRate` 对 target≥refresh 回退 skip=1（全速率）；cap 激活时 Escalate 被旁路，会话粘性降级不与 cap 冲突。✅
- **契约联动**：`AotStage4D1BContractTests` 本地化控件计数更新（SettingsCard Header 166 / Description 136、Expander Header 21 / Description 8、总计 333）；`AotStage5B4B1` bindable 清单注册 `SelectedWidgetFrameRate` + `AvailableWidgetFrameRateOptions`（nameof 计数 306→308）。✅
- 回归：x64 2998/2998 通过。

## 三、人工复验清单

1. 设置 → 常规 → 动画组出现「动画帧率」下拉（30/60/90/120 fps），默认 60。
2. 四档逐一切换后各做 10 次悬停展开/收起：日志 `CompactAnimation` 的 frames/dropped 应满足 dropped≈0（帧预算按 1000/165 不变，落位间隔=skip×6.06ms）。
3. 冷启动后立即悬停：首展不再出现 dropped=31 量级。
