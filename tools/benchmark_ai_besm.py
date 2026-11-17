#!/usr/bin/env python3
"""Measure guest *execution* time of the BESM-6 AI examples.

The full job time reported by ``besm6 run`` includes translation, loading and
printing.  That dominated the numbers in the first version of the AI article,
so the per-algorithm "solve" times were not comparable with Python function
times.  This script isolates execution with the differential method suggested
in the review:

* wrap the whole executable body of a program in ``DO 9000 IREP=1,N`` (the
  body re-initialises all state, so every pass is an independent run);
* run the same source with N=1,2,3 -- the source length is unchanged, hence
  translation and loading costs cancel in ``T(N+1) - T(N)``;
* repeat with all ``PRINT`` statements replaced by comments of identical
  length to separate computation from line-printer output.

``T_compute`` is the analogue of the Python function time (data setup +
algorithm, no output).  ``T_exec`` additionally contains the program output.
Both exclude translation and loading.

Usage::

    python3 tools/benchmark_ai_besm.py
"""
from __future__ import annotations

import argparse
import json
import re
import statistics
import subprocess
import tempfile
from pathlib import Path

DECLARATION = re.compile(
    r"^\s*(program|real|integer|logical|double|complex|character|dimension|"
    r"common|external|implicit|data|save|equivalence|subroutine|function)\b",
    re.IGNORECASE,
)
STATS = re.compile(r"Execution stats:\s*")


def source_span(lines: list[str]) -> tuple[int, int]:
    """Return [start, end) of the Fortran source between *fortran and *execute."""
    start = end = None
    for i, line in enumerate(lines):
        card = line.strip().lower()
        if card in ("*fortran", "*ftn"):
            start = i + 1
        elif card.startswith("*execute") and start is not None:
            end = i
            break
    if start is None or end is None:
        raise ValueError("no *fortran/*execute cards")
    return start, end


def body_bounds(lines: list[str], start: int, end: int) -> tuple[int, int]:
    """First executable line and the STOP that closes the main program."""
    first = None
    for i in range(start, end):
        stripped = lines[i].strip()
        if not stripped or stripped[0] in ("c", "C", "*"):
            continue
        if DECLARATION.match(lines[i]):
            continue
        first = i
        break
    if first is None:
        raise ValueError("no executable statement")

    stop = None
    for i in range(first, end):
        if re.match(r"^\s*stop\b", lines[i], re.IGNORECASE):
            stop = i
            break
    if stop is None:
        raise ValueError("no STOP in main program")
    return first, stop


def instrument(lines: list[str], start: int, end: int, repeat: int, quiet: bool) -> str:
    first, stop = body_bounds(lines, start, end)
    out: list[str] = []
    for i, line in enumerate(lines):
        if i == first:
            out.append(f"      do 9000 irep=1,{repeat}")
        if i == stop:
            out.append(" 9000 continue")
        if quiet and re.match(r"^\s*(if\s*\([^)]*\)\s*)?print\b", line, re.IGNORECASE):
            # Keep the line length identical: comments do not affect translation
            # in the differential pair, but identical lengths keep the job image
            # as close as possible.
            out.append("c" + " " * max(0, len(line) - 1))
            continue
        out.append(line)
    return "\n".join(out) + "\n"


def run_job(dotnet: str, dll: Path, job: Path, timeout: float) -> tuple[float, int, str]:
    proc = subprocess.run(
        [dotnet, str(dll), "run", str(job), "--speed", "original",
         "--stats", "--guest-clock", "--no-wall-clock"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )
    text = proc.stdout + "\n" + proc.stderr
    match = None
    for match in STATS.finditer(text):
        pass
    if match is None:
        raise RuntimeError(f"{job.name}: no execution stats\n{text[-2000:]}")
    stats, _ = json.JSONDecoder().raw_decode(text[match.end():])
    stats = stats.get("statistics", stats)
    seconds = float(stats["modelSeconds"])
    instructions = int(stats["completedInstructions"])
    return seconds, instructions, text


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--examples", type=Path, default=root / "examples" / "ai")
    parser.add_argument("--out", type=Path, default=root / "reports" / "ai-besm-results.json")
    parser.add_argument("--outputs", type=Path, default=root / "reports" / "ai-besm-output")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--dll", type=Path,
                        default=root / "src" / "Besm6.Cli" / "bin" / "Release" / "net8.0" / "besm6.dll")
    parser.add_argument("--passes", type=int, default=3,
                        help="run N=1..passes and report first differences")
    parser.add_argument("--timeout", type=float, default=600.0)
    args = parser.parse_args()

    if not args.dll.exists():
        raise SystemExit(f"build the CLI first: {args.dll} not found")
    args.outputs.mkdir(parents=True, exist_ok=True)

    rows = []
    with tempfile.TemporaryDirectory(prefix="ai-besm-") as tmp:
        work = Path(tmp)
        for dub in sorted(args.examples.glob("*.dub")):
            lines = dub.read_text(encoding="utf-8").splitlines()
            start, end = source_span(lines)
            full: dict[int, float] = {}
            quiet: dict[int, float] = {}
            instructions: dict[int, int] = {}
            for repeat in range(1, args.passes + 1):
                for name, is_quiet, sink in (("full", False, full), ("quiet", True, quiet)):
                    job = work / f"{dub.stem}-{name}-{repeat}.dub"
                    job.write_text(instrument(lines, start, end, repeat, is_quiet), encoding="utf-8")
                    seconds, count, _ = run_job(args.dotnet, args.dll, job, args.timeout)
                    sink[repeat] = seconds
                    if not is_quiet:
                        instructions[repeat] = count
                    print(f"{dub.stem:20s} {name:5s} N={repeat}  {seconds:9.6f} s")

            def first_difference(values: dict[int, float]) -> float:
                diffs = [values[n + 1] - values[n] for n in range(1, args.passes)]
                return statistics.fmean(diffs)

            exec_s = first_difference(full)
            compute_s = first_difference(quiet)
            exec_diffs = [full[n + 1] - full[n] for n in range(1, args.passes)]
            compute_diffs = [quiet[n + 1] - quiet[n] for n in range(1, args.passes)]
            rows.append({
                "id": dub.stem,
                "exec_seconds": exec_s,
                "compute_seconds": compute_s,
                "io_seconds": exec_s - compute_s,
                "exec_differences": exec_diffs,
                "compute_differences": compute_diffs,
                "total_seconds_n1": full[1],
                "original_total_seconds": None,
                "instructions_n1": instructions[1],
                "instructions_per_pass": instructions[2] - instructions[1],
                "output_file": f"{dub.stem}.txt",
            })
            print(f"{dub.stem:20s} exec={exec_s:.6f}  compute={compute_s:.6f}  io={exec_s-compute_s:.6f}")

            # Keep the unmodified program output for the accuracy table.
            original = work / f"{dub.stem}-original.dub"
            original.write_text(dub.read_text(encoding="utf-8"), encoding="utf-8")
            original_seconds, _, text = run_job(args.dotnet, args.dll, original, args.timeout)
            rows[-1]["original_total_seconds"] = original_seconds
            (args.outputs / f"{dub.stem}.txt").write_text(text, encoding="utf-8")
            print()

    data = {
        "method": "differential guest model time, DO-loop repeat, translation/loading cancelled",
        "pass_differences": args.passes - 1,
        "rows": rows,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Saved: {args.out}")
    print(f"Outputs: {args.outputs}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
