# Windows 侧门禁待验证项汇总（pending-windows-gate）

> **2026-09-01 更新：GitHub Actions CI（windows-latest）已全绿——Release x64 Build + 3015/3015 测试通过（run 33477323326）。下表中「构建」「测试」两类自动项已闭环，明细见 `ci-verification-report.md`；「人工验证」项仍需实机执行。**
>
> 背景：本轮迭代（F6 批次 A/B/C/D + F7 卫生批次 + 收敛式深度审查）在 Linux 服务器上以纯静态方式进行（代码审查 + 静态门禁脚本），**没有 WinUI3/Windows 编译环境**。所有代码改动都经过 Linux 静态验证（`scripts/quality/static_gate.py`：12 语言键一致、async void/同步等待/空 catch/反射基线对比、剪贴板写配对、契约断言重放），但**全部改动未经编译验证**。
> 请在每次推送后于 Windows 机按 `AGENTS.md`（见任务附录）执行构建与回归；本文件汇总每批待验证点，验证通过后请标记 ✅ 并注明结果。

## 门禁命令（每次推送后执行）

```powershell
Get-Process DeskBox | Where-Object { $_.Path -like 'E:\DeskBox*' } | Stop-Process -Force
C:\Users\scrip\Tools\dotnet10\dotnet.exe build .\src\DeskBox\DeskBox.csproj -c Debug --no-restore -p:Platform=x64
C:\Users\scrip\Tools\dotnet10\dotnet.exe test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore -p:Platform=x64
# 规范构建 + 重启（人工测试实例）
C:\Users\scrip\Tools\dotnet10\dotnet.exe build .\src\DeskBox\DeskBox.csproj -c Debug --no-restore
Start-Process E:\DeskBox\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.exe
# 身份核实：%LOCALAPPDATA%\DeskBox\DeskBox.log 的 [Build] buildTime 行；基线 x64 3011/3011
```

## 预期回归基线

| 提交 | 预期用例数 |
|---|---|
| `d2e7e87`（批次 A） | 3018/3018（3011 + 7） |
| `f25cb77`（批次 B） | 3023/3023（+5） |
| `12dbbf2`（批次 C） | 3025/3025（+2 净） |
| `ad8febe`（批次 D） | 死宿主专属契约用例删除后总数略降，**全绿即可** |
| `4682a02`（F7） | ARC-06 测试删除，总数再降，**全绿即可** |

## 批次 A（DEF-014/015/017）

- [ ] 构建 0 错误、警告不高于基线 24 条
- [ ] `WindowInteractionSafetyNetContractTests`（4）+ `WidgetSessionManagerTests` 新增 3 用例通过
- [ ] 可选人工观察：正常使用下日志不应出现 `[TrayBatch] Interaction watchdog`；出现即说明真有泄漏被兜住，请保留日志
- [ ] 可选人工观察：托盘/F7 唤起被前台锁拒绝时出现 `[ZOrder] Content ActivateRaisedFromTrayBatch: SetForegroundWindow FAILED`
- [ ] 可选人工观察：启动日志 `[ShowDesktop] Self-heal watcher started minimizeHook=0x... foregroundHook=0x...` 两值均非 0

## 批次 B（DEF-011/012/013）

- [ ] `QuickCaptureServiceTests` +2、`QuickCaptureDataIntegrityContractTests`（3）通过
- [ ] **人工验证（重点）**：删除一条图像随记 → 4.2s 内点撤销 → 图片完好（DEF-012 核心场景）
- [ ] 人工验证：打开 Markdown 记录编辑 → 保存 → 格式仍为 Markdown（DEF-011）
- [ ] 人工验证：自定义文字色 × 跟随主题背景 → 切换系统主题 → 跌破阈值时背景自动回退（DEF-013）

## 批次 C（DEF-018~026 含 DEF-022）

- [ ] `StabilityHardeningContractTests`（5）、胶囊 +2、天气 +1 用例通过
- [ ] **人工验证（重点）**：从文件格子拖拽文件到资源管理器/桌面（含网络盘路径）→ 拖动无 UI 冻结、落放内容完整（DEF-023 延迟供货路径）
- [ ] 人工验证：胶囊数量多 + 极小分辨率/虚拟机 → 胶囊栏不溢出屏幕（DEF-026）
- [ ] 人工验证：设置页改热键/切双击桌面开关 → 无卡顿（DEF-022 异步握手）；热键功能正常触发
- [ ] 人工验证：锁定/解锁或 Explorer 重启后热键自动恢复（生命周期恢复经 SafeFireAndForget 路径）
- [ ] 人工验证：天气格子并发/连续刷新显示正常（DEF-025）
- [ ] 人工验证：快速开关「自动整理桌面」多次 → 无崩溃（DEF-024）
- [ ] 人工验证：胶囊 live 状态/边框氛围动画视觉表现不变（DEF-018 死代码删除零行为变化确认）
- [ ] 人工验证（低频）：启动失败通知——仅当启动失败时出现「未能完全启动」toast/托盘提示（DEF-020，正常启动无感知）

