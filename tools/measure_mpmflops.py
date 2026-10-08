#!/usr/bin/env python3
"""Сбор замеров mpmflops для обоих портов (Фортран-ГДР и Паскаль БЭСМ-6).

Запускает все четыре ядра (kpart=0 — baseline, 1/2/3) и собирает
модельные такты/инструкции. Подстановка выполняется в Python (не sed),
чтобы параметры гарантированно различались между прогонами.
"""
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[1])
DLL = os.path.join(ROOT, "src/Besm6.Cli/bin/Release/net8.0/besm6.dll")
OUT = os.environ.get("BESM6_MFBENCH_OUTPUT", os.path.join(ROOT, "tests-run", "mfbench"))
# Лимит инструкций на прогон. Для ps kpart=3 требуется ~72.8 млн инструкций
# (самый тяжёлый вариант), поэтому 40 млн НЕДОСТАТОЧНО: прогон обрывается по
# лимиту, не печатает результат и молча портит замер. См. Н-14.
LIMIT = "80000000"

FTN_SRC = os.path.join(ROOT, "examples/mpmflops.dub")
PS_SRC = os.path.join(ROOT, "examples/mpmflops-pascal.dub")

# opwd -> операций на слово, как в оригинале mpmflops.c
OPWD = {1: 2, 2: 8, 3: 32}


def patch_ftn(text, kpart):
    t = text.replace("data kpart/1/", "data kpart/%d/" % kpart)
    assert ("data kpart/%d/" % kpart) in t, "FTL: подстановка kpart не сработала"
    return t


def patch_ps(text, kpart):
    t = text.replace("    kpart = 1;", "    kpart = %d;" % kpart)
    assert ("    kpart = %d;" % kpart) in t, "PS: подстановка kpart не сработала"
    return t


def run(name, src_path, kpart, patcher):
    with open(src_path, encoding="utf-8") as fh:
        text = fh.read()
    job = os.path.join(OUT, "%s.dub" % name)
    with open(job, "w", encoding="utf-8") as fh:
        fh.write(patcher(text, kpart))

    proc = subprocess.run(
        ["dotnet", DLL, "run", job, "--profile", "--limit", LIMIT, "--no-wall-clock", "--no-hang-detect"],
        capture_output=True, text=True, timeout=3600)
    out = proc.stdout + proc.stderr
    with open(os.path.join(OUT, "%s.out" % name), "w", encoding="utf-8") as fh:
        fh.write(out)

    instr = re.search(r"Инструкций:\s*(\d+)", out)
    cycles = re.search(r"Тактов \(модель\):\s*(\d+)", out)
    cpi = re.search(r"CPI модели:\s*([0-9.]+)", out)
    ncalc = re.search(r"REPEAT PASSES=\s*(\d+)", out, re.I)
    flops = re.search(r"FLOPS=\s*([0-9.+-]+)", out)
    # Самопроверка: смотрим ТОЛЬКО вывод программы (после 2-го маркера ≠),
    # иначе в неё попадает эхо FORMAT-строк из листинга компилятора.
    parts = out.split("≠")
    prog = parts[-1] if len(parts) >= 2 else out
    ok = ("CALCULATIONS PERFORMED" in prog.upper()
          and "ALL SAME: YES" in prog.upper())
    halted = re.search(r"Halted by STOP at \S+ after (\d+) instructions", out)
    # Усечение по лимиту: программа не дошла до STOP, результат не напечатан.
    # Такой прогон нельзя использовать в замерах (см. Н-14).
    truncated = "Instruction limit" in out or "did not terminate" in out
    return {
        "name": name,
        "kpart": kpart,
        "instructions": int(instr.group(1)) if instr else None,
        "cycles": int(cycles.group(1)) if cycles else None,
        "cpi": float(cpi.group(1)) if cpi else None,
        "rpts": int(ncalc.group(1)) if ncalc else None,
        "flops_raw": flops.group(1) if flops else None,
        "halted": int(halted.group(1)) if halted else None,
        "selfcheck_ok": ok,
        "truncated": truncated,
        "rc": proc.returncode,
    }


def main():
    os.makedirs(OUT, exist_ok=True)
    results = []
    for lang, src, patcher in (("ftn", FTN_SRC, patch_ftn), ("ps", PS_SRC, patch_ps)):
        for kpart in (0, 1, 2, 3):
            name = "%s_%d" % (lang, kpart)
            print("RUN %s ..." % name, flush=True)
            r = run(name, src, kpart, patcher)
            print("  -> %s" % r, flush=True)
            results.append(r)
            with open(os.path.join(OUT, "results.json"), "w", encoding="utf-8") as fh:
                json_dump(results, fh)
    with open(os.path.join(OUT, "done.txt"), "w") as fh:
        fh.write("done\n")
    print("ALL DONE", flush=True)


def json_dump(rows, fh):
    import json
    fh.write(json.dumps(rows, indent=2, ensure_ascii=False))
    fh.write("\n")


if __name__ == "__main__":
    sys.exit(main())
