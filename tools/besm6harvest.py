#!/usr/bin/env python3
"""Сбор образов ОЗУ и трасс по примерам .dub.

Для каждого задания перебирает несколько моментов снятия снимка и выбирает
тот, где больше всего НЕНУЛЕВЫХ слов: монитор затирает собственный образ к
концу задания, поэтому единого «правильного» момента нет.

Рядом кладёт разбор ec_trace.log (BESM6_TRACE=1) — он даёт точную карту
«зона ленты → адрес ОЗУ» по всем обращениям E70.

Использование:
  python3 tools/besm6harvest.py --out /tmp/harvest examples/fortran.dub ...
  python3 tools/besm6harvest.py --out /tmp/harvest --suite
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import besm6kernel as K   # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DLL = os.path.join(REPO, "src/Besm6.Cli/bin/Release/net8.0/besm6.dll")

# Примеры, покрывающие разные компиляторы и подсистемы МОНСЯ.
SUITE = [
    "fortran.dub", "ftn.dub", "setftn.dub", "bemsh.dub", "algol.dub",
    "assem.dub", "madlen.dub", "refal.dub", "pascal.dub", "e-2.dub",
    "exfor.dub", "copy.dub", "name.dub", "newlib.dub", "dos.dub",
    "ocatalog.dub", "pcatalog.dub", "sovcatal.dub", "file.dub", "edit.dub",
    "move.dub", "juggle.dub", "introspective.dub", "libpunch.dub",
]

E70_RE = re.compile(
    r"\[E70\] m16=(?P<m16>[0-7]+) cw=(?P<cw>[0-9A-Fa-f]+) op=(?P<op>R|W)"
    r"(?P<seek>\(seek\))? unit=(?P<unit>[0-7]+) page=(?P<page>[0-7]+)"
    r" zone=(?P<zone>[0-7]+) tract=(?P<tract>[0-7]+) sect=(?P<sect>\d+)"
    r" par=(?P<par>[0-7]+) rawSect=(?P<rawSect>\d+) physIo=(?P<physIo>\d+)"
    r" sectIo=(?P<sectIo>\d+) -> (?P<medium>\S+)")


def parse_e70(path):
    """Обращения E70 из ec_trace.log."""
    out = []
    if not os.path.exists(path):
        return out
    for line in open(path, errors="replace"):
        m = E70_RE.search(line)
        if not m:
            continue
        d = {k: v for k, v in m.groupdict().items() if v is not None}
        d["cw"] = int(d["cw"], 16)
        out.append(d)
    return out


def zone_map(reads, mapped_drum=0o11):
    """Карта чтений: адрес ОЗУ <- (зона ленты, сектор, длина).

    По E70: memAddr = page<<10 (+ par<<8 при sectIo),
            diskZone = tract + (drum - mappedDrum)*32, длина 1024 или 256.
    """
    per_zone = []
    for d in reads:
        # physIo/seek приходят регуляркой как СТРОКИ — приводим к int,
        # иначе сравнение d["physIo"] != 1 всегда истинно и фильтр
        # отбрасывает все записи.
        if d["op"] != "R" or int(d["physIo"]) != 1:
            continue
        drum = int(d["unit"], 8) & 31
        tract = int(d["tract"], 8)
        page = int(d["page"], 8)
        par = int(d["par"], 8)
        sect = int(d["sect"])
        sect_io = d["sectIo"] == 1
        zone = tract + (drum - mapped_drum) * 32
        addr = page << 10
        if sect_io:
            addr += par << 8
        per_zone.append({"tape_zone": zone, "sector": sect, "mem": addr,
                         "words": 256 if sect_io else 1024,
                         "sector_io": sect_io, "drum": drum})
    return per_zone


def score(image, trace):
    w = K.load_words(image)
    steps = K.parse_trace(trace, w)
    ex = {s.k for s in steps}
    nz = [a for a in range(32768) if w[a]]
    return ({"nonzero": len(nz),
             "executed": len([a for a in nz if a in ex]),
             "steps": len(steps)}, w, steps)


def harvest(job, out, points, limit):
    name = os.path.splitext(os.path.basename(job))[0]
    best = None
    for n in points:
        img = os.path.join(out, "%s.%d.img" % (name, n))
        tr = os.path.join(out, "%s.%d.trace" % (name, n))
        env = dict(os.environ, BESM6_TRACE="1")
        cmd = ["dotnet", DLL, "run", job, "--limit", str(limit),
               "--dump-mem", img, "--dump-mem-at", str(n), "--trace-regs", tr]
        subprocess.run(cmd, cwd=REPO, env=env, capture_output=True, text=True)
        log = os.path.join(out, "%s.ec.log" % name)
        if os.path.exists("ec_trace.log"):
            # shutil.move, а не os.replace: /tmp и рабочее дерево могут быть
            # на разных файловых системах, и rename() падает с EXDEV.
            shutil.move("ec_trace.log", log)
        if not os.path.exists(img):
            continue
        sc, w, steps = score(img, tr)
        sc["point"] = n
        sc["image"] = img
        sc["trace"] = tr
        if best is None or sc["nonzero"] > best["nonzero"]:
            if best:
                for f in (best["image"], best["trace"]):
                    if os.path.exists(f):
                        os.remove(f)
            best = sc
        else:
            for f in (img, tr):
                if os.path.exists(f):
                    os.remove(f)
    if best:
        best["e70"] = parse_e70(os.path.join(out, "%s.ec.log" % name))
        best["zones"] = zone_map(best["e70"])
    return best


def main():
    ap = argparse.ArgumentParser(description="Сбор образов ОЗУ и трасс")
    ap.add_argument("jobs", nargs="*")
    ap.add_argument("--out", default="/tmp/harvest")
    ap.add_argument("--points", default="100000,300000,600000")
    ap.add_argument("--limit", type=int, default=3000000)
    ap.add_argument("--suite", action="store_true")
    args = ap.parse_args()

    jobs = args.jobs or [os.path.join("examples", j) for j in SUITE]
    os.makedirs(args.out, exist_ok=True)
    points = [int(x) for x in args.points.split(",")]

    summary = {}
    for job in jobs:
        path = job if os.path.exists(job) else os.path.join("examples", job)
        if not os.path.exists(path):
            print("нет такого примера: %s" % job)
            continue
        best = harvest(path, args.out, points, args.limit)
        if not best:
            print("%-28s НЕ СОБРАН" % os.path.basename(path))
            continue
        zones = best["zones"]
        summary[os.path.basename(path)] = {
            "point": best["point"], "nonzero": best["nonzero"],
            "executed": best["executed"], "steps": best["steps"],
            "image": best["image"], "trace": best["trace"],
            "zones_read": len(zones), "zones": zones[:400]}
        print("%-28s N=%-7d ненул=%-6d исп=%-6d шагов=%-8d зон E70=%d"
              % (os.path.basename(path), best["point"], best["nonzero"],
                 best["executed"], best["steps"], len(zones)))
    with open(os.path.join(args.out, "summary.json"), "w") as f:
        json.dump(summary, f, indent=1)
    print("\nсводка: %s" % os.path.join(args.out, "summary.json"))


if __name__ == "__main__":
    main()