## 批次 D（DEF-027 死宿主删除）

- [ ] **随记全功能人工回归清单**：`rectify/F6-D-quickcapture-regression-checklist.md` 7 大类 30 项
- [ ] 构建确认无 QuickCaptureWidgetWindow 相关编译错误（应零引用）

## F7 卫生批次

- [ ] ARC-06/ANI-02 删除后构建 0 错误（AOT smoke 无 SmartAnimationAdapter 残留引用）
- [ ] 人工验证：Markdown 工具栏「更多」按钮 tooltip 各语言显示已翻译文本（CFG-08）
- [ ] 人工验证：对比度拒绝对话框关闭按钮显示已翻译「关闭」而非 `Common.Close`（CFG-08）

## 挂账条目观察（非本批修复，运行时留意）

- MEM-01（托盘旧 Icon）/MEM-02（SoftwareBitmap 释放）：下次运行窗口可用任务管理器句柄计数辅助观察
- QC-10（Markdown 无界递归 StackOverflow 面）：如遇超深嵌套 Markdown 记录卡死请保留样本

## 后续批次

（无——本轮迭代全部批次已闭环）

## F8 R2 Round 1（Linux Hermes 批次，2026-09-02）

> 对应提交：`663c593` / `cf0fadb` / `3f2504d` / `4701b39`；CI run 33561624305（Build + Test 全绿）已覆盖编译与回归层验证。以下为 CI 无法覆盖的实机项。

| # | 验证项 | 提交 | 预期表现 | 状态 |
|---|---|---|---|---|
| F8-1 | 传入任意多窗口场景反复触发格子组唤起/收起（DEF-034 相邻回归） | 4701b39 | z-order 重排正常，无可见闪烁/跳位；日志无 `[ZOrder] Window order re-checked` 异常刷屏（该日志仅在锁竞争真实发生时出现，低频属预期） | 待验证 |
| F8-2 | Store 构建（如有）：启动 + 设置页自动启动开关 + Onboarding 步骤 4 开关（DEF-035/036） | 4701b39 | UI 无冻结感；开关状态与系统设置一致；首次点击开关响应正常（缓存未命中路径） | 待验证（非 Store 构建不受影响） |
| F8-3 | 长时间挂机 + 频繁触发后台内存清理调度（DEF-037） | 4701b39 | 日志无 ObjectDisposedException / `[Memory] Background cleanup coordinator failed`；内存平稳 | 待验证 |
| F8-4 | 常规冒烟：启动 → 桌面格子加载 → 随记/文件/天气各一次交互 | 4701b39 | 全部正常（本批未触碰这些路径，仅回归确认） | 待验证 |

---

## F9 批次实机验证（DEF-043~046，ca22866，2026-09-02 Windows 侧执行）

> 门禁：HEAD `ca22866` 精确匹配；`dotnet test -p:Platform=x64` **3139/3139 全绿**；Debug 构建 0 错误；实机 `buildTime=2026-09-02 17:59:24` 启动正常。
> 验证方法：Todo 链路用「存储夹具注入（停机态）→ 真实运行时」驱动；Everything/天气/搜索用实机操作 + 日志锚点。

### F8 遗留项（上一批表）
- [x] F8-1 多窗口格子组唤起/收起——伴随验证（多格悬停连发 + 交互）无跳位异常，无 `SetForegroundWindow FAILED`
- [x] F8-3 后台内存清理日志——本会话日志无 `ObjectDisposedException`
- [x] F8-4 常规冒烟——启动 → 格子加载 → 搜索/随记/待办交互各一次，正常
- [ ] F8-2 Store 构建（如适用）——本机无 Store 构建，N/A 待有构建时补

