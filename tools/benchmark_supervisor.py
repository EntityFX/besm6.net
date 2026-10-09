#!/usr/bin/env python3
"""Sequential functional supervisor pacing/state benchmark; not an OS acceptance gate.

Build the Release runtime first. A generated probe references those exact binaries.
Reports whitelist environment fields and never record account/host names or DLL paths.
"""
import argparse
import csv
import hashlib
import json
import pathlib
import statistics
import subprocess
import sys
import time
from xml.sax.saxutils import escape

import benchmark_speed as common

ROOT = pathlib.Path(__file__).resolve().parents[1]
MODES = ("max", "original", "maxbare")
JOBS = ("empty", "periodic")
PREFIX = "Supervisor measurement: "

SOURCE = r'''
using Besm6.Architecture;
using Besm6.Core;
using Besm6.Runtime;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
int outer = int.Parse(args[0]), inner = int.Parse(args[1]);
string job = args[2], mode = args[3];
var machine = new MachineCore(ProcessorProfile.Supervisor);
var memory = machine.MappedMemory!;
uint H(Opcode op, uint address = 0, uint register = 0) => (register << 20) | ((uint)op << 12) | address;
void W(uint address, uint left, uint right) => machine.Memory.Write(address, new(((ulong)left << 24) | right));
// VLM tests the old modifier before incrementing; starting at -(N-1) gives N passes.
W(8, H(Opcode.Vtm, (uint)(32769 - outer) & 0x7FFF, 2), H(Opcode.Uj, 9));
W(9, H(Opcode.Vtm, (uint)(32769 - inner) & 0x7FFF, 1), H(Opcode.Uj, 10));
W(10, H(Opcode.Xta, 100), H(Opcode.Arx, 101));
W(11, H(Opcode.Atx, 102), H(Opcode.Vlm, 10, 1));
W(12, H(Opcode.Vlm, 9, 2), H(Opcode.Stop));
memory.PhysicalMemory.Store(100, new(12345), true, true);
memory.PhysicalMemory.Store(101, new(6789), true, true);
machine.Cpu.StartAt(8);
const ulong period = 100000;
var events = new List<object>();
ulong? nextEvent = null;
void Callback() {
    events.Add(new { tick = machine.Clock.Tick, k = machine.Cpu.GetK(), right = machine.Cpu.RightInstruction,
        a = machine.Cpu.GetA().Value, m1 = machine.Cpu.GetM(1), m2 = machine.Cpu.GetM(2) });
    nextEvent = machine.Clock.Tick + period;
    machine.Scheduler.Schedule(period, Callback);
}
if (job == "periodic") { nextEvent = period; machine.Scheduler.Schedule(period, Callback); }
long expectedInstructions = 2L + outer * (2L + 4L * inner + 1L) + 1;
long expectedCycles = Besm6Timing.CyclesOf(Opcode.Vtm) + Besm6Timing.CyclesOf(Opcode.Uj) +
    outer * (Besm6Timing.CyclesOf(Opcode.Vtm) + Besm6Timing.CyclesOf(Opcode.Uj) +
    (long)inner * (Besm6Timing.CyclesOf(Opcode.Xta) + Besm6Timing.CyclesOf(Opcode.Arx) +
    Besm6Timing.CyclesOf(Opcode.Atx) + Besm6Timing.CyclesOf(Opcode.Vlm)) + Besm6Timing.CyclesOf(Opcode.Vlm)) +
    Besm6Timing.CyclesOf(Opcode.Stop);
var watch = Stopwatch.StartNew();
var result = machine.RunInstructions(expectedInstructions + 10,
    mode == "original" ? ExecutionSpeed.Original : ExecutionSpeed.Max, mode != "maxbare");
watch.Stop();
using var physicalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
Span<byte> bytes = stackalloc byte[8];
for (uint address = 0; address < memory.Size; address++) {
    BinaryPrimitives.WriteUInt64BigEndian(bytes, memory.PhysicalMemory.ReadRaw(address).RawValue);
    physicalHash.AppendData(bytes);
}
var state = new {
    k = machine.Cpu.GetK(), right = machine.Cpu.RightInstruction, a = machine.Cpu.GetA().Value,
    y = machine.Cpu.GetY().Value, r = machine.Cpu.GetR(), c = machine.Cpu.C, applyC = machine.Cpu.ApplyC,
    m = Enumerable.Range(0,16).Select(machine.Cpu.GetM).ToArray(), tick = machine.Clock.Tick,
    supervisor = machine.Cpu.Supervisor!.Snapshot(),
    physicalMemorySha256 = Convert.ToHexString(physicalHash.GetHashAndReset()),
    physicalWordCount = memory.Size,
    brz = memory.GetOperandSnapshot().Select(e => new { tag = e.Request.EncodedValue, raw = e.Word.RawValue }).ToArray(),
    brzRawRegisters = Enumerable.Range(0,8).Select(i => memory.ReadOperandBufferRegister(i).RawValue).ToArray(),
    brs = memory.GetInstructionSnapshot().Select(e => new { tag = e.Request.EncodedValue, raw = e.Word.RawValue, e.FetchAddress }).ToArray(),
    rp = Enumerable.Range(0,32).Select(i => memory.Assignment.GetPhysicalPage((uint)i)).ToArray(),
    rz = memory.Assignment.OperandProtectionMask,
    invertLeft = memory.InvertLeftStoreControl, invertRight = memory.InvertRightStoreControl,
    fault = memory.LastFault,
    io = new { interrupts = machine.SupervisorIo!.ReadRegister(0x1F).Value, mask = machine.SupervisorIo.ExternalInterruptMask,
        peripheral = machine.SupervisorIo.PeripheralInterrupts, peripheralMask = machine.SupervisorIo.PeripheralInterruptMask,
        completedTransfers = machine.SupervisorIo.CompletedTransfers },
    events, nextEvent
};
var report = new { job, mode, outer, inner, success = result.Outcome.Success, stopped = result.Outcome.Stopped,
    instructions = result.Outcome.Instructions, expectedInstructions, expectedCycles,
    modelCycles = result.Statistics?.ModelCycles,
    loopSeconds = result.Statistics?.ElapsedSeconds ?? watch.Elapsed.TotalSeconds,
    runInstructionsSeconds = watch.Elapsed.TotalSeconds, state,
    resultValue = memory.Read(102).Value, eventCount = events.Count };
Console.WriteLine("Supervisor measurement: " + JsonSerializer.Serialize(report));
return result.Outcome.Success && result.Outcome.Stopped && result.Outcome.Instructions == expectedInstructions &&
    machine.Clock.Tick == (ulong)expectedInstructions && memory.Read(102).Value == 19134 &&
    events.Count == (job == "periodic" ? (int)((ulong)expectedInstructions / period) : 0) &&
    (result.Statistics?.ModelCycles is null || result.Statistics.ModelCycles == expectedCycles) ? 0 : 1;
'''


