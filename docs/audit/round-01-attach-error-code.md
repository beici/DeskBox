# 第 1 轮 · 桌面层 owner 挂接失败时日志里的错误码永远是 0

- 严重度：P2（诊断缺陷；行为本身在 `5e0449aa` 后已恢复）
- 区域：桌面层 z 序 / 桌面层 owner 挂接
- 状态：已修复（提交见 SUMMARY.md）

## 人话摘要

格子挂到 Explorer 桌面层时，如果 `SetWindowLongPtr(GWLP_HWNDPARENT)` 没写进去，代码会打一条
`DesktopPinned owner attach failed ... error=N` 帮助排障。但这条日志里的 `N` **永远是 0**：
代码在设置之后先读回 owner（`GetWindowLongPtr`），再取 `GetLastWin32Error()`，而这次读回调用会覆盖错误码。
于是 2026-10-23 04:20 的 252 条失败日志全部是 `error=0`，看不出是权限被拒（5）、参数非法（87）还是句柄失效（1400）。
现在改成"设置完立刻取错误码"，下一次真失败就能直接看出原因。

## 证据

日志（`%LOCALAPPDATA%\DeskBox\DeskBox.log`）：

```
[04:20:13.134] [WidgetLayer] DesktopPinned owner attach failed hwnd=0x391330 defView=0x101AE actual=0x0 error=0
[04:20:13.138] [WidgetLayer] DesktopPinned owner attach failed hwnd=0x32123E defView=0x101AE actual=0x0 error=0
... 共 252 条，全部 error=0，全部 defView=0x101AE actual=0x0
```

代码 `src/DeskBox/Services/WidgetLayerService.cs:1530-1541`：

```csharp
Win32Helper.SetLastError(0);
_ = Win32Helper.SetWindowLongPtr(windowHandle, Win32Helper.GWLP_HWNDPARENT, desktopIconView);   // 1531-1535
IntPtr actualOwner = Win32Helper.GetWindowLongPtr(windowHandle, Win32Helper.GWLP_HWNDPARENT);     // 1537 ← 覆盖 last error
if (actualOwner != desktopIconView)
{
    int error = Marshal.GetLastWin32Error();                                                     // 1540 ← 拿到的是读回的
```

`GetWindowLongPtr` 的 P/Invoke 声明带 `SetLastError = true`（`src/DeskBox/Platform/Win32Helper.cs:511-512`），
所以 1540 拿到的是"读回成功后的错误码"（0），与 1531 的失败原因无关。

另有一处过期标签：`defView=` 打的是 `FindDesktopLayerOwner()` 的返回值，即桌面层 **root**（Progman/WorkerW），
不是 `SHELLDLL_DefView` 子窗口（代码里 1513 的注释和 1595 的成功日志都已改称 "desktop layer owner"，只有这两条失败/成功行还写着 defView）。

## 用户可感知的后果

用户（本仓库是单人维护）报"格子挂不上桌面层"时，唯一的现场证据就是这条日志；`error=0` 让三种完全不同的根因
（UIPI 拒绝 / 参数非法 / 句柄已销毁）在日志里长得一模一样，排障只能靠猜。修复后同类故障可以直接定位。

## 根因

错误码读取时机错误：`GetLastError` 必须紧跟在失败的 Win32 调用之后读取，中间任何一次带 `SetLastError` 的 P/Invoke 都会覆盖它。

## 候选方案与打分

| 方案 | 说明 | 打分 |
| --- | --- | --- |
| A. 不改，日志里写"error may be lost" | 零风险但错误码依然丢失 | 消除根因 1；**否决** |
| B. 设置后立刻取错误码，失败时用该值 | 2 行改动，语义精确 | 消除根因 5；回归面 5（仅日志）；可验证性 4（需构造失败）；**采用** |
| C. 用 `SetWindowLongPtr` 的返回值判失败 | Win32 文档明确：返回值 0 也可能成功（旧 owner 为 0），必须配合 `SetLastError(0)` + `GetLastError` 才可靠；且此处已用"回读比对"作为判定，返回值不能替代 | 消除根因 3（判定方式变了，回归面大）；**否决** |

## 采用的改法

```csharp
Win32Helper.SetLastError(0);
_ = Win32Helper.SetWindowLongPtr(windowHandle, Win32Helper.GWLP_HWNDPARENT, desktopIconView);
int attachError = Marshal.GetLastWin32Error();          // 紧跟设置调用
IntPtr actualOwner = Win32Helper.GetWindowLongPtr(...); // 回读只用于判定
```

同时把 `defView=` 改成 `desktopLayerOwner=`（失败行与成功行各一处），避免维护者按 `SHELLDLL_DefView` 去找错对象。

## 影响面 / 回归风险

- 不改判定逻辑（仍是"回读 != 期望 owner"即失败），只改错误码取值时机与两个日志标签。
- 仓库内没有脚本/测试解析 `defView=`（`grep -r 'defView='` 只命中本文件两处）。

## 验证

- 单测：与基线通过数一致（见 SUMMARY.md）。
- 真机：正常启动时这条日志不应出现（修复后 `5e0449aa` 已不再触发挂接失败）；失败路径的取值正确性由代码阅读与 Win32 语义保证，
  另在文档里记录"如何人为复现"：把 `FindDesktopLayerOwner()` 换成 `GetDesktopWindow()` 之类的非法句柄即可得到 `error=1400`。
