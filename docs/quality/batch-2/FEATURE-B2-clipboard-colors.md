# FEATURE-B2 随记板块剪贴板记录自定义配色功能

- 所属：长期迭代补充批次 ｜ 分类：新增功能 ｜ 验证方式：代码层面审查 + 自动化回归（无 GUI 运行测试）

## 一、功能设计

- **入口**：随记格子「更多」菜单 →「记录配色」子菜单：跟随主题（勾选态）/ 记录文字颜色… / 记录背景颜色… / 恢复默认配色 / 状态展示（文字·自定义 / 背景·自定义，只读勾选）。
- **选色方式**：`CommunityToolkit.WinUI.Controls.ColorPicker`（项目既有引用）——可视化取色 + 内置 HEX 精确输入，满足双方式要求。
- **默认与优先级**：默认「跟随主题」（与格子前景偏好一致的语义文字色 + 系统卡片背景色）；用户任一通道设为自定义后，该通道自动切换为自定义模式并优先生效；恢复默认一键清除全部四个覆写键。
- **实时生效**：保存即改专用画刷 `Color`，记录列表立即刷新（画刷为模板共享实例，一次赋值全列表生效）；随 `WidgetConfig` 持久化，重启后仍生效。
- **对比度校验**：WCAG 相对亮度对比度计算；待校验组合（未在编辑的通道取当前生效色）对比度 < 1.3 时**拒绝保存**并弹窗提示实测对比度与最低值，旧配色保持不变——杜绝"文字完全不可读"的极端组合。
- **不破坏既有功能**：仅替换记录项卡片背景与文字前景的画刷来源；复制/粘贴/删除/选择/悬停/详情交互零改动。

## 二、代码修改模块与核心逻辑说明

| 文件 | 内容 |
|---|---|
| `src/DeskBox/Services/QuickCaptureClipboardColorSettings.cs`（新增） | 四个 Metadata 覆写键（文字/背景 × 模式/色值，键名 `QuickCaptureClipboardItemText|Background(Color)Mode`）、`NormalizeMode`、读写/归一化、`ContrastRatio`（sRGB→线性→WCAG 亮度）、`IsPairReadable`（透明通道视为对比度 1.0 直接拒绝）、`NormalizeOverrides` |
| `src/DeskBox/Views/QuickCaptureWidgetWindow.xaml` | 新增专属画刷 `QuickCaptureClipboardItemForegroundBrush`/`BackgroundBrush` + 主题哨兵 `…CardThemeBrush`（`{ThemeResource CardBackgroundFillColorDefault}`）；`ListViewItemForeground*` 六个别名改指专属前景画刷（该窗口唯一 ListView 即记录列表，作用域天然限定）；记录项卡片 `ItemMaterialBackground` 背景改用专属背景画刷 |
| `src/DeskBox/Views/QuickCaptureWidgetWindow.Appearance.cs` | `ApplyClipboardItemColors()`（跟随主题=复用格子本地语义文字画刷当前值+哨兵卡片色；自定义=写入覆写色）；基类 `ApplyWidgetForegroundAppearance` 改 virtual 后 override 挂载——主题切换/前景偏好变化/外观刷新全部复用既有刷新链路；`ShowClipboardItemColorPickerAsync`（取色器对话框+对比度校验+保存）、`ShowClipboardColorRejectedDialogAsync`、`SetClipboardItemFollowTheme`、`ResetClipboardItemColors` |
| `src/DeskBox/Views/QuickCaptureWidgetWindow.Menus.cs` | `CreateClipboardItemColorMenu` 子菜单；取色器打开复用既有「菜单关闭后入队」模式（`flyout.Closed` + `DispatcherQueue` 入队） |
| `src/DeskBox/Views/WidgetWindowBase.Foreground.cs` | `ApplyWidgetForegroundAppearance` 由 protected 改 `protected virtual`（唯一签名改动） |

## 三、关键代码实现（节选）

```csharp
// ApplyClipboardItemColors：跟随主题模式与格子前景偏好保持一致
textBrush.Color =
    QuickCaptureClipboardColorSettings.GetTextModeOverride(Config) == ModeCustom &&
    QuickCaptureClipboardColorSettings.TryGetTextColorOverride(Config, out var textColor)
        ? textColor
        : semanticTextBrush.Color;           // 格子本地语义文字画刷当前值
backgroundBrush.Color = ... 自定义 ? backgroundColor : themeCardBrush.Color; // 主题哨兵

// 对比度校验（保存前）：WCAG 相对亮度
double lighter = Math.Max(L(text), L(background));
double darker  = Math.Min(L(text), L(background));
return (lighter + 0.05) / (darker + 0.05) >= MinimumContrastRatio /* 1.3 */;
```

## 四、兼容性与风险评估

- **配置兼容**：覆写存 `WidgetConfig.Metadata`（与 `WidgetForegroundSettings` 同一模式），旧 settings.json 无该键即回退跟随主题，无迁移；`NormalizeOverrides` 对非法色值做读时清除。
- **主题兼容**：跟随主题模式读「格子本地语义文字画刷」——与格子前景色（浅色/深色/自定义）联动，浅色系统主题下对比度问题由用户自定义+校验兜底（即需求动机）；高对比度可访问性模式由既有前景调色板链路处理，记录文字继承格子语义色。
- **作用域**：`ListViewItemForeground*` 别名仅在本窗口资源字典内重定义；窗口内唯一 ListView 即记录列表，其它控件不受影响；强调色元素（选择高亮、边条）保持系统强调色（刻意保留的视觉语义）。
- **风险**：低。所有新代码路径均有 try/catch；画刷改色不触碰列表虚拟化与交互逻辑。

## 五、代码审查要点与逻辑验证结论

- 逻辑正确性：模式判定（任一通道自定义即状态勾选）与保存/恢复/跟随三条路径闭环；透明色按不可读处理。✅
- 异常处理：取色器全链 try/catch；画刷读取缺失时回退默认色（`Microsoft.UI.Colors.White/Transparent`）。✅
- 资源管理：仅改既有画刷 Color，无对象新建/泄漏；对话框随 XamlRoot 释放。✅
- 性能影响：一次赋值全列表生效（共享画刷）；无逐项遍历、无额外绑定。✅
- 一致性：与 `WidgetForegroundSettings` 覆写模式、取色器交互、菜单关闭后入队模式完全同构；本地化 12 语言同键覆盖。✅
- 回归：x64 全量 2998/2998 通过。
