#!/usr/bin/env python3
"""Run scaled Fortran programs under SIMH/DISPAK, using private disk copies.

Requires a work directory containing simh-source/, images/, jobs/, native/ and
manifest-0-1-2-3.json. No runtime installation or privileged operation is needed.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
from benchmark_ai_scaling import compare, extract_results


def normalize(text):
    # The guest's GOST alphabet uses Cyrillic glyphs for Latin lookalikes.
    return text.translate(str.maketrans("АВСЕНКМОРТХУ", "ABCEHKMOPTXY")).replace("−", "-")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--levels", default="0,1,2,3")
    parser.add_argument("--algorithms", default="")
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("--port", type=int, default=50094)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--keep-disks", action="store_true")
    parser.add_argument("--translator", choices=("ftn", "fortran"), default="ftn")
    args = parser.parse_args()
    work = args.work.resolve()
    src = work / "simh-source" / "BESM6"
    executable = work / "simh-source" / "BIN" / "besm6"
    levels = {int(v) for v in args.levels.split(",")}
    algorithms = set(filter(None, args.algorithms.split(",")))
    summary_name = "simh-results"
    if args.translator != "ftn": summary_name += "-" + args.translator
    if algorithms:
        summary_name += "-" + "-".join(sorted(algorithms))
    manifest = json.loads((work / "manifest-0-1-2-3.json").read_text())
    selected = [c for c in manifest if c["level"] in levels and (not algorithms or c["algorithm"] in algorithms)]
    root = work / "simh-runs"
    root.mkdir(exist_ok=True)
    template = (src / "input.txt").read_text(encoding="utf-8")
    prefix = template.split("*NAME", 1)[0]
    suffix = template.split("*END FILE", 1)[1]
    boot = (src / "dispak.ini").read_text(encoding="utf-8")
    boot = boot.replace("attach -e md05 krab2063.bin", "; Optional KRAB disk not present in the published image set")
    boot = boot.replace("attach tty 4199", f"attach tty 127.0.0.1:{args.port}")
    driver = "show version\nsend delay=10000\n" + (src / "expect.ini").read_text(encoding="utf-8")
    driver = driver.replace("do dispak.ini", "do boot.ini") + "\nquit\n"
    driver = driver.replace(r'send "HOMB\r"', r'send after=1000000,delay=10000,"HOMB\r"')
    # NOMB can report an empty queue while the printer still owns an active
    # buffer. Drain scheduled device events before quitting the simulator.
    driver = driver.replace("echo Done", "noexpect\nstep 100000000\necho AI_SCALING_DRIVER_COMPLETE")
    images = ["sbor2048.bin", "sbor2053.bin", "svs2048.bin", "alt2048.bin"]
    simh_commit = (work / "simh-commit.txt").read_text().strip()
    results = []
    for case in selected:
        prefix_name = case["id"] + ("-" + args.translator if args.translator != "ftn" else "")
        existing = sorted(root.glob(prefix_name + "-attempt-*/result.json"))
        cached = None
        if args.resume:
            for path in reversed(existing):
                candidate = json.loads(path.read_text())
                if candidate.get("verified") and candidate.get("sourceSha256") == case["sourceSha256"] and candidate.get("translatorControlCard", "ftn") == args.translator:
                    cached = candidate; break
        if cached:
            results.append(cached)
            print(case["id"], "cached=True", flush=True)
            continue
        attempt = len(list(root.glob(prefix_name + "-attempt-*"))) + 1
        folder = root / (prefix_name + f"-attempt-{attempt:03d}")
        folder.mkdir()
        for image in images:
            # Linux cp can create cheap COW copies, falling back to a full copy.
            subprocess.run(["cp", "--reflink=auto", str(work / "images" / image), str(folder / image)], check=True)
        shutil.copy2(src / "boot_dispak.b6", folder)
        body = (work / "native" / (case["id"] + ".f")).read_text()
        packet = prefix + "*NAME " + case["id"].upper() + "\n*TIME:10.00\n*NO LOAD LIST\n*CALL NOTLIS\n*" + args.translator.upper() + "\n" + body + "*EXECUTE\n*END FILE" + suffix
        (folder / "input.txt").write_text(packet, encoding="utf-8")
        (folder / "boot.ini").write_text(boot, encoding="utf-8")
        (folder / "run.ini").write_text(driver, encoding="utf-8")
        started = time.monotonic()
        timed_out = False
        try:
            process = subprocess.run([str(executable), "run.ini"], cwd=folder, stdin=subprocess.DEVNULL, capture_output=True, timeout=args.timeout)
            console = (process.stdout + process.stderr).decode("utf-8", errors="replace")
            returncode = process.returncode
        except subprocess.TimeoutExpired as error:
            timed_out = True
            console = ((error.stdout or b"") + (error.stderr or b"")).decode("utf-8", errors="replace")
            returncode = None
        (folder / "console.txt").write_text(console, encoding="utf-8")
        printer = (folder / "output.txt").read_text(encoding="utf-8", errors="replace") if (folder / "output.txt").exists() else ""
        observed = extract_results(normalize(printer))
        problems = compare(case["expected"], observed)
        if timed_out: problems.append("SIMH host timeout")
        if returncode not in (0, None): problems.append(f"SIMH exit {returncode}")
        if "AI_SCALING_DRIVER_COMPLETE" not in console: problems.append("Driver did not reach its completion marker")
        result = {**case, "backend": "SIMH/DISPAK/" + args.translator.upper(), "simhCommit": simh_commit, "translatorControlCard": args.translator,
                  "observed": observed, "verified": not problems, "problems": problems,
                  "hostSeconds": time.monotonic()-started, "returncode": returncode,
                  "packetSha256": hashlib.sha256(packet.encode()).hexdigest(),
                  "runDirectory": str(folder), "compilerDates": sorted(set(re.findall(r"\d\d\.\d\d\.\d\d", printer)))}
        (folder / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        results.append(result)
        (work / (summary_name + ".json")).write_text(json.dumps(results, indent=2), encoding="utf-8")
        print(case["id"], "verified=" + str(not problems), "seconds=" + str(round(result["hostSeconds"], 2)), problems, flush=True)
        if not args.keep_disks:
            for image in images + ["2052.bin", "drum1x.bin", "drum2x.bin"]:
                (folder / image).unlink(missing_ok=True)
    return 0 if all(row["verified"] for row in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
