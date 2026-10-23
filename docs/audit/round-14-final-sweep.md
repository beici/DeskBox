# 第 14 轮修复批 — 公历第 12 处 / 缓存键 / WebDAV 解码 / dim 往返 / SMTC 重入 / HH:mm / 空暂存清理

## B-1（P1）：快照清单行标题——公历家族第 12 处（已修）

第 13 轮修复了 `SettingsWindow.Maintenance.cs` 同函数的两处，漏掉 `:426` 的**行标题**（直接进 `ItemsSource`）。ar-SA 下标题显示回历年；更重的是 `CreatedAtUtc` 以不可信文件 mtime 兜底，越界 mtime 使 `{:g}` 在 `.Select(...)` 内抛异常 → 整个清单显示"读取失败"。改走 `DisplayCulture.GregorianCurrent`。

## R13-8/B-4（P3）：DisplayCulture 缓存键区分用户覆盖（已修）

缓存键从 `Name` 改为 `Name + UseUserOverride`：`CultureInfo.CurrentCulture`（带 Windows 用户覆盖）与 `GetCultureInfo(name)`（不带）不再共享同一克隆——先到者的覆盖状态不再静默覆盖另一侧。

## R12-7/A-6（P3）：WebDAV PROPFIND 单次解码 + 解码对解码自跳过（已修）

条目名从"二次 Unescape"改为单次（字面 `%XX` 的文件名不再被错误重解码）；集合自跳过比较从"解码路径 vs 未解码请求路径"改为两侧都解码（含空格的远程目录名不再混入列表）。`ParseMultistatus` 改 internal 供测试。

## A-3（P3）：SMTC 初始化重入窗口（已修）

`InitializeAsync` 在 `RequestAsync` 的 await 之后复查 `_isDisposed || _isInitialized`：同帧两个 fire-and-forget 首调不再双订阅（旧代码窗口内第二个调用者照常订阅，第一个 manager 的订阅泄漏到进程结束）。

## A-5（P3）：背景 dim 百分比往返钉 Invariant（已修）

`WidgetBackgroundCustomization` 的 4 组 `ToString("0.#")`/`TryParse` 全部显式 Invariant：en-US 写 "0.5" 后切换 de-DE 读取不再得到 5（探针级 A/B：`Expected: 0,5 Actual: 5`）。

## A-13（P3）：HH:mm 显示统一 GregorianCurrent（已修）

`TodoReminderService.FormatDueDate`、`TodoClipboardFormatter`、`TodoItemViewModel.FormatDueTime` 的纯时间串改走公历变体文化——同一条通知内不再混用阿拉伯-印度数字与拉丁数字。

## R12-8（部分，P3）：空暂存目录按龄清理（已修）

新增 `PruneStalePreRecoveryParkedDirectories`（启动时与 `TryRecoverStrandedRollback` 同点调用）：清理超过 7 天且**删除前复核不含用户文档**的 `<data>-pre-recovery-*` 暂存目录（它们本就只为"当时无用户文档"的数据目录而创建，且无人再读）。含真实数据的 rollback 目录的保留策略列为产品决策（见总报告呈报项 2）。复用既有 `TryDeleteDirectory`，模块边界破坏性操作清单无增长。

## A-1 证伪记录（销案）

子报告主张 zip `"./"` 根条目会被 containment 检查拒绝。探针证明 `Path.GetFullPath(Path.Combine(root, ".\"))` **保留尾分隔符**（返回 `root\`），原检查对 `"./"`、`"././"`、`"a/./b.txt"` 本来就通过。真正会抛的只有无斜杠的 `"."` 文件条目（病态输入，拒绝正确）。代码改动撤回；保留 `Extract_AcceptsRootDotDirectoryEntries` 钉死测试防未来回归。

## 验证

325/325（14 套件）；A/B 三处红绿（B-2/A-6/A-5，见各节）；构建 0 错误 + DLL 时间戳证明；部署真机 40 步 0 失败。
