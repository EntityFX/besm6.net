#!/usr/bin/env python3
"""Высокоуровневый листинг ядра MONSYS: базовые блоки и псевдокод.

Граф управления берётся из трассы (см. besm6kernel), а не разбирается статически:
адресное поле команд модифицируется базой C, поэтому статически цели переходов
не восстанавливаются (docs/monsys-kernel.md, раздел 2).

Использование:
  python3 tools/besm6decomp.py <image> <trace> [--start 0o76000] [--end 0o77777]
  python3 tools/besm6decomp.py <image> <trace> --routine 0o77613
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import besm6tape as T       # noqa: E402
import besm6kernel as K     # noqa: E402

START, END = 0o76000, 0o77777

# ── Именование ────────────────────────────────────────────────────────────────
# Регистры с выявленной ролью. Остальные — локальные переменные подпрограммы.
REG_ROLE = {
    0:  "ptr",     # M[0] — признак/указатель байта, обнуляется vtm/vjm
    0o17: "sp",    # M[15] — стек
    0o16: "link",  # M[14] — регистр обмена, основной обратный адрес
    0o6:  "link2",  # M[6]  — обратный адрес второго уровня
    0o7:  "link3",  # M[7]  — обратный адрес третьего уровня
}
ACC = "A"


def reg_name(reg, scope):
    if reg in REG_ROLE:
        return REG_ROLE[reg]
    return scope.get(reg, "m%d" % reg)


class Block:
    """Базовый блок: последовательность полуслов до терминатора."""

    __slots__ = ("addr", "ins", "succ", "kind", "target", "count")

    def __init__(self, addr):
        self.addr = addr
        self.ins = []
        self.succ = []
        self.kind = "fall"   # fall | jmp | call | jz | jnz | loop | ret
        self.target = None
        self.count = 0


# ── Отображение мнемоник в операции ───────────────────────────────────────────
# Комментарий: справа — семантика по Opcode.cs / соответствующим исполнителям.
PSEUDO = {
    "atx": ("A ->", "{ea} = A"),
    "xta": ("A <-", "A = {ea}"),
    "stx": ("push", "push A"),
    "xts": ("pop", "A = pop()"),
    "ita": ("A ==", "A = {r}"),
    "ati": ("== A", "{r} = A"),
    "its": ("A ==", "A = {r}; push A"),
    "sti": ("== A", "{r} = A; pop"),
    "a+x": ("A +=", "A += {ea}"),
    "a-x": ("A -=", "A -= {ea}"),
    "x-a": ("A =", "A = {ea} - A"),
    "aex": ("A ^=", "A ^= {ea}"),
    "aax": ("A &=", "A &= {ea}"),
    "aox": ("A |=", "A |= {ea}"),
    "a*x": ("A *=", "A *= {ea}"),
    "a/x": ("A /=", "A /= {ea}"),
    "amx": ("A =", "A = |{ea}| - |A|"),
    "arx": ("A r+", "A = rotate({ea})"),
    "asn": ("A sh", "A >>= {ea}"),
    "asx": ("A sh", "A = shift({ea})"),
    "e+x": ("E +=", "E += {ea}"),
    "e-x": ("E -=", "E -= {ea}"),
    "e+n": ("E +=", "E += {ea}"),
    "e-n": ("E -=", "E -= {ea}"),
    "apx": ("A pk", "A = pack({ea})"),
    "aux": ("A up", "A = unpack({ea})"),
    "acx": ("popc", "A = popcount({ea})"),
    "anx": ("fnd", "A = findone({ea})"),
    "yta": ("A <-Y", "A = Y"),
    "mtj": ("M <-", "{r} = {r2}"),
    "j+m": ("M +=", "{r} += {r2}"),
    "utc": ("base =", "C = {ea}          ; УТА: база на 1 команду"),
    "wtc": ("base =", "C = M[{ea}]       ; МОД: база из памяти"),
    "vtm": ("M =", "{r} = {ea}"),
    "utm": ("M +=", "{r} += {ea}"),
    "rte": ("E ->", "E = R"),
    "ntr": ("R =", "R = {ea}"),
    "xtr": ("R =", "R = {ea}"),
    "mod": ("R =", "R = {ea}          ; РЕГ"),
    "stop": ("halt", "stop()"),
}
for _c, _n in K.EXTRACODES.items():
    PSEUDO[_c] = ("E%s" % _n, "extracode %s" % _n)


def emit(step, scope):
    """Одна команда -> строка псевдокода."""
    name = step.name
    ea = K.eff_addr(step)
    _, _, reg, _ = T.decode_half(step.instr)
    rn = reg_name(reg, scope)
    raw = T.disasm_half(step.instr)
    if name in ("uj", "vjm", "vzm", "v1m", "vlm", "uza", "u1a"):
        return raw, "управление"
    if name not in PSEUDO:
        return raw, "?"
    _, tmpl = PSEUDO[name]
    return raw, tmpl.format(ea="0o%o" % ea, r=rn, r2=rn)


def classify_return(step, scope):
    """Возврат ли это: uj с пустым адресным полем через регистр возврата."""
    if step.name != "uj" or T.decode_half(step.instr)[1] != 0:
        return None
    reg = T.decode_half(step.instr)[2]
    if reg in REG_ROLE and REG_ROLE[reg].startswith("link"):
        return "return %s" % REG_ROLE[reg]
    return None


def find_loops(blocks, by_key, reach=None):
    """Обратные рёбра = циклы. При заданном reach — только внутри него.

    reach — множество адресов, достижимых из тела подпрограммы. Без него циклы
    ищутся по всему ядру, и тогда «хвост» (например 0o77273, куда возвращается
    диспетчер) ошибочно помечается как цикл внутри чужой подпрограммы.
    """
    loops = []
    for b in blocks:
        for s in b.succ:
            if s > b.addr or (s, False) not in by_key:
                continue
            if reach is not None and (b.addr not in reach or s not in reach):
                continue
            loops.append((b.addr, s))
    return loops


def reachable(by_key, entry):
    """Адреса, достижимые из точки входа по наблюдаемым переходам."""
    seen, stack = set(), [entry]
    while stack:
        a = stack.pop()
        if a in seen or (a, False) not in by_key:
            continue
        seen.add(a)
        stack.extend(by_key[(a, False)].succ)
    return seen


def decompile(words, steps, start, end, routine, title):
    """Структурированный псевдокод одной подпрограммы."""
    blocks, by_key = build_blocks(steps, words, start, end)
    reach = reachable(by_key, routine)
    loops = find_loops(blocks, by_key, reach)
    loop_tails = collections.defaultdict(list)
    for src, dst in loops:
        loop_tails[dst].append(src)
    out = []
    out.append("; %s   (адрес 0o%o)" % (title, routine))
    scope = {}
    seen = set()

    def walk(addr, depth, note=""):
        pad = "    " * depth
        block = by_key.get((addr, False))
        if block is None:
            out.append("%s; 0o%o: нет исполненного кода (данные/холодная ветвь)" % (pad, addr))
            return
        if addr in seen or depth > 6:
            out.append("%s; ... (0o%o) уже показан" % (pad, addr))
            return
        seen.add(addr)
        looped = addr in loop_tails
        if looped:
            out.append("%sdo {   /* цикл: 0o%o */" % (pad, addr))
            depth += 1
            pad = "    " * depth
        for i, s in enumerate(block.ins):
            last = (i == len(block.ins) - 1)
            raw, pseudo = emit(s, scope)
            ret = classify_return(s, scope)
            if ret:
                pseudo = ret
            mark = "  *" if (last and block.kind != "fall") else ""
            out.append("%s0o%05o %-18s %s%s" % (pad, s.k, raw, pseudo, mark))
            if ret:
                if looped:
                    out.append("%s} while (...);" % pad)
                return
        nxt = block.succ[0] if block.succ else None
        if block.kind == "loop" and nxt is not None:
            out.append("%s} while (0o%o);   /* ЦИКЛ vlm */" % (pad, nxt))
            return
        if block.kind in ("jz", "jnz") and nxt is not None:
            cond = "== 0" if block.kind == "jz" else "!= 0"
            out.append("%sif (%s) goto 0o%o;" % (pad, cond, nxt))
            walk(nxt, depth)
            if looped:
                out.append("%s}" % pad)
            return
        if block.kind == "call" and nxt is not None:
            out.append("%s/* CALL 0o%o, возврат в 0o%o */" % (pad, block.target or 0, nxt))
        if nxt is not None:
            walk(nxt, depth)
        elif block.kind == "fall":
            out.append("%s/* продолжение по 0o%o — вне ядра или холодный код */"
                       % (pad, addr + 1))
        if looped and block.kind in ("jmp",):
            out.append("%s}" % pad)
        return

    walk(routine, 0)
    return out


def main():
    ap = argparse.ArgumentParser(description="Высокоуровневый листинг ядра MONSYS")
    ap.add_argument("image")
    ap.add_argument("trace")
    ap.add_argument("--start", type=lambda x: int(x, 8), default=START)
    ap.add_argument("--end", type=lambda x: int(x, 8), default=END)
    ap.add_argument("--routine", type=lambda x: int(x, 8))
    ap.add_argument("--all", action="store_true", help="все найденные подпрограммы")
    args = ap.parse_args()

    words = K.load_words(args.image)
    steps = K.parse_trace(args.trace, words)
    if args.routine is not None:
        for line in decompile(words, steps, args.start, args.end,
                              args.routine, "подпрограмма"):
            print(line)
        return

    # Подпрограммы: цели vjm + точки входа извне.
    targets = collections.Counter()
    for s in steps:
        if args.start <= s.k < args.end and s.name == "vjm" and s.next_k:
            if args.start <= s.next_k < args.end:
                targets[s.next_k] += 1
    entries = [s.k for i, s in enumerate(steps)
               if args.start <= s.k < args.end
               and (i == 0 or not (args.start <= steps[i - 1].k < args.end))]
    print("=" * 72)
    print("ВЫСОКОУРОВНЕВЫЙ ЛИСТИНГ ЯДРА MONSYS  0o%o–0o%o" % (args.start, args.end))
    print("=" * 72)
    for e in sorted(set(entries)):
        print()
        for line in decompile(words, steps, args.start, args.end, e,
                              "ТОЧКА ВХОДА (управление приходит снаружи)"):
            print(line)
    for tgt, n in targets.most_common():
        print()
        for line in decompile(words, steps, args.start, args.end, tgt,
                              "ПОДПРОГРАММА, вызовов: %d" % n):
            print(line)
    if args.all:
        print()
        print(";" + "-" * 70)


def build_blocks(steps, words, start, end):
    """Разбить код на базовые блоки по АДРЕСАМ, а не по порядку трассы.

    Блок начинается с (K, half); тело — непрерывная последовательность полуслов
    до терминатора (команды, передающей управление). Правое полуслово слова
    включается только если оно реально исполнялось: если левая команда передала
    управление, правое — мёртвое.

    Возвращает (список блоков, словарь (K, half) -> блок). Поле succ у блока —
    адреса, куда управление уходило НАБЛЮДЁННЫМ ОБРАЗОМ (в трассе может быть
    несколько целей, если цель зависит от значения регистра).
    """
    executed = [s for s in steps if start <= s.k < end]
    if not executed:
        return [], {}

    hit = collections.Counter((s.k, s.half) for s in executed)
    first = {}
    obs = collections.defaultdict(set)
    for s in executed:
        first.setdefault((s.k, s.half), s)
        if s.next_k is not None:
            obs[(s.k, s.half)].add(s.next_k)

    blocks, by_key = [], {}
    for k in sorted({s.k for s in executed}):
        body = [(k, False)]
        left = T.decode_half(K.fetch(words, k, False))[0]
        if left not in K.BRANCH and hit.get((k, True)):
            body.append((k, True))
        b = Block(k)
        b.ins = [first[x] for x in body if x in first]
        b.count = sum(hit[x] for x in body)
        last = b.ins[-1] if b.ins else None
        if last is not None and last.is_branch:
            b.kind = K.BRANCH[last.name]
            b.target = K.branch_target(last)
            b.succ = sorted(obs.get((last.k, last.half), set()))
        else:
            b.kind = "fall"
            b.succ = [k + 1]
        blocks.append(b)
        by_key[(k, False)] = b
    return blocks, by_key


if __name__ == "__main__":
    main()
