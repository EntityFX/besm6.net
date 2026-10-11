#!/usr/bin/env python3
"""Collect CPU sampling and allocation profiles separately from timing runs.

Full EventPipe files stay in the local output folder. The summary contains
method/type names, weights and hashes, without host/account identifiers.
"""
import argparse
import hashlib
import json
import pathlib
import subprocess
from xml.sax.saxutils import escape

import analyze_processor_profile as sampling
import benchmark_hardware_processor as hardware
import benchmark_speed as bench

ALLOCATIONS = r'''
using Microsoft.Diagnostics.Tracing;
using System.Text.Json;
var counts = new Dictionary<string, (long Bytes, long Samples)>();
double? start = null, stop = null;
using var source = new EventPipeEventSource(args[0]);
source.Dynamic.All += ev => {
    if (ev.ProviderName != "Besm6.Stage6.Measurement") return;
    if ((int)ev.ID == 1) start = ev.TimeStampRelativeMSec;
    if ((int)ev.ID == 2) stop = ev.TimeStampRelativeMSec;
};
source.Clr.GCAllocationTick += ev => {
    if (start is null || stop is not null) return;
    var name = ev.TypeName ?? "<unknown>";
    counts.TryGetValue(name, out var previous);
    counts[name] = (previous.Bytes + (long)ev.AllocationAmount64, previous.Samples + 1);
};
source.Process();
if (start is null || stop is null || stop <= start) throw new InvalidOperationException("Missing measurement markers");
Console.WriteLine(JsonSerializer.Serialize(new { startMilliseconds=start, endMilliseconds=stop,
    allocations=counts.OrderByDescending(pair => pair.Value.Bytes)
    .Select(pair => new { type = pair.Key, sampledBytes = pair.Value.Bytes, samples = pair.Value.Samples }) }));
'''


