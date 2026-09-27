#!/usr/bin/env python3
"""
Реконструкция образа памяти БЭСМ-6 после загрузки ОС по трассе экстракодов,
затем дизассемблирование восстановленного кода.

  besm6 run examples/copy.dub --limit 20000000      # с BESM6_TRACE=1 пишет ec_trace.log
  python3 tools/besm6mem.py build  ec_trace.log tapes/monsys.9
  python3 tools/besm6mem.py dis    ec_trace.log tapes/monsys.9 --start 0o76000 --count 80
  python3 tools/besm6mem.py map    ec_trace.log

Формат E70 (совпадает с выводом BESM6_TRACE):
  [E70] m16=03002 cw=... op=R unit=021 page=00 zone=0201 tract=01 sect=2 par=00
        rawSect=0 physIo=1 sectIo=1 -> PHYSIO(drum=011,mapped=011)

Адресация: memAddr = (page<<10) + (sectIo ? paragraph<<8 : 0),
            tape offset = 1024*diskZone + 256*sector, где
            diskZone = tract + (drum - mappedDrum)*32.
"""
import argparse
import re
import sys
import importlib.util

spec = importlib.util.spec_from_file_location("bt", "tools/besm6tape.py")
bt = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bt)

RE_E70 = re.compile(
    r'\[E70\].*?op=(?P<op>R|W).*?page=(?P<page>\S+)\s+zone=(?P<zone>\S+)\s+'
    r'tract=(?P<tract>\S+)\s+sect=(?P<sect>\d+)\s+par=(?P<par>\S+)\s+'
    r'rawSect=(?P<raw>\d+)\s+physIo=(?P<phys>\d+)\s+sectIo=(?P<sectio>\d+)'
    r'.*?mapped=(?P<mapped>\S+)\)')


def o(s):
    return int(s, 8)


def parse(trace_path):
    """Возвращает список (op, memAddr, tapeOff, nwords, unit) для чтений с ленты."""
    out = []
    pat_disk = re.compile(r'\[E70\].*?op=(?P<op>R|W).*?unit=(?P<unit>\S+)\s+page=(?P<page>\S+)')
    pat = re.compile(
        r'\[E70\].*?op=(?P<op>[RW]).*?unit=(?P<unit>\S+)\s+page=(?P<page>\S+)\s+'
        r'zone=(?P<zone>\S+)\s+tract=(?P<tract>\S+)\s+sect=(?P<sect>\d+)\s+'
        r'par=(?P<par>\S+)\s+rawSect=(?P<raw>\d+)\s+physIo=(?P<phys>\d+)\s+'
        r'sectIo=(?P<sectio>\d+)')
    mapped = int(re.search(r'mapped=(0[0-7A-Fa-f]+)',
                           open(trace_path, encoding='utf-8', errors='replace').read()).group(1), 8) \
        if re.search(r'mapped=', open(trace_path, encoding='utf-8', errors='replace').read()) else 0
    for line in open(trace_path, encoding='utf-8', errors='replace'):
        m = pat.search(line)
        if not m:
            continue
        g = m.group
        if g('phys') != '1' or g('op') != 'R':
            continue                      # интересует только чтение «с ленты» (physIo)
        drum = o(g('unit')) & 31
        tract = o(g('tract'))
        zone = tract + (drum - mapped) * 32
        sect = o(g('sect')) if g('sectio') == '1' else 0
        mem = (o(g('page')) << 10) + (o(g('par')) << 8 if g('sectio') == '1' else 0)
        nw = 256 if g('sectio') == '1' else 1024
        off = 1024 * zone + 256 * sect
        out.append((mem, off, nw, drum, zone))
    return out


def build(trace, tape):
    words = bt.load_words(tape)
    # Первое чтение побеждает: при загрузке монитора страница памяти
    # заполняется один раз, последующие обращения к той же странице
    # относятся к уже загруженной ОС и не переопределяют исходный образ.
    mem = {}
    for m, off, nw, drum, zone in parse(trace):
        for i in range(nw):
            a = m + i
            if a in mem:
                continue
            if off + i < len(words):
                mem[a] = words[off + i]
    return mem


def cmd_map(args):
    ops = parse(args.trace)
    print("операций чтения с ленты: %d" % len(ops))
    print("%-8s %-9s %-7s %-7s %-5s" % ("mem", "tapeOff", "n", "drum", "zone"))
    for m, off, nw, drum, zone in ops[:args.count]:
        print("%08o %09o %7d %07o %05o" % (m, off, nw, drum, zone))


def cmd_build(args):
    mem = build(args.trace, args.tape)
    addrs = sorted(mem)
    if not addrs:
        print("ничего не восстановлено")
        return
    print("восстановлено слов: %d" % len(mem))
    print("диапазон: %06o .. %06o" % (addrs[0], addrs[-1]))
    # плотные участки
    runs = []
    s = p = addrs[0]
    for a in addrs[1:]:
        if a == p + 1:
            p = a
        else:
            runs.append((s, p))
            s = p = a
    runs.append((s, p))
    print("участков: %d" % len(runs))
    for s, e in runs[:40]:
        print("  %06o .. %06o  (%d слов)" % (s, e, e - s + 1))


def cmd_dis(args):
    mem = build(args.trace, args.tape)
    beg = args.start
    for a in range(beg, beg + args.count):
        w = mem.get(a)
        if w is None:
            print("%06o  --------  (нет данных)" % a)
        else:
            print("%06o  %012o  %s" % (a, w, bt.disasm_word(w, args.bemsh)))


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for n in ("map", "build", "dis"):
        p = sub.add_parser(n)
        p.add_argument("trace")
        if n != "map":
            p.add_argument("tape")
        p.add_argument("--count", type=int, default=64)
        p.add_argument("--start", type=lambda x: int(x, 8), default=0)
        p.add_argument("--bemsh", action="store_true")
    a = ap.parse_args()
    {"map": cmd_map, "build": cmd_build, "dis": cmd_dis}[a.cmd](a)


if __name__ == "__main__":
    main()
