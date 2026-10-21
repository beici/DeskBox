# Windows 端接手指南：合并后验收

> 交给：Windows 环境下的 AI agent
> 代码位置：`https://github.com/beici/DeskBox`，分支 **`wip/fix-bug`**，HEAD **`479339d4`**
> 本文件是自包含的。不需要读其他对话历史。

---

## 0. 一句话背景

我（Linux 端）刚把一个 191 提交的上游版本（`Tianyu199509/DeskBox` @ `bbb9e697`，v1.5.8）
合进了这个 fork（原来在 `1aa78145`，1.5.6）。**114 个冲突文件、222 个 hunk 已全部解决并推送**，
但 Linux 无法构建 WinUI3 / 跑 XAML 代码生成 / 跑测试 / 跑 PowerShell，
所以**测试套件一次都没跑过**。你的任务是把它验收到绿。

**不要重做合并**。冲突已解完，决策已记录。你要做的是**验证 + 修复验证暴露的问题**。

---

## 1. 环境准备

```powershell
git clone https://github.com/beici/DeskBox.git
cd DeskBox
git checkout wip/fix-bug
git log --oneline -1        # 必须是 479339d4
git config core.hooksPath .githooks
```

前置条件（与仓库既有要求一致）：
- .NET SDK **10.0.402**（`global.json` 钉死，`rollForward: latestPatch`）
- Rust 工具链（`rust-toolchain.toml` 钉死；测试项目会调 `scripts/build-rust-native.ps1`）
- PowerShell 7（`pwsh`）

恢复与构建（**注意 `-p:Platform=x64`，不要用默认 AnyCPU**）：

```powershell
dotnet restore .\DeskBox.sln -p:Platform=x64
dotnet build .\src\DeskBox\DeskBox.csproj --configuration Debug --no-restore -p:Platform=x64
```

---

## 2. 第一优先级：跑测试

```powershell
dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64
```

**判断标准（重要）**：只有 `passed == total` 才算绿。aborted 的 run 也会打印 `失败: 0`，
不要被它骗了——看总数。

预期规模：4,265 个 `[Fact]`/`[Theory]` + 1,883 个 `[InlineData]` 数据行。
合并前基线是 4,291 个用例全绿（1.5.6），合并后数量会有变化（上游新增了大量测试）。

### 失败最可能出现在哪

按我评估的概率排序：

1. **契约计数类**（我重算过但没跑过）。如果失败，错误信息里会直接给出「期望 X 实际 Y」，
   **按实际值改期望**，并在改动处写一行注释说明是合并后重新推导的。已知我重算过的数字：
   - `tests/DeskBox.Tests/AotStage4D1BContractTests.cs`：本地化 XAML 7 键 / 487
     （`SettingsCard|HeaderKey` 223、`DescriptionKey` 190、`SettingsExpander` 37/23、
     `InfoBar` 6/6、`TextBox|HeaderKey` 2）
   - `tests/DeskBox.Tests/SettingsSliceOwnershipContractTests.cs`：门面属性 **240**
   - `tests/DeskBox.Tests/JsonSerializationBaselineContractTests.cs`：41 文件 / 96 调用 / 37 context owner
   - `AotStage5B4B1ContractTests.cs`：可绑定计数 **33**（脚本 `publish-aot-audit.ps1:3661` 同步）
   - `scripts/publish-aot-audit.ps1`：WMC1510 = **796**（35 处字面量；验收后实测重校准值，fork 原值 742）
2. **XAML 绑定类**：`x:Bind` 指向的属性/命令不存在。我在 Linux 上做了启发式检查
   （151 个 `x:Bind`，按 DataTemplate 作用域核对，初报 7 个全部是检查器漏检多行属性声明），
   但**文本分析不等价于 XAML 编译器**。这是最可能藏真问题的地方。
3. **我新写的代码**（见 §4 的风险清单）。

### 如果是编译失败而不是测试失败

先看是不是 XAML 代码生成的问题（`XamlCompiler` 报错）。这类通常是
`x:Bind` 路径错误或 `x:Name` 缺失。错误信息会直接给出文件行号和成员名。

---

## 3. 第二优先级：AOT 审计

```powershell
pwsh -NoProfile -File .\scripts\publish-aot-audit.ps1
```

**这个脚本我在 Linux 上只做了括号平衡的结构校验，没有 PowerShell 语法解析**
（容器里没有 `pwsh`）。所以第一步先确认它能解析：

```powershell
pwsh -NoProfile -Command {
  $e=$null
  [void][System.Management.Automation.Language.Parser]::ParseFile(
    '.\scripts\publish-aot-audit.ps1', [ref]$null, [ref]$e)
  if ($e) { $e | Select-Object -First 20 } else { 'parse OK' }
}
```

语法 OK 后再跑完整审计。**WMC1510 在合并树上实测为 796**（fork 原值 742、base 866、上游 875），已按实测重校准并 bump `$auditProfileVersion` 63 → 64。
如果实际计数与钉值不符：脚本会明确报 `Stage NA WMC1510 count changed: expected=796 actual=N`，
此时按实际值更新 **36 处**（脚本 35 处 + 测试 1 处），保持全站一致。

---

## 4. 我改过的高风险代码（重点复核）

这些不是冲突解决，是我为了修复合并缺陷**新写**的代码。编译过了但没运行过。