def allocation_reader(trace_tool, output):
    matches = list(trace_tool.parent.glob(".store/dotnet-trace/*/dotnet-trace/*/tools/net*/any/Microsoft.Diagnostics.Tracing.TraceEvent.dll"))
    if len(matches) != 1:
        raise ValueError("Pass the isolated dotnet-trace tool installation")
    directory = output / "allocation-reader"; directory.mkdir()
    refs = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(matches[0].parent / (name + ".dll")))}</HintPath></Reference>'
                   for name in ("Microsoft.Diagnostics.Tracing.TraceEvent", "Microsoft.Diagnostics.FastSerialization"))
    (directory / "Reader.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>', encoding="utf-8")
    (directory / "Program.cs").write_text(ALLOCATIONS, encoding="utf-8")
    run = subprocess.run(["dotnet", "build", str(directory / "Reader.csproj"), "-c", "Release", "--nologo"],
                         capture_output=True, encoding="utf-8", errors="replace", timeout=120)
    (directory / "build.log").write_text(run.stdout + run.stderr, encoding="utf-8")
    if run.returncode: raise RuntimeError("Allocation reader failed; see local build log")
    return directory / "bin/Release/net8.0/Reader.dll"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True, type=pathlib.Path)
    parser.add_argument("--trace-tool", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--os-job", type=pathlib.Path,
                        help="Optional local pinned machine job for a hardware OS prefix profile")
    parser.add_argument("--jobs", nargs="+",
                        choices=["dh10000", "mp3", "hardware-os"] + ["hardware-" + job for job in hardware.WORKLOADS])
    args = parser.parse_args()
    dll = args.dll.resolve(); trace_tool = args.trace_tool.resolve(); output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    reader = allocation_reader(trace_tool, output)
    functional = bench.build_runtime_probe(dll, output / "functional")
    # Mark only RunLoaded; startup, guest loading and post-run hashing are excluded.
    program = output / "functional/runtime-probe/Program.cs"
    text = program.read_text(encoding="utf-8").replace("var watch = Stopwatch.StartNew();",
        "MeasurementEvents.Instance.Start();\nvar watch = Stopwatch.StartNew();").replace("watch.Stop();",
        "watch.Stop();\nMeasurementEvents.Instance.Stop();")
    text += hardware.PROBE[hardware.PROBE.index('[EventSource(Name='):]
    text = "using System.Diagnostics.Tracing;\n" + text
    program.write_text(text, encoding="utf-8")
    rebuilt = subprocess.run(["dotnet", "build", str(program.parent / "Probe.csproj"), "-c", "Release", "--nologo"],
                             capture_output=True, encoding="utf-8", errors="replace", timeout=120)
    if rebuilt.returncode: raise RuntimeError("Marked functional probe build failed")
    staged = hardware.build_probe(dll, output / "hardware")
    jobs, config = bench.prepare_jobs(output)
    commands = {job: ["dotnet", str(functional), str(jobs[job]), str(config)] for job in ("dh10000", "mp3")}
    commands.update({"hardware-" + job: ["dotnet", str(staged), job, "32760"] for job in hardware.WORKLOADS})
    if args.os_job:
        import benchmark_hardware_os as operating_system
        os_probe = hardware.build_probe(dll, output / "os", operating_system.PROBE)
        commands["hardware-os"] = ["dotnet", str(os_probe), str(args.os_job.resolve()), "1000000"]
    if args.jobs:
        if any(job not in commands for job in args.jobs):
            parser.error("hardware-os requires --os-job")
        commands = {job: commands[job] for job in args.jobs}
    result = {"environment": bench.report_environment(), "scope": "marked execution interval; startup, loading and post-run hashing excluded; profiling is not a throughput measurement",
              "runtime_sha256": hashlib.sha256((dll.parent / "Besm6.Runtime.dll").read_bytes()).hexdigest(),
              "processor_sha256": hashlib.sha256((dll.parent / "Besm6.Processor.dll").read_bytes()).hexdigest(),
              "profiles": {}}
    for job, command in commands.items():
        row = {}
        for kind, profile in (("cpu", "dotnet-sampled-thread-time"), ("allocations", "gc-verbose")):
            path = output / f"{job}-{kind}.nettrace"
            traced = subprocess.run([str(trace_tool), "collect", "--profile", profile, "--output", str(path),
                                     "--providers", "Besm6.Stage6.Measurement:0x0:4",
                                     "--show-child-io", "--"] + command,
                                    capture_output=True, encoding="utf-8", errors="replace", timeout=180)
            (output / f"{job}-{kind}.log").write_text(traced.stdout + traced.stderr, encoding="utf-8")
            if traced.returncode: raise RuntimeError("Profile run failed; see local logs")
            row[kind + "_trace_sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
            if kind == "cpu":
                converted = subprocess.run([str(trace_tool), "convert", str(path), "--format", "Speedscope"],
                                           capture_output=True, encoding="utf-8", errors="replace", timeout=120)
                if converted.returncode: raise RuntimeError("Speedscope conversion failed")
                window = subprocess.run(["dotnet", str(reader), str(path)], capture_output=True,
                                        encoding="utf-8", errors="replace", timeout=120)
                if window.returncode: raise RuntimeError("CPU measurement markers missing")
                bounds = json.loads(window.stdout)
                data = sampling.summarize(path.with_suffix(".speedscope.json"), bounds["startMilliseconds"], bounds["endMilliseconds"])
                data["file"] = path.with_suffix(".speedscope.json").name
                row["sampling"] = data
            else:
                parsed = subprocess.run(["dotnet", str(reader), str(path)], capture_output=True,
                                        encoding="utf-8", errors="replace", timeout=120)
                if parsed.returncode: raise RuntimeError("Allocation trace parsing failed")
                row["allocation_types"] = json.loads(parsed.stdout)["allocations"]
        result["profiles"][job] = row
        (output / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"profile": job, "allocation_types": row["allocation_types"][:5]}), flush=True)


if __name__ == "__main__":
    main()
