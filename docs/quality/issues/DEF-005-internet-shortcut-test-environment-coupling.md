# DEF-005 Internet 快捷方式枚举测试依赖宿主机 Steam 状态

- 优先级：P3 ｜ 状态：已修复（测试密闭化，回归通过）｜ 修复轮次：R1

## 一、问题现象

- **复现步骤**：在装有 Steam 且存在 Steam 库目录的机器上运行 `dotnet test ... -p:Platform=x64`，`FileServiceTests.EnumerateDirectoryAsync_RecognizesInternetShortcutAndHidesExtension` 失败：`Assert.Single()` 集合为空。
- **触发条件**：宿主机可解析出 Steam 库路径且库中不存在 `appmanifest_123.acf`（本机即为该环境）。
- **影响范围**：仅测试套件；生产功能（死 Steam 快捷方式过滤）为有意设计，不受影响。
- **风险等级**：低（但破坏回归门禁全绿的判定力，必须修复）。

## 二、根因分析（源码级）

- 被测链路：`FileService.TryCreateEntrySnapshot`（`src/DeskBox/Services/FileService.cs:530`）对 `.url` 文件调用 `IsDeadSteamShortcut`（:2306）——内容含 `steam://rungameid/<id>` 且 `IsSteamGameInstalled(id)` 为假时**过滤掉该文件**。`IsSteamGameInstalled` 在「能解析出 Steam 库路径但无该游戏 manifest」时返回 false（判定死亡），这正是产品意图（过滤已卸载游戏残留快捷方式）。
- 测试用例恰好使用 `Steam.url` + `steam://rungameid/123` 作为「普通 Internet 快捷方式」的夹具，隐含假设宿主机无 Steam 库。在装有 Steam 的机器上夹具被合法过滤 → 枚举为空 → 断言失败。已用 `git stash` 在干净工作树上复现，确认与 R1 改动无关的既有缺陷。
- 分类：测试隔离性缺陷（fixture 依赖环境），非产品缺陷。

## 三、优化/修复思路

改用非 Steam 协议的通用 URL（`https://example.org/`）作为夹具：测试本意是验证「.url 识别 + 显示扩展名开关下的扩展名隐藏 + TargetPath 解析」，与 Steam 过滤逻辑无关；脱离 Steam 前缀后 `IsDeadSteamShortcut` 直接短路返回 false，测试在全环境密闭。备选方案（注入 Steam 库路径依赖做 hermetic fixture）需要重构 FileService 静态依赖，违反最小侵入原则，不采用。

## 四、拟修改代码模块与功能说明（已实施）

| 文件 | 改动 |
|---|---|
| `tests/DeskBox.Tests/FileServiceTests.cs` | 夹具 `Steam.url`/`steam://rungameid/123` → `Example.url`/`https://example.org/`，断言同步更新，并附注释说明为何不能用 rungameid 夹具 |

## 五、风险评估

- 测试覆盖面不变（IsShortcut/名称/TargetPath 三项断言保留）；
- 「死 Steam 快捷方式过滤」行为暂无专属测试；若需要，应在注入 Steam 库路径的可测化改造（R2+）中补齐，而不是在当前测试里混合验证。

## 六、验证方案

- x64 全量回归 2998/2998 通过（修复前本机为 2997/2998）；
- 后续在无 Steam 环境的 CI（现有 GitHub Actions workflow）上同样通过。