def parse_run(output, returncode):
    lines = [line[len(PREFIX):] for line in output.splitlines() if line.startswith(PREFIX)]
    if returncode or len(lines) != 1:
        raise ValueError("Probe failed or did not emit exactly one measurement")
    row = json.loads(lines[0])
    if not row["success"] or not row["stopped"] or row["instructions"] != row["expectedInstructions"]:
        raise ValueError("Probe did not complete the full guest program at STOP")
    if row["state"]["tick"] != row["instructions"] or row["resultValue"] != 19134:
        raise ValueError("Guest ticks or arithmetic control value differ")
    if row["modelCycles"] is not None and row["modelCycles"] != row["expectedCycles"]:
        raise ValueError("Measured cycles differ from the shared timing-table formula")
    expected_events = row["instructions"] // 100000 if row["job"] == "periodic" else 0
    if row["eventCount"] != expected_events:
        raise ValueError("Periodic callback count differs")
    canonical = json.dumps(row["state"], sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")
    row["stateSha256"] = hashlib.sha256(canonical).hexdigest()
    row["modelSeconds"] = row["expectedCycles"] * 1e-7
    row["tempoToleranceSeconds"] = max(.020, row["modelSeconds"] * .02)
    row["tempoErrorSeconds"] = row["loopSeconds"] - row["modelSeconds"]
    row["tempoMet"] = row["mode"] != "original" or abs(row["tempoErrorSeconds"]) <= row["tempoToleranceSeconds"]
    return row


def build_probe(dll, directory):
    directory.mkdir(parents=True, exist_ok=True)
    references = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(dll.parent / (name + ".dll")))}</HintPath></Reference>'
                         for name in ("Besm6.Runtime", "Besm6.Processor", "Besm6.Architecture", "Besm6.Assembler"))
    (directory / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable><NoWarn>CS0618</NoWarn></PropertyGroup><ItemGroup>' + references +
        '</ItemGroup></Project>', encoding="utf-8")
    (directory / "Program.cs").write_text(SOURCE, encoding="utf-8")
    build = subprocess.run(["dotnet", "build", str(directory / "Probe.csproj"), "-c", "Release", "--nologo"],
                           capture_output=True, text=True, timeout=120)
    # Build output can contain local paths. Do not copy it into benchmark reports.
    if build.returncode:
        errors = [line.split(": error ", 1)[1].split(" [", 1)[0] for line in build.stdout.splitlines() if ": error " in line]
        raise RuntimeError("Generated probe build failed: " + ("; ".join(errors) or "compiler diagnostics unavailable"))
    return directory / "bin/Release/net8.0/Probe.dll"


