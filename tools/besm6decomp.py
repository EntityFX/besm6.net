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


def emit_c(step, scope):
    """Одна команда -> строка C-подобного кода.

    Отличия от машинного представления, которые здесь принципиальны:
      * память — 48-битные СЛОВА, поэтому тип w48, а не char/int;
      * целое — это тоже w48 (48 бит, дополнительный код), а не int32;
      * адресное поле модифицируется базой C, действующей ОДНУ команду,
        поэтому адрес вычисляется на месте, а не хранится в регистре;
      * переменные живут в индексных регистрах — они объявляются, а не
        выделяются в стеке.
    """
    name = step.name
    ea = K.eff_addr(step)
    _, _, reg, _ = T.decode_half(step.instr)
    rn = reg_name(reg, scope)
    a = "w48[%#o]" % ea if reg == 0 else "w48[%#o]" % ea

    if name == "atx":
        return "w48[%#o] = A;" % ea
    if name == "xta":
        return "A = w48[%#o];" % ea
    if name == "ita":
        return "A = %s;" % rn
    if name == "ati":
        return "%s = A;" % rn
    if name == "its":
        return "A = %s; push(A);" % rn
    if name == "sti":
        return "%s = A; A = pop();" % rn
    if name == "stx":
        return "push(A);"
    if name == "xts":
        return "A = pop();"
    if name == "mtj":
        return "%s = %s;" % (rn, rn)
    if name == "a+x":
        return "A += w48[%#o];" % ea
    if name == "a-x":
        return "A -= w48[%#o];" % ea
    if name == "x-a":
        return "A = w48[%#o] - A;" % ea
    if name == "amx":
        return "A = fabs(w48[%#o]) - fabs(A);" % ea
    if name == "aax":
        return "A &= w48[%#o];" % ea
    if name == "aox":
        return "A |= w48[%#o];" % ea
    if name == "aex":
        return "A ^= w48[%#o];" % ea
    if name == "a*x":
        return "A *= w48[%#o];" % ea
    if name == "a/x":
        return "A /= w48[%#o];" % ea
    if name == "arx":
        return "A = rot(A + w48[%#o]);   /* циклическое сложение */" % ea
    if name == "asn":
        return "A = shr(A, %#o);        /* сдвиг по адресу */" % T.decode_half(step.instr)[1]
    if name == "asx":
        return "A = shl(A, %#o);" % T.decode_half(step.instr)[1]
    if name == "e+x":
        return "exp_add(A, w48[%#o]);" % ea
    if name == "e-x":
        return "exp_sub(A, w48[%#o]);" % ea
    if name == "e+n":
        return "exp_add(A, %#o);" % ea
    if name == "e-n":
        return "exp_sub(A, %#o);" % ea
    if name == "apx":
        return "A = pack(w48[%#o]);" % ea
    if name == "aux":
        return "A = unpack(w48[%#o]);" % ea
    if name == "acx":
        return "A = popcount(w48[%#o]);" % ea
    if name == "anx":
        return "A = findone(w48[%#o]);" % ea
    if name == "yta":
        return "A = Y;"
    if name == "rte":
        return "E = R;"
    if name == "ntr":
        return "R = %#o;   /* РЖА: режим из адреса */" % ea
    if name == "xtr":
        return "R = %#o;" % ea
    if name == "mod":
        return "R = w48[%#o];  /* РЕГ: привилегированный */" % ea
    if name == "vtm":
        return "%s = %#o;" % (rn, ea)
    if name == "utm":
        return "%s += %#o;" % (rn, ea)
    if name == "utc":
        return "C = %#o;  /* УТА: база действует на СЛЕДУЮЩУЮ команду */" % ea
    if name == "wtc":
        return "C = w48[%#o]; /* МОД: база из памяти, на 1 команду */" % ea
    if name in K.EXTRACODES:
        return "extracode%o(A);   /* *%o — системный вызов */" % (
            K.EXTRACODES[name], K.EXTRACODES[name])
    if name == "stop":
        return "stop();"
    return None


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


