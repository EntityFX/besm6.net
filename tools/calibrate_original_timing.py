#!/usr/bin/env python3
"""Replay one Whetstone pass through candidate statistical calendars.

The functional job runs at max speed; shadow calendars never change its timer
or state. Reduced-pass results are extrapolations, not final benchmark scores.
Build the Release CLI first, then validate the chosen model on all eight passes.
"""
import argparse
import json
import math
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = [.293, 2.674, 4.477, 7.280, 18.361, 35.720, 13.963, 16.087]

PROBE = r'''
using System.Text.Json;
using Besm6.Runtime;
using Besm6.Runtime.Timing;
using Besm6.Architecture;
var machine = new MachineCore();
var loader = new DubnaLoader(machine, args[0]) {
    Speed = ExecutionSpeed.Max, GuestClockEnabled = true, UseWallClock = false,
    InstructionLimit = 2000000000, HangDetect = false, Output = _ => { }
};
var factors = JsonSerializer.Deserialize<double[]>(args[2])!;
var models = factors.Select(f => new HostedOriginalTiming(machine.Cpu,
    new HostedTimingParameters(1, 900, OperandTransfersOverlapControl: true,
        UseUnnormalizedMinimum: true, RetainStoredOperands: true,
        OperandBufferCapacity: (int)f,
        MathFactor: double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture),
        ModelTimeFactor: double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
var samples = models.Select(_ => new List<ulong>()).ToArray();
machine.Cpu.TraceInstruction += (_, _, raw, _) => {
    var instruction = InstructionCodec.DecodeHalf(raw);
    if ((uint)instruction.Opcode != 0x28) return;
    uint address = (instruction.Address + (machine.Cpu.ApplyC ? machine.Cpu.C : 0)
        + machine.Cpu.GetM(instruction.Register)) & 0x7fff;
    if (address == 28808)
        for (int i=0; i<models.Length; i++) samples[i].Add(models[i].Nanoseconds);
};
try {
    var run = loader.RunScript(args[1]);
    if (!run.Success || !loader.HaltedByStop) throw new Exception("Probe did not reach STOP");
    // CTIME calls before/after each of eight sections, second (CPU) result only.
    if (samples.Any(s => s.Count != 16)) throw new Exception("Unexpected CTIME boundaries");
    var results = factors.Select((factor, i) => new {
        operandBufferCapacity = factor,
        sectionSeconds = Enumerable.Range(0,8).Select(section =>
            (samples[i][2*section+1] - samples[i][2*section]) / 1e9 * 8 /
            (section == 0 ? 10 : 1)).ToArray()
    });
    Console.WriteLine(JsonSerializer.Serialize(results));
} finally { foreach (var model in models) model.Dispose(); }
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--operand-capacities', nargs='+', type=int, default=[8,16,32])
    parser.add_argument('--math-factor', type=float, default=1)
    parser.add_argument('--time-factor', type=float, default=1)
    args = parser.parse_args()
    if args.output.exists():
        parser.error('Choose a fresh output directory')
    if any(not 1 <= factor <= 64 for factor in args.operand_capacities):
        parser.error('Operand capacities must lie in [1, 64]')
    if any(not math.isfinite(value) or value <= 0 for value in (args.math_factor, args.time_factor)):
        parser.error('Math/time factors must be finite and positive')
    directory = args.output.resolve()
    directory.mkdir(parents=True)
    source = (ROOT / 'examples/whetstone-longbottom-forex.dub').read_text()
    if source.count('IX=1,8') != 8:
        raise ValueError('Unexpected Whetstone loop structure')
    deck = directory / 'one-pass.dub'
    deck.write_text(source.replace('IX=1,8', 'IX=1,1'))
    dlls = ROOT / 'src/Besm6.Cli/bin/Release/net8.0'
    references = ''.join(f'<Reference Include="{name}"><HintPath>{escape(str(dlls / (name + ".dll")))}</HintPath></Reference>'
        for name in ('Besm6.Runtime','Besm6.Processor','Besm6.Architecture','Besm6.Assembler'))
    (directory / 'Probe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '<AssemblyName>Besm6.Runtime.Tests</AssemblyName><ImplicitUsings>enable</ImplicitUsings>'
        '</PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>')
    (directory / 'Program.cs').write_text(PROBE)
    subprocess.run(['dotnet','build',str(directory / 'Probe.csproj'),'-c','Release'], check=True)
    result = subprocess.run(['dotnet',str(directory / 'bin/Release/net8.0/Besm6.Runtime.Tests.dll'),
        str(ROOT/'tapes'),str(deck),json.dumps(args.operand_capacities),
        str(args.math_factor),str(args.time_factor)], capture_output=True,
        text=True, check=True, timeout=600)
    (directory / 'probe.out').write_text(result.stdout + result.stderr)
    candidates = json.loads(result.stdout)
    for candidate in candidates:
        seconds = sum(candidate['sectionSeconds'])
        candidate['extrapolatedMwips'] = 80 / seconds
        candidate['relativeSectionErrors'] = [actual / expected - 1 for actual,expected
            in zip(candidate['sectionSeconds'],REFERENCE)]
        print(candidate['operandBufferCapacity'], 'MWIPS', round(80/seconds,6),
              'seconds', [round(value,3) for value in candidate['sectionSeconds']])
    (directory / 'results.json').write_text(json.dumps(dict(
        scope='One-pass shadow replay, extrapolated to eight passes; fit evidence, not independent validation',
        referenceSeconds=REFERENCE,mathFactor=args.math_factor,timeFactor=args.time_factor,
        candidates=candidates),indent=2)+'\n')


if __name__ == '__main__':
    main()
