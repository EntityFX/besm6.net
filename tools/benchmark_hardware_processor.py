#!/usr/bin/env python3
"""Serial hardware CPU benchmarks without per-instruction diagnostic history.

Build each public-API probe before measuring anything. STOP, instruction counts,
raw memory, registers and model time are checked outside the measured interval.
Small fixtures select explicit logical timings; they do not claim historical
opcode durations. Full DISPAK acceptance remains a separate diagnostic gate.
"""
import argparse
import csv
import hashlib
import json
import math
import pathlib
import statistics
import subprocess
from xml.sax.saxutils import escape

import benchmark_speed as bench

WORKLOADS = ("control", "memory", "arithmetic", "mixed")
PROBE = r'''
using Besm6.Architecture;
using Besm6.Core;
using Besm6.Runtime;
using Besm6.Runtime.Timing;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics.Tracing;

string workload = args[0];
int iterations = int.Parse(args[1]);
if (iterations < 1 || iterations > 32760) throw new ArgumentOutOfRangeException();
int rounds = args.Length > 2 ? int.Parse(args[2]) : 0;
if (rounds < 0 || rounds > 32760) throw new ArgumentOutOfRangeException();
string[] body = workload switch {
    "control" => new[] {"utm 1(2), mtj 3(2)", "utm -1(2), mtj 4(3)"},
    "memory" => new[] {"xta 100, atx 101", "xta 101, atx 102"},
    "arithmetic" => new[] {"xta 100, a+x 103", "a*x 104, a-x 103"},
    "mixed" => new[] {"xta 100, a+x 103", "atx 101, utm 1(2)"},
    _ => throw new ArgumentException("Unknown workload")
};
var machine = new MachineCore(ProcessorProfile.Supervisor);
machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
var memory = machine.MappedMemory!.PhysicalMemory;
void Code(uint address, string source) => memory.WriteRaw(address,
    MemoryWord50.Form(new Word48(Besm6.Asm.Assembler.Asm(source)), false, false));
Code(8, $"vtm -{Convert.ToString(iterations, 8)}(1), vtm 0(2)");
// Assembler numeric addresses are octal: 11 is physical address 9.
Code(9, body[0]); Code(10, body[1]); Code(11, "vlm 11(1), stop");
if (rounds != 0) {
    Code(8, $"vtm -{Convert.ToString(rounds, 8)}(5), vtm 0(2)");
    Code(9, $"vtm -{Convert.ToString(iterations, 8)}(1), utm 0(2)");
    Code(10, body[0]); Code(11, body[1]);
    Code(12, "vlm 12(1), vlm 11(5)"); Code(13, "stop, stop");
}
foreach (uint address in new uint[] {64,65,66,67,68})
    memory.WriteRaw(address, MemoryWord50.Form(Word48.FromDouble(address == 68 ? 1 : .75), true, true));
var mram = new MramPortConfiguration(new(2000), new(900), new(1500),
    MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank);
var controls = Enum.GetValues<Opcode>().ToDictionary(op => op, _ => new HardwareDuration(300));
var arithmetic = Enum.GetValues<Opcode>().ToDictionary(op => op, _ => new HardwareDuration(1200));
var devices = new SupervisorIoHardwareTiming(new(20000),new(700),new(900),new(100),new(50),new(500),new(30),new(0));
machine.UseHardwareExecution(new(mram, new(100), new(100), controls, devices, arithmetic));
machine.Cpu.StartAt(8);
MeasurementEvents.Instance.Start();
long allocated = GC.GetAllocatedBytesForCurrentThread(); int gen0 = GC.CollectionCount(0);
var watch = Stopwatch.StartNew();
long expected = rounds == 0 ? 2 + (iterations + 1L) * 5 + 1 :
    3 + (rounds + 1L) * (3 + (iterations + 1L) * 5);
var run = machine.RunInstructions(expected + 1, ExecutionSpeed.Max, false);
watch.Stop();
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated; gen0 = GC.CollectionCount(0) - gen0;
MeasurementEvents.Instance.Stop();
if (!run.Outcome.Success || !run.Outcome.Stopped || run.Outcome.Instructions != expected || machine.Clock.Tick != 0)
    throw new InvalidOperationException("STOP/count/clock validation failed: " + run.Outcome);
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
Span<byte> bytes = stackalloc byte[8];
for (uint address = 0; address < memory.CapacityWords; address++) {
    BinaryPrimitives.WriteUInt64LittleEndian(bytes, memory.ReadRaw(address).RawValue);
    hash.AppendData(bytes);
}
var state = new { K=machine.Cpu.GetK(), Right=machine.Cpu.RightInstruction,
    A=machine.Cpu.GetA().Value, Y=machine.Cpu.GetY().Value, R=machine.Cpu.GetR(),
    machine.Cpu.C, machine.Cpu.ApplyC, M=Enumerable.Range(0,16).Select(machine.Cpu.GetM).ToArray(),
    machine.Cpu.Supervisor.InternalInterrupts, machine.Cpu.Supervisor.SavedFlags,
    Memory=Convert.ToHexString(hash.GetHashAndReset()),
    Instructions=run.Outcome.Instructions, Time=machine.HardwareTimeline.Now.Nanoseconds,
    NextEvent=machine.HardwareTimeline.NextEventTime?.Nanoseconds };
Console.WriteLine(JsonSerializer.Serialize(new { workload, iterations, rounds,
    elapsedSeconds=watch.Elapsed.TotalSeconds, allocatedBytes=allocated, gen0Collections=gen0, state }));

[EventSource(Name="Besm6.Stage6.Measurement")]
sealed class MeasurementEvents : EventSource {
    public static readonly MeasurementEvents Instance = new();
    [Event(1)] public void Start() => WriteEvent(1);
    [Event(2)] public void Stop() => WriteEvent(2);
}
'''


