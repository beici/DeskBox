# 第 8 轮：天气把数组下标当日期（陈旧缓存被贴成"今天"）

## 人话摘要

天气格子把"预报列表的第一条"当成今天、"第二条"当成明天——这是按下标猜的，不是按日期认的。只要格子显示的是缓存（离线、或者刚启动还没刷新成功），而缓存已经是几天前的，用户就会看到"今天 20°"这种把三天前天气当今天的显示；"今日 UV/降水概率/日出日落"同样取的是别人的那一天。逐小时条也用本机时钟去对"现在几点"，跨时区时整条从错误的钟点开始，并把一个已经过去的小时标成"现在"。

## 问题

`src/DeskBox/ViewModels/WeatherWidgetViewModel.DataProcessing.cs`（修复前）：

1. `PopulateDailyForecast` 里 `i == 0 → "Weather.Today"`、`i == 1 → "Weather.Tomorrow"`，其余才按日期格式化（`ParseDateToDayLabel`，返回星期缩写）。
2. `ApplyWeatherData` 的"今日"指标全部取 `[0]`：`UvIndexMax[0]`、`PrecipitationProbabilityMax[0]`、`Sunrise[0]`、`Sunset[0]`。
3. `PopulateHourlyForecast` 用 `FindCurrentHourIndex(hourly.Time)` 拿**本机**时钟 `DateTimeOffset.Now.ToString("yyyy-MM-ddTHH")` 去匹配，未命中则 `startIndex = 0`，并令 `IsCurrentHour = i == 0`。

用户可感知的后果：

- 缓存是 10-20 的响应、今天是 10-23 → 日预报第一条显示"今天"，实际是三天前；UV/降水/日出日落也都是 10-20 的。
- 位置在另一个时区（例如查国外城市）→ 即使响应是新鲜的，逐小时条也从错误的钟点开始，"现在"标在错误的那一格。
- 缓存已过期时，逐小时条把最后一个历史小时标成"现在"。

## 证据

- 代码：上述三处（`DataProcessing.cs:445`、`:304`、`:498/:527` 修复后行号）。
- 行为级复现（单测，走真实生产路径 `InitializeAsync → LoadCachedWeatherAsync → ApplyWeatherData`，缓存 FetchedAtUtc = 3 天前）：
  - 修复前：`CachedForecastFromEarlierDays_KeepsItsOwnDatesInsteadOfBecomingToday` 失败，`Assert.NotEqual` 报 `Expected: Not "Today" Actual: "Today"`；`CachedForecastFromEarlierDays_MarksNoHourAsCurrent` 失败，`Assert.All` 报 5 条里 1 条 `IsCurrentHour == true`。
  - 修复后：8/8 通过（`E:\DeskBox-dev\r8-postfix2.log`，EXITCODE=0）。

## 根因

**把"数组位置"当作"日历事实"**：日/小时条目本身带日期字符串，代码却用下标推断"今天/现在"；"现在"又用本机时钟而不是响应所属位置的时钟。

## 候选方案与打分

| 方案 | 消除根因(30) | 回归面(20) | 新概念依赖(20) | 可验证性(15) | 架构一致(15) | 合计 |
|---|---|---|---|---|---|---|
| A. 按条目自带日期判定今天/明天；"现在"用位置时区解析；今日指标按日期定位 | 30 | 17 | 18 | 14 | 15 | **94** |
| B. 缓存过期时不渲染数据、显示"数据已过期" | 18 | 14 | 8 | 10 | 12 | 62（需新字符串 + 产品决策） |
| C. 缓存过期直接丢弃，等刷新成功再显示 | 12 | 16 | 16 | 12 | 8 | 64（离线时整块空白，退步） |
| D. 抓取时把"哪天是今天"写进缓存结构 | 20 | 12 | 12 | 12 | 10 | 66（改缓存 schema，旧缓存要迁移） |

采用 A。

## 改法

- 新增 `internal static DayLabelKind ClassifyDayLabel(string dateStr, DateOnly today)`（Today/Tomorrow/Date）+ `TryParseIsoDate`（`DateOnly.TryParseExact("yyyy-MM-dd", InvariantCulture)`）。
- 新增 `internal static int ResolveTodayIndex(WeatherDaily daily, DateOnly today)`：找"日期==今天"的条目；找不到（缓存窗口里没有今天）回落 0。
- 新增 `private DateTimeOffset ResolveNowInLocation()`：用 `_weatherData.Timezone`（IANA id）经 `TimeZoneInfo.TryFindSystemTimeZoneById` 换算；失败记日志并回落本机时钟。
- `PopulateDailyForecast` 用 `ClassifyDayLabel` 决定"今天/明天/星期几"；`ApplyWeatherData` 的四个"今日"指标改用 `todayIndex`。
- 逐小时：`FindCurrentHourIndex(times, now)`（命中返回下标，未命中 -1）、`FindFirstHourAtOrAfter(times, now)`（未命中时从"当前小时或下一个小时"开始）、`IsCurrentHour = idx == currentIndex`。

## 验证方法

1. 新增 `tests/DeskBox.Tests/WeatherTimeLabelTests.cs`（8 个用例：4 个纯函数、1 个文化无关性、2 个行为级、1 个边界）。
2. A/B：把实现临时改回"按下标 + 本机时钟"的接线，同一批用例 6 通过 / 2 失败；恢复后 8/8 通过。
3. 构建：`dotnet build .\src\DeskBox\DeskBox.csproj --configuration Debug --no-restore -p:Platform=x64` → 0 错误、20 警告、DLL 晚于构建开始。

## 风险与遗留

1. **陈旧缓存仍然没有任何"可能已过期"的提示**——本次只修"贴错日期"，不修"该不该显示"。要真正解决需要产品决策 + 12 语言新字符串（方案 B），留作线索。
2. 缓存窗口内没有"今天"时（过期超过预报天数），今日 UV/降水/日出日落仍回落第 0 条：数据来自哪一天没有标注，但至少不再被称作"今天"。
3. `ParseDateToDayLabel`/`FormatHourLabel`/`IsDaytimeHour` 仍用环境区域性解析 ISO 字符串（`DateTimeOffset.Parse` 无 `InvariantCulture`）：在 Um Al Qura 等非公历默认区域下可能解析失败并回退成原始字符串。P2 线索，未修。
4. `WeatherData.Timezone` 若不是可解析的 IANA id，会静默回落本机时钟（有日志）：不改变"用位置时钟"的意图，但跨时区时仍可能偏差。