PRELUDE = """/* Псевдо-C для кода ОС «Дубна» (MONSYS) — восстановлено из трассы.
 *
 * Модель, которую приходится принять (её НЕ выбирал автор кода):
 *   w48      48-битное машинное слово. И плавающая точка, и целое (48 бит,
 *            дополнительный код), и пара команд, и 6 байт текста.
 *   Формат числа: биты 47-41 = смещённый порядок (7 бит, смещение 64),
 *   биты 40-0 = знаковая 41-битная мантисса, отдельного знакового бита нет.
 *   A         аккумулятор (w48), Y - младшие разряды, E - порядок.
 *   C         база адреса, действует РОВНО на следующую команду (одноразовая).
 *   M(i)      индексные регистры; здесь это ЛОКАЛЬНЫЕ ПЕРЕМЕННЫЕ, а не память.
 *   link/link2/link3 = M(16)/M(6)/M(7) - обратные адреса: аппаратного стека
 *            вызовов нет, вложенность ограничена тремя уровнями.
 *   sp = M(17) - стек данных (push/pop), НЕ стек вызовов.
 *
 * Чего здесь нет и не может быть: имён (в образе их нет), типов (48-битное
 * слово не различает int и double), кадров стека, и учёта самомодификации
 * (2.10 % команд ядра исполняют код, отличный от снимка).
 */
typedef unsigned long long w48;
extern w48  mem48[32768];
static w48  A, Y;
static int  E, R, C, K;

static void push(w48 v) { mem48[0] = v; }   /* заглушка: реальный стек — M(17) */
static w48  pop(void)     { return mem48[0]; }
static w48  shr(w48 a, int n) { return n ? (a >> n) : a; }
static w48  shl(w48 a, int n) { return n ? (a << n) : a; }
static w48  rot(w48 a)    { return a; }
static w48  pack(w48 a)   { return a; }
static w48  unpack(w48 a) { return a; }
static w48  popcount(w48 a) { return a; }
static w48  findone(w48 a)   { return a; }
static void exp_add(w48 *a, w48 b) { (void)a; (void)b; }
static void exp_sub(w48 *a, w48 b) { (void)a; (void)b; }
static void stop(void) { }
"""


