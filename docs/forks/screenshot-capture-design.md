# 截图增强移植设计评审(补充 4)

状态:**评审定稿(2026-10-25)**。三份勘察(bbkien 规格/本仓库接线面/WIC-AOT 预研)结论已并入;实施按 §10 序列推进。
来源:bbkien2312/DeskBox-mrKBB 截图增强三连(区域捕获 49d306e9、无遮挡选中窗口捕获 ad4480c5、Ctrl+C 复制 417c060a;最终态为 7 个提交累积,scratch 库 `E:\DeskBox-scratch.git` refs `bbkien/main` = eb1efe9e,源码提取件 `.testtmp/bbkien-src/`)。

## 0. 设计评审结论(立项前置条件的回答)

立项前置(fork-network-analysis.md :92/:126/:368):System.Drawing → WIC/SoftwareBitmap 的 AOT 替换设计评审先行;若新增 NuGet(Vortice)走双 lock + WMC1510 审计。

**结论:**
1. **零新增 NuGet**:不引入 Vortice.Direct3D11(以及任何图像包)。双 lock 文件不动,无 WMC1510 审计义务增量(XAML 变更仍按仓库惯例跑一次 publish-aot-audit 防计数漂移)。
2. **丢弃 WGC(Windows.Graphics.Capture)路径**:`GraphicsCaptureSession.IsBorderRequired` 为 10.0.20348/contract v12(Win11)引入,Win10 21H2(19044)不可用 → Win10 地板上 WGC 抓窗口必现系统黄边+横幅且不可关;且 WGC 需 IDirect3DDevice interop(bbkien 为此引 Vortice)。对"自截无遮挡窗口"产品目标是硬伤。无遮挡捕获改走 **GDI PrintWindow(PW_RENDERFULLCONTENT=2)**(Snipaste 同款,可截被遮挡/背景窗口),失败回退冻结帧按窗框裁剪。
3. **像素管线 = GDI 采集 + WIC 编码**:GDI(BitBlt/CreateDIBSection/PrintWindow)产 BGRA 缓冲 → 行拷贝裁剪 → `BitmapEncoder`(PngEncoderId)编码,模式照仓库 AOT 生产已验证的 QuickCaptureService.cs:2120-2152。全链 WinRT 投影 + LibraryImport P/Invoke,AOT 安全;不新增任何 System.Drawing 使用(存量 IconHelper 挂账不动——零售 AOT 偶然可用非承诺可用,改它回归面大收益低)。
4. 剪贴板写线直接复用 QuickCaptureWidgetViewModel.Operations.cs:365-399 生产链(SetBitmap+Flush+CLIPBRD_E_CANT_OPEN 重试+DeskBoxClipboardWriteScope.MarkWrite 防 QuickCapture 回捕)。

## 1. 目标与非目标

目标:
- 热键(默认 Ctrl+Alt+S)唤起全屏冻结覆盖层,支持四种互斥选择:悬停选窗(单击锁定)、拖拽自由矩形(≥4px)、Desktop(工作区,不含任务栏)、Fullscreen(含任务栏)。
- 锁定后 Copy(按钮/Ctrl+C)写图像剪贴板(PNG 流,OS 粘贴时合成 PNG+CF_DIB 双格式);Save… 走 FileSavePicker 存 PNG(`DeskBox-yyyyMMdd-HHmmss`);Esc 取消。
- 选中窗口目标走无遮挡捕获(PrintWindow 窗口表面直取,重叠窗口不入画);区域/Desktop/Fullscreen 从冻结快照裁剪。

非目标(明确不做):
- WGC 持续捕获/游戏画面(黄边+依赖成本,未来单独立项)。
- 窗口列表选择器(bbkien a73c2174 已简化掉)。
- ThumbnailDiskCache/图标内存项(另一待办,不在本立项)。
- 进程外截图桥接;Rust 侧任何变更(ABI 2 冻结)。
- 多监视器并发快照(单监视器单冻结帧)。

## 2. 用户流程(照 bbkien 规格)

热键 → ReleaseAllModifiers → UI 线程单例开窗(已开则忽略)→ 构造:光标所在监视器(GetCursorPos→TryGetMonitorWorkArea,失败回退 1920×1080)→ 无边框 OverlappedPresenter + IsShownInSwitchers=false → MoveAndResize 覆盖整监视器 → **显示前先抓冻结快照**(防覆盖层自摄)→ 显示冻结图 + `#16000000` 暗化层 + 右上工具条 → SetWindowPos(HWND_TOPMOST)。
选择:悬停 EnumWindows 高亮/单击锁定;拖拽 ≥4px 成区域(实时尺寸提示);按钮/D=Desktop、按钮/F=Fullscreen。锁定后 Copy/Save…/重选。Copy → DataPackage.SetBitmap+Flush → 删临时 → 关窗;Save → FileSavePicker → File.Copy → 删临时 → 关窗(取消保存保持窗口);Esc → 关窗删临时。缓存目录 `{DeskBoxData}/cache/screenshots`,导出时清理 2 天前 PNG。

