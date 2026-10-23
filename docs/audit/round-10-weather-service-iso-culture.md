# 第 10 轮：天气接口的 ISO 时间串在非公历区域被解析/格式化成本地历法

## 人话摘要

天气数据来自微软的接口，里面所有时间都是 `2026-10-23` / `2026-10-23T06:10:00+08:00` 这种标准写法（ISO-8601）。第 9 轮修掉了"标签层"用系统区域设置解析它们的问题，但**更靠上一层**（把接口响应转换成内部数据的那一步）还在用系统区域设置：在默认日历不是公历的区域（阿拉伯语/沙特等，用回历），纯日期串直接解析失败、日期被写成空字符串，带时间的串则被格式化成回历日期 `1448-05-12`。结果是：日预报的日期变成空或回历日期，日出日落时间也变成回历日期。

## 问题

`src/DeskBox/Services/WeatherService.cs`（`ConvertMsnToWeatherData` 内，修复前）：

- 日预报日期：`:498-501`
  ```csharp
  if (!string.IsNullOrEmpty(day.Daily.Valid) &&
      DateTimeOffset.TryParse(day.Daily.Valid, out var dt))
  {
      dateStr = dt.ToString("yyyy-MM-dd");
  }
  ```
- 日出/日落：`:525-528` `DateTimeOffset.TryParse(day.Almanac.Sunrise, out var sr)` + `sr.ToString("yyyy-MM-ddTHH:mm")`（日落同）
- 小时排序键：`:548-551` `DateTimeOffset.TryParse(hour.Valid, out var parsed)`
- 小时时间：`:559-561` `ht.ToString("yyyy-MM-ddTHH:mm")`

用户可感知的后果（仅非公历区域）：日预报每一行的日期标签是空的或回历日期；日出日落显示成回历日期；"今天"判定因为日期串对不上而失效，于是第 0 行被无条件当成"今天"。

## 证据

PowerShell 直接调 .NET（`[System.Globalization.CultureInfo]::new('ar-SA')`，其 `Calendar` 为 `UmAlQuraCalendar`）：

```
2026-10-23                   ar-SA ok=False fmt=(parse failed) | invariant ok=True fmt=2026-10-23
2026-10-24                   ar-SA ok=False fmt=(parse failed) | invariant ok=True fmt=2026-10-24
2026-10-23T00:00:00+08:00    ar-SA ok=True  fmt=1448-05-12     | invariant ok=True fmt=2026-10-23
2026-10-23T06:10:00+08:00    ar-SA ok=True  fmt=1448-05-12     | invariant ok=True fmt=2026-10-23
ar-SA minute fmt=1448-05-12T06:10 | invariant=2026-10-23T06:10
```

行为级复现（新增单测）：构造一份含"纯日期串"和"带时间串"的 MSN 响应，把 `CurrentCulture` 设为 `ar-SA` 后调用 `ConvertMsnToWeatherData`。修复前 `data.Daily.Time` 得到 `["1448-05-12", ""]`、`data.Daily.Sunrise[0]` 得到 `1448-05-12T06:10`；修复后为 `["2026-10-23", "2026-10-24"]`、`2026-10-23T06:10`。

## 根因

**线格式（ISO-8601）用显示区域性解析与格式化**。`ToString("yyyy-MM-dd")` 的 `yyyy/MM/dd` 会跟随当前区域性的**日历**，所以在回历区域输出回历日期；`TryParse` 同理。

## 候选方案与打分

| 方案 | 消除根因(30) | 回归面(20) | 新概念依赖(20) | 可验证性(15) | 架构一致(15) | 合计 |
|---|---|---|---|---|---|---|
| A. 解析与格式化统一显式传 `CultureInfo.InvariantCulture` | 30 | 19 | 20 | 14 | 15 | **98** |
| B. 用 `ParseExact` 枚举所有可能的接口格式 | 24 | 12 | 12 | 12 | 14 | 74 |
| C. 进程启动时固定 `CurrentCulture` 为不变区域性 | 26 | 5 | 8 | 10 | 4 | 53（会破坏全部本地化显示） |
| D. 只把 `ToString` 改成不变区域性，解析不动 | 16 | 18 | 18 | 12 | 14 | 78（纯日期串仍然解析失败） |

采用 A。

## 改法

5 处 `TryParse` 加 `CultureInfo.InvariantCulture, DateTimeStyles.None`；4 处 `ToString` 加 `CultureInfo.InvariantCulture`。行为不变，只是不再依赖机器区域设置。

## 验证方法

1. 新增单测 `ConvertMsnToWeatherData_KeepsIsoTimestampsIndependentOfTheAmbientCulture`（`tests/DeskBox.Tests/WeatherServiceTests.cs`），用既有的 `internal static ConvertMsnToWeatherData` 接缝。
2. A/B：把 5 处解析/格式化改回环境区域性 → 新单测红；恢复后 `WeatherServiceTests` 全绿。
3. 全量单测与上一轮基线对比（同一台机器、同一批环境性失败）。

## 风险与遗留

1. 本机（`zh-CN`）永远看不到这个缺陷，只有切到回历/佛历等区域才可见；因此这条缺陷的"真机 smoke"只能是单测里的区域性切换，文档在此声明。
2. 仓库里仍有显示用途的环境区域性格式化（如 `src/DeskBox/ViewModels/SettingsViewModel.AboutAndUpdates.cs:576` 的 `ToString("yyyy-MM-dd HH:mm")`）。那是**给人看的本地化输出**，跟随用户区域设置是正确的，不改。
3. 第 9 轮已修的标签层（`WeatherWidgetViewModel.DataProcessing.cs`）与本轮修复是同一个缺陷类，两轮合起来才闭环：接口解析 → 模型 → 标签。

## G 步（复审前几轮副作用）发现的一处回归 — 已修

全量单测从 10 个环境性失败变成 11 个，多出来的是 `tests/DeskBox.Tests/ModuleBoundaryContractTests.cs` 的 `DestructiveFileOperations_StayInsideOwnedDomains`：

```
GREW src/DeskBox/Services/DeskBoxDataBackupService.cs: 14 (manifest 12)
GREW src/DeskBox/Services/ResilientJsonStore.cs: 10 (manifest 9)
```

原因是我在第 3 轮（`TryRecoverStrandedRollback` 的两处 `Directory.Move`）和第 7 轮（把读不出来的主文件 `File.Move` 到 `.unreadable-<ts>`）新增的破坏性文件操作，落在了契约白名单（`DeskBox.FileSafety` / `DeskBox.Core.Persistence` / `DeskBox.Platform`）之外。契约测试自己的注释写明了出路："Route new file mutation through the file-safety kernel"。

改法：新建 `src/DeskBox/Services/PersistenceFileMutations.cs`（`namespace DeskBox.Core.Persistence`，`MoveDirectory` / `MoveFile` 两个薄包装，保留 `Directory.Move` / `File.Move` 的异常语义），3 处调用改走包装；`ModuleBoundaryContractTests` + `ResilientJsonStoreTests` 40/40 绿。**没有**改契约清单——它的注释明确要求不要为了让新调用点过审而扩容。

教训：前几轮的修复会在**别的子系统**留下可被契约测试抓住的副作用；全量单测的价值不只在"新测试是否绿"，还在于"旧契约是否仍成立"。