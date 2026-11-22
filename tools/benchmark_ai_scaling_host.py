#!/usr/bin/env python3
"""Single-host, serial Python/GNU Fortran timings for the final scaling cases.

Python kernels are extracted from the independently checked literal reference
implementations, excluding Fortran source generation. GNU Fortran kernels use
the same source as the guest with output removed, called from a separate driver.
Both kernel clocks measure process CPU time. Process/compilation times use wall
time. Never run this alongside SIMH or other experiments on the benchmark host.
"""
from __future__ import annotations
import argparse
import ast
import copy
import inspect
import json
import math
from pathlib import Path
import platform
import re
import statistics
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ai_scaling_cases as workloads
from benchmark_ai_scaling import compare, extract_results


class KernelOnly(ast.NodeTransformer):
    metadata_names = {"source", "original", "body", "check", "replaced"}

    def visit_Assign(self, node):
        if any(isinstance(t, ast.Name) and t.id in self.metadata_names for target in node.targets for t in ast.walk(target)):
            return None
        return self.generic_visit(node)

    def visit_If(self, node):
        if any(isinstance(item, ast.Name) and item.id in self.metadata_names for item in ast.walk(node.test)):
            return None
        return self.generic_visit(node)

    def visit_Return(self, node):
        node = self.generic_visit(node)
        if isinstance(node.value, ast.Tuple) and len(node.value.elts) == 2 and isinstance(node.value.elts[0], ast.Name) and node.value.elts[0].id == "source":
            node.value = node.value.elts[1]
        return node


def python_kernel(algorithm):
    definition = ast.parse(inspect.getsource(workloads.GENERATORS[algorithm])).body[0]
    definition = KernelOnly().visit(copy.deepcopy(definition))
    definition.name = "kernel"
    module = ast.fix_missing_locations(ast.Module(body=[definition], type_ignores=[]))
    namespace = {"math": math, "rng": workloads.rng}
    exec(compile(module, "<verified-scaling-kernel>", "exec"), namespace)
    source = "import math\n" + inspect.getsource(workloads.rng) + "\n" + ast.unparse(module)
    return namespace["kernel"], source


def native_kernel(source):
    lines = source.splitlines()
    first = next(i for i, line in enumerate(lines) if re.match(r"\s*program\b", line, re.I))
    lines[first] = "      subroutine solvebench(sink)"
    lines.insert(first + 1, "      double precision sink")
    arguments = []
    output = []
    for line in lines:
        match = re.match(r"\s*(?:(\d+)\s+)?print\s+\d+\s*,(.*)", line, re.I)
        if match:
            arguments.extend(item.strip() for item in re.split(r",(?![^()]*\))", match[2]))
            if match[1]: output.append(f"{int(match[1]):5d} continue")
            continue
        if re.match(r"\s*stop\s*$", line, re.I):
            output.append("      sink=0.d0")
            output.extend("      sink=sink+dble(" + argument + ")" for argument in arguments)
            output.append("      return")
        else: output.append(line)
    return "\n".join(output) + "\n"


