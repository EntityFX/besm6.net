#!/usr/bin/env python3
"""Sequential original/max acceptance for a translated CPU workload, without an OS.

The Fortran compatibility benchmarks remain in benchmark_processor_stages.py.
This probe exercises mathematical instruction/operand access, divide, BRZ and
STOP with a model duration over one second; it is not an OS boot benchmark.
"""
import argparse
import csv
import hashlib
import json
import pathlib
import statistics
import subprocess
import time
from xml.sax.saxutils import escape

import benchmark_speed as bench

PROGRAM = r'''
using Besm6;
using Besm6.Architecture;
using Besm6.Core;
using Besm6.Runtime;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
var machine = new MachineCore(memoryModel: MemoryModel.Mapped);
var memory = machine.MappedMemory!;
memory.Supervisor = memory.AssignmentBlocked = memory.ProtectionBlocked = false;
memory.SetPhysicalPage(0, 1);
memory.SetPhysicalPage(1, 2);
string[] program = { "vtm -100(1), utc 0", "vtm -40000(2), utc 0",
    "xta 2000, a/x 2000", "atx 2001, vlm 12(2)", "vlm 11(1), stop" };
for (uint i=0; i<program.Length; i++)
    machine.Memory.Write(1032+i, new Word48(Besm6.Asm.Assembler.Asm(program[i])));
var one = Word48.FromDouble(1);
machine.Memory.Write(2048, one);
machine.Cpu.StartAt(8);
var loader = new DubnaLoader(machine) { Speed = args[0] == "original" ? ExecutionSpeed.Original : ExecutionSpeed.Max,
    CollectStatistics = args[0] != "max-bare", InstructionLimit=5_000_000, HangDetect=false, LoopDetect=false,
    Output = _ => { } };
var watch = Stopwatch.StartNew();
var result = loader.RunLoaded();
watch.Stop();
// VLM checks zero before increment: (16384+1)*(64+1) inner iterations.
const long expectedInstructions = 4_260_298;
const long expectedCycles = 19_170_714;
if (!result.Success || !loader.HaltedByStop || loader.InstructionsExecuted != expectedInstructions
    || machine.Clock.Tick != (ulong)expectedInstructions || machine.Cpu.GetM(1) != 0 || machine.Cpu.GetM(2) != 0
    || machine.Cpu.A != one || machine.Memory.Read(2049) != one
    || (loader.Statistics is {} stats && stats.ModelCycles != expectedCycles)) return 1;
var finalState = new { A=machine.Cpu.A.Value, Y=machine.Cpu.Y.Value, R=machine.Cpu.R,
    K=machine.Cpu.K, machine.Cpu.RightInstruction, machine.Cpu.C, machine.Cpu.ApplyC,
    M=Enumerable.Range(0,16).Select(machine.Cpu.GetM).ToArray(), Tick=machine.Clock.Tick };
var completeState = new { finalState, memory.Supervisor, memory.AssignmentBlocked, memory.ProtectionBlocked,
    memory.InvertLeftStoreControl, memory.InvertRightStoreControl, memory.LastFault,
    Rp=Enumerable.Range(0,32).Select(i=>memory.Assignment.GetPhysicalPage((uint)i)).ToArray(),
    memory.Assignment.OperandProtectionMask, Brz=memory.GetOperandSnapshot(), Brs=memory.GetInstructionSnapshot(),
    Mozu=Enumerable.Range(0,32768).Select(i=>memory.PhysicalMemory.ReadRaw((uint)i).RawValue).ToArray(),
    Panel=Enumerable.Range(1,7).Select(i=>memory.GetPanel((uint)i).RawValue).ToArray() };
Console.WriteLine(JsonSerializer.Serialize(new { instructions=loader.InstructionsExecuted, cycles=expectedCycles,
    modelSeconds=expectedCycles*1e-7, loopSeconds=loader.Statistics?.ElapsedSeconds ?? watch.Elapsed.TotalSeconds,
    runLoadedSeconds=watch.Elapsed.TotalSeconds, finalState,
    stateSha256=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(completeState))) }));
return 0;
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--warmups", type=int, default=1)
    parser.add_argument("--repeats", type=int, default=5)
    args = parser.parse_args()
    if args.warmups < 0 or args.repeats < 1:
        parser.error("warmups must be nonnegative; repeats positive")
    dll = args.dll.resolve()
    output = args.output.resolve()
    probe = output / "probe"
    probe.mkdir(parents=True, exist_ok=True)
    refs = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(dll.parent / (name + ".dll")))}</HintPath></Reference>'
                   for name in ("Besm6.Runtime", "Besm6.Processor", "Besm6.Architecture", "Besm6.Assembler"))
    (probe / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
        '<NoWarn>CS0618</NoWarn></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>', encoding="utf-8")
    (probe / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    build = subprocess.run(["dotnet", "build", str(probe / "Probe.csproj"), "-c", "Release", "--nologo"],
                           capture_output=True, encoding="utf-8", errors="replace", timeout=120)
    (probe / "build.log").write_text(build.stdout + build.stderr, encoding="utf-8")
    if build.returncode:
        raise RuntimeError("Probe build failed; see local build.log")
    result = {"environment": bench.report_environment(), "memory_model": "Mapped",
              "workload": "translated divide/load/store loop; no DATE extracodes or OS",
              "processor_sha256": hashlib.sha256((dll.parent / "Besm6.Processor.dll").read_bytes()).hexdigest(),
              "warmups": args.warmups, "repeats": args.repeats, "samples": [], "accepted": False}
    def save():
        (output / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    save()
    state_hash = None
    for repetition in range(args.warmups + args.repeats):
        modes = ["max-bare", "max", "original"]
        if repetition % 2:
            modes.reverse()
        for mode in modes:
            start = time.perf_counter()
            run = subprocess.run(["dotnet", str(probe / "bin/Release/net8.0/Probe.dll"), mode], cwd=output,
                                 capture_output=True, encoding="utf-8", errors="replace", timeout=120)
            elapsed = time.perf_counter() - start
            (output / f"{mode}-{repetition}.out").write_text(run.stdout + run.stderr, encoding="utf-8")
            if run.returncode:
                result["failed_run"] = {"mode": mode, "repetition": repetition, "exit_code": run.returncode}
                save()
                raise RuntimeError("Guest result/count/STOP validation failed; see local output")
            sample = json.loads(run.stdout)
            if state_hash is not None and sample["stateSha256"] != state_hash:
                result["error"] = "Complete CPU/memory/buffer state differs between modes"
                save()
                raise ValueError("Complete CPU/memory/buffer state differs between modes")
            state_hash = sample["stateSha256"]
            result["samples"].append({"mode": mode, "warmup": repetition < args.warmups,
                                      "process_seconds": elapsed, **sample})
            save()
    result["measurements"] = []
    for mode in ("max-bare", "max", "original"):
        samples = [s for s in result["samples"] if s["mode"] == mode and not s["warmup"]]
        errors = [abs(s["loopSeconds"] - s["modelSeconds"]) for s in samples]
        row = {"mode": mode, "loop_seconds": statistics.median(s["loopSeconds"] for s in samples),
               "process_seconds": statistics.median(s["process_seconds"] for s in samples),
               "model_seconds": samples[0]["modelSeconds"], "max_tempo_error_seconds": max(errors),
               "tempo_met": all(e <= max(.020, s["modelSeconds"] * .02) for e, s in zip(errors, samples)) if mode == "original" else None}
        result["measurements"].append(row)
        print(json.dumps(row), flush=True)
    result["accepted"] = result["measurements"][-1]["tempo_met"]
    save()
    with (output / "results.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(result["measurements"][0]))
        writer.writeheader()
        writer.writerows(result["measurements"])
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
