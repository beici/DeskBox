# 第 12 轮修复 R12-3 — 更新器"安装包缺失"路径不等父进程退出就重启

- 级别：P2（来源：第 12 轮维度 A-3）。
- 状态：已修复（代码级验证）。

## 问题

`DeskBox.Updater/Program.cs` 的"安装包不存在"分支（下载完成到 updater 启动之间安装包被杀软隔离等）直接 `RestartAfterIncompleteUpdate` 重启应用，**不先 `WaitForParentExit`**——旧实例还握着单实例互斥体（在跑实例持锁使新实例静默退出），结局可能是"整机没有 DeskBox 在跑"（DEF-113 要防的正是这个）。对照同函数"App 可执行文件不存在"分支**有**等待——不对称坐实是遗漏。

## 改法

`WaitForParentExit(options.ParentProcessId)` 提升到安装包存在性检查之前（失败路径与正常路径都先等父进程退出再重启/安装）。

## 验证

- 代码级：结构对齐"App 不存在"分支；`WaitForParentExit` 对已退出的 pid 快速返回（GetProcessById 抛 ArgumentException 被吞），无新增延迟面。
- Updater 无独立测试面（Main 入口），行为由 DEF-113 语义与分支对照论证；构建通过。
