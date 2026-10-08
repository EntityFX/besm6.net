#!/usr/bin/env python3
"""Validate BESM-6 execution speed and Fortran benchmarks on Windows or Linux.

Build Release first. An optional pre-change CLI directory provides a baseline;
its public Runtime APIs are measured by a generated, isolated calibration probe.
Performance samples always run sequentially, never alongside another benchmark.
"""
import argparse
import csv
from decimal import Decimal, localcontext
import hashlib
import json
import pathlib
import platform
import re
import statistics
import subprocess
import sys
import time
from xml.sax.saxutils import escape

ROOT = pathlib.Path(__file__).resolve().parents[1]
JOBS = ("dh0", "dh5000", "dh10000", "mp0", "mp1", "mp2", "mp3")
MEMORY_JOBS = ("mem0", "mem1", "mem2", "mem3")
OPWD = {0: 0, 1: 2, 2: 8, 3: 32}


def fortran_number(text):
    """FORTRAN-GDR writes 99.856051500828-02 without an E."""
    value = text.strip().replace("D", "E")
    if "E" not in value.upper():
        value = re.sub(r"(?<=\d)([+-]\d+)$", r"E\1", value)
    return Decimal(value)


def reference_result(part, repeats=30):
    pairs = [("0.000020", "0.999980"), ("0.000011", "1.000011"),
             ("0.000012", "0.999992"), ("0.000013", "1.000013"),
             ("0.000014", "0.999994"), ("0.000015", "1.000015"),
             ("0.000016", "0.999996"), ("0.000017", "1.000017"),
             ("0.000018", "1.000018"), ("0.000019", "1.000019"),
             ("0.000021", "1.000021")]
    with localcontext() as ctx:
        ctx.prec = 80
        value = Decimal("0.999999")
        for _ in range(repeats):
            if part == 1:
                value = (value + Decimal("0.000020")) * Decimal("0.999950")
            elif part in (2, 3):
                value = sum(((value + Decimal(a)) * Decimal(b) * (-1 if i % 2 else 1)
                             for i, (a, b) in enumerate(pairs[:3 if part == 2 else 11])), Decimal(0))
        return value