## 3. 架构映射(bbkien 件 → 本仓库落点)

| bbkien 件 | 本仓库落点 | 说明 |
|---|---|---|
| ScreenshotHotkeyService(154 行,RegisterHotKey id 0x4450+SetWindowSubclass) | 新 Services/ScreenshotHotkeyService.cs | 照 GlobalHotkeyService/SearchHotkeyService 范式;id 0x4450 与现有 0x4442/0x4444 无冲突;App 四点接线(字段/启动步 RunOptionalStartupStep/Attach(trayWindowHandle)/关机释放);RegisterHotKey/SetWindowSubclass/ReleaseAllModifiers/UiDispatcherQueue 全有现成 P/Invoke 与惯例;注册失败仅 log;RefreshRegistration() public 留口 |
| ScreenshotCaptureWindow(.xaml/.xaml.cs 454 行) | 新 Views/ScreenshotCaptureWindow.xaml/.cs | 交互骨架平移(拖拽/悬停高亮 EnumWindows 过滤 Progman\|WorkerW\|Shell_TrayWnd\|DV2ControlHost、D/F、TryGetCaptureBounds、FileSavePicker+InitializeWithWindow、CleanupOldScreenshots);窗口工程照 DisplayIdentifyOverlayWindow(AppWindow 无边框化/WS_EX_TOOLWINDOW\|NOACTIVATE\|LAYERED\|TOPMOST/MoveAndResize 物理像素/拓扑变化关闭);**透明=黑色坑:背景用冻结图填充**;图像线重写(§4) |
| SelectedWindowScreenshotService(268 行) | **并归新 Services/ScreenCaptureService.cs** | WGC 路径(Vortice+combase interop+FramePool)整段丢弃;PrintWindow 路径保留并成为唯一窗口捕获主路径,Task.Run 包装保留(PrintWindow 同步阻塞,无响应窗口会卡调用方);服务三能力:①监视器冻结帧 ②无遮挡窗口 ③缓冲裁剪+PNG 编码 |
| FindWindowAtPoint | Win32Helper 扩展 | EnumWindows 过滤:自身/不可见/自身进程/零尺寸/不含点/类名;bounds 优先 `TryGetExtendedFrameBounds`(DWMWA_EXTENDED_FRAME_BOUNDS=9,照搬 bbkien Win32Helper.cs:1197,32 行,out RECT 新 DllImport 重载——现有 :2070 为 out int 签名),失败 GetWindowRect(GetWindowRect 对带阴影窗口含 7px 阴影边,裁剪用 frame bounds 更准);z 序最顶命中;叠加 IsWindowCloaked(:2123,虚拟桌面/挂起 UWP)与 IsIconic 过滤 |
| System.Drawing 全部用法 | §4 GDI+WIC 管线 | 见下节 |
| AppSettings 三平铺字段(Enabled/Modifiers/Key) | 不进 AppSettings(冻结线 v15) | 照 PomodoroSettingsStore 先例建 ScreenshotSettingsStore(data/screenshot/settings.json) |
| 硬编码越南语 UI 文本 | Strings Screenshot.* 键 ×13 语言 | 键清单 §7 |
| (bbkien 无设置 UI,改热键只能改 JSON) | 增设最小设置节(启用开关+热键录制) | 仓库约定:热键服务配设置面(Global/Search 均有);HotkeyRecorderDialog Scope 枚举 {Global,Search} 扩 Screenshot,查所有 switch 消费点 |

覆盖层键盘:WS_EX_NOACTIVATE 无焦点,Esc/D/F/Ctrl+C 走消息层——捕获窗存活期内联注册热键(id 0x4451 起,Esc=0x4451/D=0x4452/F=0x4453/Ctrl+C=0x4454),SetWindowSubclass 转 UI 线程,关窗注销。

剪贴板纪律(强制):写入前 `DeskBoxClipboardWriteScope.MarkWrite(hasImage: true)`,再 SetBitmap(RandomAccessStreamReference.CreateFromFile(临时 PNG))+SetContent+Flush,外层 CLIPBRD_E_CANT_OPEN 重试——逐字照 Operations.cs:365-399。AOT 约束:AotStage5B4C1C2AContractTests.cs:238-251 钉死 AotNativeDropSmoke 四文件不得含 Clipboard.SetContent/GetContent——新代码不碰那四个文件即无冲突。

