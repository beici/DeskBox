# 历史改写记录：剥离 `0c5c6dbc` 的违规署名 trailer

> 日期：2026-09-24
> 对象：`merge-upstream-main` 分支**自有**的 18 条提交
> 改写前 HEAD：`0e2924ea` → 改写后 HEAD：`bc001c6e`
> 远端 `origin/wip/fix-bug` 已强推同步（`0e2924ea...bc001c6e forced update`）
> 完整机器可读映射：`.git/filter-repo/commit-map`

## 1. 为什么必须改写

`.github/workflows/ci.yml` 的 `Commit message attribution check` 步骤（第 22–31 行）
带 `if: github.event_name == 'pull_request'`，扫描 `git log --format=%B <base.sha>..HEAD`，
命中即 `exit 1`：

```
(?im)^\s*(co-authored-by|generated[ -]with|generated-by)\s*[: ]
```

`.githooks/commit-msg` 用同一条正则（仅匹配行首 trailer，正文里讨论这些词不受影响）。

提交 `0c5c6dbc`（2026-09-22，合并提交，双亲 = fork `main` 的 `bc60fbc` + 上游
`Tianyu199509/DeskBox` 的 `d4b0a7a`）的消息含行首：

```
Co-authored-by: monkeycode-ai <monkeycode-ai@chaitin.com>
```

它是 `bc60fbc` 的后代，因此**落在 `main..wip/fix-bug` 区间内**（该区间共 131 条提交）。
一旦对 `main` 开 PR，该步骤必然失败。

**此前不报错的原因**：`push` 触发只列 `main`/`master`，且署名检查只在 PR 事件下执行。
所以这是一个**潜伏**缺陷——推 `wip/fix-bug` 本身不会触发 CI。本记录的目的就是把这个
潜伏状态显式消掉。

> 附注：`d9526fde`（2026-07-24）也含 `Co-Authored-By: WorkBuddy <workbuddy@tencent.com>`，
> 但它是 `main` 的祖先，**不在 PR 区间内**，属既有历史，本次刻意未动。

## 2. 改写范围：为什么不是整支

**不能**用 `--refs refs/heads/merge-upstream-main`。那会改写该分支的**全部祖先**——包括
fork 的 `main` 与上游 v1.5.5 的全部提交，等于摧毁 fork↔upstream 关系，之后每次
`git merge upstream/main` 都会看到两段互不相关的历史。

改写集合必须精确等于「分支自有提交」：

```
git rev-list --count HEAD --not bc60fbc d4b0a7a    # = 18
```

即从分支尖端可达、且不由两个边界父提交可达的提交。`git rev-list 'A^@..B'` **不是**合法
范围语法，必须用 `B --not <parent1> <parent2>` 的显式形式。

## 3. 旧 → 新 哈希映射

| 旧 | 新 | 标题 |
|---|---|---|
| `0c5c6dbc` | `4bc5d29c` | Merge upstream/main (v1.5.5): WIP — 65 files keep conflict markers on purpose |
| `d34f0203` | `d6bf7d23` | Merge wip/fix-bug audit-docs commits (DEF-069~084 ledger) into merge tree |
| `9d1e30b6` | `91094f40` | docs(deskbox): sync merge handoff with pushed WIP state |
| `ef2238a5` | `98eb10ee` | chore(deskbox): record push/hook environment gotchas in agent memory |
| `f964d7c2` | `d16e196a` | Merge upstream/main (v1.5.5): settings slices, startup pipeline, sync hotkey API; keep fork DEF fixes and frame-rate cap |
| `1521c023` | `971b39bf` | merge(upstream-1.5.5): reconcile fork features with upstream contract ratchets |
| `b24e265d` | `01230bef` | chore(audit): recalibrate the WMC1510 pins to the measured 742 and bump the profile to 63 |
| `c131ccc3` | `ab3071f6` | fix(tests): retire the six dead QuickCapture facade budgets |
| `62d730f3` | `f41249c0` | docs(architecture): record the recalibration verification evidence |
| `bf38571c` | `a57dc540` | docs(architecture): correct the suite status to 4226/4226 green |
| `11093a89` | `5ccc3b01` | chore(repo): ignore the .workbuddy-ai agent data directory |
| `5b178872` | `a7f356cf` | docs(architecture): record the audit-run measurement and the host link limit |
| `99be3167` | `baf7d16e` | docs(architecture): correct the ILC failure cause; it was the nulled OS variable |
| `a7eba1fd` | `7989e94e` | docs(architecture): record the full AOT audit passing end to end |
| `ba50da03` | `512632a2` | fix(settings,compact): normalize fork-only shell fields and surface clamped expansions |
| `0e2924ea` | `bc001c6e` | test,docs: pin the new normalization behavior and record the 2026-09-23 review |