def parse_run(output, name, returncode):
    if returncode != 0:
        raise ValueError(f"{name}: CLI exit code {returncode}")
    halt = re.search(r"Halted by STOP at \S+ after (\d+) instructions", output)
    if not halt or "did not terminate" in output:
        raise ValueError(f"{name}: incomplete run (STOP required)")
    program = output.rsplit("≠", 1)[-1].split("Halted by STOP", 1)[0].strip()
    if name.startswith("dh"):
        passes = int(name[2:])
        if passes:
            if "ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC" not in program:
                raise ValueError(f"{name}: Dhrystone self-check failed")
            expected = {"IGLOB": 5, "C1GLB": 65, "C2GLB": 66, "A1GLOB 8": 7,
                        "A2GLOB 8/7": passes + 10, "PGLB.DISCR": 0, "PGLB.ENUM": 2,
                        "PGLB.INT": 17, "NEXTP.DISCR": 0, "NEXTP.ENUM": 1,
                        "NEXTP.INT": 18, "I1LOC": 5, "I2LOC": 13, "I3LOC": 7, "ELOC": 1}
            for label, value in expected.items():
                found = re.search(r"^" + re.escape(label) + r":\s*(-?\d+)", program, re.M)
                if not found or int(found[1]) != value:
                    raise ValueError(f"{name}: invalid {label}")
            if not re.search(r"^BLOB:\s*T\b", program, re.M):
                raise ValueError(f"{name}: invalid BLOB")
    elif name.startswith("mem"):
        part = int(name[3:])
        traffic = 0 if part == 0 else 12 if part == 1 else 18
        for label, expected in (("WORDS", 4096), ("REPEAT PASSES", 1000),
                                ("BYTES/ELEMENT", traffic), ("BAD ELEMENTS", 0)):
            found = re.search(re.escape(label) + r"=\s*(\d+)", program)
            if not found or int(found[1]) != expected:
                raise ValueError(f"{name}: invalid {label}")
        if "ALL VALUES MATCH: YES" not in program or "ALL VALUES MATCH: NO" in program:
            raise ValueError(f"{name}: memory kernel self-check failed")
        fraction = Decimal(1) / 4096
        first, last, total = {
            0: (Decimal(1), Decimal(1), Decimal(4096)),
            1: (fraction, Decimal(1), Decimal("2048.5")),
            2: (1 + 1000 * fraction, Decimal(1001), Decimal(2052596)),
            3: (1 + 500 * fraction, Decimal(501), Decimal(1028346))}[part]
        for label, reference in (("FIRST RESULT", first), ("LAST RESULT", last),
                                 ("CHECKSUM", total), ("DATA TRAFFIC BYTES", Decimal(4096 * 1000 * traffic))):
            found = re.search(re.escape(label) + r"=\s*(\S+)", program)
            if not found or abs(fortran_number(found[1]) - reference) > max(Decimal("1e-9"), abs(reference) * Decimal("1e-9")):
                raise ValueError(f"{name}: invalid {label}")
    else:
        part = int(name[2:])
        if "ALL SAME: YES" not in program or "ALL SAME: NO" in program:
            raise ValueError(f"{name}: inconsistent elements")
        required = "BASELINE RUN: NO KERNEL EXECUTED" if part == 0 else "CALCULATIONS PERFORMED"
        if required not in program or (part and "NO CALCULATIONS?" in program):
            raise ValueError(f"{name}: missing kernel calculation")
        for label, expected in (("WORDS", 9000), ("REPEAT PASSES", 30), ("OPS/WORD", OPWD[part])):
            found = re.search(re.escape(label) + r"=\s*(\d+)", program)
            if not found or int(found[1]) != expected:
                raise ValueError(f"{name}: invalid {label}")
        printed = re.search(r"FIRST RESULT=\s*(\S+)", program)
        reference = reference_result(part)
        if not printed or abs((fortran_number(printed[1]) - reference) / reference) > Decimal("1e-9"):
            raise ValueError(f"{name}: numerical reference mismatch")
        flops = re.search(r"FLOPS=\s*(\S+)", program)
        exact = Decimal(9000 * 30 * OPWD[part])
        if not flops or abs(fortran_number(flops[1]) - exact) > max(Decimal("1e-6"), exact * Decimal("1e-9")):
            raise ValueError(f"{name}: invalid FLOPS")
    match = re.search(r"^Execution stats: (.+)$", output, re.M)
    report = json.loads(match[1]) if match else None
    runtime_match = re.search(r"^Runtime measurement: (.+)$", output, re.M)
    runtime = json.loads(runtime_match[1]) if runtime_match else None
    return {"halt_instructions": int(halt[1]), "program_sha256": hashlib.sha256(program.encode()).hexdigest(),
            "report": report, "runtime": runtime}


def prepare_jobs(output):
    directory = output / "jobs"
    directory.mkdir(parents=True, exist_ok=True)
    dh = (ROOT / "examples/dhrystone-baseline-ftn.dub").read_text(encoding="utf-8")
    mp = (ROOT / "examples/mpmflops.dub").read_text(encoding="utf-8")
    if dh.count("DATA NUMBER/0/") != 1 or mp.count("data kpart/1/") != 1:
        raise ValueError("Benchmark parameter declaration changed; refusing an ambiguous substitution")
    paths = {}
    for name in JOBS:
        text = dh.replace("DATA NUMBER/0/", f"DATA NUMBER/{name[2:]}/") if name.startswith("dh") else \
            mp.replace("data kpart/1/", f"data kpart/{name[2:]}/")
        paths[name] = directory / (name + ".dub")
        paths[name].write_text(text, encoding="utf-8")
    config = output / "config.json"
    config.write_text(json.dumps({"tapes": str(ROOT / "tapes"), "useWallClock": False}), encoding="utf-8")
    return paths, config


