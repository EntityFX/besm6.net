#!/usr/bin/env python3
"""Портативный acceptance-runner для .dub-примеров (Task A5).

Прогоняет выбранные .dub-задачи через `dotnet <dll> run ...` и считает успехом
только согласованное сочетание exit code и StopReason: наличие текста
«Halted by STOP» само по себе НЕ считается успехом (A5 item 2). Сохраняет
JSON/JUnit-отчёт и полный stdout/stderr каждого сценария — их можно публиковать
как артефакты на падении CI.

Корень проекта вычисляется от расположения этого файла
(`<root>/tools/run_all_examples.py`) — в скрипте нет абсолютных путей, поэтому он
переносим и не зависит от конкретного локального checkout (A5 item 1).

Быстрый commit-gate (name/algol/bemsh) и полный набор примеров (nightly)
используют один и тот же runner/manifest (A5 item 4):

  dotnet build src/besm6.net/besm6.net.sln
  python tools/run_all_examples.py --suite fast          # commit gate
  python tools/run_all_examples.py --suite full          # full examples
  python tools/run_all_examples.py --only name,algol     # явный список
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve()
DEFAULT_ROOT = HERE.parent.parent          # <root>/tools/run_all_examples.py -> <root>
DEFAULT_DLL_REL = Path("src") / "besm6.net" / "bin" / "Debug" / "net8.0" / "besm6.dll"
SOLUTION_REL = Path("src") / "besm6.net" / "besm6.net.sln"
DEFAULT_LIMIT = "20000000"                  # 20M инструкций (совпадает с Config.DefaultLimit; защита от зависаний)
DEFAULT_TIMEOUT = 120                       # секунд на сценарий
FAST_EXAMPLES = ("name", "algol", "bemsh")  # быстрый commit-gate
SKIP_DIRS = ("tapes",)                      # каталоги в examples/, не являющиеся примерами
STOP_MARKER_OK = "Halted by STOP"           # контролируемая остановка (основной сигнал — exit code 0)
STOP_MARKER_ANY = "Halted by"               # любая контролируемая остановка
TAIL = 8000                                 # сколько символов вывода кладём в отчёт/JUnit


def default_root() -> Path:
    """Корень проекта от расположения этого файла (переносимость, A5 item 1)."""
    return DEFAULT_ROOT


def norm_only(only):
    """Принять `only` как None | frozenset имён (строка через запятую или коллекция)."""
    if only is None:
        return None
    if isinstance(only, str):
        items = [x.strip() for x in only.split(",")]
    else:
        items = [str(x).strip() for x in only]
    items = [x for x in items if x]
    return frozenset(items) if items else None

def find_dub_files(examples_dir, only=None):
    """Найти .dub в `examples_dir` (рекурсивно, кроме SKIP_DIRS), опц. фильтр по имени.

    `only` — frozenset имён (stem без `.dub`) или полный filename; если задан,
    возвращает только совпадающие. Порядок детерминирован (сортировка по пути).
    """
    examples_dir = Path(examples_dir)
    only = norm_only(only)
    files = []
    if not examples_dir.is_dir():
        return files
    for root, _dirs, fnames in os.walk(examples_dir):
        rel_root = Path(root).relative_to(examples_dir)
        if any(part in SKIP_DIRS for part in rel_root.parts):
            continue
        for f in fnames:
            if not f.endswith(".dub"):
                continue
            if only is not None and (Path(f).stem not in only and f not in only):
                continue
            files.append(Path(root) / f)
    return sorted(files)


def classify(returncode, out_text, timed_out=False):
    """Классификация сценария (A5 item 2): успех = exit code 0 И StopReason.

    `Halted by STOP` в выводе сам по себе НЕ считается успехом: основной сигнал —
    exit code (0), StopReason — подтверждение. Статусы: OK / HALTED / NO_STOP /
    LIMIT / ERROR / HANG.
    """
    if timed_out:
        return "HANG"
    text = out_text or ""
    if returncode == 0:
        if STOP_MARKER_OK in text:
            return "OK"
        if STOP_MARKER_ANY in text:
            return "HALTED"
        return "NO_STOP"
    if returncode == 2:
        return "LIMIT"
    return "ERROR"


PASS_STATUSES = {"OK", "HALTED"}


def extract_instrs(out_text):
    m = re.search(r"after (\d+) instructions", out_text or "")
    return m.group(1) if m else None


def _norm_text(s):
    s = (s or "").replace("\r\n", "\n").replace("\r", "\n")
    return "\n".join(line.strip() for line in s.split("\n") if line.strip())


def compare_golden(stdout_text, golden_text):
    """Нормализованное сравнение stdout с golden (CRLF/LF + отбел пробелов/пустых строк)."""
    return _norm_text(stdout_text) == _norm_text(golden_text)

def _count_by_status(results):
    d = {}
    for r in results:
        d[r["status"]] = d.get(r["status"], 0) + 1
    return d


def build_json_report(results, meta):
    """JSON-отчёт (A5 item 3): метаданные, сводка, публичные результаты (без полного вывода)."""
    ok = sum(1 for r in results if r["status"] in PASS_STATUSES)
    public = [{k: v for k, v in r.items() if k not in ("stdout", "stderr")} for r in results]
    return {
        "meta": meta,
        "summary": {
            "total": len(results),
            "passed": ok,
            "failed": len(results) - ok,
            "by_status": _count_by_status(results),
        },
        "results": public,
    }


def _tail(s, n=TAIL):
    s = s or ""
    return s if len(s) <= n else s[-n:]


def build_junit_xml(results, suite="besm6.examples"):
    """JUnit-совместимый XML (A5 item 3): каждый сценарий — testcase, провал — failure с выводом."""
    total = len(results)
    failed = sum(1 for r in results if r["status"] not in PASS_STATUSES)
    errors = sum(1 for r in results if r["status"] in ("EXC", "ERROR"))
    total_time = sum(float(r.get("secs", 0)) for r in results)
    ts = ET.Element("testsuites", {
        "name": suite, "tests": str(total), "failures": str(max(0, failed - errors)),
        "errors": str(errors), "skipped": "0", "time": f"{total_time:.2f}",
    })
    st = ET.SubElement(ts, "testsuite", {
        "name": suite, "tests": str(total), "failures": str(max(0, failed - errors)),
        "errors": str(errors), "skipped": "0", "time": f"{total_time:.2f}",
    })
    for r in results:
        name = os.path.basename(r["file"])
        tc = ET.SubElement(st, "testcase", {
            "name": name, "classname": suite, "time": f"{float(r.get('secs', 0)):.2f}",
        })
        if r["status"] not in PASS_STATUSES:
            message = f"status={r['status']} rc={r.get('rc')} instrs={r.get('instrs') or 'n/a'}"
            tag = "error" if r["status"] == "EXC" else "failure"
            el = ET.SubElement(tc, tag, {"message": message})
            el.text = (_tail(r.get("stdout")) + "\n--- stderr ---\n" + _tail(r.get("stderr")))
    if hasattr(ET, "indent"):
        ET.indent(ts)
    return ET.tostring(ts, encoding="unicode")


def _decode(b):
    if b is None:
        return ""
    if isinstance(b, bytes):
        return b.decode("utf-8", "replace")
    return b


def run_one(dll, path, root, limit, timeout):
    """Запустить один сценарий; вернуть результат со stdout/stderr для отчёта и golden."""
    rel = os.path.relpath(path, root)
    cmd = ["dotnet", str(dll), "run", str(path), "--limit", str(limit)]
    start = time.time()
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, cwd=str(root))
        rc, out, err = proc.returncode, proc.stdout or "", proc.stderr or ""
        timed_out = False
    except subprocess.TimeoutExpired as e:
        timed_out = True
        rc = -1
        out = _decode(e.stdout)
        err = _decode(e.stderr)
    except Exception as ex:  # noqa: BLE001 — фиксируем как EXC в отчёте
        return {"file": rel, "status": "EXC", "instrs": None, "secs": round(time.time() - start, 1),
                "rc": -1, "stdout": "", "stderr": str(ex), "timed_out": False}
    combined = out + err
    return {
        "file": rel,
        "status": classify(rc, combined, timed_out),
        "instrs": extract_instrs(combined),
        "secs": round(time.time() - start, 1),
        "rc": rc,
        "stdout": out,
        "stderr": err,
        "timed_out": timed_out,
    }


def apply_golden(result, golden_dir):
    """Если задан `golden_dir` и есть `<name>.txt`, успех дополнительно требует совпадения (A5 item 2)."""
    if not golden_dir:
        return result
    name = Path(result["file"]).stem
    gp = Path(golden_dir) / (name + ".txt")
    if not gp.exists():
        return result
    if result["status"] in PASS_STATUSES and not compare_golden(result["stdout"], gp.read_text(encoding="utf-8")):
        result = dict(result)
        result["status"] = "MISMATCH"
    return result

def main(argv=None):
    p = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--root", default=None, help="Корень проекта (по умолчанию от расположения скрипта).")
    p.add_argument("--dll", default=None, help="Путь к besm6.dll (по умолчанию под bin/Debug/net8.0).")
    p.add_argument("--limit", default=DEFAULT_LIMIT, help="Лимит инструкций на сценарий (по умолчанию 500000).")
    p.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT, help="Таймаут сценария в секундах.")
    p.add_argument("--output", default=None, help="Каталог отчётов (по умолчанию <root>/tests-run/examples).")
    p.add_argument("--suite", choices=["fast", "full"], default="full",
                   help="fast = name/algol/bemsh; full = все .dub (по умолчанию full).")
    p.add_argument("--only", default=None, help="Явный список имён без .dub через запятую; переопределяет --suite.")
    p.add_argument("--golden-dir", default=None, help="Каталог golden-выводов (по одному на сценарий).")
    args = p.parse_args(argv)

    root = Path(args.root).resolve() if args.root else default_root()
    dll = Path(args.dll).resolve() if args.dll else (root / DEFAULT_DLL_REL)
    out_dir = Path(args.output).resolve() if args.output else (root / "tests-run" / "examples")
    examples_dir = root / "examples"

    if not dll.exists():
        print(f"ОШИБКА: не найден {dll}.\n"
              f"Соберите сначала: dotnet build \"{root / SOLUTION_REL}\"", file=sys.stderr)
        return 3

    only = norm_only(args.only)
    if only is None and args.suite == "fast":
        only = frozenset(FAST_EXAMPLES)
    files = find_dub_files(examples_dir, only)
    if not files:
        print("ОШИБКА: не найдено ни одного .dub для прогона.", file=sys.stderr)
        return 3

    out_dir.mkdir(parents=True, exist_ok=True)
    scen_dir = out_dir / "scenarios"
    scen_dir.mkdir(parents=True, exist_ok=True)

    meta = {"root": str(root), "dll": str(dll), "limit": str(args.limit),
            "timeout": args.timeout, "suite": args.suite,
            "only": sorted(only) if only else None, "count": len(files)}
    results = []
    for i, f in enumerate(files, 1):
        name = Path(f).stem
        print(f"[{i}/{len(files)}] {os.path.relpath(f, root)} ... ", end="", flush=True)
        r = run_one(dll, f, root, args.limit, args.timeout)
        r = apply_golden(r, args.golden_dir)
        (scen_dir / (name + ".out")).write_text(r["stdout"], encoding="utf-8")
        (scen_dir / (name + ".err")).write_text(r["stderr"], encoding="utf-8")
        print(r["status"], f"(instrs={r['instrs'] or 'n/a'}, {r['secs']}s)")
        results.append(r)

    report = build_json_report(results, meta)
    json_path = out_dir / "examples_report.json"
    json_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    junit_path = out_dir / "junit-examples.xml"
    junit_path.write_text(build_junit_xml(results), encoding="utf-8")

    s = report["summary"]
    print(f"\n=== {s['passed']} passed / {s['failed']} failed of {s['total']} ===")
    print(f"JSON:    {json_path}")
    print(f"JUnit:   {junit_path}")
    print(f"Output:  {scen_dir}")
    return 0 if s["failed"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())