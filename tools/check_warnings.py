#!/usr/bin/env python3
"""Zero-NEW-warning baseline (Task A5, item 6).

Собирает решение, извлекает предупреждения (`warning CS####`) и сравнивает их с
закреплённым baseline: разрешено только УМЕНЬШЕНИЕ набора/числа предупреждений.
Появление НОВОГО кода, НОВОЙ пары (файл, код) или РОСТА счётчика кода -> провал
(exit 1). Существующие предупреждения разбираются по категориям и устраняются
отдельными безопасными коммитами; после каждого безопасного коммита baseline
перегенерируется флагом `--update` (см. `plans/SuperPlan.md`).

Использование:
  python tools/check_warnings.py            # проверка сборки против baseline
  python tools/check_warnings.py --update   # перегенерировать baseline (после исправлений)
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve()
DEFAULT_ROOT = HERE.parent.parent
DEFAULT_SLN = Path("src") / "besm6.net" / "besm6.net.sln"
DEFAULT_BASELINE = Path("tools") / "warnings_baseline.json"

# строка вида:  path\File.cs(12,5): warning CS8618: message [E:\...\project.csproj]
WARN = re.compile(r"^\s*(\S+?\.cs)\(\s*\d+\s*,\s*\d+\):\s*warning\s+(CS\d+):\s*(.*?)\s*(?:\[[^\]]*\])?\s*$", re.M)


def parse_warnings(text):
    """Извлечь предупреждения из вывода сборки: total, by_code, fc(файл,код), rows."""
    rows = []
    for m in WARN.finditer(text or ""):
        rows.append({"file": m.group(1), "code": m.group(2), "msg": (m.group(3) or "").strip()})
    by_code = {}
    for r in rows:
        by_code[r["code"]] = by_code.get(r["code"], 0) + 1
    fc = sorted({(r["file"], r["code"]) for r in rows})
    unique = len({(r["file"], r["code"], r["msg"]) for r in rows})
    return {"total": len(rows), "by_code": {k: by_code[k] for k in sorted(by_code)},
            "fc": fc, "unique": unique, "rows": rows}


def compare(current, allowed):
    """Сравнить текущий набор с baseline: PASS только если нет новых и нет роста.

    Возвращает {passed, new_codes, increased{code:(allowed,current)}, new_fc}.
    Уменьшение (исправление предупреждений) всегда PASS.
    """
    c_by = current["by_code"]
    a_by = allowed.get("by_code", {})
    new_codes = [c for c in c_by if c not in a_by]
    increased = {c: (a_by.get(c, 0), c_by[c]) for c in c_by
                 if c in a_by and c_by[c] > a_by.get(c, 0)}
    allowed_fc = {tuple(x) for x in allowed.get("fc", [])}
    new_fc = [f for f in current["fc"] if tuple(f) not in allowed_fc]
    passed = not new_codes and not increased and not new_fc
    return {"passed": passed, "new_codes": new_codes, "increased": increased, "new_fc": new_fc}


def load_baseline(path):
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    data["fc"] = [tuple(x) for x in data.get("fc", [])]
    return data


def write_baseline(path, current):
    data = {
        "description": "Zero-NEW-warning baseline (A5 item 6). Разрешено только уменьшение набора/числа предупреждений.",
        "note": "total=сырые строки warning в выводе сборки (дублируются); unique=уникальных (file,code,msg); MSBuild-сводка считает иначе.",
        "total": current["total"],
        "unique": current.get("unique", 0),
        "by_code": current["by_code"],
        "fc": [list(x) for x in current["fc"]],
    }
    Path(path).write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return data


def _build(sln, root):
    proc = subprocess.run(["dotnet", "build", str(sln), "--nologo", "--no-incremental"],
                          capture_output=True, text=True, cwd=str(root))
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--root", default=None, help="Корень проекта (по умолчанию от расположения скрипта).")
    p.add_argument("--sln", default=None, help="Путь к .sln (по умолчанию src/besm6.net/besm6.net.sln).")
    p.add_argument("--baseline", default=None, help="Путь к baseline JSON (по умолчанию tools/warnings_baseline.json).")
    p.add_argument("--update", action="store_true", help="Перегенерировать baseline из текущей сборки.")
    args = p.parse_args(argv)

    root = Path(args.root).resolve() if args.root else DEFAULT_ROOT
    sln = Path(args.sln).resolve() if args.sln else (root / DEFAULT_SLN)
    baseline = Path(args.baseline).resolve() if args.baseline else (root / DEFAULT_BASELINE)
    if not sln.exists():
        print(f"ОШИБКА: нет sln {sln}", file=sys.stderr)
        return 3

    rc, text = _build(sln, root)
    if rc != 0:
        print("ОШИБКА: сборка завершилась с ошибкой (последние строки):", file=sys.stderr)
        print(text[-2000:], file=sys.stderr)
        return 2

    current = parse_warnings(text)
    print(f"Сборка: {current['total']} строк warning / {current['unique']} уникальных")
    for code, n in current["by_code"].items():
        print(f"  {n:4}  {code}")

    if args.update:
        data = write_baseline(baseline, current)
        print(f"Baseline обновлён: {baseline} (total={data['total']})")
        return 0

    if not baseline.exists():
        print(f"ОШИБКА: нет baseline {baseline}. Создайте: python tools/check_warnings.py --update", file=sys.stderr)
        return 3

    allowed = load_baseline(baseline)
    result = compare(current, allowed)
    allowed_unique = int(allowed.get("unique", 0))
    fixed = max(0, allowed_unique - int(current.get("unique", 0)))
    print(f"Уникальных предупреждений: baseline={allowed_unique}, current={current['unique']}, устранено={fixed}")
    if result["passed"]:
        print("PASS: новых предупреждений нет (допустимо только уменьшение).")
        return 0

    print("FAIL: обнаружены новые предупреждения или рост:", file=sys.stderr)
    for c in result["new_codes"]:
        print(f"  НОВЫЙ КОД: {c}", file=sys.stderr)
    for c, (a, b) in result["increased"].items():
        print(f"  РОСТ: {c}: {a} -> {b}", file=sys.stderr)
    for f in result["new_fc"]:
        print(f"  НОВОЕ МЕСТО: {f[1]} в {f[0]}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())