def build_probe(dll, output, source=PROBE):
    directory = output / "probe"
    directory.mkdir(parents=True, exist_ok=True)
    refs = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(dll.parent / (name + ".dll")))}</HintPath></Reference>'
                   for name in ("Besm6.Runtime", "Besm6.Processor", "Besm6.Architecture", "Besm6.Assembler"))
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NoWarn>CS0618</NoWarn></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>'
    (directory / "Probe.csproj").write_text(project, encoding="utf-8")
    (directory / "Program.cs").write_text(source, encoding="utf-8")
    run = subprocess.run(["dotnet", "build", str(directory / "Probe.csproj"), "-c", "Release", "--nologo"],
                         capture_output=True, encoding="utf-8", errors="replace", timeout=120)
    (directory / "build.log").write_text(run.stdout + run.stderr, encoding="utf-8")
    if run.returncode:
        raise RuntimeError("Hardware probe build failed; see local build.log")
    return directory / "bin/Release/net8.0/Probe.dll"


def compare(rows, baseline, regression_limit_percent=10.0):
    """Host throughput ratios; equal guest state is a separate prerequisite."""
    lookup = {(row["workload"], row["variant"]): row for row in rows}
    result = {}
    for name in dict.fromkeys(row["variant"] for row in rows):
        ratios = [lookup[(job, baseline)]["loop_seconds"] / lookup[(job, name)]["loop_seconds"]
                  for job in dict.fromkeys(row["workload"] for row in rows)]
        gain = (math.prod(ratios) ** (1 / len(ratios)) - 1) * 100
        result[name] = {"geometric_mean_gain_percent": gain,
                        "regression_limit_percent": regression_limit_percent,
                        "regressions_within_limit": all(1 / ratio <= 1 + regression_limit_percent / 100 for ratio in ratios),
                        "gain_at_least_5_percent": gain >= 5}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--variant", action="append", required=True, metavar="NAME=DLL")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--jobs", nargs="+", choices=WORKLOADS, default=list(WORKLOADS))
    parser.add_argument("--iterations", type=int, default=32760)
    parser.add_argument("--rounds", type=int, default=0,
                        help="Optional outer guest loop; 0 retains the original short fixture")
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--regression-limit-percent", type=float, default=10.0)
    args = parser.parse_args()
    if not 1 <= args.iterations <= 32760 or not 0 <= args.rounds <= 32760 or args.warmups < 0 or args.repeats < 1:
        parser.error("Invalid iteration/warmup/repeat count")
    if not math.isfinite(args.regression_limit_percent) or args.regression_limit_percent < 0:
        parser.error("Regression limit must be finite and nonnegative")
    variants = {}
    for value in args.variant:
        name, sep, path = value.partition("=")
        if not sep or not name or name in variants or not pathlib.Path(path).is_file():
            parser.error("Variants require unique names and existing DLLs")
        variants[name] = pathlib.Path(path).resolve()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    if (output / "results.json").exists():
        parser.error("Use a new output directory; never overwrite a series")
    logs = output / "logs"; logs.mkdir(exist_ok=True)
    probes = {name: build_probe(dll, output / name) for name, dll in variants.items()}
    result = {"environment": bench.report_environment(), "validated": False,
              "scope": "public RunInstructions, no instruction observers; state hash after stopwatch",
              "probe_sha256": hashlib.sha256(PROBE.encode()).hexdigest(),
              "options": {"iterations": args.iterations, "rounds": args.rounds, "warmups": args.warmups, "repeats": args.repeats,
                          "regression_limit_percent": args.regression_limit_percent},
              "variants": {name: {"runtime_sha256": hashlib.sha256((dll.parent / "Besm6.Runtime.dll").read_bytes()).hexdigest(),
                                  "processor_sha256": hashlib.sha256((dll.parent / "Besm6.Processor.dll").read_bytes()).hexdigest()}
                           for name, dll in variants.items()}, "samples": [], "measurements": []}
    def save():
        (output / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    try:
        for job in args.jobs:
            reference = None
            for repetition in range(args.warmups + args.repeats):
                order = list(probes) if repetition % 2 == 0 else list(reversed(probes))
                for name in order:
                    run = subprocess.run(["dotnet", str(probes[name]), job, str(args.iterations), str(args.rounds)],
                                         capture_output=True, encoding="utf-8", errors="replace", timeout=120)
                    (logs / f"{job}-{name}-{repetition}.out").write_text(run.stdout + run.stderr, encoding="utf-8")
                    if run.returncode:
                        raise RuntimeError("Hardware run failed; see local log")
                    measured = json.loads(run.stdout)
                    if reference is None: reference = measured["state"]
                    if reference != measured["state"]: raise ValueError("Hardware state/count/time mismatch")
                    result["samples"].append({"variant": name, "warmup": repetition < args.warmups, **measured})
                    save()
            for name in probes:
                samples = [row for row in result["samples"] if row["variant"] == name and row["workload"] == job and not row["warmup"]]
                seconds = [row["elapsedSeconds"] for row in samples]
                result["measurements"].append({"variant": name, "workload": job, "loop_seconds": statistics.median(seconds),
                    "min_seconds": min(seconds), "max_seconds": max(seconds),
                    "allocated_bytes": statistics.median(row["allocatedBytes"] for row in samples),
                    "gen0_collections": statistics.median(row["gen0Collections"] for row in samples)})
                print(json.dumps(result["measurements"][-1]), flush=True)
        result["comparison"] = compare(result["measurements"], next(iter(probes)), args.regression_limit_percent)
        result["validated"] = True; save()
        with (output / "results.csv").open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(result["measurements"][0])); writer.writeheader(); writer.writerows(result["measurements"])
    except (RuntimeError, ValueError, subprocess.TimeoutExpired):
        result["error"] = "Hardware benchmark failed; details remain in local logs"; save(); raise


if __name__ == "__main__":
    main()
