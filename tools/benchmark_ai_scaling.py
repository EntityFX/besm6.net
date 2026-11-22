#!/usr/bin/env python3
"""Generate and verify ten scaled BESM-6 AI families and one MLP control.

The standalone AiScalingProbe observes the original timing model without wall
clock pacing. Its equivalence to original mode must be validated separately.
No modern-platform performance claims are made by this script.
"""
from __future__ import annotations

import argparse
import concurrent.futures
from dataclasses import asdict
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
from ai_scaling_cases import cases, generate


def extract_results(text):
    result = []
    for line in text.splitlines():
        if re.match(r"^(RESULT|EXTRA)\s", line):
            for token in line.split()[1:]:
                result.append(float(token.replace("D", "E")))
    return result


def compare(expected, observed, float_atol=1e-5, float_rtol=1e-7):
    problems = []
    if len(expected) != len(observed):
        return [f"Expected {len(expected)} result fields, observed {len(observed)}"]
    for i, (want, got) in enumerate(zip(expected, observed)):
        tolerance = 0.0 if isinstance(want, int) else float_atol + abs(want) * float_rtol
        if abs(want - got) > tolerance:
            problems.append(f"field {i}: expected {want}, got {got}, tolerance {tolerance}")
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--levels", default="0,1,2,3")
    parser.add_argument("--algorithms", default="")
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--generate-only", action="store_true")
    parser.add_argument("--prepared", action="store_true", help="use and hash-check the frozen full manifest without rewriting inputs")
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--mode", choices=["original", "unpaced"], default="unpaced")
    parser.add_argument("--max-model-seconds", type=float, default=120)
    args = parser.parse_args()
    work = args.work.resolve()
    for folder in ("jobs", "native", "profiles"):
        (work / folder).mkdir(parents=True, exist_ok=True)
    levels = {int(x) for x in args.levels.split(",")}
    algorithms = set(filter(None, args.algorithms.split(",")))
    selection = args.levels.replace(",", "-")
    if algorithms:
        selection += "-" + "-".join(sorted(algorithms))
    selected = [c for c in cases() if c.level in levels and (not algorithms or c.algorithm in algorithms)]
    manifest = []
    if args.prepared:
        frozen = json.loads((work / "manifest-0-1-2-3.json").read_text())
        selected_ids = {case.id for case in selected}
        manifest = [case for case in frozen if case["id"] in selected_ids]
        if len(manifest) != len(selected): raise ValueError("Frozen manifest does not contain every selected case")
        for case in manifest:
            actual = hashlib.sha256((work / "jobs" / (case["id"] + ".dub")).read_bytes()).hexdigest()
            if actual != case["sourceSha256"]: raise ValueError("Frozen source hash mismatch: " + case["id"])
    else:
        for case in selected:
            started = time.monotonic()
            source, expected = generate(case)
            path = work / "jobs" / (case.id + ".dub")
            path.write_text(source, encoding="utf-8", newline="\n")
            native = source.split("*fortran\n", 1)[1].split("*execute", 1)[0]
            (work / "native" / (case.id + ".f")).write_text(native, encoding="utf-8", newline="\n")
            manifest.append({**asdict(case), "id": case.id, "sourceSha256": hashlib.sha256(source.encode()).hexdigest(), "expected": expected, "sourceLines": len(source.splitlines()), "referenceSeconds": time.monotonic()-started})
            print(f"Generated {case.id}: {expected}", flush=True)
        (work / ("manifest-" + selection + ".json")).write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    if args.generate_only:
        return 0

    def execute(case):
        destination = work / "profiles" / (case["id"] + "-" + args.mode + ".json")
        raw = None
        if args.resume and destination.exists():
            candidate = json.loads(destination.read_text(encoding="utf-8-sig"))
            if candidate.get("sourceSha256", "").lower() == case["sourceSha256"] and candidate.get("success"):
                raw = candidate
        if raw is None:
            command = ["dotnet", str(work / "probe" / "AiScalingProbe.dll"), str(work / "jobs" / (case["id"] + ".dub")), str(destination), str(work / "cli"), args.mode, str(args.max_model_seconds)]
            process = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=260)
            (work / "profiles" / (case["id"] + "-" + args.mode + ".stdout.txt")).write_text(process.stdout + process.stderr, encoding="utf-8")
            if not destination.exists():
                return {**case, "verified": False, "problems": ["Probe did not produce JSON", process.stdout[-1000:], process.stderr[-1000:]]}
            raw = json.loads(destination.read_text(encoding="utf-8-sig"))
        observed = extract_results(raw.get("output", ""))
        problems = compare(case["expected"], observed)
        if not raw.get("success"): problems.append(raw.get("error") or raw.get("outcome") or "Execution failed")
        if raw.get("computeSeconds") is None: problems.append("First formatted output entry was not observed")
        if not raw.get("observerCountMatchesStatistics"): problems.append("Instruction observer mismatch")
        return {**case, "observed": observed, "verified": not problems, "problems": problems,
                **{key: raw.get(key) for key in ("originalModelSeconds", "beforeProgramSeconds", "computeSeconds", "computeInstructions", "computeFraction", "completedInstructions", "freeAddressOctal", "allocatedUserWordsFromLinkMap", "hostElapsedSeconds", "executionMode", "memorySha256")}}

    results = []
    summary = work / ("scaling-" + args.mode + "-" + selection + ".json")
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as executor:
        futures = {executor.submit(execute, case): case for case in manifest}
        for future in concurrent.futures.as_completed(futures):
            case = futures[future]
            try: result = future.result()
            except Exception as error: result = {**case, "verified": False, "problems": [repr(error)]}
            results.append(result)
            print(f"{result['id']}: verified={result['verified']} compute={result.get('computeSeconds')} total={result.get('originalModelSeconds')} {result['problems']}", flush=True)
            summary.write_text(json.dumps(sorted(results, key=lambda x: (x['algorithm'], x['level'])), indent=2), encoding="utf-8")
    return 0 if all(row["verified"] for row in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
