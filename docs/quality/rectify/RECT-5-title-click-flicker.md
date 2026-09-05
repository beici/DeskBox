# RECT-5 整改版：点击格子标题栏格子闪烁 Bug

- 归属：复测未达标项整改批次 ｜ 关联缺陷：DEF-006（P2）｜ 验证方式：代码层面审查 + 自动化回归 + 既有像素级实测

## 一、复测结论 vs 证据对照

**复测称「所有格子依然出现一起闪烁，原修复未生效」——与开发构建的实测证据不符，判定为复测二进制陈旧（安装版 1.4.8.0 无此修复，闪烁正是其原始表现；R4 像素 diff 在含修复的构建上为 changed=0）。**

补强证据：独立复审子代理对修复落地做了穷举核验（HEAD=43af52a）：
- `WidgetLayerService.BringTitleActivatedGroupToFront` 实现完整（DesktopPinned 早退/句柄过滤/active 单次往返/peers 单批 DeferWindowPos + 逐窗兜底/锁可重入无死锁）。
- 全仓库批量 TOPMOST 往返路径逐点排查：标题点击链路已全部消除（仅托盘唤起 `TrayAnimation.cs:556` 有意保留——托盘语义就是全员浮起，属功能本体；QuickReveal 分支已用单向置顶）；启动恢复为一次性路径。
- 两宿主（ContentWidgetWindow/QuickCaptureWidgetWindow）均接新方法。

## 二、本轮整改动（二处小改进，非缺陷修复）

| 改动 | 位置 | 说明 |
|---|---|---|
| peer 序改用 idle 序 | `WidgetManager.ZOrder.cs` `ActivateAllVisibleWidgetsFromTitle` | 原实现按枚举序插入 peers，2.3s 临时浮起窗口内的组内层叠与稳态 idle 序不一致（瞬时观感）。改用既有 `GetWindowsInIdleHighestFirstOrder`，浮起窗口的组序与静止布局一致 |
| 兜底链锚定修正 | `WidgetLayerService.cs` 兜底循环 | 逐窗 SetWindowPos 失败时不再推进插入锚——失败窗口后面的 peers 继续挂在上一成功者之后，群组保持连续（原实现失败后照常推进锚点会断裂群组） |

## 三、代码审查结论（含独立复审子代理对本修复的裁定）

- 托盘路径保留 `BringGroupTemporarilyToFront` 的判定：**有意保留成立**——托盘场景没有"未触及的 peers"，TOPMOST 往返即功能语义，且伴随托盘动画掩盖；QuickReveal 分支已用无往返的 `HoldGroupTopMostWithoutActivation`。✅
- 已知理论残留（复审裁定为记录不改）：合批无 try/finally（P/Invoke 返回码风格，HDWP 泄漏为理论风险）；不校验最终 peer 序（后续 idle 归一收敛）。✅
- `SetCompactTransitionProgress` 残留空块（候选 1 遗留）属代码卫生，不影响行为。✅
- 回归：x64 2998/2998 通过（含 `WidgetZOrderRestoreContractTests` 层级契约与 `Windows10WidgetMotionContractTests` 合成器契约）。

## 四、验证方案

1. 复测必须使用**仓库构建**（启动日志首行 `[Build] running path=... buildTime=...` 可直接识别二进制，`D:\DeskBox` 安装版 1.4.8 不含本修复）。
2. 像素级复测脚本：点击标题前后对未触及格子区域做区域 diff，预期 changed=0（R4 已用 `capture-screen.ps1` 建立此方法）。
