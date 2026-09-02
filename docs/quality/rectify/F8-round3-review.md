# R3 Round 3 全量审查报告（linux-hermes）

## 审查背景
- 基线：HEAD=b2b631e（R2 收敛态），本轮新增提交 41d70e7（DEF-039）
- 审查方式：原计划 3 subagent 并行（文件格子拖拽缩略图 / 内存性能子系统 / 修复邻接面+全局卫生），全部因 API 429 阵亡 → **主线接管亲审**，重点面逐一现场核验
- 覆盖声明：本轮覆盖上游 1.4.9 中前两轮未深审的子系统 + 全局卫生扫描 + 12 语言格式占位符（新维度）

## 审查面与结论

| 审查面 | 结论 | 关键证据（本会话现场核验） |
|---|---|---|
| 文件格子/拖拽/缩略图子系统 | GO | IconHelper 6 处 DestroyIcon 全部 finally 配对（:1450/:1485/:1544/:1549/:1585/:1590）；NativeDropTarget AddRef(:543)/Release(:630) 三条路径（DragLeave/Drop/重Enter）闭合；FileOpenRequestGate 有界（HistoryLimit=128 淘汰最旧）、UI 线程封闭、Normalize 词法-only；ReorderDropIndexCalculator 边界数学（Math.Clamp 全覆盖、空列表 0、虚拟化 fallthrough 合理） |
| 内存/性能子系统 | GO | MemoryReclaimer：GC.Collect 双趟 + MinimumCooldownSeconds 门控（GetElapsedTime 判定），无频繁卡 UI 面；与 App BackgroundMemoryCleanup 调度链耦合经 R2 已审；WidgetManager.Memory 重构（Hidden→LongHidden record struct）语义等价，IsWindowVisible 判定替代 .Visible 更准 |
| 修复邻接面回归 | GO | StoreStartupService 缓存 miss 阻塞仅在 prefetch 完成前一次；Onboarding 回调代际护栏（DEF-038）在位；SearchPopup ShowPopupSafelyAsync N2 修复未被 1.4.9 冲掉（:273 现场确认） |
| 全局卫生扫描 | PASS | async void 计数 225（<基线229，无新增）；sync_wait 命中全部为既有基线位（BoundedStaOperationRunner.Wait(0) 非阻塞、DesktopDoubleClickActivationService/ReservedHotkeyHookService 启动期 known）|
| **12 语言格式占位符**（本轮新维度） | **DEF-039 P2** | FileInfo.FileModified es-ES 'aaaa'、fr-FR 'aaaa/M/j'、ru-RU 'гггг/М/д ЧЧ:мм' 非法格式字母 → string.Format 字面量渲染，俄语用户时间戳显示假字；1.3.8 (f515632) 起长期存在；既有契约测试只比索引不比 spec（盲区） |

## DEF-039 修复（41d70e7）
- es-ES/fr-FR → `dd/MM/yyyy HH:mm`（与 pt-BR 既有合法格式同风格）；ru-RU → `dd.MM.yyyy HH:mm`（俄语标准布局）
- 新契约测试：`JsonLocales_DateTimeFormatSpecifiers_OnlyUseValidNetFormatLetters`（全 12 国 {n:spec} 字母白名单校验，引号字面量豁免，z/Z 显式排除因 DateTime 传参会抛 FormatException）
- 本地复刻测试逻辑：全库 violations=0；静态门禁 PASS；12 语言键 2582 一致

## 收敛判定
- **本轮有新 P2（DEF-039）→ 已修复闭环，R3 不收敛，进入 R4**
- R4 计划：确认修复 CI 绿后，聚焦「本地化其余维度（日期/数字 Culture 传递、RTL 镜像）+ 上游 1.4.9 其余未审文件」，若整轮无新 P0/P1/P2 即收敛

## 交付物
- [x] 本报告（rectify/F8-round3-review.md）
- [x] defect-ledger.md 增补 DEF-039
- [ ] TODO 清单核验行 + pending-gate 更新（随 R4 或修复推送批次）
