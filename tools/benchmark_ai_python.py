#!/usr/bin/env python3
"""Measure the pure-Python AI reference implementations reproducibly.

The first version of the AI article quoted a median of five sessions, but the
artifact contained a single run and the full process time was composed as
``startup + function``.  This driver fixes both points:

* five randomised sessions, five timed batches each, bootstrap 95 % CI for the
  median in-process function time;
* direct measurement of a fresh ``python tools/run_ai_one.py <id>`` process for
  the end-to-end time (start-up + import + function + output);
* direct measurement of ``python -c pass`` for the interpreter start-up.

``T_solve`` is the function time without output; ``T_process`` is a complete
process, so the two metrics are not interchangeable.
"""
from __future__ import annotations

import argparse
import json
import random
import statistics
import subprocess
import sys
import time
from pathlib import Path

from benchmark_ai import ALGORITHMS

SESSIONS = 5
BATCHES = 5


def bootstrap_ci(values: list[float], alpha: float = 0.05, draws: int = 2000,
                 seed: int = 20261117) -> tuple[float, float]:
    rng = random.Random(seed)
    medians = sorted(statistics.median(rng.choices(values, k=len(values)))
                     for _ in range(draws))
    lo = medians[int(alpha / 2 * draws)]
    hi = medians[min(draws - 1, int((1 - alpha / 2) * draws))]
    return lo, hi


def summarise(values: list[float]) -> dict[str, object]:
    lo, hi = bootstrap_ci(values)
    return {
        "median_s": statistics.median(values),
        "mean_s": statistics.fmean(values),
        "min_s": min(values),
        "max_s": max(values),
        "ci95_low_s": lo,
        "ci95_high_s": hi,
        "samples": len(values),
    }


def measure_batches(func, target: float) -> tuple[list[float], int]:
    """Calibrate the repeat count, then return per-call times of 5 batches."""
    func()
    started = time.perf_counter()
    func()
    one = time.perf_counter() - started
    repeats = max(1, min(10_000_000, int(target / max(one, 1e-9))))
    samples = []
    for _ in range(BATCHES):
        started = time.perf_counter_ns()
        for _ in range(repeats):
            func()
        samples.append((time.perf_counter_ns() - started) / 1e9 / repeats)
    return samples, repeats


def process_times(command: list[str], cwd: Path, repeats: int) -> list[float]:
    values = []
    for _ in range(repeats):
        started = time.perf_counter_ns()
        subprocess.run(command, cwd=cwd, stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, check=True)
        values.append((time.perf_counter_ns() - started) / 1e9)
    return values


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    tools = root / "tools"
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=root / "reports" / "ai-python-results.json")
    parser.add_argument("--target", type=float, default=0.10,
                        help="target seconds per timed batch")
    parser.add_argument("--process-repeats", type=int, default=50)
    parser.add_argument("--startup-repeats", type=int, default=50)
    args = parser.parse_args()

    rng = random.Random(20261117)
    batch_values: dict[str, list[float]] = {name: [] for name, _, _ in ALGORITHMS}
    session_medians: dict[str, list[float]] = {name: [] for name, _, _ in ALGORITHMS}
    repeats: dict[str, int] = {}

    for session in range(SESSIONS):
        order = list(ALGORITHMS)
        rng.shuffle(order)
        for name, func, _ in order:
            samples, count = measure_batches(func, args.target)
            batch_values[name].extend(samples)
            session_medians[name].append(statistics.median(samples))
            repeats[name] = count
        print(f"session {session + 1}/{SESSIONS} done")

    startup = process_times([sys.executable, "-c", "pass"], root, args.startup_repeats)
    print(f"startup median {statistics.median(startup)*1e3:.3f} ms")

    rows = []
    for name, func, _ in ALGORITHMS:
        values = batch_values[name]
        result = func()
        full = process_times([sys.executable, str(tools / "run_ai_one.py"), name],
                             root, args.process_repeats)
        row = {
            "id": name,
            "solve": summarise(values),
            "session_medians_s": session_medians[name],
            "repeats": repeats[name],
            "process": summarise(full),
            "result": result,
        }
        rows.append(row)
        s = row["solve"]
        p = row["process"]
        print(f"{name:20s} solve={s['median_s']*1e6:9.3f} us "
              f"[{s['ci95_low_s']*1e6:.3f}, {s['ci95_high_s']*1e6:.3f}]  "
              f"process={p['median_s']*1e3:7.3f} ms")

    data = {
        "python": sys.version.split()[0],
        "platform": sys.platform,
        "sessions": SESSIONS,
        "batches_per_session": BATCHES,
        "measurement": "in-process function time; bootstrap 95% CI of the median",
        "process_measurement": "fresh python process per sample, median + bootstrap 95% CI",
        "startup": summarise(startup),
        "rows": rows,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nSaved: {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
