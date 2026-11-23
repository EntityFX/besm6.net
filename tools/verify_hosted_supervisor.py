#!/usr/bin/env python3
"""Compare real .dub jobs in Dubna and the functional hosted supervisor.

Requires a freshly built Release CLI. Full logs go to --logs (temporary by
default); the report retains output hashes, actual CPU/service identity and
register-buffer snapshots. Native hardware is tested separately, never relabelled.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import tempfile
from pathlib import Path


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--dll", type=Path, default=root / "src/Besm6.Cli/bin/Release/net8.0/besm6.dll")
    parser.add_argument("--logs", type=Path)
    parser.add_argument("--out", type=Path, default=root / "reports/hosted-supervisor-verification.json")
    parser.add_argument("--speed", choices=("max", "original", "virtual"), default="max")
    parser.add_argument("--only", nargs="*", help="Optional example paths relative to examples/.")
    args = parser.parse_args()
    logs = args.logs or Path(tempfile.mkdtemp(prefix="besm6-hosted-supervisor-"))
    logs.mkdir(parents=True, exist_ok=True)
    jobs = ([root / "examples" / name for name in args.only] if args.only else
            [root / "examples" / name for name in ("fortran.dub", "ftn.dub", "algol.dub", "madlen.dub")]
            + sorted((root / "examples/ai").glob("*.dub"))
            + sorted((root / "examples/presentation-madlen").glob("*.dub")))
    rows = []
    for job in jobs:
        outputs, records = [], []
        for hosted in (False, True):
            command = [args.dotnet, str(args.dll), "run", str(job), "--limit", "0", "--no-wall-clock",
                       "--speed", args.speed, "--stats", "--machine-state"]
            if hosted:
                command.append("--supervisor-hosted")
            result = subprocess.run(command, cwd=root, capture_output=True, encoding="utf-8",
                                    errors="replace", timeout=120)
            text = result.stdout + "\n" + result.stderr
            name = job.relative_to(root / "examples").as_posix()
            (logs / (name.replace("/", "_") + ("-hosted.txt" if hosted else "-dubna.txt"))).write_text(text, encoding="utf-8")
            if result.returncode or "Halted by STOP" not in text:
                raise AssertionError(f"{name}, hosted={hosted}: not a successful STOP\n{text[-4000:]}")
            state_match = re.search(r"(?m)^Machine state:\s*(\{.*\})\s*$", text)
            stats_match = re.search(r"(?m)^Execution stats:\s*(\{.*\})\s*$", text)
            if not state_match or not stats_match:
                raise AssertionError(f"{name}: no machine state/statistics")
            state, statistics = json.loads(state_match[1]), json.loads(stats_match[1])["statistics"]
            expected = "Supervisor" if hosted else "Dubna"
            if state["processorProfile"] != expected or state["executionModel"] != "Functional":
                raise AssertionError(f"{name}: wrong CPU/execution identity: {state}")
            if hosted:
                assert state["memoryModel"] == "Mapped" and state["serviceRuntime"] == "HostedDubna"
                assert state["pageAssignment"] == list(range(1, 33))
                assert not any(buffer["name"] == "БРЧ" for buffer in state["buffers"])
                assert state["hardwareNanoseconds"] is None
            output = result.stdout.split("Halted by STOP", 1)[0]
            outputs.append(output)
            records.append({"hosted": hosted, "processorProfile": state["processorProfile"],
                            "serviceRuntime": state["serviceRuntime"], "memoryModel": state["memoryModel"],
                            "instructions": statistics["completedInstructions"],
                            "modelSeconds": statistics["modelSeconds"],
                            "outputSha256": digest(output.encode()), "buffers": state["buffers"]})
        if outputs[0] != outputs[1]:
            raise AssertionError(f"{name}: guest output/listing differs; see {logs}")
        marker = int(job.name[:2]) if job.parent.name == "presentation-madlen" else None
        if marker is not None:
            runtime = outputs[1].split("*EXECUTE", 1)[-1]
            assert re.search(rf"(?m)^\s*OK {marker:02d}\s*$", runtime), name
            assert not re.search(r"(?m)^\s*FAIL\d{2}\s*$", runtime), name
        rows.append({"example": name, "sourceSha256": digest(job.read_bytes()),
                     "identicalGuestOutput": True, "runs": records})
        print(f"PASS {name}: identical output, real Supervisor/Mapped/HostedDubna", flush=True)

    def form_word(payload: int, number: bool) -> int:
        left = ((payload >> 24).bit_count() & 1) ^ number
        right = (1 ^ ((payload & 0xFFFFFF).bit_count() & 1)) ^ number
        return payload | (left << 49) | (right << 48)

    assembled = subprocess.run([args.dotnet, str(args.dll), "asm", "xta 20, aax 20", "stop, stop"], cwd=root,
                               capture_output=True, text=True, check=True)
    word, stop = [int(line, 8) for line in assembled.stdout.splitlines()]
    config = json.loads((root / "examples/machines/supervisor-hardware.json").read_text(encoding="utf-8"))
    config["memoryWords"] = [{"address": 8, "raw50": form_word(word, False)},
                             {"address": 9, "raw50": form_word(stop, False)},
                             {"address": 16, "raw50": form_word(12345, True)}]
    config["timing"]["controlDurationsNs"]["Xta"] = 300
    config["timing"]["controlDurationsNs"]["Aax"] = 300
    config_path = logs / "native-hardware-read.json"
    config_path.write_text(json.dumps(config), encoding="utf-8")
    result = subprocess.run([args.dotnet, str(args.dll), "machine", str(config_path), "--machine-state", "--stats"],
                            cwd=root, capture_output=True, encoding="utf-8", errors="replace", timeout=30)
    (logs / "native-hardware-read-output.txt").write_text(result.stdout + result.stderr, encoding="utf-8")
    assert result.returncode == 0, result.stderr
    native = json.loads(result.stdout)
    read_registers = [buffer for buffer in native["registers"]["buffers"] if buffer["name"] == "БРЧ"]
    assert native["registers"]["serviceRuntime"] == "Native"
    assert native["registers"]["executionModel"] == "Hardware"
    assert native["state"]["a"] == 12345 and len(read_registers) == 4
    assert any(buffer["value"] == 12345 for buffer in read_registers)
    report = {"method": "paired hosted .dub execution; exact guest output comparison; explicit CPU/MMU/service inspection",
              "speed": args.speed, "dllSha256": digest(args.dll.read_bytes()),
              "examples": len(rows), "jobRuns": 2 * len(rows), "allPassed": True,
              "rows": rows, "nativeHardwareRead": native}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"PASS native hardware BRCh: four real registers, operand 12345 observed", flush=True)
    print(f"PASS {len(rows)} example pairs; report {args.out}; logs {logs}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
