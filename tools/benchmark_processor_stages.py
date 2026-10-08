#!/usr/bin/env python3
"""Compare compatible Release CLI snapshots in alternating, sequential runs.

Each snapshot must support --speed and --stats. Validation and temporary Fortran
decks use benchmark_speed. No benchmark processes run concurrently.
"""
import argparse
import csv
import hashlib
import json
import pathlib
import platform
import statistics
import subprocess
import sys

import benchmark_speed as bench


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--variant", action="append", required=True, metavar="NAME=DLL")
    parser.add_argument("--legacy-variant", action="append", default=[], metavar="NAME=DLL",
                        help="Pre-speed CLI baseline; measured before ordinary variants")
    parser.add_argument("--jobs", nargs="+", choices=bench.JOBS + bench.MEMORY_JOBS,
                        default=["dh5000", "dh10000", "mp1", "mp2", "mp3"])
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--stats", action="store_true", help="Measure with statistics instead of bare max")
    parser.add_argument("--runtime-probe", action="store_true",
                        help="Measure RunLoaded without observers and compare final registers/memory")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    if args.repeats < 1 or args.warmups < 0:
        parser.error("repeats must be positive; warmups must be nonnegative")
    if args.stats and args.runtime_probe:
        parser.error("--stats and --runtime-probe are mutually exclusive")
    if args.stats and args.legacy_variant:
        parser.error("A legacy CLI cannot be measured with --stats")
    variants = {}
    legacy_names = {value.partition("=")[0] for value in args.legacy_variant}
    for value in args.legacy_variant + args.variant:
        name, separator, dll = value.partition("=")
        if not separator or not name or name in variants or not pathlib.Path(dll).is_file():
            parser.error("Each variant must have a unique name and an existing DLL")
        variants[name] = pathlib.Path(dll).resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    logs = output / "logs"
    logs.mkdir(exist_ok=True)
    jobs, config = bench.prepare_jobs(output)
    if any(job.startswith("mem") for job in args.jobs):
        jobs.update(bench.prepare_memory_jobs(output))
    result = {"environment": {"platform": platform.platform(), "python": sys.version,
                              "dotnet": subprocess.check_output(["dotnet", "--info"], text=True)},
              "options": {"jobs": args.jobs, "repeats": args.repeats, "warmups": args.warmups,
                          "stats": args.stats, "order": "alternates each repetition"},
              "variants": {name: {"dll": str(dll), "sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
                                  "processor_sha256": hashlib.sha256((dll.parent / "Besm6.Processor.dll").read_bytes()).hexdigest(),
                                  "runtime_sha256": hashlib.sha256((dll.parent / "Besm6.Runtime.dll").read_bytes()).hexdigest(),
                                  "architecture_sha256": hashlib.sha256((dll.parent / "Besm6.Architecture.dll").read_bytes()).hexdigest()}
                           for name, dll in variants.items()},
              "calibration": {}, "samples": {}, "measurements": [], "validated": False}
    result["options"]["runtime_probe"] = args.runtime_probe
    result["options"]["legacy_variants"] = sorted(legacy_names)
    result["options"]["loop_scope"] = "RunLoaded including entry/exit and output completion" if args.runtime_probe else "ExecutionStatistics" if args.stats else None

    def save():
        (output / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        if result["measurements"]:
            with (output / "results.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fieldnames=list(result["measurements"][0]))
                writer.writeheader()
                writer.writerows(result["measurements"])

    try:
        probes = {name: bench.build_runtime_probe(dll, output / name, legacy=name in legacy_names)
                  for name, dll in variants.items()} if args.runtime_probe else {}
        legacy_calibration = {name: bench.build_baseline_probe(dll, output / name)
                              for name, dll in variants.items() if name in legacy_names}
        for job in args.jobs:
            common = ["run", str(jobs[job]), "--config", str(config),
                      "--limit", "100000000", "--no-wall-clock", "--no-hang-detect"]
            commands = {name: ["dotnet", str(dll)] + common + ([] if name in legacy_names else ["--speed", "max"])
                        for name, dll in variants.items()}
            reference = None
            for name, command in commands.items():
                calibration_command = ["dotnet", str(legacy_calibration[name]), str(jobs[job]), str(config), "100000000"] \
                    if name in legacy_names else command + ["--stats", "--profile"]
                measured = bench.execute(calibration_command, output, job,
                                         logs / f"{job}-{name}-calibration.out", 120)
                if reference is not None:
                    bench.parity(reference, measured)
                reference = measured if reference is None else reference
                result["calibration"][job + ":" + name] = measured
                result["samples"][job + ":" + name] = []
            if args.runtime_probe:
                commands = {name: ["dotnet", str(probes[name]), str(jobs[job]), str(config)]
                            for name in variants}
            for repetition in range(args.warmups + args.repeats):
                order = list(commands)
                if repetition % 2:
                    order.reverse()
                for name in order:
                    command = commands[name] + (["--stats"] if args.stats else [])
                    measured = bench.execute(command, output, job,
                                             logs / f"{job}-{name}-{repetition}.out", 120)
                    bench.parity(reference, measured)
                    result["samples"][job + ":" + name].append(
                        {"warmup": repetition < args.warmups, **measured})
                save()
            baseline = None
            baseline_loop = None
            for name in commands:
                samples = [s for s in result["samples"][job + ":" + name] if not s["warmup"]]
                median = statistics.median(s["process_seconds"] for s in samples)
                baseline = median if baseline is None else baseline
                loop = statistics.median(s["runtime"]["elapsedSeconds"] for s in samples) if args.runtime_probe else \
                    statistics.median(s["report"]["statistics"]["elapsedSeconds"] for s in samples) if args.stats else None
                baseline_loop = loop if baseline_loop is None else baseline_loop
                row = {"job": job, "variant": name, "process_seconds": median,
                       "time_reduction_percent": (1 - median / baseline) * 100,
                       "speedup": baseline / median,
                       "process_min_seconds": min(s["process_seconds"] for s in samples),
                       "process_max_seconds": max(s["process_seconds"] for s in samples),
                       "loop_seconds": loop,
                       "loop_speedup": baseline_loop / loop if loop else None,
                       "loop_time_reduction_percent": (1 - loop / baseline_loop) * 100 if loop else None}
                result["measurements"].append(row)
                print(json.dumps(row), flush=True)
            save()
        result["workload_metrics"] = bench.workload_metrics(result["measurements"], result["calibration"])
        result["validated"] = True
        save()
        return 0
    except (ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        save()
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