### F9 新增项
- [x] **F9-4（DEF-045）未启用 Everything 快速输入**：`searchEverythingEnabled=false`，搜索框 26 字符 81ms 输入无卡顿，日志零逐击键探测（30s 缓存生效）
- [x] **F9-5（DEF-044）Everything 3s 超时**：**N/A**——本机未装 Everything 且未启用集成，超时路径由 `EverythingIntegrationTests` 门禁用例覆盖
- [x] **F9-6（DEF-046）天气**：全程正常渲染（23°C 风速 4.7m/s），无异常日志；负风向防护由 `WeatherWindDirectionMapperTests` 覆盖
- [x] **F9-1a（DEF-043）外部写入→UI 实时合并**：停机态注入存储 → 启动后列表/计数即时更新，无重载无闪烁（`ApplyExternalStoreChange` 实证）
- [x] **F9-1b（DEF-043）提醒触发**：3 次夹具任务均准时弹出（`[TodoReminder] Native notification shown` ×3），`reminderLastNotifiedAt` 正确回写存储
- [x] **F9-2（DEF-043）格子内手动完成**：UI 勾选 → `isCompleted=True` 原子写回（`MutateAsync` 路径门控实证）；「过期通知点完成无害失败」由 `TodoReminderServiceTests` 覆盖
- [x] **F9-3（DEF-043）贪睡字段存活**：`snoozedUntil` 经应用加载 + 保存周期后磁盘原样保留，未被冲掉
- [x] **F9-7 Todo 全功能回归**：创建（UI 输入 + Enter）、详情编辑、完成勾选、计数联动（全部/今天/重要/已完成）、外部变更合并——手感正常；通知按钮链路由 `TodoNotificationActivationRouterTests`（8 用例含 complete/snooze/legacy-snooze10）覆盖
- [ ] **F9-1c 通知按钮人工点击**：横幅被系统勿扰抑制 + toast 未驻留通知中心（见下），合成点击无法安全命中；请人工执行一次：造一条 2 分钟后到期+提醒的任务 → 弹出后点通知「完成」→ 确认格子同步变完成
- [ ] **F9-3 人工补点**：弹出后点通知「贪睡 10 分钟」→ 格子内编辑其它字段 → 重启 → 确认贪睡时间与编辑共存

### 验证中发现的新问题（立案待评估，未改码）
- **[新发现] 提醒 toast 未在通知中心驻留**：3 次复现——横幅弹出（勿扰下被抑制）后打开通知中心均为空（「没有新通知」），用户失去未点击提醒的后续处理入口。代码侧未发现程序化移除（`TodoReminderService` 无 Remove 调用）。候选根因：勿扰模式系统行为 vs AppNotification 配置（过期/驻留标志）。**请人工确认一次**：提醒弹出后 1 分钟内打开通知中心检查是否可见；确认缺失再立案修复。

### 日志红线（本会话 6008 行之后）
`[TodoReminder] Store-changed subscriber failed` = 0；`[TodoReminder] Complete failed` = 0；`[Everything] Query IPC failed` = 0。✅

---

## 胶囊动画/层级批次（DEF-056/057/058，2026-09-04 Windows 侧执行）

> 全程在 Windows 实机完成：`DESKBOX_PERF_LOG=1` + `DESKBOX_VERBOSE_LOG=1` 取证 → 修复 → 同法复测。回归 **3202/3202**（3179 基线 + 23 新增用例）。

### 已完成的实机核验

- [x] 展开动画前后对照（`[Perf] CompactAnimation`）：展开 median maxFrameMs **17.5 → 8.8ms**、dropped median **3 → 0**、≥90ms 停顿 **27/160 → 2/10**（仅剩会话内首两次冷展开）；收起路径始终 dropped=0，构成同格子对照实验
- [x] 展开热路径零 peer-order 批：`Expanded lease acquired` 之前不再出现 `Window order minimized`
- [x] 空闲整理收敛：每轮固定 `moved=1 kept=11`（修复前 `moved=9→8→7→6` 逐轮递减、永不为 0）；启动后可见 `Window order already correct`（零 SetWindowPos）
- [x] 标题栏空白单击 ×12：每次仅 `moved=1`，不再连带重排 owner 组
- [x] 格子可见性：12 个格子 z-order 秩 **3–14 连续**，远高于 `Progman@35`（DEF-058 回归已排除）

### 仍需用户实机观感确认

- [ ] 悬停自动展开的**丝滑度**（165Hz 下几何步进由 55fps 提升到 82fps）
- [ ] 点击格子标题栏空白处是否还看得到边缘闪烁（残留：被点击的那一个格子约 130ms 后回位 1 次）
- [ ] 连续快速悬停相邻胶囊（前一个未收完就悬停下一个）是否还有卡顿
- [ ] **格子收起后不再出现暂时消失/变空白**（DEF-058 验证点，请重点复测）
- [ ] Win+D「显示桌面」后格子仍在（owner 附着未被标志改动影响）
- [ ] 拖动格子经过其他应用窗口时格子仍在其之上（抬升路径未被削弱）

### 已知残留（未在本批处理）

