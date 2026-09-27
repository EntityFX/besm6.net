#!/usr/bin/env python3
"""
Дизассемблер и анализатор лент БЭСМ-6 (SIMH-образ: 6 байт на 48-битное слово).

Использование:
  python3 tools/besm6tape.py info   tapes/monsys.9
  python3 tools/besm6tape.py dis    tapes/monsys.9 [--start N] [--count N] [--bemsh]
  python3 tools/besm6tape.py text   tapes/b.7 [--start N] [--count N] [--mode gost|ascii|cosy]
  python3 tools/besm6tape.py strings tapes/monsys.9 [--min N]
  python3 tools/besm6tape.py zones   tapes/monsys.9

Таблицы мнемоник и кодирование полуслов соответствуют
src/Besm6.Assembler/OpcodeTable.cs и src/Besm6.Architecture/InstructionCodec.cs.
"""
import argparse
import os
import re
import sys

# ── Мнемоники (MADLEN), короткие 000–077 ────────────────────────────────────
SHORT = [
    "atx", "stx", "mod", "xts", "a+x", "a-x", "x-a", "amx",
    "xta", "aax", "aex", "arx", "avx", "aox", "a/x", "a*x",
    "apx", "aux", "acx", "anx", "e+x", "e-x", "asx", "xtr",
    "rte", "yta", "ext", "*33", "e+n", "e-n", "asn", "ntr",
    "ati", "sti", "ita", "its", "mtj", "j+m", "*46", "*47",
    "*50", "*51", "*52", "*53", "*54", "*55", "*56", "*57",
    "*60", "*61", "*62", "*63", "*64", "*65", "*66", "*67",
    "*70", "*71", "*72", "*73", "*74", "*75", "*76", "*77",
]
# Мнемоники (БЭМШ), короткие
SHORT_BEMSH = [
    "зп", "зпм", "рег", "счм", "сл", "вч", "вчоб", "вчаб",
    "сч", "и", "нтж", "слц", "знак", "или", "дел", "умн",
    "сбр", "рзб", "чед", "нед", "слп", "вчп", "сд", "рж",
    "счрж", "счмр", "зпп", "счп", "слпа", "вчпа", "сда", "ржа",
    "уи", "уим", "счи", "счим", "уии", "сли", "соп", "э47",
    "э50", "э51", "э52", "э53", "э54", "э55", "э56", "э57",
    "э60", "э61", "э62", "э63", "э64", "э65", "э66", "э67",
    "э70", "э71", "э72", "э73", "э74", "э75", "э76", "э77",
]
# Длинные 0200–0370
LONG = [
    "*20", "*21", "utc", "wtc", "vtm", "utm", "uza", "u1a",
    "uj", "vjm", "ij", "stop", "vzm", "v1m", "*36", "vlm",
]
LONG_BEMSH = [
    "э20", "э21", "мода", "мод", "уиа", "слиа", "по", "пе",
    "пб", "пв", "выпр", "стоп", "пио", "пино", "втбрз", "цикл",
]

# ── GOST-10859, позиция -> символ ────────────────────────────────────────────
_GOST = {
    0: "0", 1: "1", 2: "2", 3: "3", 4: "4", 5: "5", 6: "6", 7: "7",
    8: "8", 9: "9", 10: "+", 11: "-", 12: "/", 13: ",", 14: ".",
    15: " ", 16: "е", 17: "@", 18: "(", 19: ")",
    32: "*", 33: "'", 34: "]", 35: "↑", 36: "?", 37: ":", 38: "↑",
    40: "А", 41: "Б", 42: "В", 43: "Г", 44: "Д", 45: "Е", 46: "Ж",
    47: "З", 50: "И", 51: "Й", 52: "К", 53: "Л", 54: "М", 55: "Н",
    56: "О", 57: "П", 60: "Р", 61: "С", 62: "Т", 63: "У",
    64: "Ф", 65: "Х", 66: "Ц", 67: "Ч", 70: "Ш", 71: "Щ", 72: "Ъ",
    73: "Ы", 74: "Ь", 75: "Э", 76: "Ю", 77: "Я",
}


def load_words(path):
    """Читает SIMH-образ: 6 байт на слово, big-endian."""
    data = open(path, "rb").read()
    if len(data) % 6:
        raise SystemExit("размер не кратен 6: %d байт" % len(data))
    n = len(data) // 6
    return [int.from_bytes(data[i * 6:(i + 1) * 6], "big") for i in range(n)]


def octal(v, width=0):
    s = format(v, "o")
    return s.rjust(width, "0") if width else s


def decode_half(h, bemsh=False):
    """Декодирует 24-битное полуслово: (мнемоника, число знаков, текст)."""
    h &= 0xFFFFFF
    reg = (h >> 20) & 0xF
    is_long = (h >> 19) & 1
    if is_long:
        op = (h >> 12) & 0xF8
        addr = h & 0x7FFF
        name = (LONG_BEMSH if bemsh else LONG)[(op >> 3) & 0xF]
    else:
        op = (h >> 12) & 0x3F
        addr = h & 0xFFF
        if (h >> 18) & 1:
            addr |= 0x7000
        name = (SHORT_BEMSH if bemsh else SHORT)[op]
    return name, addr, reg, is_long


