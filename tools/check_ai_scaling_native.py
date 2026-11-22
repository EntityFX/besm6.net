#!/usr/bin/env python3
"""Check generated scaling sources with native GNU Fortran; no timing claims."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from benchmark_ai_scaling import compare, extract_results


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--manifest", default="manifest-0-1-2-3.json")
    args = parser.parse_args()
    work = args.work.resolve()
    manifest = json.loads((work / args.manifest).read_text())
    target = work / "native-results"
    target.mkdir(exist_ok=True)
    version = subprocess.check_output(["gfortran", "--version"], text=True).splitlines()[0]
    results = []
    for case in manifest:
        executable = target / case["id"]
        compile_result = subprocess.run(["gfortran", "-std=legacy", "-ffixed-line-length-none", "-O0", "-fcheck=all", "-ftrapv", str(work / "native" / (case["id"] + ".f")), "-o", str(executable)], capture_output=True, text=True, timeout=30)
        run = None
        observed = []
        problems = []
        if compile_result.returncode:
            problems.append("Compilation failed")
        else:
            run = subprocess.run([str(executable)], capture_output=True, text=True, timeout=60)
            if run.returncode: problems.append("Native execution failed")
            observed = extract_results(run.stdout)
            problems.extend(compare(case["expected"], observed))
        (target / (case["id"] + ".txt")).write_text(compile_result.stdout + compile_result.stderr + (run.stdout + run.stderr if run else ""))
        row = {**case, "compiler": version, "observed": observed, "verified": not problems, "problems": problems}
        results.append(row)
        (work / "native-results.json").write_text(json.dumps(results, indent=2))
        print(case["id"], "verified=" + str(not problems), problems, flush=True)
    return 0 if all(row["verified"] for row in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
