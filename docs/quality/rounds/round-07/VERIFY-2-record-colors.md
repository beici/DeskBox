# VERIFY-2 验证版：随记记录配色生效修复 + 设置入口迁移

- 归属：遗留问题清单批次（项②）｜ 关联：BATCH2-F2 / RECT-3 ｜ 验证方式：代码层面审查 + 自动化回归；运行时人工复验由用户执行

## 一、「无法生效」根因（复检 + 本轮补强）

1. **历史根因（RECT-3 已修）**：配色功能曾整挂在零实例化的 `QuickCaptureWidgetWindow` 上（运行时不可达），已移植生产宿主 `QuickCaptureSurfaceContent`。
2. **本轮复检发现两个残余「不生效」成因并修复**：
   - 模板中时间戳/次级文字带**显式主题前景**（`Foreground="{ThemeResource TextFillColorSecondaryBrush}"`），绕过容器继承——自定义文字色永远作用不到它们。修复：新增派生画刷 `QuickCaptureClipboardItemSecondaryBrush`（自定义模式 = 主文字色 0xE8 透明度降阶，保证对比度关系；主题模式 = 原主题次级色），时间戳改引该画刷。
   - `TryGetThemeColor` 仅查裸 Color 资源，而主题令牌多为 Brush 形态 → 跟随主题回退可能落错。修复：Brush 形态回退解析。

## 二、设置入口迁移至「功能格子-随记」选项卡

| 要求 | 实现 |
|---|---|
| 入口迁入主控制面板「功能格子-随记」 | `SettingsWindow.xaml` 的 `QuickCaptureSettingsSection`（随记功能区）新增「记录配色」SettingsExpander：主体 =「记录文字颜色…」按钮（显示当前状态：跟随主题 / 自定义），展开项 =「记录背景颜色…」（显示当前 HEX）与「恢复默认」 |
| 完整保留对比度校验/一键恢复/持久化/主题兼容 | 新增共享编辑器 `QuickCaptureClipboardColorEditor`（取色器+HEX、WCAG 对比度拒绝、Metadata 覆写持久化、Reset）；随记格子内菜单与设置入口**共用同一编辑器与同一配置源**，双入口不可漂移；surface 的 `ApplyClipboardItemColors` 保持为唯一应用点，设置提交后经 `WidgetManager.ApplyQuickCaptureClipboardColorsToLoadedWidgets()` 即时推送到所有已加载随记 surface |

## 三、代码验证要点结论

- **配色样式优先级**：自定义模式优先（GetModeOverride=Custom 即用覆写色）；跟随主题回退链 = 主题哨兵画刷 → App 资源（Color 或 Brush 形态）→ 硬编码兜底。✅
- **状态同步**：设置入口提交 → UpdateWidget（配置）+ Manager 推送（运行中 surface）+ 按钮文案刷新；格子菜单入口提交 → UpdateWidget + 本 surface 立即应用（其它 surface 下次 Appearance 刷新或经设置入口推送）。✅
- **边界色值容错**：任意非法存储值由 `NormalizeOverrides` 在加载时清除（已入管线）；对比度 <1.3 的提交被拒并弹窗显示实测比率。✅
- **实时生效**：自定义色写入专属 SolidColorBrush 实例（列表模板共享引用），单次赋值全列表生效，无逐项遍历。✅
- 回归：x64 2998/2998 通过（12 语言键位覆盖校验通过，新增 6 键）。

## 四、人工复验清单

1. 设置 → 功能格子 → 随记 →「记录配色」：设文字色 → 打开的随记记录列表主文字+时间戳全部变色；设背景色 → 记录卡片变色；按钮显示当前状态/HEX。
2. 「恢复默认」→ 回主题色，按钮回到「跟随主题」。
3. 对比度接近的组合（如白底白字）被拒绝弹窗。
4. 格子内菜单（右键随记 → 更多 → 记录配色）与设置入口互相一致。
