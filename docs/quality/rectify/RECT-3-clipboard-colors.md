# RECT-3 整改版：随记板块剪贴板自定义配色功能

- 归属：复测未达标项整改批次 ｜ 关联缺陷：补充批次 B2（P1 功能缺失）｜ 验证方式：代码层面审查 + 自动化回归

## 一、复测结论 vs 证据对照

**复测称「未找到自定义文字颜色与背景色的设置入口与功能逻辑，属于需求遗漏」——复测结论部分成立，且是本批审计发现的最严重缺陷：**

- B2 功能在补充批次中被实现到了 `QuickCaptureWidgetWindow`（独立窗口宿主），但**该窗口类在当前源码中零实例化点**——随记板块生产路径已迁移到统一内容宿主 `ContentWidgetWindow` + `QuickCaptureSurfaceContent`（`WidgetManager.SurfaceContent.cs` 注释明示 "already migrated off a top-level, type-specific host"）。菜单、画刷、应用逻辑全部落在不可达的死窗口里，**运行时入口确实不存在**——审计子代理 A 以约 95% 置信度经全仓库穷举 grep 证实（`new QuickCaptureWidgetWindow` 零命中；生产创建路径逐层读毕）。
- （复测若跑安装版 1.4.8 则更早缺失，结论同样成立；两因叠加。）

**根因**：实现时沿用了过时的架构文档假设（`current_architecture.md` 曾记载「Only QuickCapture still owns a dedicated host」），未核实随记宿主已迁移——架构文档与代码的时滞问题已记录进台账。

## 二、修复方案与代码修改说明（已实施：完整移植到生产宿主）

| 文件 | 改动 |
|---|---|
| `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml` | `UserControl.Resources` 新增三支专属画刷（前景/背景/主题哨兵，初值走 ThemeResource）；记录项 `QuickCaptureSurfaceItemRoot` 背景改用专属背景画刷；`ListView.ItemContainerStyle` 增加 Foreground Setter 指向专属前景画刷（容器级继承覆盖全部子文本） |
| `Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs` | 移植全部应用逻辑：`ApplyClipboardItemColors`（跟随主题=主题哨兵/语义色；自定义=覆写色）、取色器对话框（含 HEX 输入，WinUI 内置 ColorPicker `IsHexInputVisible` 默认开启）、对比度校验拒绝、跟随主题/一键恢复；挂载 `ActualThemeChanged` 刷新与构造后首次应用；配置持久化经 `ViewModel.Config` + `_settingsService.UpdateWidget` |
| `Views/ContentWidgetWindow.Commands.cs` | 生产随记格子的 More 菜单（QuickCapture kind）新增「记录配色」子菜单（跟随主题/文字色/背景色/恢复默认/状态展示），取色器打开复用 flyout.Closed + 枚举 pending 模式（`_pendingClipboardColorPicker`） |
| `Services/SettingsService.cs` | `QuickCaptureClipboardColorSettings.NormalizeOverrides` 接入加载归一化管线（修复审计发现的死代码问题，对齐 `WidgetForegroundSettings` 模式） |

死窗口 `QuickCaptureWidgetWindow` 中的旧实现保留未删（该类整体退役属独立清理任务，不在本批最小侵入范围）。

## 三、代码审查结论

- 完整性矩阵（对照原始需求）：配色入口 ✓（生产随记 More 菜单）/ 取色器+HEX ✓ / 主题兼容 ✓（ActualThemeChanged + 哨兵画刷）/ 持久化 ✓（Metadata 覆写 + NormalizeOverrides 入管线）/ 对比度校验 ✓（阈值 1.3，拒绝弹窗）/ 一键恢复 ✓。✅
- 审计提示的次级文字（时间戳/摘要）对比度保护不足：属增强项，主文字/背景对已校验，列入后续（见验证方案）。
- 回归：x64 2998/2998 通过。

## 四、验证方案

1. 生产随记格子 More 菜单应出现「记录配色」（QuickCapture kind 条件挂载）。
2. 文字色/背景色自定义 → 记录列表即时变色并持久化；HEX 输入可用；不可读组合被拒。
3. 恢复默认 → 回到主题色；主题切换 → 跟随主题模式跟随。
