#!/usr/bin/env python3
"""Compile the BESM-6 AI examples with modern gfortran and measure them.

Two metrics are produced:

``process``
    end-to-end time of the unchanged program (process start + algorithm +
    output), as in the first version of the article.

``solve``
    in-process time of the algorithm itself.  The main program is turned into
    an external subroutine that returns one result, all ``PRINT`` statements
    are removed, and a separate driver calls it many times between two
    ``system_clock`` readings.  Because the subroutine is compiled in another
    translation unit, the compiler cannot inline or collapse the repeated
    calls, and because the returned value is accumulated and printed by the
    driver, it cannot delete the computation.  The repeat count is read at run
    time.

The unchanged executable's stdout is stored under ``reports/ai-fortran-output``
so the accuracy table is backed by an artifact.
"""
from __future__ import annotations

import argparse
import json
import os
import re
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

RESULT_EXPR = {
    "01_perceptron": "w1",
    "02_knn": "p",
    "03_kmeans": "c1",
    "04_naive_bayes": "s(1)",
    "05_decision_stump": "best",
    "06_genetic": "bf",
    "07_annealing": "fb",
    "08_qlearning": "q(1,1)",
    "09_astar": "l",
    "10_mlp_xor": "loss",
    "11_chess_mate": "mate",
}

PER_PASS = re.compile(r"PER_PASS\s+([0-9.Ee+\-]+)")


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


def to_subroutine(source: str, stem: str) -> tuple[str, str]:
    """Return (subroutine source, subroutine name) for one .dub program."""
    lines = source.splitlines()
    program = next((i for i, line in enumerate(lines)
                    if re.match(r"^\s*program\b", line, re.IGNORECASE)), None)
    if program is None:
        raise ValueError("no PROGRAM statement")
    end = next((i for i in range(program + 1, len(lines))
                if lines[i].strip().lower() == "end"), None)
    if end is None:
        raise ValueError("no END of main program")

    result = RESULT_EXPR[stem]
    name = "ai_" + stem[3:]
    main: list[str] = []
    for i in range(program, end + 1):
        line = lines[i]
        if i == program:
            main.append(f"      subroutine {name}(aiout)")
            main.append("      real*8 aiout")
            continue
        if re.match(r"^\s*(if\s*\([^)]*\)\s*)?print\b", line, re.IGNORECASE):
            main.append("c" + " " * max(0, len(line) - 1))
            continue
        if re.match(r"^\s*stop\b", line, re.IGNORECASE):
            main.append("c" + " " * max(0, len(line) - 1))
            continue
        if lines[i].strip().lower() == "end":
            main.append(f"      aiout=dble({result})")
            main.append("      end")
            continue
        main.append(line)
    # Keep any subroutines that follow the main program (chess has CHK).
    return "\n".join(main + lines[end + 1:]) + "\n", name


def driver_source(name: str) -> str:
    return f"""      program driver
      integer*8 t0,t1,rate
      integer n,i
      real*8 aiout,sum,elapsed
      character*32 arg
      external {name}
      n=1
      call getarg(1,arg)
      if (arg .ne. ' ') read(arg,'(i10)') n
      sum=0.
      call system_clock(t0,rate)
      do 10 i=1,n
        call {name}(aiout)
        sum=sum+aiout
   10 continue
      call system_clock(t1,rate)
      elapsed=dble(t1-t0)/dble(rate)/dble(n)
      print 9998, elapsed, sum
 9998 format('PER_PASS ',e16.8,' SUM ',e16.8)
      end
"""