def self_test():
    row = dict(success=True, stopped=True, instructions=3, expectedInstructions=3,
               resultValue=19134, state={"tick": 3}, modelCycles=4, expectedCycles=4,
               job="empty", eventCount=0, mode="max", loopSeconds=.01)
    assert parse_run(PREFIX + json.dumps(row), 0)["modelSeconds"] == 4e-7
    for bad in ("", PREFIX + json.dumps({**row, "stopped": False}),
                PREFIX + json.dumps({**row, "modelCycles": 5})):
        try:
            parse_run(bad, 0)
        except ValueError:
            pass
        else:
            raise AssertionError("Invalid or truncated measurement was accepted")
    print("Supervisor benchmark parser self-test passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=pathlib.Path, default=ROOT / "src/Besm6.Runtime/bin/Release/net8.0/Besm6.Runtime.dll")
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "tests-run/supervisor-benchmark")
    parser.add_argument("--jobs", nargs="+", choices=JOBS, default=list(JOBS))
    parser.add_argument("--modes", nargs="+", choices=MODES, default=list(MODES))
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--outer", type=int, default=48)
    parser.add_argument("--inner", type=int, default=32767)
    parser.add_argument("--timeout", type=float, default=120)
    parser.add_argument("--smoke", action="store_true", help="One short sample per job/mode; not a pacing/performance gate")
    parser.add_argument("--build-only", action="store_true", help="Build generated probe without executing guest benchmarks")
    parser.add_argument("--self-test", action="store_true", help="Check strict output validation without building or running")
    parser.add_argument("--require-tempo", action="store_true", help="Fail if any original sample exceeds pacing tolerance")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.smoke:
        args.outer, args.inner, args.warmups, args.repeats = 1, 7, 0, 1
    if args.repeats < 1 or args.warmups < 0 or not 1 <= args.outer <= 32767 or not 1 <= args.inner <= 32767:
        parser.error("Invalid repetitions or guest loop counts")
    if not args.dll.is_file():
        parser.error("Build the Release runtime before benchmarking")
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=True)
    report = {"environment": common.report_environment(), "scope": "functional supervisor RunInstructions; not OS gate",
              "options": {"outer": args.outer, "inner": args.inner, "jobs": args.jobs, "modes": args.modes,
                          "warmups": args.warmups, "repeats": args.repeats, "smoke": args.smoke,
                          "order": "sequential; mode order reverses each repetition", "date_dependency": "none"},
              "binaries": {name: hashlib.sha256((args.dll.parent / name).read_bytes()).hexdigest()
                           for name in ("Besm6.Runtime.dll", "Besm6.Processor.dll", "Besm6.Architecture.dll")},
              "probeSourceSha256": hashlib.sha256(SOURCE.encode("utf-8")).hexdigest(),
              "physicalMemoryEncoding": "all raw50 words as zero-extended big-endian uint64 in physical address order",
              "samples": [], "medians": [], "validated": False, "originalTempoMet": None}

    def save():
        (directory / "results.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        rows = [{k: v for k, v in sample.items() if k != "state"} for sample in report["samples"]]
        if rows:
            with (directory / "results.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
                writer.writeheader()
                writer.writerows(rows)

    try:
        probe = build_probe(args.dll.resolve(), directory / "probe")
        if args.build_only:
            report["buildOnly"] = True
            save()
            print("Supervisor benchmark probe compiled; no measurements executed")
            return 0
        references = {}
        for repetition in range(args.warmups + args.repeats):
            order = list(args.modes)
            if repetition % 2:
                order.reverse()
            for job in args.jobs:
                for mode in order:
                    begin = time.perf_counter()
                    process = subprocess.run(["dotnet", str(probe), str(args.outer), str(args.inner), job, mode],
                                             cwd=directory, capture_output=True, text=True, timeout=args.timeout)
                    (directory / f"{job}-{mode}-{repetition}.out").write_text(process.stdout, encoding="utf-8")
                    sample = parse_run(process.stdout, process.returncode)
                    sample.update(processSeconds=time.perf_counter() - begin, warmup=repetition < args.warmups,
                                  repetition=repetition)
                    parity = (sample["stateSha256"], sample["instructions"], sample["expectedCycles"])
                    if job in references and parity != references[job]:
                        raise ValueError("Original/max/maxbare guest state, event order or counters differ")
                    references[job] = parity
                    if not args.smoke and sample["modelSeconds"] < 1:
                        raise ValueError("A pacing benchmark must represent at least one model second")
                    report["samples"].append(sample)
                    save()
                    print(json.dumps({k: sample[k] for k in ("job", "mode", "warmup", "loopSeconds", "modelSeconds", "tempoMet")}), flush=True)
        measured = [sample for sample in report["samples"] if not sample["warmup"]]
        for job in args.jobs:
            for mode in args.modes:
                samples = [sample for sample in measured if sample["job"] == job and sample["mode"] == mode]
                report["medians"].append({"job": job, "mode": mode,
                    "loopSeconds": statistics.median(s["loopSeconds"] for s in samples),
                    "processSeconds": statistics.median(s["processSeconds"] for s in samples),
                    "loopMinimumSeconds": min(s["loopSeconds"] for s in samples),
                    "loopMaximumSeconds": max(s["loopSeconds"] for s in samples)})
        original = [sample for sample in measured if sample["mode"] == "original"]
        report["originalTempoMet"] = all(sample["tempoMet"] for sample in original) if original else None
        report["validated"] = True
        save()
        return 0 if not args.require_tempo or report["originalTempoMet"] is True else 2
    except (ValueError, RuntimeError, subprocess.TimeoutExpired) as failure:
        report["error"] = type(failure).__name__ + ": supervisor benchmark validation failed"
        save()
        print(str(failure), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
