# 第 14 轮（从零）全量代码缺陷审查 · 总报告（循环收官轮）

- 基线：`wip/fix-bug` @ `fa6321b0`（第 13 轮修复提交之后）。
- 方式：延续"从零重审"纪律——两个 SubAgent 禁读 `docs/audit/`，重点攻击第 13 轮 6 个新提交后全量扫描；主流程逐条复核，并用运行时探针定论存疑主张。
- 原始报告：`round-14-scout-a.md`（P2×2 + P3×11）、`round-14-scout-b.md`（**P1×1** + P2×1 + P3×7）。

## 复核结论（主流程裁决）

1. **两报告第 4 次独立确认第 11-13 轮全部修复主体稳健**（Invariant 清扫 17 处语义等价、公历 11 位点定性成立、会话键生命周期各中止路径闭合、DST 接缝逐点等价、Setting 探测契约恢复），各自留痕。
2. **A-1（zip `"./"` 根条目拒绝解压）经探针证伪**：`Path.GetFullPath(Path.Combine(root, ".\"))` **保留尾分隔符**（返回 `root\` 而非 `root`），因此原 containment 检查对 `"./"`、`"././"`、`"a/./b.txt"` 本来就通过——子报告"StartsWith 必为 false"的推演不成立。真正会抛的只有无斜杠的 `"."` **文件**条目（病态输入，拒绝属正确行为）。按第 4/5 轮纪律：代码改动已撤回，保留行为钉死测试（`Extract_AcceptsRootDotDirectoryEntries`，防止未来重写 containment 时回归）。
3. 其余主张全部核实成立（B-1 同函数第 12 处漏网位点、B-2 双向滞留 + `IsInitialized` 通知为零绑定、A-6 双解码 + 跳过比较错位、A-5 dim 往返、A-3 重入窗口、R13-8 缓存键、A-13 数字形状）。
4. B-7（`FileInfo.FileModified` 三语言日期 token 本地化）经程序化复核为**事实成立但需定性**：键/占位符序号一致，仅格式串本地化——技术上无害（GregorianCurrent 求值），列为政策呈报项。

## 风险等级分布（去重合并后）

| 等级 | 数量 | 处置 |
| --- | --- | --- |
| P0 / P1 | 0 / 1 | B-1（第 12 处公历位点，含快照 mtime 越界使整个清单报"读取失败"）— **本轮已修** |
| P2 | 3 | B-2（外部 snooze 双向显示滞留）— **已修**；A-1 — **证伪销案**；A-2（云备份 UpdateOptions/在途上传成功戳竞态）— 台账（修复需 gate 设计） |
| P3 | 新 12 | 已修 7：A-3（SMTC 重入）、A-5（dim 往返）、A-6（WebDAV 解码）、A-13（HH:mm 数字形状）、R13-8（缓存键）、R12-8 部分（空暂存目录清理）；台账 5：A-4（QuickCapture 内容寻址裸写）、A-7（5 分钟超时容量假设）、A-8（_isChecking 竞态/async void 暴露面）、A-9（Glance 孤儿文件）、A-10（marker 删除失败误报）；产品决策 3：B-5（日期文化双轨）、B-6（中继 widgetId）、B-7（token 政策）；B-3（云备份页语言切换陈旧显示）台账 |

## 产品决策呈报项（循环收官清单，供维护者裁决）

1. **snooze 宽限语义（A-4/B-7，第 13 轮提出）**：通知面长期不可用时，"从未显示过的 snooze"在 1 分钟宽限后永久静默且 `SnoozedUntil` 永久滞留 store（小睡时 due 已被提前消费，两条触发器都不可达）。选项：(a) 宽限关闭且从未显示 → 释放 `SnoozedUntil`（恢复 due 通道评估）；(b) 接受现状 + 诊断日志计数。旧行为（无限重试）已被第 12 轮判定为缺陷，不存在无代价选项。
2. **stranded rollback 保留策略（R12-8 核心）**：恢复成功路径在删 rollback 前崩溃留下的**含真实数据**的 `restore-rollback/<guid>` 目录无清理路径、可累积整份数据大小。本轮已修**空** `-pre-recovery-*` 暂存目录的按龄清理（删除前复核无用户文档）；**含数据**目录的清理是数据删除决策（30 天？提示用户？），需产品裁决。
3. **日期显示文化双轨（B-5）**：Glance 跟应用语言文化，其余 26 个位点跟系统格式文化（均已是公历变体）。统一到任一口径都有代价，需产品定性。
4. **`FileInfo.FileModified` token 政策（B-7）**：fr-FR/pt-BR/ru-RU 本地化了日期顺序 token（键与占位符序号仍一致）。若为有意区域化，建议在 AGENTS.md 记录豁免口径。
5. **提醒中继按 widgetId 过滤（B-6）**：`TodoWidgetViewModel` 不持有 widgetId，中继无法过滤；双重 scoped restore 可复制 item id 后跨部件污染。修复需 VM 暴露 widgetId（架构小改）。
6. **云备份 UpdateOptions 竞态（A-2）**：UI 改端点与在途上传的成功戳回填可交叉，新目标可能被盖上从未运行过的 `LastSuccessUtc`。修复需引入云备份 gate 或锁序设计。

## 本轮修复清单（验证详情见 round-14-*.md）

1. **B-1**：`SettingsWindow.Maintenance.cs:426` 快照清单行标题——公历家族第 12 处（同函数内第 13 轮漏掉）。
2. **B-2/R13-9**：`ApplyExternalStoreChange` 的 snooze/完成/更新时间改走 VM setter（`SnoozedUntil`/`CompletedAt`/`UpdatedAt`），打开的 Todo 格子实时刷新"已暂至"徽标；纯簿记字段（ReminderLastNotifiedAt 等）仍写模型。
3. **R13-8/B-4**：`DisplayCulture` 缓存键加入 `UseUserOverride` 区分，CurrentCulture（带 Windows 用户覆盖）与 `GetCultureInfo`（不带）不再共享克隆。
4. **R12-7/A-6**：WebDAV PROPFIND 条目名单次解码 + 自跳过比较改为"解码对解码"（含空格目录与字面 `%XX` 文件名）。
5. **A-3**：`MusicSessionService.InitializeAsync` 在 `RequestAsync` 之后复查 `_isInitialized`，关闭同帧重入的双订阅窗口。
6. **A-5**：背景 dim 百分比持久化往返全部钉 InvariantCulture（en-US 写 → de-DE 读不再把 0.5 读成 5）。
7. **A-13**：提醒/剪贴板/Todo 的 `HH:mm` 系列显示统一走 `GregorianCurrent`（同一条通知不再混用两种数字形状）。
8. **R12-8（部分）**：启动时清理超过 7 天且（删除前复核）不含用户文档的 `-pre-recovery-*` 暂存目录；含数据的 rollback 目录保留待产品决策。

## 验证总览

- 325/325 定向套件绿（14 套件，含 ModuleBoundaryContractTests——清理例程复用既有 `TryDeleteDirectory`，破坏性操作清单无增长）。
- A/B 红绿：B-2（旧合并下 `Expected: False, Actual: True` 陈旧徽标）、A-6（`Assert.Single() Failure: 2 items`——自条目未跳过 + 文件名双解码 `2026-10` vs `2026%2D10` 一次失败呈现两个缺陷面）、A-5（`Expected: 0,5 Actual: 5`）。
- A-1 证伪撤回后全量复跑仍 325/325。
- 构建 0 错误（DLL 时间戳证明）；部署真机重启 40 步 0 失败。