**SHA 未变**（消息未改，故对象未变）：`e9bab845`、`ba92af20`。

## 4. 校验证据

| 检查项 | 结果 |
|---|---|
| 边界提交 `bc60fbc` / `d4b0a7a` | SHA 未变 |
| HEAD 树 | 逐字节不变：`029e9f12757bf0c67378520102711990a2149f9f` |
| 新合并提交 `4bc5d29c` | 树 `25ef357f…`、父 `bc60fbc9 d4b0a7a2`，与原 `0c5c6dbc` 完全一致 |
| 18 条的 `%T`/作者/提交者/日期 | 逐条比对一致（差异仅剩父哈希字段） |
| 18 条的父提交数量 | 逐条一致 |
| 真正新增的提交对象 | 仅 1 个（`0c5c6dbc` → `4bc5d29c`）；其余变化均由父链传递 |
| `main` 分支 | 仍为 `bc60fbc`，未动 |
| 旧对象 | 未被清理，`git cat-file -t 0c5c6dbc` 仍可解析 |
| `origin` / `upstream` remote | 未被 filter-repo 删除 |
| replace refs / `refs/original` | 无 |
| `main..wip/fix-bug` 区间 trailer 扫描 | **0 命中** → CI 署名步骤将通过 |

## 5. 消息层的净变化

除「删除 1 行 trailer」外，还有 **5 处交叉引用哈希被同步更新**（`git-filter-repo` 的
默认行为：把消息里引用的旧哈希替换为新哈希，避免产生悬空引用）：

| 所在提交（新） | 引用变化 |
|---|---|
| `d16e196a` | `ef2238a` → `98eb10e` |
| `971b39bf` | `f964d7c` → `d16e196` |
| `f41249c0` | `c131ccc` → `ab3071f` |
| `baf7d16e` | `5b17887` → `a7f356c` |
| `7989e94e` | `99be316` → `baf7d16` |

若改用 `--preserve-commit-hashes` 可让消息逐字不变，但上述引用会变成**无法解析的悬空
引用**，等于由改写本身引入新缺陷，故未采用。

## 6. 复现命令

```bash
git-filter-repo --force \
  --refs refs/heads/merge-upstream-main '^bc60fbc' '^d4b0a7a' \
  --message-callback 'return re.sub(rb"(?im)^[ \t]*co-authored-by:[ \t]*monkeycode-ai[^\r\n]*\r?\n", b"", message)'

git push --force-with-lease=refs/heads/wip/fix-bug:<expected-sha> \
  origin refs/heads/merge-upstream-main:refs/heads/wip/fix-bug
```

`--refs` 接受负向修订（`^sha`）并隐含 `--partial`：未列入的提交保持原 SHA，旧对象不被
prune。执行前务必先 `cp -r .git <backup>`，并在一次性 `--mirror` 克隆上验证后再动真仓。

## 7. 对仓库内既有引用的影响

本仓库内**已跟踪文档**中早于本次改写写下的提交哈希，指的是**改写前**的对象，无法再从
分支解析（旧对象仍在本地对象库中，`git show <旧哈希>` 仍可用；GitHub 侧在 GC 前亦可达）。
请用第 3 节的映射表换算，例如 `a7eba1f` 现在是 `7989e94e`、`0e2924ea` 现在是 `bc001c6e`。

涉及的文件：`.monkeycode/docs/merge-upstream-handoff.md`、
`docs/architecture/pluginization-roadmap.md`、`docs/quality/reviews/2026-09-23-daily-commit-review.md`。

> 其中 `2026-09-23-daily-commit-review.md` 关于 git 对象损坏事故的叙述（约第 970–982 行）
> 属于**历史记录**，其旧哈希是当时事实的一部分，**不应改写**。

## 8. 操作须知

本次是对**已推送共享分支**的历史改写。任何已克隆该分支的工作副本都会与远端分叉，需
`git fetch origin && git reset --hard origin/wip/fix-bug`（或重新克隆）后再继续。改写前
的完整对象库另存于 `E:/DeskBox-git-backup-20260924-2313`（150 MB）。
