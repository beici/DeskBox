# R13 整改方案（round-13 双路审查闭环批次）

> 输入：`docs/quality/rounds/round-13/全量代码缺陷审查总报告.md`（DEF-125、DEF-126）。
> 原则：最小侵入、语义零变化（除缺陷方向本身）、不触碰 z-order 红线与 12 语言键集。
> 基线：`wip/fix-bug` @ `7387efeb`，工作树仅含 round-13 文档产出。

## 1. 逐项处置

### W1 ｜ DEF-125 ｜ FolderWatcherService.StartAsync 成功分支缺代际复核（P3，DEF-123 同族收口）

- **现状（主流程核验）**：失败分支（:285-295）已有「probe await + `startGeneration` 守卫」，成功分支（:302-312，审查建议 1 行号更正）在 `bool queryStarted = await TryStartQueryWatcherAsync(folderPath, generation)`（:285）后直接 `SetHealth(Watching/Degraded)` + `_reconnectPath = null; _reconnectAttempt = 0`——query await 期间被新调用取代时，旧调用结算覆盖新调用健康状态并取消其已排程重连。
- **修法**：`queryStarted` await 之后、`if (!nativeStarted && !queryStarted)` 之前补同款守卫：
  ```csharp
  lock (_lock)
  {
      if (_isDisposed || startGeneration != _watchGeneration)
      {
          return;
      }
  }
  ```
  失败分支内部 probe await 后的既有守卫保留（其覆盖 probe 窗口）；两处守卫分别覆盖 query 窗口与 probe 窗口，无冗余。
- **验证**：构建 + 全量回归兜底（FolderWatcher 无行为级用例）+ 静态走查（守卫位点与失败分支对称）。可接受性留档（审查建议 2）：新守卫是全方法首个 WatchedPath（:279）已写入的 return 路径——瞬态旧路径仅暴露于 Health 诊断快照，由取代方 Stop()（:640 置 null）自愈。

### W2 ｜ DEF-126 ｜ SearchPopupWindow 子类钩子无对称卸载（P3）

- **现状（主流程核验）**：`:168-176` 构造期 `SetWindowSubclass` 安装、返回值存入只写字段 `_isPopupCloseWatcherInstalled`；`OnWindowClosed`（:4508-4542）全量清理链缺 `RemoveWindowSubclass`；全仓其余 9 处子类化均有对称卸载（DesktopOrganizationWindow.xaml.cs:230-241 且含 WM_NC_DESTROY 兜底）。
- **修法**：`OnWindowClosed` 顶部（退订组之前）补：
  ```csharp
  if (_isPopupCloseWatcherInstalled)
  {
      _ = Win32Helper.RemoveWindowSubclass(
          _hwnd,
          _popupCloseWatcherProc,
          PopupCloseWatcherSubclassId);
      _isPopupCloseWatcherInstalled = false;
  }
  ```
  （`Win32Helper.RemoveWindowSubclass` 已存在于 Platform/Win32Helper.cs:1634-1636；镜像 DesktopOrganizationWindow.RemoveMinimumSizeHook 三行范式；字段恢复可读。WM_NC_DESTROY 兜底有意省略——OnWindowClosed 内移除已消除所述危害，comctl32 随窗口销毁丢弃子类链，先例中的兜底属可选加固。）
- **验证**：构建 + 全量回归。

## 2. 不进本批（延后维持，与总报告 §5 一致）

ANI-06 补齐位点 ×9、DEF-080 补齐位点 ×4（均并入既有编号挂账）、观察项 8 条（A 面 O-24~O-26 + B 面 5 条）。

## 3. 门禁

1. 停止仓库路径 DeskBox 实例 → Debug x64 构建 0 错误；
2. `python scripts/quality/static_gate.py` PASS（零 async void/同步等待/空 catch 新增）；
3. x64 全量回归全绿（基线 4297/4297）；
4. 启动规范 Debug 实例核验；
5. 台账/TODO 收口（DEF-125/126 → ✅ 已修复；整改批小节按 R12 固化纪律——落地与门禁完成前保持「待实施」占位）。

## 4. 风险与回滚

- 2 源文件，每处 ≤10 行；无签名/序列化/ABI/依赖变更；行为变化仅缺陷方向（旧调用不再覆盖新调用状态、子类对称卸载）。
- 回滚：单 commit revert。
