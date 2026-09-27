#!/usr/bin/env python3
"""Сводная карта кода ОС и компиляторов по собранным образам.

Читает summary.json сборщика (tools/besm6harvest.py) и для каждого примера
раскладывает ОЗУ на участки, определяя для каждого:
  * исполнялся ли он (по трассе);
  * из какой зоны какой ленты он загружен (по карте E70);
  * ядро это или подсистема (ядро = 0o76000–0o77777).

Использование:
  python3 tools/besm6corpus.py /tmp/harvest [--tapes tapes] [--json out.json]
"""
import argparse
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import besm6kernel as K   # noqa: E402
import besm6harvest as H  # noqa: E402

KERNEL = (0o76000, 0o77777)
TAPES = [("monsys.9", 0o11), ("b.7", 0o7), ("bemsh.739", None),
         ("librar.12", None), ("librar.37", None)]
TAPE_BY_ID = {}


def load_tapes(tdir):
    """Ленты -> {volume_id: имя}; id берём из констант TapeImage."""
    ids = {"monsys.9": K.__dict__.get("_", None)}
    return TAPE_BY_ID


def regions(words, steps, zones, lo=0, hi=32768):
    """Участки непустых слов с разметкой: исполнялся / из какой зоны."""
    ex = {s.k for s in steps}
    nz = [a for a in range(lo, hi) if words[a]]
    if not nz:
        return []
    # зона ленты -> список адресов ОЗУ
    zmap = collections.defaultdict(list)
    for z in zones:
        for a in range(z["mem"], min(z["mem"] + z["words"], hi)):
            zmap[a].append(z)
    out, cur = [], [nz[0]]
    for a in nz[1:]:
        if a == cur[-1] + 1:
            cur.append(a)
        else:
            out.append(cur)
            cur = [a]
    out.append(cur)
    rows = []
    for r in out:
        a0, a1 = r[0], r[-1] + 1
        exec_n = len([a for a in r if a in ex])
        zs = collections.Counter()
        for a in r:
            for z in zmap.get(a, ()):
                zs[z["tape_zone"]] += 1
        kind = "ядро" if a1 > KERNEL[0] and a0 <= KERNEL[1] else "подсистема"
        rows.append({"start": a0, "end": a1, "words": a1 - a0,
                     "executed": exec_n, "kind": kind,
                     "zones": sorted(zs)[:6]})
    return rows


def main():
    ap = argparse.ArgumentParser(description="Сводная карта кода ОС")
    ap.add_argument("harvest")
    ap.add_argument("--json", default=None)
    ap.add_argument("--min-words", type=int, default=8)
    args = ap.parse_args()

    path = os.path.join(args.harvest, "summary.json")
    with open(path) as f:
        summary = json.load(f)

    report = {}
    print("%-26s %7s %7s %7s  %s" % ("пример", "ненул", "исполн", "участк", "крупнейшие участки (адрес: слов/исполн)"))
    print("-" * 100)
    for name in sorted(summary):
        s = summary[name]
        if not os.path.exists(s["image"]) or not os.path.exists(s["trace"]):
            continue
        words = K.load_words(s["image"])
        steps = K.parse_trace(s["trace"], words)
        rows = regions(words, steps, s.get("zones") or [])
        big = sorted(rows, key=lambda r: -r["words"])[:4]
        desc = "  ".join("0o%05o:%d/%d" % (r["start"], r["words"], r["executed"])
                         for r in big)
        print("%-26s %7d %7d %7d  %s" % (name, s["nonzero"], s["executed"],
                                         len(rows), desc))
        report[name] = {"point": s["point"], "nonzero": s["nonzero"],
                        "executed": s["executed"],
                        "zones_read": s.get("zones_read", 0),
                        "regions": rows}
    if args.json:
        with open(args.json, "w") as f:
            json.dump(report, f, indent=1)
        print("\nподробно: %s" % args.json)


if __name__ == "__main__":
    main()

