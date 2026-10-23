# 第 9 轮：ISO 时间字符串用"显示区域性"解析（非公历区域下日标签退化成原始日期）

## 人话摘要

天气格子里"今天/明天"之外的日期标签本该显示"周三"这种星期缩写，但它把接口给的 ISO 字符串交给**系统当前区域性**去解析。在默认日历不是公历的区域（如阿拉伯语/沙特，Um Al Qura 历），`"2026-10-25"` 这种纯日期字符串会直接解析失败，于是标签退化成原始字符串——用户看到的是 `2026-10-25` 而不是星期。时间串（`"2026-10-23T14:00"`）能解析，但会被读成回历年份，只有"小时"恰好不受影响。

## 问题

`src/DeskBox/ViewModels/WeatherWidgetViewModel.DataProcessing.cs`（修复前）四处：

- `IsDaytimeHour`（`:645` 附近）`DateTimeOffset.Parse(isoTime)`
- `FormatHourLabel`（`:658`）同上
- `FormatTime`（`:671`）同上
- `ParseDateToDayLabel`（`:684`）`DateTimeOffset.Parse(dateStr)`

用户可感知的后果：在默认日历非公历的区域里，日预报从第 3 条起显示成 `2026-10-25` 这样的原始日期字符串；`ParseDateToDayLabel` 的 `catch` 把"解析失败"和"格式未知"混在一起，所以不会有任何提示。

## 证据

探针（临时测试，测完删除；结果写入 `%TEMP%\probe-culture.txt`，本机即 `E:\DeskBox-dev\testtmp\probe-culture.txt`），`CultureInfo.CurrentCulture = ar-SA`：

```
2026-10-23       => THROW FormatException
2026-10-23T14:00 => OK 1448-05-12 14:00 hour=14 ddd=周五
2026-10-23T06:10 => OK 1448-05-12 06:10 hour=6  ddd=周五
```

结论：纯日期串**抛异常**（标签退化）；带时间的串被读成回历日期，但 `Hour` 正确，因此小时标签/日出日落时间当时没有坏，只有日标签坏。

行为级复现（新增单测 `DayLabels_StayReadableUnderANonGregorianSystemCalendar`）：把系统区域性设为 `ar-SA` 后加载一份缓存预报，修复前 3 条日标签全部包含 `"2026"`（即原始日期串），修复后全部是星期缩写。

## 根因

**把"线格式"（ISO-8601）交给"显示区域性"解析**。ISO 字符串与区域性无关，解析时必须用 `InvariantCulture`；只有**格式化输出**才应该跟随用户语言。

## 候选方案与打分

| 方案 | 消除根因(30) | 回归面(20) | 新概念依赖(20) | 可验证性(15) | 架构一致(15) | 合计 |
|---|---|---|---|---|---|---|
| A. 四处解析全部改成 `DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None)` | 30 | 19 | 20 | 14 | 15 | **98** |
| B. 改用 `ParseExact` 并枚举 API 的所有时间格式 | 24 | 12 | 12 | 12 | 14 | 74（格式多、易漏，失败面更大） |
| C. 让整个应用固定用不变区域性运行 | 26 | 6 | 8 | 10 | 4 | 54（会破坏本地化显示，不可接受） |
| D. 只在标签层兜底（解析失败就显示星期几以外的占位） | 10 | 18 | 16 | 12 | 10 | 66（治标） |

采用 A。

## 改法

四处 `DateTimeOffset.Parse(x)` → `DateTimeOffset.Parse(x, CultureInfo.InvariantCulture, DateTimeStyles.None)`；格式化仍用目标语言区域性（`dt.ToString("ddd", culture)` 不变）。

## 验证方法

1. 新增单测 `DayLabels_StayReadableUnderANonGregorianSystemCalendar`（`tests/DeskBox.Tests/WeatherTimeLabelTests.cs`）。
2. A/B：只把四处解析改回环境区域性 → 该用例失败（`Assert.DoesNotContain` 报 3 条里 3 条都含 `"2026"`），恢复后 `WeatherTimeLabelTests` 9/9 通过（EXITCODE=0）。
3. 构建：0 错误、20 警告、DLL 晚于构建开始。

## 风险与遗留

1. 同一类"线格式用显示区域性解析"的写法在仓库里还有 6 处，本轮未改（见 §10）：`src/DeskBox/Services/WeatherService.cs:499/:525/:527/:549/:559`（MSN 接口的 ISO 字符串）、`src/DeskBox/Services/WebDavBackupTransport.cs:294`（HTTP `Last-Modified` 头，RFC 7231 格式）。第 10 轮统一扫尾。
2. 解析失败仍然静默（`catch` 后返回原始字符串）：本轮只保证"在非公历区域也能解析成功"，没有加"解析失败要不要提示"的逻辑。
