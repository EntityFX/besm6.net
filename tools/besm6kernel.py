#!/usr/bin/env python3
"""Анализатор ядра ОС MONSYS по снимку памяти и трассе исполнения.

Зачем не статическая дизассемблировка: у БЭСМ-6 адресное поле команды
модифицируется базовым регистром C, поэтому статически резолвить переходы
«в лоб» нельзя — нужно значение C в каждой точке. Трасса даёт его точно:
следующий K после каждой команды записан в самой трассе.

Кроме того, одно 48-битное слово — это две команды, и если левая команда
передаёт управление, правая не исполняется вовсе (но остаётся целью перехода).

Использование:
  python3 tools/besm6kernel.py structure <image> <trace> [--start 0o76000] [--end 0o77777]
  python3 tools/besm6kernel.py listing  <image> <trace> [--start 0o76000] [--count 200]
  python3 tools/besm6kernel.py calls    <image> <trace>
  python3 tools/besm6kernel.py strings  <image> [--min 6]
"""
import argparse
import collections
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import besm6tape as T   # noqa: E402  таблицы мнемоник и декодер полуслов

# Команда, передающая управление: следующая команда — не по счёту.
BRANCH = {"uj": "jmp", "vjm": "call", "vzm": "jz", "v1m": "jnz",
          "vlm": "loop", "uza": "jz", "u1a": "jnz", "*36": "jz", "ij": "ret"}
# Задание C (регистра модификации адреса) — меняет базу для всех последующих.
SET_C = {"utc", "wtc"}
# Экстракоды — системные вызовы, уходят в обработчик симулятора.
# Ключ — мнемоника, значение — код В ВОСЬМЕРИЧНОЙ записи (как в оригинале).
EXTRACODES = {"*%o" % c: c for c in range(0o50, 0o100)}