def disasm_half(h, bemsh=False):
    name, addr, reg, _ = decode_half(h, bemsh)
    s = name
    if addr != 0:
        if addr >= 0x7FC0:
            s += " -%s" % octal((addr ^ 0x7FFF) + 1)
        else:
            s += " %s" % octal(addr)
    if reg != 0:
        if addr == 0:
            s += " "
        s += "(%s)" % octal(reg)
    return s


def disasm_word(w, bemsh=False):
    left = disasm_half((w >> 24) & 0xFFFFFF, bemsh)
    if (w & 0xFFFFFF) == 0:
        return left
    return left + "," + disasm_half(w & 0xFFFFFF, bemsh)


# ── Текст ───────────────────────────────────────────────────────────────────
def gost_line(word, width=6):
    """GOST-10859: 6 символов на полуслово (6 бит каждый, 24 бита)."""
    out = []
    for shift in range(23, -1, -6):
        code = (word >> shift) & 0x3F
        out.append(_GOST.get(code, "·"))
    return "".join(out)


def gost_word(w, groups=4):
    """8 символов на слово: 4 группы по 6 бит из каждого полуслова."""
    hi = (w >> 24) & 0xFFFFFF
    lo = w & 0xFFFFFF
    return gost_line(hi) + gost_line(lo)


def ascii_line(word):
    """8-битный ASCII в старших байтах (как в B/FORTRAN)."""
    b = word.to_bytes(6, "big")
    return "".join(chr(x) if 32 <= x < 127 else "·" for x in b)


# ── Команды ─────────────────────────────────────────────────────────────────
def cmd_info(args):
    w = load_words(args.tape)
    print("файл:      %s" % args.tape)
    print("размер:    %d байт" % os.path.getsize(args.tape))
    print("слов:      %d" % len(w))
    print("зон(1024): %d" % (len(w) // 1024))
    nz = [i for i in range(0, len(w), 1024) if any(w[i:i + 1024])]
    print("непустых:  %d, из них первая: %d" % (len(nz), nz[0] if nz else -1))
    if w:
        print("слово 0:   %012o" % w[0])
        print("дизасм:    %s" % disasm_word(w[0]))


def cmd_dis(args):
    w = load_words(args.tape)
    beg = args.start
    end = min(beg + args.count, len(w))
    for i in range(beg, end):
        print("%06o  %012o  %s" % (i, w[i], disasm_word(w[i], args.bemsh)))


def cmd_text(args):
    w = load_words(args.tape)
    beg = args.start
    end = min(beg + args.count, len(w))
    for i in range(beg, end):
        if args.mode == "ascii":
            s = ascii_line(w[i])
        elif args.mode == "gost8":
            s = gost_word(w[i])
        else:
            s = gost_line((w[i] >> 24) & 0xFFFFFF) + gost_line(w[i] & 0xFFFFFF)
        print("%06o  %012o  |%s|" % (i, w[i], s))


def cmd_strings(args):
    """Ищет подряд идущие печатаемые ASCII-слова."""
    w = load_words(args.tape)
    run = []
    runs = []
    for i, x in enumerate(w):
        b = x.to_bytes(6, "big")
        s = "".join(chr(c) if 32 <= c < 127 else "·" for c in b)
        if s.count("·") == 0:
            run.append((i, s))
        else:
            if len(run) >= args.min:
                runs.append(run)
            run = []
    if len(run) >= args.min:
        runs.append(run)
    for r in runs:
        text = "".join(s for _, s in r)
        print("%06o-%06o  %s" % (r[0][0], r[-1][0], text))


def cmd_zones(args):
    w = load_words(args.tape)
    for z in range(0, len(w) // 1024):
        blk = w[z * 1024:(z + 1) * 1024]
        if not any(blk):
            continue
        nz = sum(1 for x in blk if x)
        first = octal(blk[0]) if blk[0] else "-"
        print("зона %4d  непустых %4d  первое %12s  %s" % (
            z, nz, first, disasm_word(blk[0]) if blk[0] else ""))


def main():
    ap = argparse.ArgumentParser(description="Дизассемблер лент БЭСМ-6")
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("info", "dis", "text", "strings", "zones"):
        p = sub.add_parser(name)
        p.add_argument("tape")
        p.add_argument("--start", type=lambda x: int(x, 8), default=0)
        p.add_argument("--count", type=int, default=64)
        p.add_argument("--min", type=int, default=4)
        p.add_argument("--bemsh", action="store_true")
        p.add_argument("--mode", default="gost", choices=("gost", "gost8", "ascii"))
    args = ap.parse_args()
    {"info": cmd_info, "dis": cmd_dis, "text": cmd_text,
     "strings": cmd_strings, "zones": cmd_zones}[args.cmd](args)


if __name__ == "__main__":
    main()
