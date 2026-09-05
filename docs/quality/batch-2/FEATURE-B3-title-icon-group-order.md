# FEATURE-B3 格子标题布局自定义、图标自定义与格子组标题排序功能

- 所属：长期迭代补充批次 ｜ 分类：新增功能（组内排序为既有能力确认+入口统一）｜ 验证方式：代码层面审查 + 自动化回归（无 GUI 运行测试）

## 一、功能设计

### 1. 标题与图标位置对齐（新增）
- 入口：格子「更多」菜单 →「标题与图标」→ 左对齐 / 居中对齐 / 右对齐（勾选态指示当前值）→「将该对齐应用到全部格子」（批量）。
- 作用对象：标题栏内「图标 + 标题文字」整体（`TitleIdentityHost`）；组模式下同步作用于组切换器（右对齐生效，左/中保持既有 Stretch 布局以不破坏切换器交互）。
- 生效链路：挂载于两宿主的 `ApplyTitleBarLayout` 尾部（主题/外观/chrome 刷新自动重放）；切换实时生效、随配置持久化；文字过长仍由既有 `TextTrimming` 处理，无截断/布局错乱新风险。

### 2. 格子自定义图标（新增）
- 入口：「标题与图标」→「选择自定义图标…」（`FileOpenPickerService`，过滤 PNG/ICO/JPG/JPEG）→「恢复默认图标」/「恢复全部格子默认图标」（批量）。
- 适配：`WidgetTitleIcon` 新增 `CustomImageSource` 依赖属性，自定义源优先生效，经 `ColorIcon` 宿主居中 + `Stretch=Uniform` 适配标题图标尺寸（不变形）；空值即回退内置图标。
- 持久化：绝对路径存 `WidgetConfig.Metadata["WidgetCustomIconPath"]`；读取时校验扩展名与文件存在性，文件丢失/解码失败回退默认图标（仅记日志）。

### 3. 格子组标题顺序自定义（既有能力确认）
- 现状核实：组内成员顺序自定义**已完整实现**——组切换器「移动」子菜单提供每成员上移/下移（`WidgetGroupTitleSwitcher.xaml.cs:1224` 起），事件链 `ReorderRequested → GroupMemberReorderRequested → ReorderWidgetGroupMemberAsync`（`WidgetManager.Groups.cs:1561`），以 `WidgetGroupOrder.MoveToTargetSlot` 持久化到 `group.MemberIds`；排序仅改变标题栏成员展示/切换顺序，不触内容与展开收起逻辑，且实现注释已为拖拽预留稳定目标槽语义。
- 本批次动作：不重复实现；将「标题与图标」「边距」等新入口与组切换器菜单并列，并在本文档固化能力边界（交互形态为菜单排序；标题栏标签拖拽重排列为后续增强候选，因当前切换器为「单成员显示+切换」形态而非并排标签条，强行加拖拽收益低、易碎性高）。

## 二、代码修改模块与核心逻辑说明

| 文件 | 内容 |
|---|---|
| `src/DeskBox/Services/WidgetTitleAppearanceSettings.cs`（新增） | 对齐常量/归一化/全局+覆写解析（`ResolveAlignment` 全局默认 + Metadata 覆写）、图标路径读写（扩展名+存在性校验）、`NormalizeGlobal` |
| `src/DeskBox/Models/AppSettings.cs` | 新增全局默认 `WidgetTitleAlignment = "Left"`（`ApplyDefaultPreferences` 已登记重置策略，契约测试把关） |
| `src/DeskBox/Controls/WidgetTitleIcon.xaml.cs` | `CustomImageSource` DP + `ApplyVisualState` 自定义分支 + `ApplyCustomImageIcon`（居中/统一缩放/尺寸 clamp，资产名缓存复位） |
| `src/DeskBox/Controls/WidgetShell.xaml.cs` | `SetTitleAlignment`（TitleIdentityHost + GroupTitleSwitcher）、`SetTitleCustomIcon` 转发 |
| `src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs` | `ApplyTitleAppearance`（解析→布局+图标；BitmapImage 异步解码、异常回退默认）、`CreateTitleAppearanceMenu`（对齐三选/批量对齐/选图标/单格恢复/全部恢复，批量经 `WidgetManager.ApplyTitleAppearanceToVisibleWidgets` 走既有外观预览链路） |
| `ContentWidgetWindow.Commands.cs` / `QuickCaptureWidgetWindow.Menus.cs` / 两宿主 `ApplyTitleBarLayout` | 菜单挂载 + 应用链路挂载 |

## 三、关键代码实现（节选）

```csharp
// WidgetTitleIcon：自定义源优先，居中+统一缩放（尺寸适配与居中裁剪）
if (CustomImageSource is not null)
{
    ApplyCustomImageIcon(iconSize);   // clamp 到 18–34px，Uniform 居中
    ApplySurfaceCornerRadiusOverride();
    return;                            // 跳过内置 Line/Filled/Text 模式
}

// 对齐解析：全局默认 + per-widget 覆写
string alignment = WidgetTitleAppearanceSettings.ResolveAlignment(Config, SettingsService.Settings);
WidgetShellControl.SetTitleAlignment(alignment switch { Center => Center, Right => Right, _ => Left });

// 批量恢复/对齐：清覆写 + 全窗口外观预览重放
foreach (WidgetConfig widget in SettingsService.Settings.Widgets)
    WidgetTitleAppearanceSettings.SetCustomIconPath(widget, null);
SettingsService.UpdateWidgetsBatch(SettingsService.Settings.Widgets);
App.Current?.WidgetManager?.ApplyTitleAppearanceToVisibleWidgets();
```

## 四、兼容性与风险评估

- **配置兼容**：全局仅增一个带默认值与重置策略的字段；覆写走 Metadata（旧文件免迁移）；组排序持久化（`MemberIds`）为既有存储，未动。
- **交互兼容**：对齐不改变标题点击/拖拽/展开收起路径（仅 HorizontalAlignment）；组模式右对齐外保持 Stretch，避免压缩切换器可点区域。
- **图标安全**：路径读取时校验扩展名与文件存在性；`BitmapImage` 构造/解码异常被捕获并回退默认图标，不阻断布局链。
- **风险**：低。批量操作走既有 `ApplyAppearancePreview` 全窗重放，属既有低频路径。

## 五、代码审查要点与逻辑验证结论

- 逻辑正确性：对齐三态归一化（非法值回退 Left）；图标缓存键在自定义↔内置切换时正确复位（`_currentColorAssetName` 清空，防止残留内置 SVG）。✅
- 异常处理：picker/BitmapImage/批量化全链 try/catch。✅
- 资源管理：`BitmapImage` 随 XAML 解码缓存管理，低频创建；无新增计时器/句柄。✅
- 性能影响：`ApplyTitleAppearance` 仅在标题栏布局重放（低频）时执行；批量恢复为用户显式低频操作。✅
- 一致性：覆写模式/菜单风格/刷新链路与 `WidgetForegroundSettings` 及既有 builder 完全同构；12 语言同键覆盖。✅
- 回归：x64 全量 2998/2998 通过（含 `ApplyDefaultPreferences` 覆盖契约——新增字段已登记）。