def decompile_c(words, steps, start, end, routine, title):
    """C-подобный листинг одной подпрограммы: объявления, тело, goto-метки.

    Метки собираются ДО прохода: иначе пришлось бы ставить их постфактум,
    а места перехода разбросаны по телу.
    """
    blocks, by_key = build_blocks(steps, words, start, end)
    reach = reachable(by_key, routine)
    loops = find_loops(blocks, by_key, reach)
    loop_tails = collections.defaultdict(list)
    for src, dst in loops:
        loop_tails[dst].append(src)

    # цели переходов внутри тела подпрограммы
    targets = set()
    for a in reach:
        b = by_key.get((a, False))
        if b is None or not b.ins:
            continue
        last = b.ins[-1]
        if last.is_branch:
            t = K.branch_target(last)
            if t in reach:
                targets.add(t)
    targets.discard(routine)

    out = ["", "/* %s — 0o%o */" % (title, routine)]
    used = collections.Counter()
    seen = set()

    def walk(addr, depth):
        pad = "    " * depth
        if addr in targets and addr not in seen:
            out.append("%sL_%o:" % (pad, addr))
        block = by_key.get((addr, False))
        if block is None:
            out.append("%sgoto L_%o;   /* 0o%o: кода нет (данные/холодная ветвь) */"
                       % (pad, addr, addr))
            return
        if addr in seen or depth > 8:
            return
        seen.add(addr)
        looped = addr in loop_tails
        if looped:
            out.append("%sfor (;;) {   /* цикл: 0o%o */" % (pad, addr))
            depth += 1
            pad = "    " * depth
        for s in block.ins:
            nm0, _, reg, _ = T.decode_half(s.instr)
            # M(0) — тоже индексный регистр (признак/указатель байта), его
            # нельзя отбрасывать вместе с "нулевым" reg: ita 0 = A := ptr.
            used[reg_name(reg, {})] += 1
            if s.is_branch:
                continue
            line = emit_c(s, {})
            if line:
                out.append("%s%s   /* 0o%05o %s */"
                           % (pad, line, s.k, T.disasm_half(s.instr)))
        last = block.ins[-1] if block.ins else None
        if last is None:
            return
        nm, _, reg, _ = T.decode_half(last.instr)
        nxt = block.succ[0] if block.succ else None
        rn = reg_name(reg, {})
        if nm == "vjm":
            out.append("%s%s = %#o; goto f_0o%o;   /* ПВ: вызов, адрес возврата в %s */"
                       % (pad, rn, nxt, K.branch_target(last), rn))
            if nxt in reach:
                walk(nxt, depth)
        elif nm == "uj":
            ret = classify_return(last, {})
            if ret:
                out.append("%sgoto %s;   /* ВЫХОД */" % (pad, ret.split()[1]))
            else:
                tgt = K.branch_target(last)
                out.append("%sgoto L_%o;   /* ПБ */" % (pad, tgt))
                if tgt in reach:
                    walk(tgt, depth)
        elif nm in ("vzm", "v1m", "uza", "u1a"):
            cond = ("%s == 0" % rn) if nm in ("vzm", "uza") else ("%s != 0" % rn)
            if nxt is not None:
                out.append("%sif (%s) goto L_%o;" % (pad, cond, nxt))
                if nxt in reach:
                    walk(nxt, depth)
        elif nm == "vlm":
            out.append("%sif (%s == 0) break;" % (pad, rn))
            out.append("%s%s++;" % (pad, rn))
            out.append("%sgoto L_%o;   /* ЦИКЛ vlm */" % (pad, nxt))
        elif nxt is not None and nxt in reach:
            walk(nxt, depth)
        if looped:
            out.append("%s}" % pad)

    walk(routine, 0)

    locals_ = [k for k in used if k not in ("link", "link2", "link3", "sp", "ptr")]
    decls = []
    if used.get("link"):
        decls.append("int link;   /* обратный адрес, M(16) */")
    if used.get("link2"):
        decls.append("int link2;  /* обратный адрес, M(6) */")
    if used.get("link3"):
        decls.append("int link3;  /* обратный адрес, M(7) */")
    if used.get("sp"):
        decls.append("int sp;     /* стек данных, M(17) */")
    if used.get("ptr"):
        decls.append("w48 ptr;    /* признак/указатель байта, M(0) */")
    if locals_:
        decls.append("int %s;   /* локальные переменные в индексных регистрах */"
                     % ", ".join(sorted(set(locals_))))
    header = ["", "static void f_0o%o(void)   /* %s */" % (routine, title), "{"]
    header += ["    " + d for d in decls] if decls else []
    return header + out + ["}", ""]



def main():
    ap = argparse.ArgumentParser(description="Высокоуровневый листинг ядра MONSYS")
    ap.add_argument("image")
    ap.add_argument("trace")
    ap.add_argument("--start", type=lambda x: int(x, 8), default=START)
    ap.add_argument("--end", type=lambda x: int(x, 8), default=END)
    ap.add_argument("--routine", type=lambda x: int(x, 8))
    ap.add_argument("--lang", default="pseudo", choices=("pseudo", "c"))
    ap.add_argument("--no-prelude", action="store_true",
                    help="без заголовка-пролога (при склейке нескольких файлов)")
    ap.add_argument("--all", action="store_true", help="все найденные подпрограммы")
    args = ap.parse_args()

    words = K.load_words(args.image)
    steps = K.parse_trace(args.trace, words)
    c_mode = args.lang == "c"

    def render(routine, title):
        if c_mode:
            return decompile_c(words, steps, args.start, args.end, routine, title)
        return decompile(words, steps, args.start, args.end, routine, title)

    if args.routine is not None:
        for line in render(args.routine, "подпрограмма"):
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
    if c_mode:
        if not args.no_prelude:
            print(PRELUDE)
        for e in sorted(set(entries)):
            for line in render(e, "ТОЧКА ВХОДА (управление приходит снаружи)"):
                print(line)
        for tgt, n in targets.most_common():
            for line in render(tgt, "вызовов: %d" % n):
                print(line)
        return
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
