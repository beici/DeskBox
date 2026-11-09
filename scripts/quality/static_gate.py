#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DeskBox 静态验证门禁（Linux 无编译环境 · 每批次推送前运行并留输出）
检查项：
  1. 13 语言键一致性与占位符一致性（Strings/*.json）
  2. async void 计数 <= 基线
  3. 剪贴板写配对：Clipboard.SetContent 附近必须有 MarkWrite/MarkText（豁免清单管理）
  4. 禁止模式：UI 路径新增 GetAwaiter().GetResult()/.Result/.Wait()、新增空 catch、新增反射
  5. 契约断言重放：ContractTests 内字符串断言在缓存的仓库文本中重放
用法：python3 scripts/quality/static_gate.py [--json OUT.json]
"""
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Windows GBK 控制台打印 ✗/✓ 等符号会 UnicodeEncodeError 裸退（R2-⑩ 过程缺陷）：
# 统一重配 stdout/stderr 为 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
SRC = os.path.join(ROOT, "src", "DeskBox")
TESTS = os.path.join(ROOT, "tests", "DeskBox.Tests")
BASELINE_FILE = os.path.join(ROOT, "scripts", "quality", "static-baseline.json")
LANGS = ["ar-SA", "bn-BD", "de-DE", "en-US", "es-ES", "fr-FR",
         "hi-IN", "ja-JP", "ko-KR", "pt-BR", "ru-RU", "zh-CN", "zh-TW"]

# DEF-027: 死宿主 QuickCaptureWidgetWindow 已随统一内容窗口路径删除，其 3 处
# 剪贴板写随之消失，豁免清单清空（后续如需豁免，在此重新登记）。
CLIPBOARD_EXEMPT_FILES = []

PLACEHOLDER_RE = re.compile(r"\{(\w+)\}")


_RG_AVAILABLE = shutil.which("rg") is not None
_REPO_FILE_CACHE = None

# rg 缺失时的纯 Python 回退扫描扩展集（覆盖本脚本搜索的 src 与 tests 内容）
_FALLBACK_EXTS = {".cs", ".xaml", ".json", ".py", ".ps1", ".rs", ".iss"}


def _fallback_scan(pattern, path):
    """rg 不可用时的等价回退：逐行正则扫描，输出同为 '文件:行号:内容'。"""
    compiled = re.compile(pattern)
    results = []
    if os.path.isfile(path):
        candidates = [path]
    else:
        candidates = []
        for dirpath, dirnames, filenames in os.walk(path):
            dirnames[:] = [d for d in dirnames
                           if not d.startswith(".") and d not in ("bin", "obj")]
            candidates.extend(
                os.path.join(dirpath, f) for f in sorted(filenames)
                if os.path.splitext(f)[1].lower() in _FALLBACK_EXTS)
    for file_path in candidates:
        try:
            with open(file_path, "r", encoding="utf-8", errors="replace") as fh:
                for line_no, line in enumerate(fh, 1):
                    if compiled.search(line):
                        results.append(f"{file_path}:{line_no}:{line.rstrip()}")
        except OSError:
            continue
    return results


def _repository_file_paths():
    """Use the same file inventory with and without ripgrep, including new text formats."""
    if os.path.exists(os.path.join(ROOT, ".git")) and shutil.which("git"):
        result = subprocess.run(
            ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if result.returncode != 0:
            raise RuntimeError(f"Cannot enumerate repository files: {result.stderr.strip()}")
        return [os.path.normpath(os.path.join(ROOT, path))
                for path in sorted(set(result.stdout.split("\0"))) if path]

    # Source archives can be checked without Git. Exclude generated directories,
    # but do not restrict text to a suffix list that can miss native/build inputs.
    paths = []
    skip_dirs = {"bin", "obj", "artifacts", ".artifacts", "Output", "TestResults",
                 "node_modules", "target", "packages", "__pycache__"}
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames
                       if (not d.startswith(".") or d in (".github", ".githooks")) and d not in skip_dirs]
        paths.extend(os.path.join(dirpath, fn) for fn in sorted(filenames))
    return sorted(paths)


def _fallback_repo_files():
    """Shared repository text cache for contract replay and multiline checks."""
    global _REPO_FILE_CACHE
    if _REPO_FILE_CACHE is not None:
        return _REPO_FILE_CACHE
    cache = {}
    for file_path in _repository_file_paths():
        if not os.path.isfile(file_path):
            continue  # A tracked file may have been deleted in the working tree.
        with open(file_path, "rb") as source:
            raw = source.read()
        if b"\0" not in raw:
            cache[file_path] = raw.decode("utf-8", errors="replace").replace("\r\n", "\n")
    _REPO_FILE_CACHE = cache
    return cache


def rg(pattern, path, extra_args=None):
    """返回 (count, lines) —— lines 为 '文件:行号:内容' 列表"""
    if not _RG_AVAILABLE:
        # 环境无 ripgrep（如 CI 最小镜像）：回退到纯 Python 扫描，语义等价，
        # 避免整门禁在第一个 rg 调用上裸 traceback 退出（R2-⑩ 过程缺陷）。
        return _fallback_scan(pattern, path)
    cmd = ["rg", "--no-heading", "-n", pattern, path]
    if extra_args:
        cmd += extra_args
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    lines = [l for l in p.stdout.splitlines() if l.strip()]
    return lines


# rg 输出行按 '文件:行号:内容' 解析；Windows 盘符路径自带冒号（E:\...），
# 不能用 split(":", 2)——非贪婪正则回溯到「冒号后是行号」处才定界。
_LOC_RE = re.compile(r"^(.+?):(\d+):(.*)$", re.S)


def parse_loc(line):
    m = _LOC_RE.match(line)
    if not m:
        return (line, -1, "")
    return (m.group(1), int(m.group(2)), m.group(3))


def check_strings():
    errors, stats = [], []
    # 防漂移：Strings/ 目录实际语言集必须与 LANGS 完全一致——新增/删除语言
    # 而忘改本清单时立即失败，而不是静默漏检一个语言。
    strings_dir = os.path.join(SRC, "Strings")
    actual_langs = sorted(
        f[:-5] for f in os.listdir(strings_dir) if f.endswith(".json"))
    if actual_langs != sorted(LANGS):
        missing = sorted(set(LANGS) - set(actual_langs))
        extra = sorted(set(actual_langs) - set(LANGS))
        errors.append(
            f"Strings/ 目录与 LANGS 清单不一致: 清单多 {missing}, 目录多 {extra}")
        return errors, stats
    keysets = {}
    for lang in LANGS:
        path = os.path.join(SRC, "Strings", f"{lang}.json")
        if not os.path.exists(path):
            errors.append(f"缺少语言文件: {lang}.json")
            continue
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            errors.append(f"{lang}.json 解析失败: {e}")
            continue
        flat = {}
        def walk(obj, prefix=""):
            for k, v in obj.items():
                key = f"{prefix}.{k}" if prefix else k
                if isinstance(v, dict):
                    walk(v, key)
                else:
                    flat[key] = v
        walk(data)
        keysets[lang] = flat
        stats.append(f"{lang}: {len(flat)} 键")
    if not keysets:
        return errors, stats
    ref = max(keysets.values(), key=len)
    ref_lang = [l for l in LANGS if keysets.get(l) is ref][0]
    for lang in LANGS:
        if lang not in keysets:
            continue
        missing = set(ref) - set(keysets[lang])
        extra = set(keysets[lang]) - set(ref)
        if missing:
            errors.append(f"{lang} 缺键({len(missing)}): {sorted(missing)[:5]} ...")
        if extra:
            errors.append(f"{lang} 多键({len(extra)}): {sorted(extra)[:5]} ...")
        for k in set(ref) & set(keysets[lang]):
            pv = set(PLACEHOLDER_RE.findall(str(ref[k])))
            pa = set(PLACEHOLDER_RE.findall(str(keysets[lang][k])))
            if pv != pa:
                errors.append(f"{lang} 键 {k} 占位符不一致: 基准{sorted(pv)} vs 该语言{sorted(pa)}")
    return errors, stats


def check_async_void():
    lines = rg(r"\basync void\b", SRC)
    baseline = load_baseline()
    base_count = baseline.get("async_void_count")
    return lines, base_count


def _method_start_line(lines, idx):
    """向上找最近的成员声明行（粗略方法边界），返回其行号(0-based)"""
    decl = re.compile(r"^\s*(public|private|internal|protected)\s.*[\(\{]")
    for i in range(idx, max(idx - 400, -1), -1):
        if decl.match(lines[i]):
            return i
    return max(idx - 12, 0)


def check_clipboard_pairing():
    writes = rg(r"Clipboard\.SetContent", SRC)
    marks = rg(r"MarkWrite\(|MarkText\(", SRC)
    exempt_real = set()
    for f in CLIPBOARD_EXEMPT_FILES:
        exempt_real.add(os.path.normpath(os.path.join(ROOT, f)))
    def norm(p):
        return os.path.normpath(p)
    # 文件 -> {行号: True}，用于同方法体判定
    mark_by_file = {}
    for l in marks:
        fp, ln, _ = parse_loc(l)
        mark_by_file.setdefault(norm(fp), {})[ln] = True
    unpaired = []
    paired = 0
    for l in writes:
        fp, ln, text = parse_loc(l)
        if norm(fp) in exempt_real:
            continue
        near = [m for m in mark_by_file.get(norm(fp), {}) if abs(m - ln) <= 12]
        if near:
            paired += 1
            continue
        # 同方法体回溯：MarkWrite 在 SetContent 之前的同一方法体内即算配对
        # （对齐 R6 复核澄清：FileSurfaceContent.xaml.cs:3721 先标记，方法内两条写路径共用）
        try:
            with open(fp, encoding="utf-8") as f:
                flines = f.readlines()
            start = _method_start_line(flines, ln - 1)
            earlier = [m for m in mark_by_file.get(norm(fp), {}) if start + 1 <= m <= ln]
            if earlier:
                paired += 1
                continue
        except Exception:
            pass
        unpaired.append(l)
    return paired, unpaired, len(writes), len(marks)


def check_forbidden_patterns():
    lines_result = rg(r"GetAwaiter\(\)\.GetResult\(\)|\.Result\b|\.Wait\(\)", SRC)
    empty_catch = []
    # Count catch blocks, not each output line of a multiline rg match.
    compiled = re.compile(r"catch\s*(\([^)]*\))?\s*\{\s*\}", re.S)
    for file_path, content in _fallback_repo_files().items():
        if not file_path.startswith(SRC):
            continue
        for m in compiled.finditer(content):
            line_no = content.count("\n", 0, m.start()) + 1
            snippet = " ".join(m.group(0).split())
            empty_catch.append(f"{file_path}:{line_no}:{snippet}")
    reflection = rg(r"GetType\(\)\.GetMethod|Activator\.CreateInstance|Assembly\.Load|typeof\([^)]*\)\.GetField|typeof\([^)]*\)\.GetProperty", SRC)
    return lines_result, empty_catch, reflection


_ASSERTION_LITERAL_RE = re.compile(r'Assert\.(?:Contains|DoesNotContain)\(\s*"(?:[^"\\]|\\.)*"')


def _contract_search_content(path, content):
    # Contracts also inspect test project files, probes and other test methods.
    # Keep those targets, but erase assertion literals so they cannot validate themselves.
    relative = os.path.relpath(path, ROOT).replace(os.sep, "/")
    if relative.startswith("tests/") and relative.endswith(".cs"):
        content = _ASSERTION_LITERAL_RE.sub("", content)
    return content.replace("\r\n", "\n")


def _literal_repository_contents():
    """Load repository text once, excluding test assertion literals."""
    return [_contract_search_content(path, content)
            for path, content in _fallback_repo_files().items()]


def decode_csharp_literal(literal):
    """Decode regular C# string escapes without corrupting non-ASCII text."""
    simple = {"n": "\n", "r": "\r", "t": "\t", "0": "\0", "a": "\a",
              "b": "\b", "f": "\f", "v": "\v", '"': '"', "'": "'", "\\": "\\"}
    def replace(match):
        value = match.group(1)
        if value[0] in ("u", "U", "x"):
            return chr(int(value[1:], 16))
        return simple[value]
    return re.sub(r"\\(u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|x[0-9a-fA-F]{1,4}|.)", replace, literal)


def replay_contract_assertions():
    """把 ContractTests 内的字符串断言在仓库文本缓存中定长匹配重放。
    Contains：0 命中=失联（基线豁免历史项，新增失联才报）。
    DoesNotContain：全仓命中仅作观察，切片作用域由真实测试裁决。
    搜索空间包含构建描述符、探针及其他测试实现，但移除测试断言字面量。
    返回 (hits, new_misses, all_misses, doesnot_violations)。"""
    repository_contents = _literal_repository_contents()
    repository_contents_lower = [content.lower() for content in repository_contents]
    all_items = []
    doesnot_items = []
    cfiles = []
    if os.path.isdir(TESTS):
        for dirpath, _, files in os.walk(TESTS):
            for fn in files:
                if "ContractTests" in fn and fn.endswith(".cs"):
                    cfiles.append(os.path.join(dirpath, fn))
    for cf in cfiles:
        rel = os.path.relpath(cf, ROOT).replace(os.sep, "/")
        try:
            with open(cf, encoding="utf-8") as source:
                src = source.read()
        except Exception:
            continue
        # Only the explicit string-comparison overload is a source-literal assertion.
        # Collection assertions and ordinary Contains calls require the real tests.
        pattern = r'Assert\.(Contains|DoesNotContain)\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*[A-Za-z_]\w*\s*,\s*StringComparison\.(Ordinal|OrdinalIgnoreCase)\s*\)'
        for m in re.finditer(pattern, src):
            kind = m.group(1)
            raw_literal = m.group(2)
            lit = decode_csharp_literal(raw_literal)
            # Keep historical report/baseline identities stable while searching decoded text.
            label = raw_literal.replace('\\"', '"').replace("\\\\", "\\")
            # 定长匹配：断言字面量按原文长度在仓库全文中查找（剔除转义空白差异影响的最小化处理）
            if len(lit) < 6:
                continue
            normalized = lit.replace("\r\n", "\n")
            if m.group(3) == "OrdinalIgnoreCase":
                total = any(normalized.lower() in content for content in repository_contents_lower)
            else:
                total = any(normalized in content for content in repository_contents)
            if kind == "DoesNotContain":
                doesnot_items.append((rel, label, total))
            else:
                all_items.append((rel, label, total))
    misses = [f"{rel} :: {lit[:70]}" for rel, lit, total in all_items if total == 0]
    hits_n = sum(1 for _, _, t in all_items if t > 0)
    doesnot_violations = [
        f"DoesNotContain violation :: {rel} :: {lit[:70]}"
        for rel, lit, total in doesnot_items if total > 0]
    baseline = load_baseline()
    base_misses = set(baseline.get("contract_misses", []))
    new_misses = [m for m in misses if m not in base_misses]
    return hits_n, new_misses, misses, doesnot_violations


def load_baseline():
    if os.path.exists(BASELINE_FILE):
        with open(BASELINE_FILE, encoding="utf-8") as f:
            return json.load(f)
    return {}


def save_baseline(data):
    with open(BASELINE_FILE, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def main():
    args = sys.argv[1:]
    out_json = None
    if "--json" in args:
        out_json = args[args.index("--json") + 1]
    report = {}
    failures = []
    print("=" * 62)
    print("DeskBox 静态验证门禁 static_gate.py")
    print("=" * 62)

    # 1. 13 语言
    errs, stats = check_strings()
    print(f"\n[1] 13 语言键/占位符一致性: {'PASS' if not errs else 'FAIL'}")
    for s in stats:
        print("    " + s)
    for e in errs:
        print("    ✗ " + e)
        failures.append("strings: " + e)

    # 2. async void
    av_lines, base_count = check_async_void()
    print(f"\n[2] async void 计数: {len(av_lines)} (基线 {base_count})")
    if base_count is not None and len(av_lines) > base_count:
        for l in av_lines:
            print("    " + l)
        failures.append(f"async void {len(av_lines)} > 基线 {base_count}")

    # 3. 剪贴板配对
    paired, unpaired, wcount, mcount = check_clipboard_pairing()
    print(f"\n[3] 剪贴板写配对: SetContent={wcount}, MarkWrite/MarkText={mcount}, 配对={paired}, 未配对={len(unpaired)}")
    for l in unpaired:
        print("    ✗ " + l)
        failures.append("clipboard: " + l)

    # 4. 禁止模式
    sync_wait, empty_catch, reflection = check_forbidden_patterns()
    baseline = load_baseline()
    sync_base = baseline.get("sync_wait_count")
    empty_base = baseline.get("empty_catch_count")
    refl_base = baseline.get("reflection_count")
    print(f"\n[4] 禁止模式: 同步等待={len(sync_wait)} (基线 {sync_base}), 空 catch={len(empty_catch)} (基线 {empty_base}), 反射={len(reflection)} (基线 {refl_base})")
    for label, cur, base, lines in [
            ("sync_wait", len(sync_wait), sync_base, sync_wait),
            ("empty_catch", len(empty_catch), empty_base, empty_catch),
            ("reflection", len(reflection), refl_base, reflection)]:
        if base is not None and cur > base:
            for l in lines:
                print(f"    ✗ [{label} 新增] " + l)
            failures.append(f"{label} 新增 {cur - base} 处")

    # 5. 契约断言重放
    hits, new_misses, all_misses, doesnot_hits = replay_contract_assertions()
    print(f"\n[5] 契约断言重放: 命中 {hits} / 失联 {len(all_misses)}（其中新增 {len(new_misses)}）")
    for m in new_misses:
        print("    ✗ 新增失联: " + m)
        failures.append("contract-replay: " + m)
    # DoesNotContain 断言钉的是「某生产文件切片内不得出现 X」，其作用域是
    # 断言的第二个参数（切片变量），不是全仓。全仓定长重放必然在无关生产
    # 文件命中（实测 Windows10WidgetMotion/WindowShadow 系列 40+ 假阳性），
    # 因此这里只记录为可观察项，真裁决由 x64 全量测试中的断言本体执行。
    if doesnot_hits:
        print(f"    ⚠ DoesNotContain 全仓命中 {len(doesnot_hits)} 处（切片作用域断言，仅供参考）")

    print("\n" + "=" * 62)
    if failures:
        print(f"门禁结论: FAIL ({len(failures)} 项)")
        for f_ in failures:
            print("  - " + f_)
        rc = 1
    else:
        print("门禁结论: PASS")
        rc = 0
    report = {
        "async_void_count": len(av_lines),
        "clipboard_setcontent": wcount,
        "clipboard_marks": mcount,
        "clipboard_unpaired": unpaired,
        "sync_wait_count": len(sync_wait),
        "empty_catch_count": len(empty_catch),
        "reflection_count": len(reflection),
        "contract_hits": hits,
        "contract_misses": all_misses,
        "doesnot_hits": doesnot_hits,
        "failures": failures,
    }
    if out_json:
        if "--update-baseline" in args:
            # A baseline written while the gate is red turns today's failure into
            # tomorrow's budget (DEF-151): the file used to keep a stale
            # `"failures": ["async void 257 > 基线 254"]` from exactly that.
            # Counting/structural failures block the write; contract-replay
            # misses are observations that the baseline keeps on purpose, so they
            # only need an explicit --accept-new-misses after a human review.
            blocking = [f for f in failures if not f.startswith("contract-replay:")]
            if blocking:
                print("拒绝写入基线：门禁当前为 FAIL（" + "; ".join(blocking) + "），先消除红灯再更新（DEF-151）。")
            elif new_misses and "--accept-new-misses" not in args:
                print(f"拒绝写入基线：有 {len(new_misses)} 条新增契约失联，核验后加 --accept-new-misses 再更新。")
            else:
                # The baseline is a budget ledger, not a run report: keep the
                # counters/known sets, drop the run-time failure list.
                save_baseline({k: v for k, v in report.items() if k != "failures"})
        out_dir = os.path.dirname(out_json)
        if out_dir:
            os.makedirs(out_dir, exist_ok=True)
        with open(out_json, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        print(f"报告已写入 {out_json}")
    sys.exit(rc)


if __name__ == "__main__":
    main()