def prepare_memory_jobs(output):
    directory = output / "jobs"
    directory.mkdir(parents=True, exist_ok=True)
    source = (ROOT / "examples/memspeed-ftn.dub").read_text(encoding="utf-8")
    if source.count("data kpart/1/") != 1:
        raise ValueError("Memory kernel declaration changed")
    paths = {}
    for name in MEMORY_JOBS:
        paths[name] = directory / (name + ".dub")
        paths[name].write_text(source.replace("data kpart/1/", f"data kpart/{name[3:]}/"), encoding="utf-8")
    return paths


def build_baseline_probe(dll, output):
    """One calibration run captures old cycles and the complete final state without modifying old code."""
    directory = output / "baseline-probe"
    directory.mkdir(parents=True, exist_ok=True)
    refs = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(dll.parent / (name + ".dll")))}</HintPath></Reference>'
                   for name in ("Besm6.Runtime", "Besm6.Processor", "Besm6.Architecture", "Besm6.Assembler"))
    (directory / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
        '<NoWarn>CS0618</NoWarn></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>', encoding="utf-8")
    (directory / "Program.cs").write_text(r'''
using Besm6;
using Besm6.Core;
using Besm6.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
var config = Config.Load(args[1]);
config.UseWallClock = false;
var machine = MachineFactory.CreateMachine(config);
var loader = MachineFactory.CreateLoader(config, machine);
loader.InstructionLimit = long.Parse(args[2]);
var profiler = new OpcodeProfiler();
ProcessorSnapshot? last = null;
loader.TypedInstructionTrace = record => { profiler.Observe(record); last = record.After; };
var result = loader.RunScript(args[0]);
Console.WriteLine(result);
if (!result.Success || last is null) return 1;
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
Span<byte> bytes = stackalloc byte[6];
for (uint address = 0; address < (uint)machine.Memory.Size; address++) {
    ulong word = machine.Memory.Read(address).Value;
    for (int i=0; i<6; i++) bytes[i] = (byte)(word >> (40-8*i));
    hash.AppendData(bytes);
}
var report = new {
    statistics = new { CompletedInstructions=profiler.TotalInstructions, ModelCycles=profiler.TotalCycles, ModelSeconds=profiler.ModeledSeconds },
    finalState = new { K=machine.Cpu.GetK(), IsRightHalf=machine.Cpu.RightInstruction, A=machine.Cpu.GetA().Value,
        Y=machine.Cpu.GetY().Value, R=machine.Cpu.GetR(), machine.Cpu.C, machine.Cpu.ApplyC,
        last.EffectiveAddress, InterceptCount=machine.Cpu.InterceptCount, InterceptAddress=machine.Cpu.InterceptAddr,
        M=Enumerable.Range(0,16).Select(machine.Cpu.GetM).ToArray(), ClockTicks=machine.Clock.Tick },
    memorySha256=Convert.ToHexString(hash.GetHashAndReset()) };
Console.WriteLine("Execution stats: " + JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
return 0;
''', encoding="utf-8")
    built = subprocess.run(["dotnet", "build", str(directory / "Probe.csproj"), "-c", "Release", "--nologo"],
                           capture_output=True, text=True, timeout=120)
    (directory / "build.log").write_text(built.stdout + built.stderr, encoding="utf-8")
    if built.returncode:
        raise RuntimeError("Baseline probe build failed; see " + str(directory / "build.log"))
    return directory / "bin/Release/net8.0/Probe.dll"


def parity(left, right):
    if (left["halt_instructions"], left["program_sha256"]) != (right["halt_instructions"], right["program_sha256"]):
        raise ValueError("Instruction count or program output changed")
    left_state = left.get("runtime") or left["report"]
    right_state = right.get("runtime") or right["report"]
    if left_state and right_state:
        for field in ("finalState", "memorySha256"):
            if left_state[field] != right_state[field]:
                raise ValueError("Final machine state changed: " + field)
    if left["report"] and right["report"]:
        for field in ("completedInstructions", "modelCycles"):
            if left["report"]["statistics"][field] != right["report"]["statistics"][field]:
                raise ValueError("Completed instruction/cycle count changed: " + field)


