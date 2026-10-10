"""Compare the current ALU with the pinned upstream division candidate.

The candidate table is software evidence, not an independently verified hardware
oracle. --check reproduces recorded current bits without downloading anything.
Initial capture: --upstream-table path/to/pinned/alu_test.cpp --output report.json
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "1d73a86ae71ac8d6a9d1a56a487007e5a240f8ba"
TABLE_SHA256 = "cc24a9678a5b2541b1cbd7790bd04f8189a5d69a168e0fe0edbfbd7e93185158"
REPORT = ROOT / "reports/hardware-time/division-version-audit.json"
PROBE = r'''
using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using Besm6.Core;
using Besm6.Architecture;

var input = JsonDocument.Parse(File.ReadAllText(args[0]));
var results = input.RootElement.EnumerateArray().Select(row => {
    var cpu = new Processor(new Memory());
    cpu.SetA(Convert.ToUInt64(row.GetProperty("dividend").GetString(), 8));
    cpu.SetY(0xAB1234567890);
    cpu.SetR(6); // Dubna default: normalization, no rounding, logical group
    string? error = null;
    try { cpu.ArithDivide(new Word48(Convert.ToUInt64(row.GetProperty("divisor").GetString(), 8))); }
    catch (ProcessorException ex) { error = ex.Message; }
    return new { current = Convert.ToString((long)cpu.A.Value, 8).PadLeft(16, '0'),
                 y = cpu.Y.Value.ToString("X12"), error };
}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(results));

sealed class Memory : IMemory {
    public Word48 Read(uint address) => Word48.Zero;
    public void Write(uint address, Word48 word) { }
    public int Size => 32768;
}
'''


def run_probe(rows):
    with tempfile.TemporaryDirectory(prefix="besm6-division-audit-") as directory:
        directory = Path(directory)
        project = ROOT / "src/Besm6.Processor/Besm6.Processor.csproj"
        (directory / "Probe.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>'
            f'<ItemGroup><ProjectReference Include="{escape(str(project))}" /></ItemGroup></Project>',
            encoding="utf-8")
        (directory / "Program.cs").write_text(PROBE, encoding="utf-8")
        (directory / "input.json").write_text(json.dumps(rows), encoding="utf-8")
        build = subprocess.run(["dotnet", "build", str(directory / "Probe.csproj"), "-c", "Release", "-v", "q"],
                               text=True, capture_output=True, timeout=180)
        if build.returncode:
            raise RuntimeError(build.stdout + build.stderr)
        run = subprocess.run(["dotnet", str(directory / "bin/Release/net8.0/Probe.dll"),
                              str(directory / "input.json")], text=True, capture_output=True, timeout=60, check=True)
        return json.loads(run.stdout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upstream-table", type=Path)
    parser.add_argument("--output", type=Path, default=REPORT)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if args.check and args.upstream_table:
        parser.error("--check uses the recorded table; do not supply --upstream-table")
    if args.upstream_table:
        if hashlib.sha256(args.upstream_table.read_bytes()).hexdigest() != TABLE_SHA256:
            raise ValueError("The input is not the pinned upstream table")
        source = args.upstream_table.read_text(encoding="utf-8")
        table = source.split("TEST_F(dubna_machine, alu_div_table)", 1)[1].split("};", 1)[0]
        rows = [dict(dividend=f"{int(a, 8):016o}", divisor=f"{int(b, 8):016o}", candidate=f"{int(c, 8):016o}")
                for a, b, c in re.findall(r"\{\s*(0[0-7]+),\s*(0[0-7]+),\s*(0[0-7]+)\s*\}", table)]
        if len(rows) != 200:
            raise ValueError(f"Expected pinned 200-row table, found {len(rows)}")
        report = dict(candidate_commit=COMMIT, candidate_url=f"https://github.com/besm6/dubna/commit/{COMMIT}",
                      candidate_table_sha256=hashlib.sha256(args.upstream_table.read_bytes()).hexdigest(),
                      classification="Software-version difference; hardware source not yet independently verified",
                      rau=6, vectors=rows)
    else:
        report = json.loads(args.output.read_text(encoding="utf-8"))
        if report["candidate_commit"] != COMMIT or report["candidate_table_sha256"] != TABLE_SHA256:
            raise ValueError("Unexpected candidate provenance")
        rows = report["vectors"]
    actual = run_probe(rows)
    if len(actual) != len(rows):
        raise ValueError("Truncated probe results")
    if args.check:
        for index, (row, observed) in enumerate(zip(rows, actual)):
            for field in ("current", "y", "error"):
                if row[field] != observed[field]:
                    raise AssertionError(f"Row {index}: changed {field}: {row[field]} -> {observed[field]}")
        matches = sum(r["current"] == r["candidate"] and r["error"] is None for r in rows)
        if matches != report["matches"] or len(rows) - matches != report["differences"]:
            raise AssertionError("Incorrect recorded comparison totals")
    else:
        for row, observed in zip(rows, actual):
            row.update(observed)
            row["matches_candidate"] = row["current"] == row["candidate"] and row["error"] is None
        report["matches"] = sum(row["matches_candidate"] for row in rows)
        report["differences"] = len(rows) - report["matches"]
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n")
        with args.output.with_suffix(".csv").open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
    print(f"Reproduced {len(rows)} divisions: {report['matches']} match candidate; {report['differences']} differ")


if __name__ == "__main__":
    main()