| 位置 | 我做了什么 | 怎么复核 |
|---|---|---|
| `src/DeskBox/Features/Appearance/AppearanceSettingsViewModel.cs:1631` | 新增 `AnimationFrameRate` + `AvailableAnimationFrameRateOptions`（约 60 行），把 fork 的动画帧率上限 UI 移植进上游外观编辑器 | 打开设置 → 外观 → 动画，确认帧率下拉框出现、四档 30/60/90/120 可选、选择后 `settings.json` 的 `widgetAnimationFrameRate` 变化、胶囊动画实际节奏变化 |
| `src/DeskBox/Services/FeatureWidgetsSettingsCoordinator.cs:110,125` | 把 fork 的音乐按类型存储写入点迁进协调器（2 行 `_musicSettingsStore.Update(...)`） | 改音乐「封面背景」「封面悬停动效」→ 重启 → 确认音乐格子保持新值；同时确认 `data/music/settings.json` 被更新 |
| `src/DeskBox/App.xaml.cs:2415` | 恢复 `service.TodoStoreChanged += RelayTodoStoreChangedByReminder;` | 建一个带提醒的待办 → 等提醒触发 → 确认待办格子实时刷新（而不是要手动切页） |
| `src/DeskBox/Controls/WidgetContents/QuickCaptureWidgetContentAdapter.cs:248` | 新增 `MaterializedSurface` 访问器 | 随记格子右键 → 确认「记录配色」菜单项**可用**（不是灰的），点开能出取色器 |
| `src/DeskBox/Services/WidgetManager.ZOrder.cs:1046` | 合并了上游 #375 的重复抬升抑制 + fork 的单格抬升 | 快速连点某个格子标题栏，确认不闪烁、不整组重排 |
| `src/DeskBox/Views/ContentWidgetWindow.Commands.cs` | 把 fork 的随记剪贴板取色分支搬进上游的 try/finally 链 | 同第 4 行；另确认关掉菜单后没有句柄泄漏（任务管理器 GDI 句柄不持续增长） |

**`src/DeskBox/Helpers/NativeFileDragOut.cs` 被我删了**。理由：上游用
`ActiveDeskBoxDragRegistry` 取代了它的机制（`ContentWidgetWindow.NativeDragDrop.cs:875,1259`），
合并后它零调用点，且它调的 4 个 `Win32Helper` 方法上游已删。请重点验：
**从文件格子拖出文件到桌面 / 拖回同一格子（应被拒绝）**。

---

## 5. 真机行为冒烟（按重要性排序）

1. **拖出/拖入**（我删了 fork 的第二个 `Items_DragStarting`，行为依赖上游处理器全覆盖）
   - Win10 上拖出无操作选择框
   - 托管快捷方式拖出
   - 拖回源格子应被拒绝（不启动、不导入）
2. **胶囊动画**：悬停展开/收起流畅度（帧率上限现在可调，见 §4 第 1 行）
3. **设置页**：外观/动画节（含新帧率卡片）、随记记录配色、音乐节、文件叠放节
4. **引导流程**（我保留了本地五步流程，删了上游的 Scene/Steps）
5. **托管存储迁移**（现在走上游的事务式复制流程，fork 的逐项重试/跳过流程已退役）
6. **托盘显隐 / Show Desktop 自愈**（fork 的自愈服务 + 上游的 tray-exit 硬杀兜底并存）
7. **待办提醒 → 格子实时刷新**（见 §4 第 3 行）

---

## 6. 完整决策记录在哪

`docs/architecture/upstream-merge-1.5.8.md`（624 行，中文）。里面有：

- §一 结果总览（114 文件 / 222 hunk / 989 文件变更）
- §二 总体策略（默认保留本地 / 上游更优时采用 / 互补时取并集 / 谁是"活的"）
- §三 逐文件冲突决策表（含每个 hunk 的理由）
- §四 我 review 发现并修复的 13 个问题（含根因、修复、验证）
- §五 被上游机制取代而退役的 3 项本地实现
- §六 验证方式与**未验证边界**
- §七 Windows 验收清单

遇到不确定的冲突决策，先查这份文档；文档没覆盖的，用
`git show 1aa78145:<file>`（本地侧）、`git show bbb9e697:<file>`（上游侧）、
`git show d4b0a7a2:<file>`（merge base）三路对比来判断。

---

## 7. 约束（务必遵守）

- **提交身份**：`Simon <1047078635@qq.com>`。**禁止**任何 `Co-Authored-By` /
  `Generated with` 之类署名——`.githooks/commit-msg` 会拒绝，CI 也会失败。
  用 `git -c user.name=Simon -c user.email=1047078635@qq.com commit ...`
- **不要用 `--no-verify`** 绕过钩子。
- **保留本地功能行为**：这是合并的硬约束。如果某个修复会改变 fork 的既有行为，
  先停下来问。
- **12 语言键集必须一致**：`src/DeskBox/Strings/*.json` 十二个文件键集与占位符
  必须完全相同（当前 3,090 键；合并时的 3,095 已清掉 5 个被并集复活的退役键）。改任何文案都要 12 个一起改。
- **改动契约计数时同步脚本**：`scripts/publish-aot-audit.ps1` 和对应
  `AotStage*ContractTests.cs` 里的字面量是成对的，改一处必须改另一处。
- 回退点：分支 `backup/pre-upstream-1.5.8` = `1aa78145`（合并前的 1.5.6 全绿状态）。

---

## 8. 回报格式建议

每修一个问题，记录：**现象 → 根因 → 改动文件:行 → 验证方式**。
全部绿了之后，把这些追加到 `docs/architecture/upstream-merge-1.5.8.md` 的
「§八 Windows 端验收结果」一节，并单独提交（不要 amend 合并提交）。

如果测试大面积失败（比如 >50 个用例），**先别逐个修**——大概率是同一个根因
（比如某个 XAML 控件整体没生成）。先分类统计失败用例名，找共性，再动手。
