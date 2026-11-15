#!/usr/bin/env python3
"""Pure-Python reference implementations of the ten BESM-6 AI examples.

The algorithms and fixed datasets mirror examples/ai/*.dub.  The script is
used to compare host Python time with the calibrated BESM-6 model.
"""
from __future__ import annotations

import json
import math
import time
from pathlib import Path


def perceptron():
    x1 = [0.0, 0.0, 1.0, 1.0]
    x2 = [0.0, 1.0, 0.0, 1.0]
    y = [0.0, 0.0, 0.0, 1.0]
    w1 = w2 = b = 0.0
    eta = 0.1
    for _ in range(20):
        for i in range(4):
            out = w1 * x1[i] + w2 * x2[i] + b
            out = 1.0 if out > 0.0 else 0.0
            err = y[i] - out
            w1 += eta * err * x1[i]
            w2 += eta * err * x2[i]
            b += eta * err
    outputs = []
    for i in range(4):
        out = w1 * x1[i] + w2 * x2[i] + b
        outputs.append(1.0 if out > 0.0 else 0.0)
    return {"weights": [w1, w2, b], "outputs": outputs}


def knn():
    xt = [1.0, 2.0, 3.0, 6.0, 7.0, 8.0]
    yt = [0, 0, 0, 1, 1, 1]
    queries = [2.5, 5.5, 7.5]
    classes = []
    for q in queries:
        distances = [abs(x - q) for x in xt]
        used = [False] * 6
        total = 0
        for _ in range(3):
            best = None
            for i in range(6):
                if used[i]:
                    continue
                if best is None or distances[i] < distances[best]:
                    best = i
            used[best] = True
            total += yt[best]
        classes.append(1 if total > 1 else 0)
    return {"queries": queries, "classes": classes}


def kmeans():
    x = [1.0, 2.0, 3.0, 10.0, 11.0, 12.0]
    c1, c2 = 1.0, 12.0
    for _ in range(10):
        s1 = s2 = 0.0
        n1 = n2 = 0
        for value in x:
            if abs(value - c1) <= abs(value - c2):
                s1 += value
                n1 += 1
            else:
                s2 += value
                n2 += 1
        if n1:
            c1 = s1 / n1
        if n2:
            c2 = s2 / n2
    clusters = [1 if abs(value - c1) <= abs(value - c2) else 2 for value in x]
    return {"centers": [c1, c2], "clusters": clusters}


def naive_bayes():
    a = [0, 0, 0, 1, 1, 1, 1, 0]
    b = [0, 1, 0, 0, 1, 1, 0, 1]
    y = [0, 0, 0, 0, 1, 1, 1, 1]
    n = [0, 0]
    na = [[0, 0], [0, 0]]
    nb = [[0, 0], [0, 0]]
    for i in range(8):
        c = y[i]
        n[c] += 1
        na[c][a[i]] += 1
        nb[c][b[i]] += 1
    scores = []
    for c in range(2):
        pa = (na[c][1] + 1.0) / (n[c] + 2.0)
        pb = (nb[c][1] + 1.0) / (n[c] + 2.0)
        scores.append(0.5 * pa * pb)
    predicted = 0 if scores[0] > scores[1] else 1
    return {"counts": n, "scores": scores, "predicted": predicted}


def decision_stump():
    x = [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0]
    y = [0, 0, 0, 0, 1, 1, 1, 1]
    best = 100
    bt = bd = 0
    for t in range(1, 8):
        threshold = t + 0.5
        for direction in (1, 2):
            errors = 0
            for i in range(8):
                p = 1 if x[i] >= threshold else 0
                if direction == 2:
                    p = 1 - p
                if p != y[i]:
                    errors += 1
            if errors < best:
                best = errors
                bt = t
                bd = direction
    return {"threshold": bt + 0.5, "direction": bd, "errors": best}


