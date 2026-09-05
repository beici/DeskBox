# FEATURE-B1 格子边距手动精确输入功能

- 所属：长期迭代补充批次 ｜ 分类：新增功能 ｜ 验证方式：代码层面审查 + 自动化回归（无 GUI 运行测试）

## 一、功能设计

- **入口**：格子标题栏「更多」菜单 →「边距设置…」（两种宿主：ContentWidgetWindow 与 QuickCaptureWidgetWindow）。
- **对话框**：模式切换（统一边距 / 分边设置，`RadioButtons`）+ 数值输入（统一模式 1 个输入框；分边模式 上/下/左/右 4 个输入框）+「应用到全部可见格子」复选 + 校验提示行。
- **边距语义**：格子对应边到所在显示器工作区（WorkArea）对应边的像素距离。分边模式：输入某边值 → 沿该轴把格子移到该边距处（每轴取距离该边较近的一侧解释，避免死区歧义）；统一模式：格子沿**当前最近的边**吸附，使该边距离等于输入值。
- **实时预览与生效**：输入合法值即移动窗口（所见即所得）。取消（Cancel/关闭）时恢复对话框打开时捕获的初始位置并持久化（单格模式）；「确认」保留当前结果并保存边距录入模式偏好。批量模式即时生效（不随取消撤销，已在界面语义与文档中标注）。
- **双向同步**：对话框每次打开时从窗口实时 bounds 派生四边距离填充输入框——拖动/缩放后的新位置自然反映为新的边距数值；输入应用后同样回填最新派生值，不维护第二份位置数据（单一事实来源）。
- **范围与容错**：0–200px 整数；非法输入（非数字/越界）在 TextChanged 即被拦截并显示范围提示，不触发移动；Save 时刻二次校验全部输入框。
- **持久化**：位置经既有锚点坐标模型持久化（`CapturePositionAnchor` + `UpdateConfigFromPhysicalBounds`，与拖拽同链路）；仅「录入模式偏好」存 `WidgetConfig.Metadata["WidgetMarginEntryMode"]`（覆写模式，旧配置文件免迁移兼容）。
- **批量**：勾选后经 `WidgetManager.MoveVisibleWidgets` 遍历全部可见格子按同一规则移动并逐格持久化。

## 二、代码修改模块与核心逻辑说明

| 文件 | 内容 |
|---|---|
| `src/DeskBox/Services/WidgetMarginSettings.cs`（合并在 `WidgetTitleAppearanceSettings.cs` 文件内） | 模式常量/归一化、Metadata 读写、`ClampMargin`/`TryParseMargin`（0–200 拦截） |
| `src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs`（新增） | `ShowMarginDialogAsync`（对话框构建、模式切换可见性、TextChanged 预览+拦截、Cancel 恢复、Save 保存偏好）、`ApplyMarginsFromDialog`（单格/批量分派 + 双向同步回填）、`ApplyOwnMarginDelta`（SetWindowPos + `CapturePositionAnchor` + `UpdateConfigBoundsFromPhysical`）、`ShiftBoundsToMargins`/`ShiftBoundsToNearestEdge`（纯函数几何计算）、`GetCurrentWorkAreaMargins` |
| `src/DeskBox/Services/WidgetManager.BulkAppearance.cs`（新增） | `MoveVisibleWidgets(TransformWidgetBounds)`：遍历可见窗口 → 物理移动（`SWP_NOZORDER|SWP_NOACTIVATE`，不动 z-order 不抢焦点）→ `WidgetPositioningService.UpdateConfigFromPhysicalBounds` 持久化 → 批量 `SaveDebounced` |
| `src/DeskBox/Views/ContentWidgetWindow.Commands.cs` / `QuickCaptureWidgetWindow.Menus.cs` | More 菜单挂「边距设置…」入口 |

## 三、关键代码实现（节选）

```csharp
// 统一模式：沿最近边吸附（纯函数，返回 null 表示无需移动）
int min = Math.Min(Math.Min(left, top), Math.Min(right, bottom));
int clamped = Math.Clamp(margin, 0, 200);
if (min == left) x = workArea.X + clamped;
else if (min == top) y = workArea.Y + clamped;
else if (min == right) x = workRight - clamped - bounds.Width;
else y = workBottom - clamped - bounds.Height;

// 双向同步：对话框数值始终从实时 bounds 派生
(int newLeft, int newTop, int newRight, int newBottom) = GetCurrentWorkAreaMargins();
leftBox.Text = newLeft.ToString(CultureInfo.InvariantCulture); // 四框 + 统一框依次回填
```

## 四、兼容性与风险评估

- **配置兼容**：不新增布局字段——位置由既有 `Config` 锚点模型承载（拖拽同链路持久化），旧配置读写零影响；唯一的 Metadata 键缺失即回退统一模式。
- **多显示器/DPI**：工作区经 `DisplayArea.GetFromPoint(Nearest)` 按格子当前所在屏解析；移动是物理像素操作，持久化经既有物理→锚点换算服务，与拖拽一致。
- **风险**：批量模式不随 Cancel 撤销（语义已在复选框文案与本文档标注）；带位置锚定的格子经批量移动后，锚点在下次恢复时以最近一次持久化为准（与拖拽行为一致）。
- **性能**：仅对话框打开期间每次合法编辑触发一次 `SetWindowPos`；无轮询、无后台开销。

## 五、代码审查要点与逻辑验证结论

- 逻辑正确性：统一/分边/批量三条路径均经 0–200 校验拦截；Save 时二次全框校验；几何函数为纯函数且对「已满足值」返回 null 短路。✅
- 异常处理：对话框全链 try/catch；`GetWindowRect` 失败短路；picker/解析异常仅记日志不崩溃。✅
- 资源管理：对话框控件由 XamlRoot 生命周期管理；无新增句柄/计时器。✅
- 性能影响：无全局重绘（仅目标窗口移动）；批量路径单次合批 SetWindowPos 逐窗调用（非帧路径）。✅
- 一致性：持久化复用 `CapturePositionAnchor`+`UpdateConfigFromPhysicalBounds` 既有链路；配置覆写模式与 `WidgetForegroundSettings` 同构；默认值契约测试（`ApplyDefaultPreferences`）已补登记。✅
- 回归：x64 全量 2998/2998 通过。