- 会话内每个格子**首两次**展开仍有 100–140ms 停顿，来自内容首次实体化 + Debug JIT 分层编译；Release/AOT 下形态不同，需另行测量。
- 标题栏按下时仍会把被点击格子抬到普通带顶部（`ActivateAllVisibleWidgetsFromTitle` 与 `ElevateForInteraction` 各一次），因此约 130ms 后的空闲整理必然要回位 1 次。归零需把抬升延后到拖拽真正越过阈值，会改变拖拽语义，留作后续专项。


---

## 胶囊点击/形变连续性批次（DEF-059/060，2026-09-04 第二轮）

> 门禁：Debug 构建 0 错误 20 警告（均为既有）；`dotnet test -p:Platform=x64` **3218/3218 全绿**（3202 基线 + 16 新增）；实机 `buildTime=2026-09-04 19:50:31`。
> 上一批「已知残留」的两条本批全部闭环：标题栏抬升已延后到拖拽阈值；首次展开停顿已由跳步改为吸收。

### 已完成核验（脚本 + 日志）

- [x] **标题栏空白连点 3 次：日志 `[ZOrder]`/`[WidgetLayer]` 零条**（修复前每次 6 条事务：owner 分离 → TOPMOST → NOTOPMOST → HWND_TOP → owner 挂回 → 12 窗口空闲整理）
- [x] 冷态首次形变全程有帧可画：File 展开 `frames 6→42`、QuickCapture 收起 `32→44`、Search 收起 `26→42`；`firstFrameMs` 仍会记录 117–176ms 的首次光栅化，但不再被折算成进度跳步
- [x] 暖态（第二次起）无回归：`dropped=0~2`、`maxFrameMs 7.7–12.9`、`stalledMs=0`、`elapsedMs≈271`（与修复前一致）
- [x] 设置成本证伪埋点：`CompactTransitionSetup totalMs=0.5–1.6ms`（presentation/freeze/refreshRate/border/prepare 分项全部 ≤1.2ms）
- [x] 静置层级正确：12 格子 owner=`SHELLDLL_DefView`、`TopMost=False`、秩位于 `Shell_TrayWnd` 之下

### 仍需用户实机观感确认

- [ ] **点击标题栏空白处：两侧格子边缘是否已完全不闪**（本批主目标，请重点复测截图中的那两条竖带）
- [ ] **悬停自动展开是否丝滑**：会话内每个格子第一次展开会先「顿一下再平滑展开」（约 120–180ms 延迟后完整动画），不再是「跳到半开再补完」——请确认这个手感是否可接受
- [ ] 拖动格子（真的拖动，不是点一下）功能正常：抬到最上层、吸附参考线、Ctrl 协同移动、胶囊栏重排
- [ ] 位置锁定的格子点击标题栏：不再抬升（有意变更），确认无异常
- [ ] 点击格子后格子会随系统激活抬到前面、失焦后回到桌面层——确认不会长期挡住其它应用窗口

### 已知残留（未在本批处理）

- 冷态首次展开的 117–176ms 光栅化停顿本身仍存在（Debug/JIT 环境更明显），本批只保证它不再破坏动画连续性。彻底消除需要在预热阶段强制一次真实渲染（如离屏渲染预热），属独立专项。
- 不移动的点击不再触发空闲层级整理，因此点击后被激活的格子会保持在同伴之上，直到下一次展开/收起或托盘切换重新归一化。相邻格子在当前布局下互不重叠，无可见影响。

---

## 收起层级 + 边距参考系批次（DEF-061/062，2026-09-06）

> 门禁：Debug 构建 0 错误 20 警告（均为既有）；`dotnet test -p:Platform=x64` **3236/3236 全绿**。

### 已完成核验（脚本 + 日志 + 测试）

- [x] **DEF-061 静置态收起零 owner 移动**：桌面点空白使格子回到桌面层后做整列悬停扫掠，`bottom=True` **0 次**、`Window order minimized` **0 次**、逐次出现 `Resting band rejoin hwnd=... below=...`
- [x] DEF-061 收起动画：`frames=46 dropped=0 maxFrameMs 7.9–8.5 firstFrameMs≤3.8 stalledMs=0`
- [x] DEF-061 收起末尾结算成本埋点：`CompactCollapseSettle totalMs=1.3–2.2ms`（分项 ≤1.4ms），证伪「结算太重」这条假设
- [x] **DEF-062 桌面图标几何实机可读**：`DesktopIconGeometryServiceTests` 在真实会话读到 **24 个图标矩形**，全部宽高为正、命中缓存复用
- [x] DEF-062 纯策略单测：最近邻居优先（图标/格子按距离取胜）、无邻居退回工作区、仅擦到角不算邻居、四边位移落点、容差按 DPI 缩放（1.0/1.25/1.5/2.0）