def build_runtime_probe(dll, output, legacy=False):
    """Measure RunLoaded without observers; inspect state only after execution.

    LoadScript prepares the same MONSYS job as RunScript outside the stopwatch.
    RunLoaded includes loop entry/exit and buffered output completion. No per-
    instruction instrumentation is installed, so the unobserved path is eligible.
    """
    directory = output / "runtime-probe"
    directory.mkdir(parents=True, exist_ok=True)
    refs = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(dll.parent / (name + ".dll")))}</HintPath></Reference>'
                   for name in ("Besm6.Runtime", "Besm6.Processor", "Besm6.Architecture", "Besm6.Assembler"))
    (directory / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
        '<NoWarn>CS0618</NoWarn></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>', encoding="utf-8")
    program = r'''
using Besm6;
using Besm6.Core;
using Besm6.Runtime;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
var config = Config.Load(args[1]);
config.UseWallClock = false;
var machine = MachineFactory.CreateMachine(config);
var loader = MachineFactory.CreateLoader(config, machine);
loader.Speed = ExecutionSpeed.Max;
loader.InstructionLimit = 100_000_000;
loader.WallClockLimitMs = 0;
loader.HangDetect = false;
loader.LoopDetect = false;
loader.CollectStatistics = false;
loader.LoadScript(args[0]);
var watch = Stopwatch.StartNew();
var result = loader.RunLoaded();
watch.Stop();
Console.WriteLine(result);
if (!result.Success || !loader.HaltedByStop) return 1;
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
Span<byte> bytes = stackalloc byte[6];
for (uint address = 0; address < (uint)machine.Memory.Size; address++) {
    ulong word = machine.Memory.Read(address).Value;
    for (int i=0; i<6; i++) bytes[i] = (byte)(word >> (40-8*i));
    hash.AppendData(bytes);
}
// Reflection after STOP does not install trace hooks or change the hot path.
var state = typeof(Processor).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(machine.Cpu)!;
var ea = (uint)state.GetType().GetField("EffectiveAddress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
var report = new {
    elapsedSeconds = watch.Elapsed.TotalSeconds,
    finalState = new { K=machine.Cpu.GetK(), IsRightHalf=machine.Cpu.RightInstruction, A=machine.Cpu.GetA().Value,
        Y=machine.Cpu.GetY().Value, R=machine.Cpu.GetR(), machine.Cpu.C, machine.Cpu.ApplyC,
        EffectiveAddress=ea, InterceptCount=machine.Cpu.InterceptCount, InterceptAddress=machine.Cpu.InterceptAddr,
        M=Enumerable.Range(0,16).Select(machine.Cpu.GetM).ToArray(), ClockTicks=machine.Clock.Tick },
    memorySha256=Convert.ToHexString(hash.GetHashAndReset()) };
Console.WriteLine("Runtime measurement: " + JsonSerializer.Serialize(report,
    new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
return 0;
'''
    if legacy:
        program = program.replace("loader.Speed = ExecutionSpeed.Max;\n", "").replace("loader.CollectStatistics = false;\n", "")
    (directory / "Program.cs").write_text(program, encoding="utf-8")
    built = subprocess.run(["dotnet", "build", str(directory / "Probe.csproj"), "-c", "Release", "--nologo"],
                           capture_output=True, text=True, timeout=120)
    (directory / "build.log").write_text(built.stdout + built.stderr, encoding="utf-8")
    if built.returncode:
        raise RuntimeError("Runtime probe build failed; see " + str(directory / "build.log"))
    return directory / "bin/Release/net8.0/Probe.dll"


def execute(command, directory, name, log_path, timeout):
    start = time.perf_counter()
    process = subprocess.run(command, cwd=directory, capture_output=True, encoding="utf-8", errors="replace", timeout=timeout)
    elapsed = time.perf_counter() - start
    output = process.stdout + process.stderr
    log_path.write_text(output, encoding="utf-8")
    parsed = parse_run(output, name, process.returncode)
    parsed["process_seconds"] = elapsed
    return parsed


