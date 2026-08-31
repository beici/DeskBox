#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DeskBox 静态验证门禁（Linux 无编译环境 · 每批次推送前运行并留输出）
检查项：
  1. 12 语言键一致性与占位符一致性（Strings/*.json）
  2. async void 计数 <= 基线
  3. 剪贴板写配对：Clipboard.SetContent 附近必须有 MarkWrite/MarkText（豁免清单管理）
  4. 禁止模式：UI 路径新增 GetAwaiter().GetResult()/.Result/.Wait()、新增空 catch、新增反射
  5. 契约断言重放：ContractTests 内字符串断言以 rg 重放
用法：python3 scripts/quality/static_gate.py [--json OUT.json]
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "src", "DeskBox")
TESTS = os.path.join(ROOT, "tests", "DeskBox.Tests")
BASELINE_FILE = os.path.join(ROOT, "scripts", "quality", "static-baseline.json")
LANGS = ["ar-SA", "bn-BD", "de-DE", "en-US", "es-ES", "fr-FR",
         "hi-IN", "ja-JP", "pt-BR", "ru-RU", "zh-CN", "zh-TW"]

# 已知豁免：死宿主 3 处剪贴板写（随 DEF-027 处置），批次 D 删除后自动清零
CLIPBOARD_EXEMPT_FILES = [
    "src/DeskBox/Views/QuickCaptureWidgetWindow.SelectionAndDrop.cs",
    "src/DeskBox/Views/QuickCaptureWidgetWindow.Items.cs",
    "src/DeskBox/Views/QuickCaptureWidgetWindow.Attachments.cs",
]

PLACEHOLDER_RE = re.compile(r"\{(\w+)\}")


def rg(pattern, path, extra_args=None):
    """返回 (count, lines) —— lines 为 '文件:行号:内容' 列表"""
    cmd = ["rg", "--no-heading", "-n", pattern, path]
    if extra_args:
        cmd += extra_args
    p = subprocess.run(cmd, capture_output=True, text=True)
    lines = [l for l in p.stdout.splitlines() if l.strip()]
    return lines


def check_strings():
    errors, stats = [], []
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
        parts = l.split(":", 2)
        mark_by_file.setdefault(norm(parts[0]), {})[int(parts[1])] = True
    unpaired = []
    paired = 0
    for l in writes:
        parts = l.split(":", 2)
        fp, ln, text = parts[0], int(parts[1]), parts[2]
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
    p = subprocess.run(
        ["rg", "--no-heading", "-n", "-U", r"catch\s*(\([^)]*\))?\s*\{\s*\}", SRC],
        capture_output=True, text=True)
    for l in p.stdout.splitlines():
        if l.strip():
            empty_catch.append(l)
    reflection = rg(r"GetType\(\)\.GetMethod|Activator\.CreateInstance|Assembly\.Load|typeof\([^)]*\)\.GetField|typeof\([^)]*\)\.GetProperty", SRC)
    return lines_result, empty_catch, reflection


def replay_contract_assertions():
    """把 ContractTests 内的字符串断言用 rg 定长匹配重放，只报告「新增失联」。
    基线：static-baseline.json 的 contract_misses（克隆时快照）。
    返回 (hits, new_misses, base_miss_count)。"""
    all_items = []
    cfiles = []
    if os.path.isdir(TESTS):
        for dirpath, _, files in os.walk(TESTS):
            for fn in files:
                if "ContractTests" in fn and fn.endswith(".cs"):
                    cfiles.append(os.path.join(dirpath, fn))
    for cf in cfiles:
        rel = os.path.relpath(cf, ROOT)
        try:
            src = open(cf, encoding="utf-8").read()
        except Exception:
            continue
        for m in re.finditer(r'(?:Contains|DoesNotContain)\(\s*"((?:[^"\\]|\\.)*)"', src):
            lit = m.group(1)
            lit = lit.replace('\\"', '"').replace("\\\\", "\\")
            # 定长匹配：断言字面量按原文长度在仓库全文中查找（剔除转义空白差异影响的最小化处理）
            if len(lit) < 6:
                continue
            esc = re.escape(lit)
            r = subprocess.run(["rg", "-F", "-c", "--no-filename", "--", lit, ROOT],
                               capture_output=True, text=True)
            total = sum(int(x) for x in r.stdout.split())
            all_items.append((rel, lit, total))
    misses = [f"{rel} :: {lit[:70]}" for rel, lit, total in all_items if total == 0]
    hits_n = sum(1 for _, _, t in all_items if t > 0)
    baseline = load_baseline()
    base_misses = set(baseline.get("contract_misses", []))
    new_misses = [m for m in misses if m not in base_misses]
    return hits_n, new_misses, misses


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

    # 1. 12 语言
    errs, stats = check_strings()
    print(f"\n[1] 12 语言键/占位符一致性: {'PASS' if not errs else 'FAIL'}")
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
    hits, new_misses, all_misses = replay_contract_assertions()
    print(f"\n[5] 契约断言重放: 命中 {hits} / 失联 {len(all_misses)}（其中新增 {len(new_misses)}）")
    for m in new_misses:
        print("    ✗ 新增失联: " + m)
        failures.append("contract-replay: " + m)

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
        "failures": failures,
    }
    if out_json:
        save_baseline(report) if "--update-baseline" in args else None
        with open(out_json, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        print(f"报告已写入 {out_json}")
    sys.exit(rc)


if __name__ == "__main__":
    main()
