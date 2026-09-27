#!/usr/bin/env python3
"""Слияние трасс нескольких заданий и оценка структурируемости кода.

Зачем: CFG, построенный по ОДНОЙ трассе, неполон — ветвление, не взятое в
этом прогоне, просто отсутствует. Из-за этого петли выглядят неразрешимыми.
Слияние трасс разных заданий даёт объединённый граф и честную оценку того,
какая доля кода переводится в if/while автоматически.

Собранные образы могут различаться: ОС самомодифицирующаяся (2.10 % команд
ядра исполняют не тот код, что в снимке). Поэтому тело блока берётся из
каждой трассы отдельно, а решение о структуре принимается по объединённым
рёбрам.

Использование:
  python3 tools/besm6merge.py /tmp/hm --start 0o76000 --end 0o77777
"""
import argparse
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import besm6tape as T     # noqa: E402
import besm6kernel as K   # noqa: E402
import besm6decomp as D   # noqa: E402


def load_runs(root):
    """Все пары (образ, трасса) из каталога сборки."""
    out = []
    for f in sorted(os.listdir(root)):
        if f.endswith(".img"):
            tr = os.path.join(root, f[:-4] + ".trace")
            if os.path.exists(tr):
                out.append((os.path.join(root, f), tr))
    return out


def merge(runs, start, end):
    """Объединённые наблюдения: (k, half) -> {next_k}, плюс счётчики."""
    succ = collections.defaultdict(set)
    count = collections.Counter()
    per_run = {}
    for img, tr in runs:
        w = K.load_words(img)
        steps = K.parse_trace(tr, w)
        local = collections.defaultdict(set)
        n = 0
        for s in steps:
            if not (start <= s.k < end):
                continue
            n += 1
            count[(s.k, s.half)] += 1
            if s.next_k is not None:
                succ[(s.k, s.half)].add(s.next_k)
                local[(s.k, s.half)].add(s.next_k)
        per_run[os.path.basename(img)] = n
    return succ, count, per_run


def blocks_from(succ, count, words, start, end):
    """Базовые блоки по объединённым рёбрам.

    Тело блока — левое полуслово плюс правое, если левое не передаёт
    управление. Терминатор — последнее исполненное полуслово, если оно
    передаёт управление."""
    addrs = sorted({k for (k, h) in succ if h is False or (k, h) in succ})
    addrs = sorted({k for (k, h) in count if not h})
    blocks = {}
    for k in addrs:
        if not (start <= k < end):
            continue
        body = [(k, False)]
        left = T.decode_half(K.fetch(words, k, False))[0]
        if left not in K.BRANCH and count.get((k, True)):
            body.append((k, True))
        term = body[-1] if K.BRANCH.get(
            T.decode_half(K.fetch(words, k, body[-1][1]))[0]) else None
        edges = set()
        is_return = False
        if term:
            nm, ad, rg, _ = T.decode_half(K.fetch(words, k, term[1]))
            # ВОЗВРАТ: uj с пустым адресным полем через регистр возврата.
            # Управление уходит ВЫЗЫВАЮЩЕМУ, и тело подпрограммы на этом
            # заканчивается. Если такие рёбра не отбросить, обход засасывает
            # в каждую подпрограмму весь общий диспетчер.
            is_return = nm == "uj" and ad == 0
        if term and not is_return:
            edges = set(succ.get(term, ()))
            edges.add(k + 1)          # невзятая ветвь
        blocks[k] = {"body": body, "edges": edges, "term": term,
                     "is_return": is_return}
    return blocks



def structurability(blocks, entry, call_targets):
    """Разрешима ли подпрограмма в if/while.

    Возвращает (узлы, неразрешимые петли, выходы). Петля неразрешима, если
    её заголовок имеет входящее ребро извне тела петли — тогда while не
    представим, нужен goto или дублирование кода.
    """
    succ, seen, stack, exits = {}, set(), [entry], 0
    while stack:
        a = stack.pop()
        if a in seen or (a in call_targets and a != entry) or a not in blocks:
            continue
        seen.add(a)
        b = blocks[a]
        succ[a] = b["edges"]
        if b["is_return"]:
            exits += 1                  # тело подпрограммы здесь закончено
            continue
        for e in b["edges"]:
            if e in call_targets and e != entry:
                exits += 1
            else:
                stack.append(e)
    preds = collections.defaultdict(list)
    for a, es in succ.items():
        for e in es:
            preds[e].append(a)
    bad = 0
    for a in seen:
        for s in succ[a]:
            if s > a:
                continue                       # не обратное ребро
            loop, st = set(), [s]
            while st:
                x = st.pop()
                if x in loop or x == a:
                    continue
                loop.add(x)
                st.extend(y for y in succ.get(x, ()) if y in seen)
            if any(p not in loop for p in preds[s]):
                bad += 1
    return seen, bad, exits


def main():
    ap = argparse.ArgumentParser(description="Слияние трасс и оценка структурируемости")
    ap.add_argument("root")
    ap.add_argument("--start", type=lambda x: int(x, 8), default=0o76000)
    ap.add_argument("--end", type=lambda x: int(x, 8), default=0o77777)
    ap.add_argument("--json", default=None)
    args = ap.parse_args()

    runs = load_runs(args.root)
    if not runs:
        raise SystemExit("в %s нет пар образ/трасса" % args.root)
    print("сливается трасс: %d" % len(runs))

    succ, count, per_run = merge(runs, args.start, args.end)
    words = K.load_words(runs[0][0])          # тело блока — из первого образа
    blocks = blocks_from(succ, count, words, args.start, args.end)
    call_targets = {k for (k, h) in count
                    if T.decode_half(K.fetch(words, k, h))[0] == "vjm"}
    # Подпрограммы — это ЦЕЛИ вызовов плюс адреса, в которые управление
    # приходит извне диапазона. Считать подпрограммой каждый узел нельзя:
    # тогда «подпрограмм» столько же, сколько блоков, и оценка бессмысленна.
    entered = {s for (k, h), es in succ.items() for s in es
               if not (args.start <= s < args.end)}
    entries = sorted((set(call_targets) | entered) & set(blocks))

    print("узлов в объединённом графе: %d" % len(blocks))
    print("целей вызова vjm: %d" % len(call_targets))
    print()
    print("%-9s %6s %6s %6s  %s" % ("подпр.", "узлов", "петли", "выход", "вердикт"))
    ok_n = rows = 0
    report = {}
    for r in entries:
        nodes, bad, exits = structurability(blocks, r, call_targets)
        if not nodes:
            continue
        rows += 1
        ok = bad == 0
        ok_n += ok
        report["0o%o" % r] = {"nodes": len(nodes), "bad_loops": bad,
                              "exits": exits, "structurable": ok}
        print("0o%-7o %6d %6d %6d  %s"
              % (r, len(nodes), bad, exits,
                 "разрешима в if/while" if ok else "нужен goto (%d)" % bad))
    print()
    print("структурируемых: %d из %d (%.0f%%)"
          % (ok_n, rows, 100.0 * ok_n / max(rows, 1)))
    if args.json:
        with open(args.json, "w") as f:
            json.dump({"runs": len(runs), "nodes": len(blocks),
                       "calls": sorted("0o%o" % c for c in call_targets),
                       "routines": report}, f, indent=1)
        print("подробно: %s" % args.json)


if __name__ == "__main__":
    main()
