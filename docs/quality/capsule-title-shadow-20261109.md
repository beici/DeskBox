# 胶囊标题变暗与背景闪亮：排查和修复手册

## 结论

2026-11-09 的原生窗口对照实验确认：同列上方胶囊被 Windows 激活后升到下方胶囊之前，系统阴影覆盖下方标题、图标和背景。应用只记录了逻辑抬升状态，没有为“已收起窗口的原生激活”安排同类窗口顺序恢复；这条路径没有展开租约，也不会触发收起释放。普通空闲整理又跳过逻辑抬升窗口，因此异常持续到下一次展开、收起或其他恢复操作。

同时修复了普通窗口误入 TopMost 层、动画禁用重绘以及无变化时反复刷新边框的问题。它们会造成相似的暗化、旧像素残留或整块背景闪亮，诊断时需要区分。

## 快速判断

| 表现 | 优先检查 | 判定方式 |
| --- | --- | --- |
| 标题、图标、背景一起变暗，靠近上边缘更明显 | 同类 HWND 顺序及系统阴影 | 保持内容不变，只恢复下方胶囊优先的顺序；颜色立即恢复则支持阴影覆盖 |
| 激活已收起胶囊后持续变暗，悬浮一轮才恢复 | 原生 Activated 路径是否安排必需恢复 | 记录激活返回值、实际前台 HWND 和恢复请求，不能只模拟鼠标悬浮 |
| 普通胶囊突然压住置顶应用或材质发生变化 | WS_EX_TOPMOST 与插入锚点 | 对照展开前后原生样式；普通窗口不得使用置顶窗口作为未经处理的插入锚点 |
| 动画之后出现旧像素，下一次重绘才恢复 | SetWindowPos 标志 | 检查是否使用 SWP_NOREDRAW 而没有对应区域失效处理 |
| 整块背景短暂变亮 | owner、SWP_FRAMECHANGED 与背景刷新频率 | 确认无变化的 owner 或同类排序没有触发全组边框重建 |

不要先修改标题 Foreground、图标透明度、材质浓度或全局阴影设置。这些修改不能修复窗口顺序错误，还会改变正常外观。

## 复现证据

在同一桌面上排列多个间距较小的胶囊，取两个相同图标的实心像素做对照：

1. 正确同类顺序下，图标像素为 `(97, 201, 254)`。
2. 仅将上方胶囊 A 放到下方胶囊 B 前面，B 的同一像素降至 `(84, 173, 219)`，约为正常值的 86%。
3. 恢复顺序后，50 ms 采样已经恢复原色，无需修改 XAML 或刷新材质。
4. 从正确顺序调用 `SetForegroundWindow` 激活已经收起的 A，同样复现暗化，500 ms 和 2500 ms 采样仍异常。

颜色数值属于当次环境的对照证据，不是跨主题、DPI 或 GPU 的硬编码验收阈值。截图、正式数据、HWND、进程信息和原始日志只留在本机，不随源码或安装包发布。

## 修复落点和约束

修改窗口层级前先读 `docs/architecture/current_architecture.md` 和 `docs/architecture/[重要勿删]widget_zorder_lifecycle.md`。

| 代码 | 职责 |
| --- | --- |
| `src/DeskBox/Views/ContentWidgetWindow.WindowInteraction.cs` | 原生激活处理及托盘整组重断言后，若窗口处于紧凑收起状态，排队 `compact-window-activated`，要求同类顺序恢复；不在激活回调里直接重排 HWND |
| `src/DeskBox/Services/IdleWidgetZOrderRequest.cs` | 保存 `requirePeerOrderRestore` 和回调代际；普通请求合并、旧回调失效和有限重试不会丢失未完成的恢复义务；成功且代际匹配后才清除 |
| `src/DeskBox/Services/WidgetManager.ZOrder.cs` | 必需恢复涵盖可见且逻辑抬升的同类窗口，允许托盘会话内恢复；动画、展开租约和活跃交互仍阻止重排 |
| `src/DeskBox/Services/WidgetPeerOrderBandPolicy.cs` | 按当前原生顺序分区，其他可见窗口和 TopMost 边界切断区间，只在区间内应用下方优先顺序 |
| `src/DeskBox/Platform/Win32Helper.cs` | `ResolvePeerZOrderAnchor` 统一保护置顶边界：普通窗口遇到置顶锚点时改用 `HWND_TOP` |
| `src/DeskBox/Services/WidgetLayerService.cs` | 桌面层抬升复用安全锚点，跳过无需更改的 owner；保留真实桌面层变化后的必要背景刷新 |
| `src/DeskBox/Views/WidgetWindowBase.Bounds.cs` | 中间动画帧保留 `SWP_NOCOPYBITS`，去除 `SWP_NOREDRAW`，让系统正常重绘暴露区域 |

