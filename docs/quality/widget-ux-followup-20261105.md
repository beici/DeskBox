# 天气、截图与番茄钟体验优化

## 交付内容

- Fluent周日图标对应WMO 80阵雨，太阳和云并不表示天气按时间从多云转晴。重绘rain-showers.svg，太阳光线更短、云层更柔和、雨滴与已有rain.svg保持一致；不改天气映射。
- 自动选区从每次PointerMoved枚举系统窗口，改为冻结屏幕时在后台记录窗口顺序与边界，再按指针坐标命中。支持DeskBox自身可见窗口，不再统一排除当前进程及所有工具窗；仍跳过隐藏/最小化/cloaked/穿透窗口和桌面宿主。选区裁剪到当前冻结屏幕，桌面空白回退完整屏幕。
- 相同预选矩形不重复更新布局，放大镜更新合并到下一显示帧，同一物理像素不重复采样。关闭或隐藏时解除Rendering订阅。
- 放大镜与坐标/色值合并为160 DIP宽圆角卡片，采用细绿色十字线、色块、等宽色值；按整卡实际高度避让屏幕边缘，长翻译可换行。
- 关闭截图根元素的KeyboardAcceleratorPlacementMode自动提示，去掉悬浮Esc提示框，保留Esc逐级退出行为。
- 未确认选区时显示“按Ctrl+C复制色值”；确认选区后Ctrl+C复制截图。C仍可切换取色模式，Enter或双击可快速确认并复制窗口截图。拖动中不响应Ctrl+C，文字编辑时保留文本复制语义。色值复制异常保留错误日志与界面反馈。
- 番茄钟采用浅/深中性盘面、细进度环、轻字重等宽数字、小番茄标记和圆角矩形主按钮；专注/短休息/长休息仍使用原有强调色。计时、暂停、轮次及响应式宿主逻辑保持；高对比模式使用系统颜色。

## 代码位置

- src/DeskBox/Assets/WeatherIcons/fluent/rain-showers.svg
- src/DeskBox/Views/ScreenshotCaptureWindow.xaml、.xaml.cs
- src/DeskBox/Services/ScreenshotSelectionSnapshot.cs
- src/DeskBox/Controls/WidgetContents/PomodoroWidgetContent.xaml、.xaml.cs
- tests/DeskBox.Tests/ScreenshotSelectionSnapshotTests.cs
- tests/DeskBox.Tests/RoundSixAccessibilityContractTests.cs：文字对比度断言改为读取实际中性盘面颜色，维持4.5:1要求。

## 验证结果

- 最终x64 Debug构建：0错误，20条既有警告。E:/DeskBox-debug/DeskBox.dll写入时间2026-11-05 16:05:43，晚于本次构建开始，产物与测试引用DLL哈希相同。
- 定向158项：157通过、0失败、1既有天气资源重复检查跳过。新增4项测试覆盖遮挡顺序、负坐标跨屏裁剪、右下排他边界、桌面空白回退。
- 全量6377项：6366通过、10失败、1跳过；原9失败仍在，多出的CloudBackupScopedTests.ScopedRestore_MultipleOrphans_PairOntoFreeWidgets在DeskBoxDataBackupService.cs:1169 Directory.Move收到Access denied。与前次审计偶发错误处于同一路径；本轮未改备份代码。完整备份组复跑46/46通过，但不把复跑解释为问题已修复，不称全量全绿。原始TRX和日志保留。
- AOT按项目要求执行publish-aot-audit.ps1；应用和Updater原生编译完成，但审计2项失败：当前树既有SearchResultRowAutomationPeer非partial引发CsWinRT1028及警告集合扩张。WMC1510=808。本轮未修改该文件；AOT审计不算通过。
- 原AOT输出目录先移动到同父目录win-x64-preserved-ux-时间戳，未删除原审计产物；实际路径在.scratch/widget-ux-followup/preserved-aot-path.txt。
- 13种语言资源key覆盖一致；复用已有Screenshot.ColorHint/ColorCopied，无新增翻译。截图资源占位符索引一致。
- 阵雨SVG在20/24/32/96px及浅/深/蓝色背景离线渲染检查。番茄钟离线预览仅用于布局方案核查，不是WinUI实机截图。
- 两个并行代理因上游协议错误失败，工作由主会话完成。未控制桌面、未启动DeskBox，未发布安装器，未提交或推送。

## 用户手动验收

1. 从E:/DeskBox-debug/DeskBox.exe启动，Fluent天气查看阵雨小图标。
2. 启动截图，在多个重叠窗口、DeskBox格子和桌面空白之间快速移动，预选边框应随指针直接切换；单击确认、拖动框选和双击复制正常。
3. 未确认选区时Ctrl+C，粘贴应为当前色值，截图窗口保持打开；确认选区后Ctrl+C应复制图片。选文字时仍复制文字。
4. 在屏幕四角检查圆角放大镜整卡不出界，无Esc悬浮框；Esc仍能逐级取消。125%/150%缩放下检查十字中心与色值一致。
5. 番茄钟检查浅/深主题、窄/小格子、专注/休息、开始/暂停/重置/跳过；时间与进度保持同步，数字不被裁剪。

自动选区仍按窗口边界工作，不宣称具备微信完整的UI元素级语义选区；实际跟手感和视觉效果留给用户手测确认。
