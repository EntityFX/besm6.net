#!/usr/bin/env python3
"""Assemble the three measurement files into ``reports/ai-comparison.json``.

This replaces the ad-hoc notebook step that produced the first version of the
AI article.  All speed-up values in the article are generated here, so table
and text cannot drift apart again.

Definitions
-----------
``S_solve``      BESM-6 compute / Python function time
``S_solve_BF``   BESM-6 compute / Fortran in-process time
``S_solve_FP``   Fortran in-process time / Python function time
``S_total_BP``   full BESM-6 job / full Python process
``S_total_BF``   full BESM-6 job / full Fortran process
``S_process_FP`` full Fortran process / full Python process (launch overhead)
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import platform
import statistics
from pathlib import Path


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def stats(values: list[float]) -> dict[str, float]:
    return {
        "min": min(values),
        "max": max(values),
        "median": statistics.median(values),
    }


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    reports = root / "reports"
    parser = argparse.ArgumentParser()
    parser.add_argument("--besm", type=Path, default=reports / "ai-besm-results.json")
    parser.add_argument("--python", type=Path, default=reports / "ai-python-results.json")
    parser.add_argument("--fortran", type=Path, default=reports / "ai-fortran-results.json")
    parser.add_argument("--out", type=Path, default=reports / "ai-comparison.json")
    parser.add_argument("--host", default="")
    args = parser.parse_args()

    besm = load(args.besm)
    python = load(args.python)
    fortran = load(args.fortran)

    by_id = {row["id"]: row for row in besm["rows"]}
    py = {row["id"]: row for row in python["rows"]}
    ft = {row["id"]: row for row in fortran["rows"]}
    ids = list(by_id)
    if set(ids) != set(py) or set(ids) != set(ft):
        raise SystemExit("algorithm ids differ between the three measurement files")

    rows = []
    for name in ids:
        b, p, f = by_id[name], py[name], ft[name]
        solve = b["compute_seconds"]
        p_solve = p["solve"]["median_s"]
        f_solve = f["solve"]["median_s"]
        p_process = p["process"]["median_s"]
        f_process = f["process"]["median_s"]
        total = b["original_total_seconds"]
        row = {
            "id": name,
            "besm_compute_s": solve,
            "besm_compute_se_s": b.get("compute_se", 0.0),
            "besm_exec_s": b["exec_seconds"],
            "besm_exec_se_s": b.get("exec_se", 0.0),
            "besm_io_s": b["io_seconds"],
            "besm_total_s": total,
            "besm_instructions_per_pass": b["instructions_per_pass"],
            "besm_instructions_per_second_exec": b["instructions_per_pass"] / b["exec_seconds"],
            "python_solve_s": p_solve,
            "python_solve_ci95_s": [p["solve"]["ci95_low_s"], p["solve"]["ci95_high_s"]],
            "python_process_s": p_process,
            "python_process_ci95_s": [p["process"]["ci95_low_s"], p["process"]["ci95_high_s"]],
            "fortran_solve_s": f_solve,
            "fortran_process_s": f_process,
            "speedup_besm_python_solve": solve / p_solve,
            "speedup_besm_fortran_solve": solve / f_solve,
            "speedup_fortran_python_solve": p_solve / f_solve,
            "speedup_besm_python_total": total / p_process,
            "speedup_besm_fortran_total": total / f_process,
            "speedup_python_fortran_process": p_process / f_process,
            "python_result": p["result"],
            "besm_output": f"reports/ai-besm-output/{name}.txt",
            "fortran_output": f"reports/ai-fortran-output/{name}.txt",
        }
        rows.append(row)

    def column(key: str) -> dict[str, float]:
        return stats([row[key] for row in rows])

    data = {
        "generated_at": dt.datetime.now().astimezone().isoformat(timespec="seconds"),
        "host": args.host or platform.node(),
        "platform": platform.platform(),
        "methods": {
            "besm": "differential guest model time; translation/loading and output separated",
            "python": python["measurement"],
            "python_process": python["process_measurement"],
            "fortran": fortran["measurement"],
        },
        "versions": {
            "python": python["python"],
            "gfortran": fortran["gfortran"],
        },
        "summary": {
            "speedup_besm_python_solve": column("speedup_besm_python_solve"),
            "speedup_besm_fortran_solve": column("speedup_besm_fortran_solve"),
            "speedup_fortran_python_solve": column("speedup_fortran_python_solve"),
            "speedup_besm_python_total": column("speedup_besm_python_total"),
            "speedup_besm_fortran_total": column("speedup_besm_fortran_total"),
            "speedup_python_fortran_process": column("speedup_python_fortran_process"),
        },
        "rows": rows,
    }
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"{'algorithm':20s} {'BESM solve':>11s} {'Py solve':>10s} {'S_solve':>9s} "
          f"{'S_solve_BF':>10s} {'S_FP solve':>10s}")
    for row in rows:
        print(f"{row['id']:20s} {row['besm_compute_s']*1e3:9.3f}ms "
              f"{row['python_solve_s']*1e6:8.2f}us "
              f"{row['speedup_besm_python_solve']:9.0f} "
              f"{row['speedup_besm_fortran_solve']:10.0f} "
              f"{row['speedup_fortran_python_solve']:10.2f}")
    print("\nSummary:")
    for name, value in data["summary"].items():
        print(f"  {name:32s} {value['min']:10.3g} .. {value['max']:10.3g}  "
              f"median {value['median']:.3g}")
    print(f"\nSaved: {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
