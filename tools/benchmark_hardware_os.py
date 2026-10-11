#!/usr/bin/env python3
"""Measure a pinned hardware OS boot prefix without instruction observers.

The prefix is a throughput workload, not proof of completing an OS job. Full
DISPAK acceptance, operator actions and output checks remain a separate gate.
Images are read from a local machine job; their configured hashes are checked
by Runtime. All state/media hashing runs after the measured execution interval.
"""
import argparse
import csv
import hashlib
import json
import math
import pathlib
import statistics
import subprocess

import benchmark_hardware_processor as hardware
import benchmark_speed as bench

PROBE = r'''
using Besm6.Architecture;
using Besm6.Core;
using Besm6.Runtime;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

var configuration = MachineJobConfiguration.Load(args[0]);
if (configuration.ParseExecutionModel() != MachineExecutionModel.Hardware)
    throw new ArgumentException("The OS throughput job must select hardware execution.");
long limit = long.Parse(args[1]);
if (limit < 1) throw new ArgumentOutOfRangeException();
var machine = configuration.CreateMachine();
var memory = machine.MappedMemory!;
var io = machine.SupervisorIo!;
MeasurementEvents.Instance.Start();
long allocated = GC.GetAllocatedBytesForCurrentThread();
int gen0 = GC.CollectionCount(0);
var watch = Stopwatch.StartNew();
var run = machine.RunInstructions(limit, ExecutionSpeed.Max, false);
watch.Stop();
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
gen0 = GC.CollectionCount(0) - gen0;
MeasurementEvents.Instance.Stop();
if (!run.Outcome.LimitExceeded || run.Outcome.Stopped || run.Outcome.ErrorMessage is not null ||
    run.Outcome.Instructions < 1 || run.Outcome.Instructions > limit || machine.Clock.Tick != 0)
    throw new InvalidOperationException("OS prefix boundary/count/clock validation failed.");

string WordsHash(IEnumerable<MemoryWord50> words) {
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> bytes = stackalloc byte[8];
    foreach (var word in words) {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, word.RawValue);
        digest.AppendData(bytes);
    }
    return Convert.ToHexString(digest.GetHashAndReset());
}
var disks = configuration.Devices!.DiskImages.Select(disk => disk.Unit)
    .Concat(configuration.Devices.ScratchDisks.Select(disk => disk.Unit)).Order().ToArray();
var state = new {
    Instructions = run.Outcome.Instructions, AttemptLimit = limit, Clock = machine.Clock.Tick,
    Time = machine.HardwareTimeline.Now.Nanoseconds,
    NextEvent = machine.HardwareTimeline.NextEventTime?.Nanoseconds,
    K = machine.Cpu.GetK(), Right = machine.Cpu.RightInstruction,
    A = machine.Cpu.GetA().Value, Y = machine.Cpu.GetY().Value, R = machine.Cpu.GetR(),
    machine.Cpu.C, machine.Cpu.ApplyC, M = Enumerable.Range(0,16).Select(machine.Cpu.GetM).ToArray(),
    Supervisor = machine.Cpu.Supervisor!.Snapshot(),
    Memory = WordsHash(Enumerable.Range(0,(int)memory.PhysicalMemory.CapacityWords)
        .Select(index => memory.PhysicalMemory.ReadRaw((uint)index))),
    Disks = disks.ToDictionary(unit => unit.ToString(), unit => WordsHash(io.SnapshotDisk(unit))),
    Drums = configuration.Devices.ScratchDrums.Order().Select(unit => WordsHash(io.SnapshotDrum(unit))).ToArray(),
    Panels = Enumerable.Range(1,7).Select(index => memory.GetPanel((uint)index).RawValue).ToArray(),
    Assignments = Enumerable.Range(0,32).Select(index => memory.Assignment.GetPhysicalPage((uint)index)).ToArray(),
    Protection = memory.Assignment.OperandProtectionMask,
    Operands = memory.GetOperandSnapshot(),
    OperandRegisters = Enumerable.Range(0,8).Select(index => memory.ReadOperandBufferRegister(index).RawValue).ToArray(),
    Commands = memory.GetInstructionSnapshot(), memory.LastFault,
    io.ExternalInterrupts, io.ExternalInterruptMask, io.PeripheralInterrupts, io.PeripheralInterruptMask,
    io.IsSerialIdle
};
Console.WriteLine(JsonSerializer.Serialize(new { workload="os_boot_prefix", elapsedSeconds=watch.Elapsed.TotalSeconds,
    allocatedBytes=allocated, gen0Collections=gen0, state }));
'''
PROBE += hardware.PROBE[hardware.PROBE.index('[EventSource(Name='):]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--variant", action="append", required=True, metavar="NAME=DLL")
    parser.add_argument("--machine-job", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--instructions", type=int, default=1_000_000)
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--regression-limit-percent", type=float, default=10.0)
    args = parser.parse_args()
    if args.instructions < 1 or args.warmups < 0 or args.repeats < 1:
        parser.error("Invalid instruction/warmup/repeat count")
    if not math.isfinite(args.regression_limit_percent) or args.regression_limit_percent < 0:
        parser.error("Regression limit must be finite and nonnegative")
    if not args.machine_job.is_file():
        parser.error("A local machine job is required")
    variants = {}
    for value in args.variant:
        name, sep, path = value.partition("=")
        if not sep or not name or name in variants or not pathlib.Path(path).is_file():
            parser.error("Variants require unique names and existing DLLs")
        variants[name] = pathlib.Path(path).resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if (output / "results.json").exists():
        parser.error("Use a new output directory; never overwrite a series")
    logs = output / "logs"
    logs.mkdir(exist_ok=True)
    probes = {name: hardware.build_probe(dll, output / name, PROBE) for name, dll in variants.items()}
    result = {"environment": bench.report_environment(), "validated": False,
        "scope": "public RunInstructions OS boot prefix; no per-instruction observers; hashes after stopwatch",
        "full_os_job_acceptance": False,
        "probe_sha256": hashlib.sha256(PROBE.encode()).hexdigest(),
        "machine_job_sha256": hashlib.sha256(args.machine_job.read_bytes()).hexdigest(),
        "options": {"instructions": args.instructions, "warmups": args.warmups, "repeats": args.repeats,
                    "regression_limit_percent": args.regression_limit_percent},
        "variants": {name: {"runtime_sha256": hashlib.sha256((dll.parent / "Besm6.Runtime.dll").read_bytes()).hexdigest(),
                            "processor_sha256": hashlib.sha256((dll.parent / "Besm6.Processor.dll").read_bytes()).hexdigest()}
                     for name, dll in variants.items()}, "samples": [], "measurements": []}
    def save():
        (output / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    reference = None
    try:
        for repetition in range(args.warmups + args.repeats):
            order = list(probes) if repetition % 2 == 0 else list(reversed(probes))
            for name in order:
                run = subprocess.run(["dotnet", str(probes[name]), str(args.machine_job.resolve()), str(args.instructions)],
                    capture_output=True, encoding="utf-8", errors="replace", timeout=600)
                (logs / f"os-{name}-{repetition}.out").write_text(run.stdout + run.stderr, encoding="utf-8")
                if run.returncode:
                    raise RuntimeError("OS throughput run failed; see local log")
                measured = json.loads(run.stdout)
                if reference is None:
                    reference = measured["state"]
                if reference != measured["state"]:
                    raise ValueError("OS prefix state/count/time mismatch")
                result["samples"].append({"variant": name, "warmup": repetition < args.warmups, **measured})
                save()
        for name in probes:
            samples = [row for row in result["samples"] if row["variant"] == name and not row["warmup"]]
            seconds = [row["elapsedSeconds"] for row in samples]
            result["measurements"].append({"variant": name, "workload": "os_boot_prefix",
                "loop_seconds": statistics.median(seconds), "min_seconds": min(seconds), "max_seconds": max(seconds),
                "allocated_bytes": statistics.median(row["allocatedBytes"] for row in samples),
                "gen0_collections": statistics.median(row["gen0Collections"] for row in samples)})
            print(json.dumps(result["measurements"][-1]), flush=True)
        result["comparison"] = hardware.compare(result["measurements"], next(iter(probes)), args.regression_limit_percent)
        result["validated"] = True
        save()
        with (output / "results.csv").open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(result["measurements"][0]))
            writer.writeheader()
            writer.writerows(result["measurements"])
    except (RuntimeError, ValueError, subprocess.TimeoutExpired):
        result["error"] = "OS throughput failed; details remain in local logs"
        save()
        raise


if __name__ == "__main__":
    main()
