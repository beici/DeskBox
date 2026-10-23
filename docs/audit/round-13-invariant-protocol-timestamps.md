# 第 13 轮修复 R13-1 — 协议/文件名时间戳统一钉 InvariantCulture

- 级别：P2（第 13 轮 A-1+A-2 与台账 11.A-4 合并；主流程复核后位点定稿 15 处插值 + 2 处 F2 键）。
- 状态：已修复并验证。

## 问题

发向远端或供机器回读的标识符用 CurrentCulture 渲染年份：ar-SA 等非公历默认格式机器上——
- `GlanceImageService.cs:421`：Wikimedia "精选"分类名拼出回历 1448 → Commons 无此分类 → API 静默返回空，该类目永久无图；
- `CloudBackupService.cs:648`：云快照名内嵌回历年 → `ParseSnapshotTimestamp`（Invariant）解析必然失败 → 排序/时间戳退化、跨设备混排命名破坏保留策略次序；
- 其余 13 处（本地备份名 ×5、拖拽文件名 ×2、附件回退名、更新器 helper 目录名、`.unreadable-`/`.corrupt-` 侧车 ×2、速记录出名、诊断包名、更新器日志时间戳）用户可见年份错误；`PruneQuarantinedCopies` 按文件名序在混排命名组下会选错保留副本；UmAlQura 1900-2077 范围外时钟直接抛异常。
- `CitySearchService` 去重键 `F2` 数值格式随文化（逗号小数点区域键形不同）：当前同次运行一致故良性，一并钉死。

## 改法

全部改 `string.Create(CultureInfo.InvariantCulture, $"…")`（插值保持原样）。`BuildRemoteSnapshotName` 改 internal 供回环测试。

## 验证

- 新增 `RemoteSnapshotName_RoundTripsUnderANonGregorianSystemFormat`（ar-SA 下生成→解析成功且含公历年）。A/B：改回裸插值 → 红（解析返回 null）。
- 271/271 定向套件绿（含 ResilientJsonStoreTests、CloudBackup 两套件、Localization 两套件）；构建 0 错误；部署真机 40 步 0 失败。
