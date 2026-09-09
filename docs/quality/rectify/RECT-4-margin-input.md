# RECT-4 整改版：胶囊格子手动边距输入功能

- 归属：复测未达标项整改批次 ｜ 关联缺陷：补充批次 B1（P1 功能「未实现」+ 5 处真实缺口）｜ 验证方式：代码层面审查 + 自动化回归

## 一、复测结论 vs 证据对照

**复测称「未找到边距手动输入的设置功能，属于需求遗漏」——在开发构建上不成立**：入口（格子标题栏 More 菜单「边距设置…」/右键菜单，`ContentWidgetWindow.Commands.cs`）与功能（对话框、批量、持久化）均在 HEAD 且经 R4 GUI 实测（输入 66 即时移动、取消精确恢复）。判定复测二进制陈旧（安装版 1.4.8 无此功能）。

**但审计子代理对照原始需求逐条核对后，确认了 5 处真实缺口/缺陷**——本批全部修复或如实标注：

## 二、根因分析与缺口清单（审计子代理发现）

| 编号 | 缺口 | 根因 |
|---|---|---|
| B1-G1 | 位置锁定被绕过：已锁定格子仍被边距对话框移动 | 边距路径未检查 `IsPositionLocked`（拖拽路径有检查） |
| B1-G2 | 程序化回写触发虚假校验错误：预填/回写的实测边距可能 >200，程序化 `.Text` 赋值触发 TextChanged→校验，弹「非法」提示 | 无「程序化写入 vs 用户编辑」区分 |
| B1-G3 | 批量模式取消不恢复 | 单窗取消仅还原发起窗口（设计如此并已标注） |
| B1-G4 | Save 静默失败：>200 的实况值使整表校验失败且无提示 | Save 对全部框做 0–200 校验 |
| B1-G5 | 批量移动把折叠胶囊瞬态几何写入持久化配置 | `MoveVisibleWidgets` 只过滤 Visible，未过滤胶囊态 |
| B1-G6 | 逐边模式编辑远侧被静默吞掉：编辑非最近边的框不生效且输入被回写清除 | 近侧仲裁不看「用户编辑的是哪一侧」 |

## 三、修复方案与代码修改说明（已实施）

| 文件 | 改动 |
|---|---|
| `WidgetManager.BulkAppearance.cs` | `MoveVisibleWidgets` 增加守卫：跳过 `Config.IsPositionLocked`（G1）、`IsCompactArrangementActive`/`IsCompactCollapsedState`（G5，胶囊瞬态几何永不入 resting 配置） |
| `WidgetManager.cs` | `IDesktopWidgetWindow` 新增 `IsCompactCollapsedState`（实现：`WidgetWindowBase.Collapse.cs` 的 `IsCompactBoundsStateActive`） |
| `WidgetWindowBase.TitleAppearance.cs` | ① `ApplyOwnMarginDelta` 前置 `Config.IsPositionLocked` 拦截（G1）与 `IsCompactBoundsStateActive` 拦截（胶囊态瞬态移动不持久化，G5 单窗面）；② 新增 `suppressMarginPreview` 标志：程序化回写（`SyncBoxesFromLiveMargins`）期间禁止重入校验循环（G2）；③ `Save` 校验失败改为**显示内联错误**而非静默 return（G4）；④ `UpdateModeVisibility` 切回统一模式时用实时边距重填（P2-5 陈旧值）；⑤ `TryPreview` 记录用户编辑的边（`editedSides` 集合），`ShiftBoundsToMarginsForSides` 按**编辑侧优先**生效——远侧编辑不再被近侧仲裁吞掉（G6） |
| `ShiftBoundsToMarginsForSides`（重命名/重写） | 仅用户编辑过的边生效；Left/Right（或 Top/Bottom）同轴双编辑按 Left/Top 优先；宽度固定语义保持 |

语义说明（对照原需求）：批量设置语义为「应用到全部可见格子」（多选机制产品中不存在，隐藏/托盘格子不参与）；取消恢复仅覆盖单窗模式（批量即时生效已标注）——两项为评审后的有意语义，记录于文档。

## 四、代码审查结论

- editedSides 以 TryPreview（用户路径）登记、SyncBoxesFromLiveMargins 被抑制不登记——程序化回写永不污染编辑意图。✅
- `IsCompactCollapsedState` 经接口暴露，`WidgetWindowBase` 单实现覆盖双宿主。✅
- Save 校验失败现在有内联提示（原 G4 静默）。✅
- 回归：x64 2998/2998 通过。

## 五、验证方案

1. 位置锁定开启 → 边距对话框输入 → 格子不移动。
2. 逐边模式编辑非最近边（如贴右窗口改 Left）→ 格子左移到指定边距（修复前无效且输入被清）。
3. 统一/逐边来回切换 → 统一框始终显示实时值。
4. 折叠胶囊上「应用到全部」→ 胶囊被跳过，展开后配置无损。
5. 取消 → 单窗恢复原位。
