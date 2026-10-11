#!/usr/bin/env python3
"""Check public machine profile/speed parity and hardware original pacing.

One warmup and five measured runs by default. A temporary copy of the small
standalone fixture gives each hardware command an explicit one-second logical
UU delay. This checks pacing, not historical opcode timing or CPU throughput.
"""
import argparse
import csv
import hashlib
import json
import pathlib
import subprocess
import tempfile
import time
import benchmark_speed as bench


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--repeats", type=int, default=5)
    args = parser.parse_args()
    if args.warmups < 0 or args.repeats < 1:
        parser.error("warmups must be nonnegative and repeats positive")
    dll = args.dll.resolve()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    logs = output / "logs"; logs.mkdir(exist_ok=True)
    root = pathlib.Path(__file__).resolve().parent.parent
    fixture = json.loads((root / "examples/machine-timing.json").read_text(encoding="utf-8"))
    fixture["timing"]["controlDurationsNs"] = {"Vtm": 1_000_000_000, "Stop": 1_000_000_000}
    result = {"environment": bench.report_environment(), "validated": False, "purpose": "Logical machine profile/speed parity and hardware pacing",
              "warmups": args.warmups, "repeats": args.repeats,
              "dll_sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
              "fixture": fixture, "samples": []}
    reference = None
    with tempfile.TemporaryDirectory(prefix="besm6-machine-timing-") as temporary:
        config = pathlib.Path(temporary) / "machine.json"
        config.write_text(json.dumps(fixture), encoding="utf-8")
        for model in ("functional", "hardware"):
            model_time = None
            for speed in ("max", "original"):
                for iteration in range(args.warmups + args.repeats):
                    start = time.perf_counter()
                    completed = subprocess.run(["dotnet", str(dll), "machine", str(config),
                        "--execution", model, "--speed", speed, "--stats"], capture_output=True, text=True, timeout=30)
                    elapsed = time.perf_counter() - start
                    stem = f"{model}-{speed}-{iteration}"
                    (logs / (stem + ".out")).write_text(completed.stdout, encoding="utf-8")
                    (logs / (stem + ".err")).write_text(completed.stderr, encoding="utf-8")
                    if completed.returncode != 0:
                        raise RuntimeError(f"Machine run failed: {stem}; see local logs")
                    report = json.loads(completed.stdout)
                    if not report["success"] or not report["stopped"] or report["instructions"] != 2:
                        raise RuntimeError(f"Incomplete run: {stem}")
                    if reference is None: reference = report["state"]
                    if reference != report["state"]: raise RuntimeError(f"Guest state mismatch: {stem}")
                    if report["functionalTicks"] != (2 if model == "functional" else 0):
                        raise RuntimeError(f"Clock units mismatch: {stem}")
                    stats = report["statistics"]
                    if model_time is None: model_time = stats["modelSeconds"]
                    if model_time != stats["modelSeconds"]: raise RuntimeError(f"Model time mismatch: {stem}")
                    error = abs(stats["elapsedSeconds"] - stats["modelSeconds"])
                    if model == "hardware" and speed == "original" and error > max(.020, stats["modelSeconds"] * .02):
                        raise RuntimeError(f"Hardware pacing tolerance exceeded: {stem}")
                    result["samples"].append({"model": model, "speed": speed, "warmup": iteration < args.warmups,
                        "process_seconds": elapsed, "loop_seconds": stats["elapsedSeconds"], "model_seconds": stats["modelSeconds"],
                        "hardware_nanoseconds": report["hardwareNanoseconds"], "absolute_error_seconds": error,
                        "state_sha256": hashlib.sha256(json.dumps(report["state"], sort_keys=True).encode()).hexdigest()})
                    (output / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    result["validated"] = True
    (output / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    with (output / "results.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(result["samples"][0])); writer.writeheader(); writer.writerows(result["samples"])
    print(json.dumps({"validated": True, "samples": len(result["samples"])}))


if __name__ == "__main__":
    main()