def run_gfortran(gfortran: str, args: list[str]) -> None:
    result = subprocess.run([gfortran, *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("gfortran failed\n" + result.stderr)


def compile_one(name: str, source: str, work: Path, gfortran: str) -> Path:
    src = work / (name + ".f")
    exe = work / name
    src.write_text(source, encoding="utf-8")
    run_gfortran(gfortran, ["-O2", "-std=legacy", "-ffixed-form",
                            "-o", str(exe), str(src)])
    return exe


def compile_timed(stem: str, source: str, work: Path, gfortran: str) -> Path:
    sub_src = work / (stem + "_sub.f")
    drv_src = work / "driver.f"
    sub_obj = work / (stem + "_sub.o")
    drv_obj = work / "driver.o"
    exe = work / (stem + "_timed.exe")
    body, name = to_subroutine(source, stem)
    sub_src.write_text(body, encoding="utf-8")
    drv_src.write_text(driver_source(name), encoding="utf-8")
    # -O0 for the timed unit: these toy programs are deterministic, and -O2
    # constant-folds several of them (k-NN, decision stump, genetic) to nothing.
    flags = ["-O0", "-std=legacy", "-ffixed-form"]
    run_gfortran(gfortran, [*flags, "-c", str(sub_src), "-o", str(sub_obj)])
    run_gfortran(gfortran, [*flags, "-c", str(drv_src), "-o", str(drv_obj)])
    run_gfortran(gfortran, [str(sub_obj), str(drv_obj), "-o", str(exe)])
    return exe


def measure_process(exe: Path, repeats: int) -> tuple[list[float], str]:
    times = []
    last = ""
    for _ in range(repeats):
        started = time.perf_counter()
        result = subprocess.run([str(exe)], capture_output=True, text=True)
        times.append(time.perf_counter() - started)
        last = result.stdout
    return times, last


def run_instrumented(exe: Path, n: int) -> float:
    result = subprocess.run([str(exe), str(n)], capture_output=True, text=True)
    match = PER_PASS.search(result.stdout)
    if result.returncode != 0 or match is None:
        raise RuntimeError(f"instrumented run failed:\n{result.stdout}\n{result.stderr}")
    return float(match.group(1))


def calibrate(exe: Path, target: float) -> tuple[int, float]:
    n = 100
    for _ in range(12):
        seconds = run_instrumented(exe, n)
        if seconds > 0 and seconds * n >= target:
            return n, seconds
        n = min(100_000_000, n * (100 if seconds <= 0 else 10))
    return n, seconds


def summarise(values: list[float]) -> dict[str, float]:
    return {
        "median_s": statistics.median(values),
        "min_s": min(values),
        "max_s": max(values),
        "samples": len(values),
    }


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--examples", type=Path, default=root / "examples" / "ai")
    parser.add_argument("--out", type=Path, default=root / "reports" / "ai-fortran-results.json")
    parser.add_argument("--outputs", type=Path, default=root / "reports" / "ai-fortran-output")
    parser.add_argument("--gfortran", default="gfortran")
    parser.add_argument("--repeats", type=int, default=200)
    parser.add_argument("--mlp-repeats", type=int, default=50)
    parser.add_argument("--samples", type=int, default=7)
    parser.add_argument("--target", type=float, default=0.3)
    args = parser.parse_args()

    # MinGW executables need libgfortran/libgcc/libwinpthread from the compiler
    # bin directory, which is not necessarily on PATH.
    compiler_dir = str(Path(args.gfortran).resolve().parent)
    os.environ["PATH"] = compiler_dir + os.pathsep + os.environ.get("PATH", "")

    args.outputs.mkdir(parents=True, exist_ok=True)
    version = subprocess.run([args.gfortran, "--version"], capture_output=True,
                             text=True).stdout.splitlines()[0]

    with tempfile.TemporaryDirectory(prefix="ai-fortran-") as tmp:
        work = Path(tmp)
        base = compile_one("baseline", BASELINE, work, args.gfortran)
        baseline_times, _ = measure_process(base, args.repeats)
        baseline = summarise(baseline_times)

        rows = []
        for dub in sorted(args.examples.glob("*.dub")):
            if dub.stem not in RESULT_EXPR:
                raise SystemExit(f"add a result expression for {dub.stem}")
            source = extract_source(dub)
            original = compile_one(dub.stem, source, work, args.gfortran)
            repeats = args.mlp_repeats if dub.stem.startswith("10_") else args.repeats
            process_times, stdout = measure_process(original, repeats)
            (args.outputs / f"{dub.stem}.txt").write_text(stdout, encoding="utf-8")

            timed = compile_timed(dub.stem, source, work, args.gfortran)
            n, _ = calibrate(timed, args.target)
            solve_times = [run_instrumented(timed, n) for _ in range(args.samples)]

            rows.append({
                "id": dub.stem,
                "process": summarise(process_times),
                "solve": summarise(solve_times),
                "solve_scale": n,
                "repeats": repeats,
                "returncode": 0,
                "stdout_tail": stdout[-800:],
                "output_file": f"{dub.stem}.txt",
            })
            print(f"{dub.stem:20s} solve={statistics.median(solve_times)*1e6:10.3f} us "
                  f"(N={n})  process={statistics.median(process_times)*1e3:7.3f} ms")

    data = {
        "gfortran": version,
        "flags": "-O2 -std=legacy -ffixed-form",
        "baseline": baseline,
        "measurement": "solve: external subroutine called repeatedly, system_clock, no output; process: unchanged executable",
        "rows": rows,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nbaseline {baseline['median_s']*1e3:.3f} ms")
    print(f"Saved: {args.out}")
    print(f"Outputs: {args.outputs}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
