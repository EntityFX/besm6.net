#!/usr/bin/env python3
"""Validate guest-owned ratings, guards and speed parity; not a throughput benchmark."""
import argparse
from decimal import Decimal
import hashlib
import json
import pathlib
import re
import subprocess
import sys

import benchmark_speed as bench

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCES = {
    "dh": "dhrystone-dmips.dub", "mp": "mpmflops-mflops.dub",
    "mem": "memspeed-mbps.dub", "whet": "whetstone-mwips.dub",
}
UNITS = {"dh": "DMIPS", "mp": "MFLOPS", "mem": "MB/S", "whet": "MWIPS"}
CASES = ("dh-good", "dh-zero", "dh-off", "dh-bad", "dh-short",
         "mp1-good", "mp2-good", "mp3-good", "mp-zero", "mp-off", "mp-bad", "mp-short",
         "mem1-good", "mem2-good", "mem3-good", "mem-zero", "mem-off", "mem-bad", "mem-short",
         "whet-good", "whet-zero", "whet-off", "whet-bad", "whet-short")


def substitute(source, old, new):
    if source.count(old) != 1:
        raise ValueError("Guest source anchor changed or is ambiguous")
    return source.replace(old, new)


def prepare(case):
    family = "dh" if case.startswith("dh") else "mp" if case.startswith("mp") else "mem" if case.startswith("mem") else "whet"
    source = (ROOT / "examples" / SOURCES[family]).read_text(encoding="utf-8-sig")
    scenario = case.split("-")[1]
    part = int(case.split("-")[0][-1]) if case.split("-")[0][-1].isdigit() else 1
    clock = scenario != "off"
    if family == "dh":
        if scenario in ("zero", "short"):
            source = substitute(source, "DATA NUMBER/5000/", "DATA NUMBER/0/" if scenario == "zero" else "DATA NUMBER/1/")
        if scenario == "bad":
            source = substitute(source, "      IF (NBAD .EQ. 0) PRINT 1200", "      NBAD=NBAD+1\n      IF (NBAD .EQ. 0) PRINT 1200")
    elif family in ("mp", "mem"):
        source = substitute(source, "data kpart/1/", f"data kpart/{0 if scenario == 'zero' else part}/")
        if scenario == "bad":
            anchor = "      nbad = 0" if family == "mp" else "      nbad=0"
            source = substitute(source, anchor, "      x(1)=0.\n" + anchor)
        if scenario == "short":
            # A genuine small kernel with matching local verification constants.
            if family == "mp":
                source = substitute(source, "nwrds/9000/, rpts/30/", "nwrds/1/, rpts/1/")
                source = substitute(source, "nwrds .ne. 9000 .or. rpts .ne. 30", "nwrds .ne. 1 .or. rpts .ne. 1")
                source = substitute(source, f"refval={float(bench.reference_result(1)):.14f}",
                                    f"refval={float(bench.reference_result(1, 1)):.14f}")
            else:
                source = substitute(source, "nwrds/4096/, rpts/1000/", "nwrds/1/, rpts/1/")
                source = substitute(source, "nwrds .ne. 4096 .or. rpts .ne. 1000", "nwrds .ne. 1 .or. rpts .ne. 1")
    else:
        if scenario in ("zero", "short"):
            source = substitute(source, "DATA NRUNS / 100 /", "DATA NRUNS / 0 /" if scenario == "zero" else "DATA NRUNS / 1 /")
        if scenario == "bad":
            source = substitute(source, "DATA REFVAL / -1.13323943676865", "DATA REFVAL / 0.")
        if scenario == "short":
            # Deterministic guard probe: one observed timer quantum. Section
            # controls still execute and pass; no rating may be printed.
            source = substitute(source, "      OPS=NOPS*FLOAT(NRUNS)", "      SECS=0.02\n      OPS=NOPS*FLOAT(NRUNS)")
    return family, scenario, part, clock, source


def extract(output, returncode):
    if returncode != 0:
        raise ValueError("Guest process did not succeed")
    halted = re.search(r"Halted by STOP at \S+ after (\d+) instructions", output)
    if not halted or "did not terminate" in output:
        raise ValueError("Guest did not reach STOP")
    program = output.rsplit("≠", 1)[-1].split("Halted by STOP", 1)[0].strip()
    stats = re.search(r"^Execution stats: (.+)$", output, re.M)
    if not stats:
        raise ValueError("Final state/statistics are missing")
    return program, int(halted[1]), json.loads(stats[1])