## 4. 图像管线(GDI 采集 + WIC 编码;替换全部 System.Drawing 用法)

统一像素出口 `(byte[] bgra, width, height, stride)`,BGRA 字节序 GDI=WIC 一致,无 swizzle。

1. **监视器冻结帧**(替换 CopyFromScreen):`GetDC(NULL)`(或监视器 DC)→ CreateCompatibleDC → `CreateDIBSection`(32bpp,biHeight 负=顶朝下 BGRA)→ `BitBlt(SRCCOPY)`(选区冻结帧不加 CAPTUREBLT——分层窗口tooltip 不需要且更快)→ 缓冲常驻内存(4K ~33MB 瞬时,单帧可接受),冻结 PNG 文件仅作覆盖层显示源(或直接 Image 控件喂解码后的 SoftwareBitmap,免落盘——实施时取简者)。
2. **无遮挡窗口**:`CreateDIBSection(w,h)` → `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=2)`;前置 IsIconic/IsWindowCloaked 过滤;覆盖矩阵(Chrome/Electron/WinUI/UWP)需冒烟验证;失败回退=冻结帧按 frame bounds 裁剪(可能含遮挡像素,优雅降级)。
3. **裁剪**(替换 Bitmap.Clone):DIB 缓冲逐行 `Array.Copy`(stride 数学,矩形先 Math.Clamp 防越界)→ 目标缓冲;免 decoder 往返。
4. **编码**(替换 Save+ImageFormat.Png):`BitmapEncoder.CreateAsync(PngEncoderId, stream)` + `SetPixelData(Bgra8, Straight, w, h, 96, 96, pixels)` + FlushAsync——照 QuickCaptureService.cs:2120-2152 模式(仓库 AOT 生产已验证)。
5. **alpha 规整**:GDI BitBlt/PrintWindow 产出 alpha 常为 0;全零 alpha 时逐像素强制 0xFF(照 native/deskbox-thumbnail-proxy/src/main.rs:2380 仓库既有先例),编码用 Straight。
6. **剪贴板**:PNG 临时文件 + SetBitmap(CreateFromFile)——OS 粘贴合成 PNG+CF_DIB 双格式(读线 IQuickCaptureClipboardReader.cs:42-105 即物证),PNG 保 alpha,DIB 兼容老应用。
7. 结构体:坐标运算用手写 `readonly record struct`(int 物理像素);WinUI 拖拽事件给 double 点,显式取整换算。

## 5. 设置存储

ScreenshotSettingsStore(照 PomodoroSettingsStore:36,268 行先例):
- 模型 `ScreenshotSettings{bool HotkeyEnabled=true; string HotkeyGesture="Ctrl+Alt+S"}`(归一化复用 GlobalHotkeyGesture 解析族;存归一化字符串,服务端 TryParse)。
- data/screenshot/settings.json;锁内 Load/Update/_writeChain/WaitForPendingPersist;ResilientJsonStore+backup;静态 Changed 事件。
- JsonSerializationBaseline 契约:新增 store 文件计数(Serialize×1+Deserialize×2=3)→ 43→44 文件、102→105 调用、39→40 owners,同步 9 文件锁步链(基线+AotStage5B4B1/B2A/B2B1/B2B2A/B2B2B1/B2B2B2/C3B2B1/C3B2B2A 字面量;C3B2B1 :165 还钉基线方法名 FortyThree→FortyFour 字符串)。

## 6. 热键接线

- App.xaml.cs 四点(字段/启动步/Attach 托盘 hwnd/关机释放),照 SearchHotkeyService :6863-6878 同构。
- 手势冲突检查复用 GlobalHotkeyService.NormalizeGesture/IsValidGesture 族;录制 UI 复用 HotkeyRecorderDialog(Scope 扩枚举)。

## 7. 本地化键清单(~17 键 ×13 语言)

Screenshot.Title、Screenshot.Instruction、Screenshot.SizeHint({0}×{1})、Screenshot.SelectWindow、Screenshot.Region、Screenshot.Desktop、Screenshot.Fullscreen、Screenshot.Copy、Screenshot.Save、Screenshot.ReSelect、Screenshot.Cancel、Screenshot.Saved({0})、Screenshot.SaveFailed、Screenshot.CaptureFailed、Screenshot.WindowNotFound、Settings.Screenshot.Title、Settings.Screenshot.EnableToggle(.Description)、Settings.Screenshot.Hotkey(.Description)。
放置:照 b84a2bcb 先例(Screenshot.* runtime 块+Settings.Screenshot.* 设置块,13 文件含 ko-KR 自译;占位符全语言一致;键插中文件带逗号,勿插尾部——1df8bff3 教训)。