DRIVER = """      program driver
      integer i,n
      character*32 arg
      double precision t0,t1,sink,total
      call get_command_argument(1,arg)
      read(arg,*) n
      total=0.d0
      call cpu_time(t0)
      do 10 i=1,n
        call solvebench(sink)
        total=total+sink
   10 continue
      call cpu_time(t1)
      print *,t1-t0,total
      end
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--samples", type=int, default=5)
    parser.add_argument("--process-samples", type=int, default=10)
    parser.add_argument("--target-seconds", type=float, default=.15)
    parser.add_argument("--cpu", type=int, help="logical CPU for this benchmark and its child processes; default first allowed CPU")
    args = parser.parse_args()
    work = args.work.resolve()
    manifest = json.loads((work / "manifest-0-1-2-3.json").read_text())
    selected = [case for case in manifest if case["level"] == 3 or case["algorithm"] == "mlp"]
    if len(selected) != 11: raise RuntimeError("Expected ten large cases plus the MLP control")
    root = work / "host-benchmark"
    root.mkdir(exist_ok=True)
    (root / "driver.f").write_text(DRIVER)
    compiler = subprocess.check_output(["gfortran", "--version"], text=True).splitlines()[0]
    import os
    affinity = None
    if hasattr(os, "sched_getaffinity"):
        allowed = sorted(os.sched_getaffinity(0))
        cpu = args.cpu if args.cpu is not None else allowed[0]
        os.sched_setaffinity(0, {cpu})
        affinity = sorted(os.sched_getaffinity(0))
    cpuinfo = Path("/proc/cpuinfo").read_text() if Path("/proc/cpuinfo").exists() else ""
    model = re.search(r"(?m)^model name\s*:\s*(.*)$", cpuinfo)
    metadata = {"python": sys.version, "compiler": compiler, "platform": platform.platform(), "processor": model[1] if model else platform.processor(), "cpuAffinity": affinity, "kernelClock": "process CPU time: process_time_ns / CPU_TIME", "nativeOptimization": "-O0, separate compilation, no LTO", "concurrency": 1}
    results = []
    for case in selected:
        folder = root / case["id"]
        folder.mkdir(exist_ok=True)
        parameters = case["parameters"]
        function, source = python_kernel(case["algorithm"])
        observed_python = function(parameters)
        problems = compare(case["expected"], observed_python)
        if problems: raise RuntimeError((case["id"], problems))
        script = folder / "python_kernel.py"
        script.write_text(source + "\nif __name__ == '__main__':\n    print('RESULT', *kernel(" + repr(parameters) + "))\n")
        started = time.process_time_ns(); function(parameters)
        warm_seconds = (time.process_time_ns() - started) / 1e9
        repetitions_python = max(1, min(100000, math.ceil(args.target_seconds / max(warm_seconds, 1e-7))))
        python_samples = []
        for _ in range(args.samples):
            started = time.process_time_ns()
            for _ in range(repetitions_python): function(parameters)
            python_samples.append((time.process_time_ns()-started)/1e9/repetitions_python)

        original = work / "native" / (case["id"] + ".f")
        executable = folder / "program"
        compilation_samples = []
        for _ in range(3):
            started = time.perf_counter_ns()
            compilation = subprocess.run(["gfortran", "-std=legacy", "-ffixed-line-length-none", "-O0", str(original), "-o", str(executable)], capture_output=True, text=True, check=True)
            compilation_samples.append((time.perf_counter_ns()-started)/1e9)
        validation = subprocess.run([str(executable)], capture_output=True, text=True, check=True)
        observed_fortran = extract_results(validation.stdout)
        problems = compare(case["expected"], observed_fortran)
        if problems: raise RuntimeError((case["id"], problems))
        (folder / "validation.txt").write_text(validation.stdout)
        kernel_source = folder / "kernel.f"
        kernel_source.write_text(native_kernel(original.read_text()))
        kernel_object = folder / "kernel.o"
        subprocess.run(["gfortran", "-std=legacy", "-ffixed-line-length-none", "-O0", "-c", str(kernel_source), "-o", str(kernel_object)], capture_output=True, check=True)
        driver = folder / "driver"
        subprocess.run(["gfortran", "-O0", str(root / "driver.f"), str(kernel_object), "-o", str(driver)], capture_output=True, check=True)
        def native_sample(n):
            run = subprocess.run([str(driver), str(n)], capture_output=True, text=True, check=True)
            fields = run.stdout.split()
            if len(fields) != 2: raise RuntimeError("Unexpected output inside native timing kernel: " + run.stdout[:200])
            return float(fields[0].replace("D", "E"))
        warm_native = native_sample(1)
        repetitions_native = max(1, min(1000000, math.ceil(args.target_seconds / max(warm_native, 1e-7))))
        native_samples = [native_sample(repetitions_native)/repetitions_native for _ in range(args.samples)]
        process_python = []
        process_fortran = []
        for _ in range(args.process_samples):
            started = time.perf_counter_ns()
            subprocess.run([sys.executable, str(script)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
            process_python.append((time.perf_counter_ns()-started)/1e9)
            started = time.perf_counter_ns()
            subprocess.run([str(executable)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
            process_fortran.append((time.perf_counter_ns()-started)/1e9)
        row = {**case, "pythonObserved": observed_python, "fortranObserved": observed_fortran, "verified": True,
               "pythonComputeSeconds": statistics.median(python_samples), "pythonComputeSamples": python_samples, "pythonRepetitions": repetitions_python,
               "fortranComputeSeconds": statistics.median(native_samples), "fortranComputeSamples": native_samples, "fortranRepetitions": repetitions_native,
               "pythonProcessSeconds": statistics.median(process_python), "pythonProcessSamples": process_python,
               "fortranProcessSeconds": statistics.median(process_fortran), "fortranProcessSamples": process_fortran,
               "fortranCompilationSeconds": statistics.median(compilation_samples), "fortranCompilationSamples": compilation_samples}
        results.append(row)
        (work / "host-benchmark-results.json").write_text(json.dumps({"metadata": metadata, "rows": results}, indent=2))
        print(case["id"], "Python", row["pythonComputeSeconds"], "Fortran", row["fortranComputeSeconds"], flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