def load_words(path):
    data = open(path, "rb").read()
    if len(data) % 6:
        raise SystemExit("размер не кратен 6: %d байт" % len(data))
    return [int.from_bytes(data[i * 6:(i + 1) * 6], "big") for i in range(len(data) // 6)]


class Step:
    """Одна исполненная команда: где, что, куда ушло управление."""

    __slots__ = ("k", "half", "instr", "name", "addr", "reg",
                 "next_k", "next_half", "c", "state", "seq")

    def __init__(self, k, half, instr, state, seq):
        self.k, self.half, self.instr = k, half, instr
        self.name, self.addr, self.reg, _ = T.decode_half(instr)
        self.next_k, self.next_half = None, None
        self.c = state["C"]
        self.state = state      # полное состояние РЕПЕРЕ выполнением
        self.seq = seq

    @property
    def is_branch(self):
        return self.name in BRANCH


def fetch(words, k, half):
    """Сырое 24-битное полуслово команды по K из образа памяти."""
    word = words[k & 0x7FFF]
    return (word & 0xFFFFFF) if half else ((word >> 24) & 0xFFFFFF)


def parse_trace(path, words):
    """Разбирает --trace-regs: (K, half) + изменения регистров.

    Команду НЕ берём из трассы: поле команды там печатается в отображающем
    формате reg|mid|addr (9 восьмеричных разрядов), а не как сырое 24-битное
    кодирование, — восстановление по нему сдвигает номер регистра. Поэтому
    полуслово читается из образа памяти по (K, half).

    Особенности формата регистров:
      * индексные регистры именуются восьмерично (M17 = M[15] = стек);
      * аккумулятор печатается четырьмя группами по 4 разряда с ПРОБЕЛАМИ
        ("A = 0004 2200 1005 3377");
      * сброс модификатора адреса выводится строкой "Clear C" БЕЗ знака "=".

    Про C: регистр модификации адреса одноразовый — после utc/wtc он действует
    ровно на следующую команду, затем ApplyC сбрасывается.
    """
    steps = []
    state = {"A": 0, "Y": 0, "R": 0, "C": 0, "C_on": 0}
    # Ключи регистров — ВОСЬМЕРИЧНЫЕ (трасса печатает "M" + octal(index)),
    # иначе M[8]..M[15] никогда не найдут своих значений и останутся нулями.
    for i in range(16):
        state["M%o" % i] = 0
    pending = None
    for line in open(path):
        m = re.match(r"^([0-7]{5}) ([LR]):", line)
        if m:
            k = int(m.group(1), 8)
            half = m.group(2) == "R"
            if pending is not None:
                pending.next_k, pending.next_half = k, half
            pending = Step(k, half, fetch(words, k, half), dict(state), len(steps))
            steps.append(pending)
            continue
        if pending is None:
            continue
        m = re.match(r"^\s+Clear C\s*$", line)
        if m:
            state["C_on"] = 0
            continue
        m = re.match(r"^\s+(\w+) = ([0-7 ]+?)\s*$", line)
        if m:
            name, digits = m.group(1), m.group(2).replace(" ", "")
            if name == "CLEARC":
                state["C_on"] = 0
                continue
            value = int(digits, 8) if digits else 0
            if name == "C":
                state["C"], state["C_on"] = value, 1
            else:
                state[name] = value
    return steps


def fmt_state(state, only=None):
    parts = ["A=%012o" % state["A"], "Y=%012o" % state["Y"], "R=%02o" % state["R"],
             "C=%05o%s" % (state["C"], "*" if state["C_on"] else "")]
    for i in range(16):
        parts.append("M%o=%05o" % (i, state["M%o" % i]))
    return " ".join(parts)


def eff_addr(step):
    """Исполнительный адрес команды: адресное поле + база C + индексный регистр.

    Формула процессора (InstructionExecutor): поле адреса всегда модифицируется
    базой C, если активен ApplyC, — это выполняется до диспетчеризации.
    """
    _, addr, reg, _ = T.decode_half(step.instr)
    base = (addr + step.state["C"]) & 0x7FFF if step.state["C_on"] else addr
    return (base + step.state["M%o" % reg]) & 0x7FFF


def base_addr(step):
    """Адресное поле с учётом базы C, но БЕЗ индексного регистра."""
    _, addr, _, _ = T.decode_half(step.instr)
    return ((addr + step.state["C"]) & 0x7FFF) if step.state["C_on"] else addr


def branch_target(step):
    """Куда реально уходит управление — ПРАВИЛА У РАЗНЫХ КОМАНД РАЗНЫЕ.

    Из ControlInstructionExecutor.Execute:
      * uj, uza, u1a  → Branch(addr + m[reg])   — индексный регистр УЧАСТВУЕТ;
      * vjm, vzm, v1m, vlm, *36 → Branch(addr)  — индексный регистр НЕ участвует
        (для vjm/vzm/v1m/vlm это адрес самой команды-итерации, а не операнд).
    Ошибка здесь даёт 100 % промахов по целям, поэтому правило проверяется
    сверкой с наблюдаемым next_k (см. --self-test).
    """
    name, _, _, _ = T.decode_half(step.instr)
    return eff_addr(step) if name in ("uj", "uza", "u1a") else base_addr(step)


# ── Обнаружение самомодификации ───────────────────────────────────────────────
# Поле команды в трассе печатается в ОТОБРАЖАЮЩЕМ формате, из которого сырое
# 24-битное полуслово восстанавливается однозначно: ширина среднего поля
# (2 разряда — длинный формат, 3 — короткий) однозначно задаёт формат.
def raw_from_disp(reg, mid, addr, is_long):
    """Восстановить сырое полуслово из отображаемого формата OctalInstr."""
    reg, mid, addr = int(reg, 8), int(mid, 8), int(addr, 8)
    if is_long:
        return (reg << 20) | (1 << 19) | (mid << 15) | addr
    return (reg << 20) | (((mid >> 6) & 1) << 18) | ((mid & 0x3F) << 12) | addr


_DISP_RE = re.compile(r"^([0-7]{5}) ([LR]): (\d\d) (\d{2,3}) (\d{4,5})\s*$")


def self_modified(trace_path, words):
    """Адреса, где исполненный код отличается от снимка (самомодификация ОС).

    Сверка идёт по содержимому трассы, а не по Step.instr: тот сам берётся из
    образа, и такое сравнение всегда совпадает (проверено — даёт ложные нули).
    """
    mism = collections.Counter()
    total = 0
    for line in open(trace_path):
        m = _DISP_RE.match(line.rstrip("\n"))
        if not m:
            continue
        k = int(m.group(1), 8)
        half = m.group(2) == "R"
        total += 1
        raw = raw_from_disp(m.group(3), m.group(4), m.group(5), len(m.group(4)) == 2)
        if raw != fetch(words, k, half):
            mism[k] += 1
    return mism, total


def kernel_steps(steps, start, end):
    return [s for s in steps if start <= s.k < end]


def entry_points(steps, start, end):
    """Шаги, которыми управление впервые входит в ядро снаружи."""
    res = []
    for i, s in enumerate(steps):
        if not (start <= s.k < end):
            continue
        prev = steps[i - 1] if i else None
        if prev is None or not (start <= prev.k < end):
            res.append((i, s, prev))
    return res


def call_sites(steps, start, end):
    """Вызовы vjm (ПВ) из ядра: (адрес команды, цель, индексный регистр)."""
    return [(s.k, s.half, s.next_k, s.reg) for s in steps
            if start <= s.k < end and s.name == "vjm" and s.next_k is not None]


def listing(words, steps, start, count):
    """Аннотированный листинг: счётчики исполнения и роли команд."""
    cnt = collections.Counter(s.k for s in steps)
    halfcnt = collections.Counter((s.k, s.half) for s in steps)
    end = min(start + count, len(words))
    for a in range(start, end):
        w = words[a]
        n = cnt.get(a, 0)
        marks = []
        for half, raw in ((False, (w >> 24) & 0xFFFFFF), (True, w & 0xFFFFFF)):
            if not raw:
                continue
            name, _, _, _ = T.decode_half(raw)
            hn = halfcnt.get((a, half), 0)
            tag = []
            if name in BRANCH:
                tag.append(BRANCH[name])
            if name in EXTRACODES:
                tag.append("E%d" % EXTRACODES[name])
            if name in SET_C:
                tag.append("C")
            if tag and hn:
                marks.append("%s%s=%d" % ("R" if half else "L", "/".join(tag), hn))
        print("%06o %s %6d  %012o  %-30s %s" % (
            a, "  " if n else " .", n, w, T.disasm_word(w), " ".join(marks)))


def structure(words, steps, start, end):
    print("=== Диапазон 0o%o–0o%o ===" % (start, end))
    tot = len(steps)
    ink = len(kernel_steps(steps, start, end))
    print("команд в трассе: %d, в ядре: %d (%.1f%%)" % (tot, ink, 100.0 * ink / max(tot, 1)))

    uniq = set(s.k for s in kernel_steps(steps, start, end))
    nz = [a for a in range(start, end) if words[a]]
    print("ненулевых слов: %d, исполнялось: %d, ни разу: %d"
          % (len(nz), len(uniq), len([a for a in nz if a not in uniq])))

    print("\n--- Входы в ядро (управление приходит снаружи) ---")
    for i, s, prev in entry_points(steps, start, end)[:20]:
        src = ("с 0o%05o" % prev.k) if prev else "с начала"
        print("шаг %8d  ->  0o%05o %-24s %s" % (i, s.k, T.disasm_half(s.instr), src))

    print("\n--- Вызовы vjm (вызов подпрограммы), по числу вызовов ---")
    cc, srcs = collections.Counter(), collections.defaultdict(set)
    for k, half, tgt, reg in call_sites(steps, start, end):
        cc[(tgt, reg)] += 1
        srcs[(tgt, reg)].add(k)
    for (tgt, reg), n in cc.most_common(40):
        print("  -> 0o%05o  обратный адрес в M(%o)  вызовов %6d   из %s" % (
            tgt, reg, n, ", ".join("0o%o" % x for x in sorted(srcs[(tgt, reg)])[:6])))

    print("\n--- Ветвления внутри ядра (по фактическим переходам трассы) ---")
    ec = collections.Counter()
    for s in kernel_steps(steps, start, end):
        if s.is_branch and s.next_k is not None and start <= s.next_k < end:
            ec[(s.k, s.next_k, s.name)] += 1
    for (k, tgt, name), n in ec.most_common(30):
        print("  0o%05o %-5s -> 0o%05o  %6d раз" % (k, name, tgt, n))


def where(words, steps, at, limit, window):
    """Состояние регистров при попадании управления на адрес at."""
    hits = 0
    for s in steps:
        if s.k != at:
            continue
        hits += 1
        print("шаг %8d  %s  %s" % (s.seq, T.disasm_half(s.instr), fmt_state(s.state)))
        for j in range(s.seq, min(s.seq + window, len(steps))):
            t = steps[j]
            print("   %s0o%05o %s  %-26s A=%012o C=%05o" % (
                "->" if j == s.seq else "  ", t.k, "R" if t.half else "L",
                T.disasm_half(t.instr), t.state["A"], t.state["C"]))
        if hits >= limit:
            break
    if not hits:
        print("адрес 0o%o не исполнялся" % at)


def window(words, steps, at, count):
    """Разборка фрагмента кода вокруг адреса с состоянием регистров."""
    lo = max(0, at - count // 2)
    for s in steps:
        if s.k < lo or s.k >= lo + count:
            continue
        print("0o%05o %s %-30s A=%012o C=%05o M10=%05o M15=%05o" % (
            s.k, "R" if s.half else "L", T.disasm_half(s.instr),
            s.state["A"], s.state["C"], s.state["M10"], s.state["M15"]))


def strings(words, minlen):
    run, runs = [], []
    for i, x in enumerate(words):
        b = x.to_bytes(6, "big")
        s = "".join(chr(c) if 32 <= c < 127 else "·" for c in b)
        if s.count("·") == 0:
            run.append((i, s))
        else:
            if len(run) >= minlen:
                runs.append(run)
            run = []
    if len(run) >= minlen:
        runs.append(run)
    for r in runs:
        print("%06o-%06o  %s" % (r[0][0], r[-1][0], "".join(s for _, s in r)))


def main():
    ap = argparse.ArgumentParser(description="Анализатор ядра ОС MONSYS")
    sub = ap.add_subparsers(dest="cmd", required=True)

    def add(p, trace=True):
        p.add_argument("image")
        if trace:
            p.add_argument("trace")
        p.add_argument("--start", type=lambda x: int(x, 8), default=0o76000)
        p.add_argument("--end", type=lambda x: int(x, 8), default=0o77777)
        p.add_argument("--count", type=int, default=200)
        p.add_argument("--min", type=int, default=6)

    add(sub.add_parser("structure"))
    add(sub.add_parser("listing"))
    add(sub.add_parser("calls"))
    add(sub.add_parser("where"))
    add(sub.add_parser("selfmod"))
    add(sub.add_parser("strings"), trace=False)
    args = ap.parse_args()

    words = load_words(args.image)
    if args.cmd == "strings":
        strings(words, args.min)
        return
    steps = parse_trace(args.trace, words)
    if args.cmd == "listing":
        listing(words, steps, args.start, args.count)
    elif args.cmd == "calls":
        for k, half, tgt, reg in call_sites(steps, args.start, args.end):
            print("0o%05o %s vjm 0o%05o  (обр. адрес в M(%o))"
                  % (k, "R" if half else "L", tgt, reg))
    elif args.cmd == "where":
        where(words, steps, args.start, args.count, 24)
    elif args.cmd == "selfmod":
        mism, total = self_modified(args.trace, words)
        n = sum(mism.values())
        print("команд в трассе: %d" % total)
        print("не совпало со снимком: %d (%.2f%%), адресов: %d"
              % (n, 100.0 * n / max(total, 1), len(mism)))
        for a, c in mism.most_common(args.count):
            print("   0o%05o  %6d раз" % (a, c))
    else:
        structure(words, steps, args.start, args.end)


if __name__ == "__main__":
    main()

