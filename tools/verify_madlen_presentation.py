#!/usr/bin/env python3
"""Run presentation MADLEN jobs, boundary cases and failing-check controls.

Build the Release CLI first. The historical MADLEN translator and loader are
executed by the emulator; this does not use the host-side assembler API.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import tempfile
from pathlib import Path


def replace_once(source: str, old: str, new: str) -> str:
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence: {old!r}")
    return source.replace(old, new, 1)


def check_documentation(folder: Path, jobs: list[Path]) -> None:
    """Do not let presentation snippets drift from the executable examples."""
    def normal(text: str) -> str:
        return "\n".join(line.rstrip() for line in text.strip("\n").splitlines())

    document = (folder / "README.md").read_text(encoding="utf-8")
    blocks = {normal(block) for block in re.findall(r"```text\n(.*?)\n```", document, re.S)}
    count = 0
    for job in jobs:
        for match in re.finditer(r"(?ms)^C BEGIN (CODE|DATA|FUNCTION|OUTPUT)\n(.*?)^C END \1$",
                                 job.read_text(encoding="utf-8")):
            if normal(match.group(2)) not in blocks:
                raise AssertionError(f"README differs from {job.name}, block {match.group(1)}")
            count += 1
    if count != 18:
        raise AssertionError(f"Expected 18 documentation blocks, got {count}")
    print(f"PASS documentation: all {count} code/data blocks match the source jobs", flush=True)


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--dll", type=Path, default=root / "src/Besm6.Cli/bin/Release/net8.0/besm6.dll")
    parser.add_argument("--logs", type=Path)
    parser.add_argument("--out", type=Path, default=root / "reports/madlen-presentation-verification.json")
    parser.add_argument("--check-docs-only", action="store_true")
    args = parser.parse_args()
    folder = root / "examples/presentation-madlen"
    presentation = sorted(folder.glob("*.dub"))
    if len(presentation) != 8:
        raise AssertionError("Expected eight presentation jobs")
    check_documentation(folder, presentation)
    if args.check_docs_only:
        return 0
    dll = args.dll.resolve()
    if not dll.is_file():
        parser.error(f"Build the Release CLI first: {dll}")
    logs = args.logs or Path(tempfile.mkdtemp(prefix="besm6-madlen-"))
    logs.mkdir(parents=True, exist_ok=True)
    results: list[dict] = []

    def run(job: Path, case: str, number: int, mode: str, expected_ok: bool = True) -> None:
        command = [args.dotnet, str(dll), "run", str(job), "--speed", mode,
                   "--stats", "--guest-clock", "--no-wall-clock"]
        proc = subprocess.run(command, cwd=root, capture_output=True, encoding="utf-8",
                              errors="replace", timeout=90)
        text = proc.stdout + "\n" + proc.stderr
        log = logs / f"{case}-{mode}.txt"
        log.write_text(text, encoding="utf-8")
        if proc.returncode:
            raise AssertionError(f"{case}: CLI exit {proc.returncode}; see {log}")
        execution = re.search(r"(?m)^\*EXECUTE\s*$", proc.stdout)
        if execution is None:
            raise AssertionError(f"{case}: no *EXECUTE; see {log}")
        guest_output = proc.stdout[execution.end():].split("Execution stats:", 1)[0]
        markers = re.findall(r"(?m)^\s*(OK \d{2}|FAIL\d{2})\s*$", guest_output)
        expected = f"OK {number:02d}" if expected_ok else f"FAIL{number:02d}"
        if markers != [expected]:
            raise AssertionError(f"{case}: expected runtime marker {expected!r}, got {markers}; see {log}")
        if not re.search(r"Halted by STOP at [0-9A-Fa-f]+ after \d+ instructions", text):
            raise AssertionError(f"{case}: did not halt by STOP; see {log}")
        match = re.search(r"Execution stats:\s*", text)
        if match is None:
            raise AssertionError(f"{case}: missing statistics; see {log}")
        stats, _ = json.JSONDecoder().raw_decode(text[match.end():])
        if stats["statistics"]["completedInstructions"] <= 0:
            raise AssertionError(f"{case}: no instructions executed")
        if case == "08-devices":
            if not re.search(r"(?m)^\s*RESULT\s*$", guest_output):
                raise AssertionError(f"{case}: computed-value heading not printed")
            if not re.search(r"(?m)^\s*\+600000[^\r\n]\+02\s*$", guest_output):
                raise AssertionError(f"{case}: computed 60.0 not printed numerically; see {log}")
        try:
            source_name = job.relative_to(root).as_posix()
        except ValueError:
            source_name = job.name
        results.append({
            "case": case, "source": source_name, "mode": mode,
            "sourceSha256": hashlib.sha256(job.read_bytes()).hexdigest(),
            "expectedMarker": expected, "observedMarkers": markers,
            "negativeControl": not expected_ok, "exitCode": proc.returncode,
            "haltedByStop": True,
            "completedInstructions": stats["statistics"]["completedInstructions"],
            "modelSeconds": stats["statistics"]["modelSeconds"],
            "memorySha256": stats["memorySha256"],
            "runtimeOutput": guest_output.strip(),
        })
        print(f"PASS {case:32s} {mode:8s} {expected}", flush=True)

    for job in presentation:
        for mode in ("max", "original"):
            run(job, job.stem, int(job.name[:2]), mode)

    cases: list[tuple[str, Path, list[tuple[str, str]]]] = [
        ("03-swapped", presentation[2], [
            (" X:       ,REAL,7.", " X:       ,REAL,3."),
            (" Y:       ,REAL,3.", " Y:       ,REAL,7."),
        ]),
        ("03-equal", presentation[2], [(" Y:       ,REAL,3.", " Y:       ,REAL,7.")]),
        ("03-negative", presentation[2], [
            (" X:       ,REAL,7.", " X:       ,REAL,-3."),
            (" Y:       ,REAL,3.", " Y:       ,REAL,-7."),
            (" EXPECT:  ,REAL,7.", " EXPECT:  ,REAL,-3."),
        ]),
        ("06-empty", presentation[5], [
            (" COUNT:   ,EQU,3", " COUNT:   ,EQU,0"),
            (" EXPECT:  ,REAL,60.", " EXPECT:  ,REAL,0."),
            (" RAWCOUNT:,LOG,3", " RAWCOUNT:,LOG,0"),
        ]),
        ("06-single", presentation[5], [
            (" COUNT:   ,EQU,3", " COUNT:   ,EQU,1"),
            (" EXPECT:  ,REAL,60.", " EXPECT:  ,REAL,10."),
            (" RAWCOUNT:,LOG,3", " RAWCOUNT:,LOG,1"),
        ]),
        ("07-zero", presentation[6], [
            (" ARG:     ,REAL,3.", " ARG:     ,REAL,0."),
            (" EXPECT:  ,REAL,6.", " EXPECT:  ,REAL,0."),
        ]),
        ("07-negative", presentation[6], [
            (" ARG:     ,REAL,3.", " ARG:     ,REAL,-3."),
            (" EXPECT:  ,REAL,6.", " EXPECT:  ,REAL,-6."),
        ]),
    ]
    for case, original, replacements in cases:
        source = original.read_text(encoding="utf-8")
        for old, new in replacements:
            source = replace_once(source, old, new)
        job = logs / f"{case}.dub"
        job.write_text(source, encoding="utf-8")
        for mode in ("max", "original"):
            run(job, case, int(case[:2]), mode)

    # A zero CLI exit or an OK string in the translator listing is not a pass.
    # Prove that each actual guest check rejects a deliberately wrong expected value.
    for original in presentation:
        source = original.read_text(encoding="utf-8")
        source, count = re.subn(r"(?m)^( (?:EXPECT\d?|EXP0):\s*,(?:REAL|LOG),)[^\r\n]+$",
                                r"\g<1>99.", source, count=1)
        if count != 1:
            raise AssertionError(f"No expectation to mutate: {original.name}")
        # LOG uses octal digits: use a valid unequal bit pattern, not REAL syntax.
        if original.name.startswith("02-"):
            source = source.replace(" EXPECT:  ,LOG,99.", " EXPECT:  ,LOG,77")
        case = original.stem + "-bad-expectation"
        job = logs / f"{case}.dub"
        job.write_text(source, encoding="utf-8")
        run(job, case, int(case[:2]), "max", expected_ok=False)

    # These pre-existing learning examples explicitly said they had not been run.
    legacy = sorted((root / "examples/learning-madlen").glob("*.dub"))
    for job in legacy:
        run(job, "learning-" + job.stem, int(job.name[:2]), "max")

    report = {
        "method": "historical MADLEN translation, guest execution, exact runtime marker after *EXECUTE, STOP and stats",
        "command": "dotnet <release-besm6.dll> run <job.dub> --speed <max|original> --stats --guest-clock --no-wall-clock",
        "dllSha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
        "summary": {
            "presentationExamples": len(presentation), "baseRuns": 2 * len(presentation),
            "boundaryCases": len(cases), "boundaryRuns": 2 * len(cases),
            "negativeControls": len(presentation), "existingLearningExamples": len(legacy),
            "totalRuns": len(results), "passed": len(results),
        },
        "runs": results,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report["summary"], ensure_ascii=False), flush=True)
    print(f"Report: {args.out}", flush=True)
    print(f"Logs: {logs}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