动画结束、收起提交、托盘切换结束和交互结束都会继续处理待完成的恢复。顺序正确时无需原生写入；恢复复用现有移动规划器，不增加常驻轮询、不激活窗口，也不跨越其他应用。

## 回归步骤

1. 开启系统窗口阴影，将多个胶囊纵向紧密排列，观察相同图标和标题的亮度。
2. 连续展开、收起上方胶囊，快速扫过多个胶囊以覆盖动画交错。
3. 使用托盘或全局唤起快捷键显示整组，再重复操作。
4. **重启后直接激活已经收起的上方胶囊，不先展开。** 检查激活是否真正成功，以及动画静止后下方胶囊是否变暗。
5. 展开期间切到其他应用再返回，确认第三方窗口顺序、置顶状态和输入焦点保持正确。
6. 分别覆盖 Dynamic、DesktopPinned、QuickReveal，浅/深主题、Acrylic/Mica、不同 DPI，以及 Windows 10/11。

期望：交互结束后顺序稳定，文字、图标和背景恢复正常；无需下一次悬浮来修复。普通悬浮不残留 TopMost，展开窗口仍能覆盖同类窗口，正在交互时不被强制沉底。

自动测试（先按仓库流程完成 x64/win-x64 restore）：

```powershell
dotnet test tests/DeskBox.Tests/DeskBox.Tests.csproj --no-restore `
  -p:Platform=x64 -p:RuntimeIdentifier=win-x64 `
  --filter "FullyQualifiedName~CapsuleShadowPeerOrderTests|FullyQualifiedName~WidgetPeerZOrderAnchorTests|FullyQualifiedName~WidgetCohesionExitZOrderContractTests|FullyQualifiedName~WidgetZOrderRestoreContractTests|FullyQualifiedName~Windows10WidgetMotionContractTests"
```

测试覆盖请求合并、代际、交互打断、有限重试、第三方窗口分区、置顶锚点、缺失/重复扫描，以及顺序正确时零移动。`WidgetPeerZOrderAnchorTests` 使用真正的隐藏 HWND 验证系统行为。

## 已验证范围与局限

最终原生激活修复的定向套件为 **230/230 通过**。真实应用中 10 次成功的原生激活对应 41/41 帧可见采样正常，原生同类顺序稳定且没有 TopMost 残留；同时检查了 API 返回值和实际前台 HWND，排除了“操作没有执行但截图正常”的误判。

视觉验证覆盖本机 Windows 11、125% DPI、Dynamic 模式及 Acrylic。重启后的连续激活测试曾被系统前台限制和外部遮挡打断，不能记为完整通过；仅有一次重启后的有效激活完成全部采样。其他系统、层级模式和显示配置仍需按上面的矩阵实机复查。

早期完整测试曾有 16 项失败及 1 项跳过，不能用定向测试代替完整测试结论。本次公开发布的完整验证结果单独记录在 `docs/releases/v1.5.9.md`，避免将历史结果误认为发布验收结果。

性能取舍：恢复系统重绘会增加必要的绘制工作，但保留帧合并和禁止旧像素复制，并移除无变化 owner 与排序产生的全组边框刷新。未引入依赖或数据迁移，主要回归范围是窗口层级、托盘唤起、展开动画和不同系统的桌面材质。