### 仍需用户实机观感确认

- [ ] **收起动画 + 完全收起瞬间**：相邻几个格子是否已不再一起变暗闪一下（本批主目标）
- [ ] **边距对话框**：右键标题栏 →「边距设置…」，确认默认展开的是上/下/左/右四个输入框，且每个标题后面写着它当前参考的对象（最近的格子 / 最近的桌面图标 / 屏幕边缘）
- [ ] 在某一侧确实有桌面图标或文件夹时，输入的数值是相对那个图标测量的（改数值后格子应贴着图标而不是贴屏幕边）
- [ ] 某一侧确实空无一物时退回「屏幕边缘」是否符合预期（这是设计上的兜底，标题会写明）
- [ ] 「应用到全部可见格子」在新参考系下的批量效果
- [ ] 移动桌面图标后再打开对话框，参考对象与数值应在 1.5 秒内反映新位置（缓存有效期）

### 已知残留 / 观察项

- 桌面图标矩形取的是 LVIR_BOUNDS（图标 + 文字标签的整体外框），因此贴到长文件名图标旁边时，参考边界会包含文字宽度。如需只按图标图形对齐，可改用 LVIR_ICON，属可配置项，本批未开。
- 悬停展开会把格子抬到应用窗口之上（`TryBringAbovePeerWidgetsAtDesktopLayer` 走 HWND_TOP），失焦后才由相对恢复链路回落。本批未改动该路径；如果实际使用中发现格子长期压住其它窗口，需要单独立案（属红线文档 §坑 #2 邻域）。

---

## 边距编辑器宿主批次（DEF-063，2026-09-06）

> 门禁：Debug 构建 0 错误 20 警告（均为既有）；`dotnet test -p:Platform=x64` **3253/3253 全绿**（本批新增 17）。

### 已完成核验（合成输入 + 窗口枚举 + 截图逐项比对）

- [x] **编辑器不再被格子裁切**：格子实测 391×408 物理像素，编辑器窗口 `边距设置… rect=2027,44 525x535`，四个输入框（上/下/左/右）+ 说明行 + 「应用到全部可见格子」+ 保存/取消**全部可见、无需滚动**
- [x] **置位与钳制**：格子贴在屏幕右缘时窗口自动左移到 `x=2027`（=2560−525−8），格子贴顶时 `y` 钳到 8；纯函数 `WidgetDialogLayout` 单测覆盖居中、边缘钳制、副显示器工作区、窗口大于工作区四种情形
- [x] **多位数输入一次落位**：在「上」输入 `33`，格子从 y=107 一次移动到 **y=85**（参考边界 52 + 33），中途不再逐位跳动
- [x] **非焦点边实时重算**：同一次编辑后「下」自动变为 22（=515−(85+408)），焦点框保持用户输入不被改写
- [x] **取消回位 / 保存持久化**：取消把格子放回打开对话框时的位置；保存后位置留存且编辑器关闭
- [x] **颜色选择器同宿主**：「文字与可读性 → 自定义颜色…」窗口 475×785，色谱 + 明度条 + RGB/十六进制 + 红/绿/蓝三行 + 保存/取消全显
- [x] **程序化回写不再冒充用户编辑**：修前实测「下=0」被当成用户编辑并把格子拖到邻居身上（y 从 112 变 107），修后同一操作序列不再发生

### 仍需用户实机观感确认

- [ ] 编辑器窗口的位置手感：默认居中于被编辑的格子并置顶显示，是否符合预期（也可拖动窗口标题栏挪开）
- [ ] 160ms 防抖后的预览节奏：输入过程中格子跟随是否够即时、又不再逐位乱跳
- [ ] 「应用到全部可见格子」在新宿主下的批量效果（本批未改批量语义）
- [ ] 窄工作区/低分辨率或 100% 缩放机器上的观感（预算随工作区收缩，必要时改单列并滚动）

### 已知残留 / 观察项

- 对置边（上/下 或 左/右）同时被编辑时，二者可能互相矛盾（格子高度固定），当前按 左→上→右→下 的顺序应用，**后应用的一侧生效**。属 DEF-062 既有语义，本批未改。
- 「最近邻居」是按当前位置解析的：移动之后可能有另一个更近的对象接管该侧，因此保存后显示的数值可以与刚输入的值不同（例如输入 60 落位后显示 55）。这是参考系的真实结果而非丢失输入，焦点框在编辑期间不会被改写。
