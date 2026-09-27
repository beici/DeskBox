# R13 整改报告（round-13 闭环批次，2026-09-27）

> 输入：`rounds/round-13/全量代码缺陷审查总报告.md`（DEF-125、DEF-126）。
> 方案：`R13-remediation-plan.md`（独立完善性审查**首审 GO**，3 条建议全部采纳：成功分支行号更正 :302-312、WatchedPath 可接受性留档、WM_NC_DESTROY 兜底省略理由）。
> 门禁总览：Debug x64 构建 **0 错误**（22 警告 ≤ 基线 24）；`static_gate.py` **PASS**（五项与基线持平）；x64 全量回归 **4297/4297**；新实例 **PID 32244** @ 规范 Debug 路径。

## 1. 逐项落地

| 编号 | 落点 | 改动 |
|---|---|---|
| DEF-125 | `Services/FolderWatcherService.cs`（StartAsync） | `queryStarted` await 之后、if/else 之前补同款代际复核（`_isDisposed \|\| startGeneration != _watchGeneration` → return）——闭合 query await 窗口；失败分支内部 probe 守卫保留（覆盖 probe 窗口）；可接受性留档：新守卫是全方法首个 WatchedPath 已写入的 return 路径，瞬态旧路径仅暴露于 Health 诊断快照、由取代方 Stop() 自愈 |
| DEF-126 | `Views/SearchPopupWindow.xaml.cs`（OnWindowClosed 顶部） | `if (_isPopupCloseWatcherInstalled) { _ = Win32Helper.RemoveWindowSubclass(_hwnd, _popupCloseWatcherProc, PopupCloseWatcherSubclassId); _isPopupCloseWatcherInstalled = false; }`——字段恢复可读；WM_NC_DESTROY 兜底有意省略（OnWindowClosed 内移除已消除所述危害，先例中的兜底属可选加固，理由留档于方案） |

净变化：2 源文件（+24/−0）；无签名/序列化/ABI/依赖变更；行为变化仅缺陷方向。

## 2. 门禁证据

1. 停实例（PID 30924）→ 构建 0 错误。
2. x64 全量回归 → **4297/4297 通过、0 失败**（2m04s）。
3. `static_gate.py --json rectify/r13-static-gate.json` → **PASS**（五项与基线持平）。
4. 新实例 PID 32244 @ 规范 Debug 路径。

## 3. 延后维持（与总报告 §5 一致）

ANI-06 补齐位点 ×9、DEF-080 补齐位点 ×4（并入既有编号挂账）、观察项 8 条（A 面 O-24~O-26 + B 面 5 条）。
