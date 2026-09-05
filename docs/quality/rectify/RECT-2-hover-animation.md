# RECT-2 整改版：格子悬停自动展开动画卡顿问题

- 归属：复测未达标项整改批次 ｜ 关联缺陷：DEF-003（P1）｜ 验证方式：代码层面审查 + 自动化回归

## 一、复测结论 vs 证据对照

**复测称「动画依然存在卡顿，未达稳定流畅目标」——与开发构建的三组 A/B 实测不符（修复前 3–10% 掉帧 → R1 修复 0.49% → 候选 1 后 0%，165Hz、maxFrame 6.8–7.9ms），判定为复测二进制陈旧（安装版 1.4.8.0 构建于 2026-08-29 19:14，早于修复提交 97eedca）。** 整改要求中的「Vue3 端帧动画/虚拟 DOM」同样不存在于本仓库（技术栈核验见 RECT-1）。

## 二、根因分析（本轮新增发现）

候选 1 已消除合成期全部逐帧 DP 写，帧路径唯一剩余的每帧堆分配是 `MoveWindowWithoutPersisting`（`WidgetWindowBase.Collapse.cs:3676`）向 `TryQueueBoundsMove` 传入的三个 lambda——每帧捕获 `bounds`+`this` 产生 3 个闭包（Gen0 压力，非卡顿主因）。

冷启动「首次悬停展开」的一次性掉帧（dropped=31/60）根因：全量 warm-up 切片受 app-idle 门控，若用户在 warm-up 完成前悬停，期限兜底走 `ExpandWithLiveLayoutFallback`，完整布局成本落进首展动画。

## 三、修复方案与代码修改说明（已实施）

| 改动 | 位置 | 说明 |
|---|---|---|
| 门控外预算化预热 | `Collapse.cs` 运行循环 | 当全量切片被门控拒绝且视觉树未预热时，执行 `PrimeCompactExpansionVisualTree("warmup-gate-priming")`——该切片自带预算（4ms/48 节点），把冷启动首展的大头工作提前到悬停之前，且不破坏 app-idle 让路语义（守卫在拒绝分支内） |
| epoch 重挂升级 urgent | `App_MemoryCleanupEpochAdvanced` | 可见窗口的内存 epoch 重预热改走 urgent 队列（`IsWindowVisible` 判定）——用户下一次悬停即可命中，而非等后台空闲 |

评估后**不做**：MoveWindowWithoutPersisting 闭包改字段缓存（收益 Gen0 级、改动侵入帧路径，风险/收益比不划算，留观察）。

## 四、代码审查结论

- 新增预热分支不改变既有 Ready/Progressed/Blocked 循环语义（拒绝分支内执行、异常仅记日志）；`_isCompactExpansionVisualTreePrimed` 守卫防重复预热。✅
- urgent 判定用 `Win32Helper.IsWindowVisible`（usings 已有）。✅
- 回归：x64 2998/2998 通过。✅

## 五、验证方案

1. 冷启动后立即悬停任意胶囊 → 首展应命中预算化预热（日志 `CompactWarmup ... warmup-gate-priming`）+ `CompactAnimation` dropped 不再出现 31/60 量级（首展 dropped 观察项据此复核关闭）。
2. 稳态 S3 五连悬停维持 dropped=0。