def genetic():
    p = [3, 12, 21, 30]
    fit = [0] * 4
    for _ in range(8):
        for i in range(4):
            fit[i] = p[i] * p[i]
        i1 = 0
        for i in range(1, 4):
            if fit[i] > fit[i1]:
                i1 = i
        i2 = None
        for i in range(4):
            if i == i1:
                continue
            if i2 is None or fit[i] > fit[i2]:
                i2 = i
        p1, p2 = p[i1], p[i2]
        c1 = (p1 // 4) * 4 + (p2 - 4 * (p2 // 4))
        c2 = (p2 // 4) * 4 + (p1 - 4 * (p1 // 4))
        c1 += 3
        if c1 > 31:
            c1 -= 32
        c2 += 5
        if c2 > 31:
            c2 -= 32
        p = [p1, p2, c1, c2]
    bx = p[0]
    bf = fit[0]
    for i in range(1, 4):
        value = p[i] * p[i]
        if value > bf:
            bx = p[i]
            bf = value
    return {"best_x": bx, "fitness": bf}


def annealing():
    def f(value):
        return 9.0 - (value - 3.0) ** 2

    iseed = 12345
    x = 0
    fx = f(x)
    best = x
    fb = fx
    t = 5.0
    for _ in range(30):
        iseed = (iseed * 75 + 74) % 65537
        r1 = iseed / 65537.0
        xn = x - 1 if r1 < 0.5 else x + 1
        xn = max(0, min(10, xn))
        fn = f(xn)
        accept = fn >= fx
        if not accept:
            iseed = (iseed * 75 + 74) % 65537
            r2 = iseed / 65537.0
            accept = r2 < math.exp((fn - fx) / t)
        if accept:
            x, fx = xn, fn
            if fx > fb:
                best, fb = x, fx
        t *= 0.8
    return {"best_x": best, "best_f": fb, "final_x": x, "final_f": fx, "temperature": t}


def qlearning():
    q = [[0.0, 0.1] for _ in range(4)]
    alpha, gamma, epsilon = 0.5, 0.9, 0.2
    iseed = 12345
    for _ in range(20):
        s = 0
        for _ in range(10):
            iseed = (iseed * 75 + 74) % 65537
            r = iseed / 65537.0
            if r < epsilon:
                iseed = (iseed * 75 + 74) % 65537
                r2 = iseed / 65537.0
                action = 0 if r2 < 0.5 else 1
            else:
                action = 0 if q[s][0] >= q[s][1] else 1
            ns = s - 1 if action == 0 else s + 1
            ns = max(0, min(3, ns))
            reward = 1 if ns == 3 else 0
            maxq = max(q[ns])
            q[s][action] += alpha * (reward + gamma * maxq - q[s][action])
            s = ns
            if s == 3:
                break
    policy = [0 if q[s][0] >= q[s][1] else 1 for s in range(3)]
    return {"q": q, "policy": policy}


def astar():
    size = 5
    obstacles = {(1, 1), (1, 2), (2, 2), (3, 1)}  # zero-based (row, col)
    start = (0, 0)
    goal = (4, 4)
    moves = [(-1, 0), (1, 0), (0, -1), (0, 1)]
    g = {start: 0}
    f = {start: abs(start[0] - goal[0]) + abs(start[1] - goal[1])}
    parent = {}
    open_set = {start}
    closed = set()
    while open_set:
        current = min(open_set, key=lambda p: (f[p], p[0] * size + p[1]))
        if current == goal:
            break
        open_set.remove(current)
        closed.add(current)
        for dr, dc in moves:
            nxt = (current[0] + dr, current[1] + dc)
            if not (0 <= nxt[0] < size and 0 <= nxt[1] < size):
                continue
            if nxt in obstacles or nxt in closed:
                continue
            ng = g[current] + 1
            if ng >= g.get(nxt, 10**9):
                continue
            g[nxt] = ng
            f[nxt] = ng + abs(nxt[0] - goal[0]) + abs(nxt[1] - goal[1])
            parent[nxt] = current
            open_set.add(nxt)
    if goal not in parent and goal != start:
        return {"path_length": 0, "path": []}
    path = [goal]
    while path[-1] != start:
        path.append(parent[path[-1]])
    path.reverse()
    return {"path_length": len(path), "path": path}


def mlp_xor():
    x1 = [0.0, 0.0, 1.0, 1.0]
    x2 = [0.0, 1.0, 0.0, 1.0]
    y = [0.0, 1.0, 1.0, 0.0]
    w11, w12, w21, w22 = 0.5, 0.1, 0.3, 0.7
    v1, v2 = 0.2, 0.4
    b1, b2, b3 = -0.1, 0.2, -0.3
    eta = 0.5
    for _ in range(2000):
        for i in range(4):
            z1 = w11 * x1[i] + w12 * x2[i] + b1
            h1 = 1.0 / (1.0 + math.exp(-z1))
            z2 = w21 * x1[i] + w22 * x2[i] + b2
            h2 = 1.0 / (1.0 + math.exp(-z2))
            z3 = v1 * h1 + v2 * h2 + b3
            out = 1.0 / (1.0 + math.exp(-z3))
            d3 = out - y[i]
            d1 = d3 * v1 * h1 * (1.0 - h1)
            d2 = d3 * v2 * h2 * (1.0 - h2)
            v1 -= eta * d3 * h1
            v2 -= eta * d3 * h2
            b3 -= eta * d3
            w11 -= eta * d1 * x1[i]
            w12 -= eta * d1 * x2[i]
            w21 -= eta * d2 * x1[i]
            w22 -= eta * d2 * x2[i]
            b1 -= eta * d1
            b2 -= eta * d2
    outputs = []
    loss = 0.0
    for i in range(4):
        z1 = w11 * x1[i] + w12 * x2[i] + b1
        h1 = 1.0 / (1.0 + math.exp(-z1))
        z2 = w21 * x1[i] + w22 * x2[i] + b2
        h2 = 1.0 / (1.0 + math.exp(-z2))
        z3 = v1 * h1 + v2 * h2 + b3
        out = 1.0 / (1.0 + math.exp(-z3))
        outputs.append(out)
        loss += (out - y[i]) ** 2
    return {"outputs": outputs, "loss": loss}


ALGORITHMS = [
    ("01_perceptron", perceptron, 2000),
    ("02_knn", knn, 5000),
    ("03_kmeans", kmeans, 2000),
    ("04_naive_bayes", naive_bayes, 20000),
    ("05_decision_stump", decision_stump, 5000),
    ("06_genetic", genetic, 2000),
    ("07_annealing", annealing, 5000),
    ("08_qlearning", qlearning, 2000),
    ("09_astar", astar, 5000),
    ("10_mlp_xor", mlp_xor, 20),
]


def bench():
    results = {}
    for name, func, repeats in ALGORITHMS:
        value = func()  # warmup and accuracy result
        start = time.perf_counter()
        for _ in range(repeats):
            func()
        elapsed = time.perf_counter() - start
        seconds = elapsed / repeats
        results[name] = {
            "python_seconds": seconds,
            "repeats": repeats,
            "result": value,
        }
        print(f"{name:20s} {seconds * 1e6:10.3f} us/run  {value}")
    out = Path(__file__).resolve().parent.parent / "reports" / "ai-python-results.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nSaved: {out}")


if __name__ == "__main__":
    bench()