## 8. 契约与测试面

- 非新 WidgetKind:FeatureWidgetKind/WidgetContentFactory/SettingsSliceOwnership 不触;新增设置节带 ToggleSwitch×1/SettingsCard×N → AotStage4D1B 计数重钉+ToggleSwitchAccessibleName 白名单(新节文件+AutomationNameKey 契约:<ToggleSwitch 后第一个属性)。
- JsonSerializationBaseline 43→44 九文件锁步链(§5)。
- ModuleBoundary:新 P/Invoke(GetDC/BitBlt/CreateCompatibleDC/CreateDIBSection/GetDIBits/PrintWindow/TryGetExtendedFrameBounds)全部进 Platform/Win32Helper.cs(LibraryImport;现有 CreateCompatibleDC/DeleteDC 在 SessionDisplayFont 私有,提公共或并存),服务层零 P/Invoke。
- SettingsDeferredSections/SearchCatalog:新设置节三重门(SectionRoutes/SubSectionTagOrder/SectionTemplate)同落+重生成 scripts/update-settings-search-catalog.ps1 产物。
- 新测试:ScreenshotSettingsStoreTests(store 范式例,含损坏回退+双存 .bak 语义)、裁剪数学纯逻辑例(Clamp/行拷贝/stride)、热键手势归一化例;PrintWindow/覆盖窗/WGC-free 冒烟=dev-restart 手动面。
- AOT 审计:实施毕跑 scripts/publish-aot-audit.ps1(运行期间禁改仓库任何文件——v1.17 教训)。

## 9. Win10 21H2 / ARM64 兼容性

- PrintWindow XP 起;PW_RENDERFULLCONTENT 未在文档 flag 表但 Win8.1+ 广泛使用(Snipaste 同款),覆盖矩阵冒烟验证。DWMWA_EXTENDED_FRAME_BOUNDS Vista DWM 起。
- GDI/DWM/WIC 全架构无关用户态 API,x64/ARM64 一致;ARM64 上 x64 模拟窗口(Chrome/Electron)的 WM_PRINT 行为列入冒烟矩阵。
- 混合 DPI:物理像素直算(PerMonitorV2 下 BitBlt/GetWindowRect 均物理像素,无 System.Drawing DPI 缩放坑);覆盖层视觉↔物理换算照 bbkien ActualWidth 比例近似(1-2px 已知限制)。

## 10. 实施提交序列(提案)

1. docs:设计评审定稿(本文件)+〇节登记(立项表补充 4 状态+修订记录)。
2. feat:ScreenCaptureService 地基+ScreenshotSettingsStore+Win32Helper 增补+store 测试+锁步链 43→44。
3. feat:截图覆盖窗与四种选择+冻结快照管线+本地化 ×13。
4. feat:无遮挡窗口捕获(PrintWindow+回退)+Copy/Save 链+剪贴板接线。
5. feat:热键服务+App 接线+设置节+录制 scope+search catalog 重生成+AotStage4D1B/ToggleSwitch 重钉。
6. docs:完成状态登记(〇节+修订记录)+current_architecture.md 服务清单刷新。

每步:构建(真实门 src/DeskBox/bin/x64/Debug/net10.0-windows10.0.22621.0/DeskBox.dll 写时间)+全量测试(LocalLow 配方;当前机器唯一失败=AV 拦 StartupRegistryAccessTests 已报用户)+终项 AOT 审计+dev-restart 冒烟。

## 11. 风险清单

1. PrintWindow(PW_RENDERFULLCONTENT)对 Chrome/Electron/WinUI/UWP/最小化/cloaked 的覆盖矩阵——冒烟验证,失败回退冻结帧裁剪(降级不阻断)。
2. 覆盖窗透明=黑色(DisplayIdentifyOverlayWindow:17-20)——冻结图填充。
3. NOACTIVATE 无焦点——内联热键消息层(id 0x4451-0x4454)。
4. 混合 DPI 近似换算 1-2px 偏差(bbkien 同款,记为已知限制)。
5. CAPTUREBLT 不加→分层窗口(tooltip)不入冻结帧(设计选择,非缺陷)。
6. 4K 冻结帧 ~33MB 瞬时内存(单帧,可接受)。
7. GDI P/Invoke 仓库首用面(BitBlt/CreateDIBSection 等)——成熟 API,集中 Win32Helper+冒烟。
