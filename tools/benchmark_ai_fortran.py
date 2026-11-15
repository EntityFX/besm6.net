#!/usr/bin/env python3
"""Compile the BESM-6 AI examples with modern gfortran and time them.

The .dub control cards are stripped, the fixed-form source is compiled with
-O2 -std=legacy -ffixed-form, and the resulting executable is run repeatedly.
Timing is end-to-end (process startup + algorithm + output), because the
algorithms themselves are far below process-level timer resolution.
"""
from __future__ import annotations

import argparse
import json
import statistics
import subprocess
import tempfile
import time
from pathlib import Path

BASELINE = """      program base
      print 10, 1
      stop
   10 format(i5)
      end
"""


def extract_source(dub: Path) -> str:
    lines = dub.read_text(encoding="utf-8").splitlines()
    start = None
    for i, line in enumerate(lines):
        if line.strip().lower() in ("*fortran", "*ftn"):
            start = i + 1
            break
    if start is None:
        raise ValueError(f"{dub}: no *fortran/*ftn card")
    end = None
    for j in range(start, len(lines)):
        if lines[j].strip().lower().startswith("*execute"):
            end = j
            break
    if end is None:
        raise ValueError(f"{dub}: no *execute card")
    return "\n".join(lines[start:end]) + "\n"


def compile_one(dub: Path, work: Path, gfortran: str) -> Path:
    source = work / (dub.stem + ".f")
    exe = work / dub.stem
    source.write_text(extract_source(dub), encoding="utf-8")
    result = subprocess.run(
        [gfortran, "-O2", "-std=legacy", "-ffixed-form", "-o", str(exe), str(source)],
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(f"{dub.name}: gfortran failed\n{result.stderr}")
    return exe


def measure(exe: Path, repeats: int) -> tuple[float, float, float, int, str]:
    times: list[float] = []
    last = None
    for _ in range(repeats):
        started = time.perf_counter()
        last = subprocess.run([str(exe)], capture_output=True, text=True)
        times.append(time.perf_counter() - started)
    return (
        statistics.median(times),
        min(times),
        max(times),
        last.returncode if last else -1,
        last.stdout[-800:] if last else "",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--examples", type=Path, default=Path("examples/ai"))
    parser.add_argument("--out", type=Path, default=Path("reports/ai-fortran-results.json"))
    parser.add_argument("--gfortran", default="gfortran")
    parser.add_argument("--repeats", type=int, default=500)
    parser.add_argument("--mlp-repeats", type=int, default=50)
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="ai-fortran-") as tmp:
        work = Path(tmp)
        baseline_source = work / "baseline.f"
        baseline_exe = work / "baseline"
        baseline_source.write_text(BASELINE, encoding="utf-8")
        subprocess.run(
            [args.gfortran, "-O2", "-std=legacy", "-ffixed-form", "-o", str(baseline_exe), str(baseline_source)],
            check=True,
            capture_output=True,
            text=True,
        )
        baseline_s, _, _, _, _ = measure(baseline_exe, args.repeats)

        rows = []
        for dub in sorted(args.examples.glob("*.dub")):
            exe = compile_one(dub, work, args.gfortran)
            repeats = args.mlp_repeats if dub.stem.startswith("10_") else args.repeats
            med, low, high, rc, tail = measure(exe, repeats)
            rows.append(
                {
                    "id": dub.stem,
                    "full_seconds": med,
                    "min_seconds": low,
                    "max_seconds": high,
                    "repeats": repeats,
                    "returncode": rc,
                    "stdout_tail": tail,
                }
            )
            print(f"{dub.stem:20s} median={med*1000:7.3f} ms  min={low*1000:7.3f}  max={high*1000:7.3f}")

    data = {
        "gfortran": subprocess.run([args.gfortran, "--version"], capture_output=True, text=True).stdout.splitlines()[0],
        "flags": "-O2 -std=legacy -ffixed-form",
        "baseline_seconds": baseline_s,
        "rows": rows,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nbaseline {baseline_s*1000:.3f} ms")
    print(f"Saved: {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