def workload_metrics(measurements, calibration):
    """Keep model and host scores separate; subtract matching zero-job medians."""
    rows = {(row["job"], row["variant"]): row for row in measurements}
    metrics = {}
    for (job, variant), row in rows.items():
        base = "dh0" if job.startswith("dh") else "mem0" if job.startswith("mem") else "mp0"
        if job == base or (base, variant) not in rows:
            continue
        base_row = rows[(base, variant)]
        cycles = calibration[job + ":" + variant]["report"]["statistics"]["modelCycles"] \
            - calibration[base + ":" + variant]["report"]["statistics"]["modelCycles"]
        if cycles <= 0:
            raise ValueError("Nonpositive workload model time")
        amount = int(job[2:]) / 1757 if job.startswith("dh") else \
            4096 * 1000 * (12 if job == "mem1" else 18) / 1e6 if job.startswith("mem") else \
            9000 * 30 * OPWD[int(job[2:])] / 1e6
        process = row["process_seconds"] - base_row["process_seconds"]
        loop = row["loop_seconds"] - base_row["loop_seconds"] \
            if row["loop_seconds"] is not None and base_row["loop_seconds"] is not None else None
        metrics[job + ":" + variant] = {
            "unit": "DMIPS" if job.startswith("dh") else "MB/s" if job.startswith("mem") else "MFLOPS",
            "workload_model_cycles": cycles, "model_score": amount / (cycles * 1e-7),
            "host_process_workload_seconds": process,
            "host_process_score": amount / process if process > 0 else None,
            "host_loop_workload_seconds": loop,
            "host_loop_score": amount / loop if loop is not None and loop > 0 else None}
    return metrics


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=pathlib.Path, default=ROOT / "src/Besm6.Cli/bin/Release/net8.0/besm6.dll")
    parser.add_argument("--baseline-dll", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "tests-run/speed")
    parser.add_argument("--jobs", nargs="+", choices=JOBS, default=list(JOBS))
    parser.add_argument("--modes", nargs="+", choices=("max", "original"), default=["max", "original"])
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--limit", type=int, default=100_000_000)
    parser.add_argument("--timeout", type=int, default=120)
    args = parser.parse_args()
    if args.repeats < 1 or args.warmups < 0 or args.limit < 1:
        parser.error("repeats/limit must be positive and warmups nonnegative")
    args.output = args.output.resolve()
    args.dll = args.dll.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    logs = args.output / "logs"
    logs.mkdir(exist_ok=True)
    paths, config = prepare_jobs(args.output)
    probe = build_baseline_probe(args.baseline_dll.resolve(), args.output) if args.baseline_dll else None
    metadata = {"platform": platform.platform(), "python": sys.version, "dotnet": subprocess.check_output(
        ["dotnet", "--info"], text=True), "arguments": {k: str(v) if isinstance(v, pathlib.Path) else v for k, v in vars(args).items()}}
    results = {"environment": metadata, "calibration": {}, "samples": {}, "measurements": [], "accepted": False}

    def save():
        (args.output / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
        rows = results["measurements"]
        if rows:
            with (args.output / "results.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
                writer.writeheader()
                writer.writerows(rows)

    try:
        for name in args.jobs:
            common = ["run", str(paths[name]), "--config", str(config), "--limit", str(args.limit), "--no-wall-clock", "--no-hang-detect"]
            reference = None
            if probe:
                reference = execute(["dotnet", str(probe), str(paths[name]), str(config), str(args.limit)], args.output,
                                    name, logs / f"{name}-baseline-calibration.out", args.timeout)
                results["calibration"][name + ":baseline"] = reference
            variants = []
            if args.baseline_dll:
                variants.append(("baseline", ["dotnet", str(args.baseline_dll.resolve())] + common))
            for mode in args.modes:
                command = ["dotnet", str(args.dll)] + common + ["--speed", mode]
                calibrated = execute(command + ["--stats", "--profile"], args.output, name,
                                     logs / f"{name}-{mode}-calibration.out", args.timeout)
                if not calibrated["report"]:
                    raise ValueError("Target CLI does not support --stats")
                if reference:
                    parity(reference, calibrated)
                else:
                    reference = calibrated
                results["calibration"][name + ":" + mode] = calibrated
                if mode == "max":
                    variants.append((mode, command))
                    variants.append(("max-stats", command + ["--stats"]))
                else:
                    variants.append((mode, command + ["--stats"]))
            for variant, command in variants:
                samples = []
                results["samples"][name + ":" + variant] = []
                for repetition in range(args.warmups + args.repeats):
                    measured = execute(command, args.output, name, logs / f"{name}-{variant}-{repetition}.out", args.timeout)
                    parity(reference, measured)
                    results["samples"][name + ":" + variant].append({
                        "warmup": repetition < args.warmups, **measured})
                    if repetition >= args.warmups:
                        samples.append(measured)
                process_seconds = statistics.median(row["process_seconds"] for row in samples)
                reports = [row["report"]["statistics"] for row in samples if row["report"]]
                loop_seconds = statistics.median(row["elapsedSeconds"] for row in reports) if reports else None
                cycles = reference["report"]["statistics"]["modelCycles"]
                row = {"job": name, "variant": variant, "process_seconds": process_seconds,
                       "loop_seconds": loop_seconds, "model_seconds": cycles * 1e-7, "model_cycles": cycles,
                       "halt_instructions": reference["halt_instructions"],
                       "host_loop_mips": reference["report"]["statistics"]["completedInstructions"] / loop_seconds / 1e6 if loop_seconds else None,
                       "allocated_bytes": statistics.median(r["allocatedBytes"] for r in reports) if reports else None,
                       "original_tempo_met": all(abs(r["elapsedSeconds"] - r["modelSeconds"]) <= max(0.020, r["modelSeconds"] * 0.02)
                           for r in reports) if variant == "original" and cycles * 1e-7 >= 1 else None}
                results["measurements"].append(row)
                print(json.dumps(row), flush=True)
                save()
        metrics = {}
        for name in args.jobs:
            base = "dh0" if name.startswith("dh") else "mp0"
            if name == base or base + ":" + args.modes[0] not in results["calibration"]:
                continue
            measured = results["calibration"][name + ":" + args.modes[0]]["report"]["statistics"]
            baseline = results["calibration"][base + ":" + args.modes[0]]["report"]["statistics"]
            seconds = (measured["modelCycles"] - baseline["modelCycles"]) * 1e-7
            if seconds <= 0:
                raise ValueError("Nonpositive workload model time")
            metrics[name] = {"workload_model_seconds": seconds}
            if name.startswith("dh"):
                metrics[name].update(dhrystones_per_second=int(name[2:]) / seconds,
                                     dmips=int(name[2:]) / seconds / 1757)
            else:
                metrics[name]["model_mflops"] = 9000 * 30 * OPWD[int(name[2:])] / seconds / 1e6
        results["model_metrics"] = metrics
        comparisons = {}
        for name in args.jobs:
            if name in ("dh0", "mp0"):
                continue
            rows = {r["variant"]: r for r in results["measurements"] if r["job"] == name}
            if "baseline" in rows and "max" in rows:
                gain = 1 - rows["max"]["process_seconds"] / rows["baseline"]["process_seconds"]
                comparisons[name] = {"time_reduction_percent": gain * 100, "at_least_5_percent": gain >= 0.05, "no_3_percent_regression": gain >= -0.03}
        results["performance_comparison"] = comparisons
        results["accepted"] = all(r["original_tempo_met"] is not False for r in results["measurements"]) and \
            all(r["at_least_5_percent"] for r in comparisons.values())
        save()
        print("Accepted:", results["accepted"], "Report:", args.output / "results.json", flush=True)
        return 0 if results["accepted"] else 1
    except (ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        results["error"] = str(error)
        save()
        print("Benchmark rejected:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