def validate(program, family, scenario, part):
    unit = UNITS[family]
    score = re.search(r"^GUEST (?:MODEL )?" + re.escape(unit) + r":\s*(\S+)", program, re.M)
    if scenario != "good":
        reason = "ZERO" if scenario == "zero" else "CONTROL VALUES DO NOT MATCH" if scenario == "bad" else "ENABLE --GUEST-CLOCK OR RUN LONGER"
        unavailable = re.search(r"^" + re.escape(unit + " UNAVAILABLE: " + reason) + r"(?:\s.*)?$", program, re.M)
        if score or not unavailable:
            raise ValueError("Guest rating guard failed")
        return {"rating": None, "guard": scenario}
    seconds = re.search(r"^GUEST (?:MODEL )?CPU SECONDS:\s*(\S+)", program, re.M)
    if not score or not seconds:
        raise ValueError("Guest rating/time are missing")
    elapsed = bench.fortran_number(seconds[1])
    printed = bench.fortran_number(score[1])
    if elapsed < Decimal("0.04") or printed <= 0:
        raise ValueError("Guest reported a rating from an insufficient interval")
    if family == "dh":
        if "ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC" not in program:
            raise ValueError("Dhrystone controls failed")
        expected = Decimal(5000) / (elapsed * 1757)
    elif family == "mp":
        if "ALL SAME: YES" not in program:
            raise ValueError("MPMFLOPS controls failed")
        first = re.search(r"FIRST RESULT=\s*(\S+)", program)
        reference = bench.reference_result(part)
        if not first or abs((bench.fortran_number(first[1]) - reference) / reference) > Decimal("1e-9"):
            raise ValueError("MPMFLOPS independent numerical reference failed")
        expected = Decimal(9000 * 30 * bench.OPWD[part]) / (elapsed * 1_000_000)
    elif family == "mem":
        if "ALL VALUES MATCH: YES" not in program or not re.search(r"BAD ELEMENTS=\s*0\b", program):
            raise ValueError("Memspeed controls failed")
        traffic = 4096 * 1000 * (12 if part == 1 else 18)
        expected = Decimal(traffic) / (elapsed * 1_000_000)
    else:
        if "ALL 8 CONTROL VALUES MATCH: YES" not in program or not re.search(r"BAD CONTROL VALUES=\s*0\b", program):
            raise ValueError("Whetstone controls failed")
        reference = ["-1.1332394367686494", "-1.1332181986811034", "0", "12",
                     "0.49999052352563617", "0.99999988878912238", "3", "0.75001003274361189"]
        for index, value in enumerate(reference, 1):
            match = re.search(r"^N" + str(index) + r"\s+.+?\s+(\S+)\s*$", program, re.M)
            if not match or abs(bench.fortran_number(match[1]) - Decimal(value)) > Decimal("1e-8"):
                raise ValueError("Whetstone independent section reference failed")
        expected = Decimal(15869 * 100 * 10) / (elapsed * 1_000_000)
    if abs(printed - expected) > max(Decimal("0.0000006"), abs(expected) * Decimal("0.000002")):
        raise ValueError("Guest score formula failed")
    return {"rating": float(printed), "seconds": float(elapsed), "unit": unit, "guard": None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--cases", nargs="+", choices=CASES, default=list(CASES))
    parser.add_argument("--modes", nargs="+", choices=("max", "original", "virtual"), default=["max", "original"])
    parser.add_argument("--timeout", type=int, default=600)
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Choose a fresh output directory; previous evidence is retained")
    args.output.mkdir(parents=True)
    (args.output / "logs").mkdir()
    (args.output / "jobs").mkdir()
    config = args.output / "config.json"
    config.write_text(json.dumps({"tapes": str(ROOT / "tapes"), "useWallClock": False}), encoding="utf-8")
    result = {"validated": False, "scope": "Guest formula/control/guard checks; exact parity only between model-clock speeds",
              "environment": bench.report_environment(), "dll_sha256": hashlib.sha256(args.dll.read_bytes()).hexdigest(),
              "cases": [], "minimum_guest_seconds": 0.04}

    def save():
        (args.output / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")

    try:
        for case in args.cases:
            family, scenario, part, clock, source = prepare(case)
            deck = args.output / "jobs" / (case + ".dub")
            deck.write_text(source, encoding="utf-8")
            reference = None
            for speed in args.modes:
                command = ["dotnet", str(args.dll), "run", str(deck), "--config", str(config),
                           "--no-wall-clock", "--no-hang-detect", "--speed", speed, "--stats", "--limit", "200000000"]
                if clock:
                    command.append("--guest-clock")
                run = subprocess.run(command, cwd=ROOT, capture_output=True, encoding="utf-8", errors="replace", timeout=args.timeout)
                output = run.stdout + run.stderr
                (args.output / "logs" / (case + "-" + speed + ".out")).write_text(output, encoding="utf-8")
                program, instructions, report = extract(output, run.returncode)
                if scenario == "good" and family != "whet":
                    job = "dh5000" if family == "dh" else family + str(part)
                    bench.parse_run(output, job, run.returncode)
                rated = validate(program, family, scenario, part)
                state = {"program_sha256": hashlib.sha256(program.encode()).hexdigest(), "halt_instructions": instructions,
                         "finalState": report["finalState"], "memorySha256": report["memorySha256"],
                         "instructions": report["statistics"]["completedInstructions"], "cycles": report["statistics"]["modelCycles"]}
                if speed != "virtual" and reference is not None and state != reference:
                    raise ValueError("Guest rating/state/count/time changed with speed")
                if speed != "virtual":
                    reference = state
                result["cases"].append({"case": case, "speed": speed, "clock": clock,
                                        "source_sha256": hashlib.sha256(source.encode()).hexdigest(),
                                        "state": state, **rated})
                save()
                print(case, speed, "passed", flush=True)
        result["validated"] = True
        save()
        return 0
    except (ValueError, subprocess.TimeoutExpired) as error:
        result["error"] = "Guest verification failed; details remain in local logs"
        save()
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
