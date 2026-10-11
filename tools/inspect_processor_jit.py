#!/usr/bin/env python3
"""Capture host JIT code locally and export a compact, path-free method summary.

This diagnostic run is separate from throughput measurements. Native addresses,
raw assembly and process output remain local; the summary contains method names,
compilation tiers, code sizes and call targets only.
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import subprocess

import benchmark_hardware_processor as hardware
import benchmark_speed as bench


def summarize(text):
    methods = []
    for section in text.split("; Assembly listing for method ")[1:]:
        title = section.splitlines()[0]
        size = re.search(r"; Total bytes of code (\d+)", section)
        calls = sorted({target for target in re.findall(r"\bcall\s+\[(.+)\]", section)
                        if not target.startswith("0x")})
        methods.append({"method": title, "code_bytes": int(size.group(1)) if size else None,
                        "calls": calls})
    if not methods:
        raise ValueError("No matching JIT assembly was emitted")
    return methods


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--methods", default="get_HasPendingInstruction IsPreparedInstructionActive MemLoad")
    parser.add_argument("--workload", choices=hardware.WORKLOADS, default="arithmetic")
    parser.add_argument("--functional-job", choices=["dh10000", "mp3"],
                        help="Inspect a hosted functional job instead of a synthetic hardware workload")
    args = parser.parse_args()
    dll = args.dll.resolve()
    if not dll.is_file():
        parser.error("A completed CLI Release DLL is required")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if (output / "summary.json").exists():
        parser.error("Use a new output directory")
    if args.functional_job:
        probe = bench.build_runtime_probe(dll, output)
        jobs, config = bench.prepare_jobs(output)
        command = ["dotnet", str(probe), str(jobs[args.functional_job]), str(config)]
        workload = args.functional_job
    else:
        probe = hardware.build_probe(dll, output)
        command = ["dotnet", str(probe), args.workload, "32760"]
        workload = args.workload
    assembly = output / "jit.txt"
    environment = os.environ.copy()
    environment["DOTNET_JitDisasm"] = args.methods
    environment["DOTNET_JitStdOutFile"] = str(assembly)
    run = subprocess.run(command, env=environment,
                         capture_output=True, encoding="utf-8", errors="replace", timeout=120)
    (output / "run.log").write_text(run.stdout + run.stderr, encoding="utf-8")
    if run.returncode:
        raise RuntimeError("JIT diagnostic execution failed; see local log")
    if not assembly.is_file():
        raise RuntimeError("JIT diagnostic file was not emitted")
    result = {"environment": bench.report_environment(),
        "scope": "host JIT diagnostic; not a throughput measurement",
        "workload": workload, "requested_methods": args.methods,
        "probe_sha256": hashlib.sha256((output / "runtime-probe/Program.cs").read_bytes()
            if args.functional_job else hardware.PROBE.encode()).hexdigest(),
        "runtime_sha256": hashlib.sha256((dll.parent / "Besm6.Runtime.dll").read_bytes()).hexdigest(),
        "processor_sha256": hashlib.sha256((dll.parent / "Besm6.Processor.dll").read_bytes()).hexdigest(),
        "assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        "methods": summarize(assembly.read_text(encoding="utf-8"))}
    (output / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"compilations": len(result["methods"])}))


if __name__ == "__main__":
    main()
