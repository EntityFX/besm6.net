#!/usr/bin/env python3
"""Prepare a pinned DISPAK hardware boot fixture and evaluate an external probe.

No hosted Dubna job is used as evidence of an operating-system boot. Downloaded
historical media stay in the specified working directory, outside source control.
The probe command consumes --fixture and must emit the explicit JSON contract
described by --help. Physical memory capacity remains an explicit gate input.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import pathlib
import re
import subprocess
import sys
import urllib.request

SIMH_REVISION = "9e318d16a203a3efae3420ea3f5700a8d7e3bec3"
ARCHIVE_REVISION = "c30c14eb7bb7d56de477c8aaa3c1d6be9d10a320"
BOOT_HASH = "a09a284778dee7a491b579dc4f8e9ca4c906174da1314582bab51afc8524cdfb"
MEDIA = {
    "sbor2048.bin": "844919c089de8afad8f2adbf428a2013f780b2679f377e9506f834d03433dc51",
    "sbor2053.bin": "a9bdbbdd0f0eaf517fd09c86b30ad1917c41c06f9986cb9d50c3704fd9c60342",
    "krab2063.bin": "e3bb28e4bb816eefac2b2513758d843c05ef6bd4b66e447f11e637c7c7d3f6fc",
    "svs2048.bin": "75617c04fee7d90dfce12d7ea82fe8c5ae88897825508094313f970ace59b69b",
    "alt2048.bin": "45ecfa45bb6d75829ec0690c6b47994aabbb238778a6714375044dab8c220a80",
}
EXPECTED_PRIMES = [9433, 19081, 29147, 39631, 50359, 61297, 72467, 83701, 95101]
SUPPORT_FILES = {
    "input.txt": "80f1ffa7298610026c7ed91752c91c808a3e645dc8d145c63ac16063457826e9",
    "expect.ini": "398f76c843a3ca4cb5f18a5b040e1e2630e00c3703adb9fae82421806a3b119d",
    "dispak.ini": "93d0833af58616f8ab8a757dbf607deb5e922766fa90c19f5ce4337a716f8627",
}

RUNTIME_PROBE = r'''
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Reflection;
using System.Security.Cryptography;
using System.Buffers.Binary;
using Besm6.Architecture;
using Besm6.Core;
using Besm6.Runtime;

using var fixture = JsonDocument.Parse(File.ReadAllText(args[0]));
string media = args[1];
long limit = long.Parse(args[2]);
ulong timerPeriod = ulong.Parse(args[3]);
using var detailed = args[4] == "true" ? new StreamWriter(Path.Combine(media, "runtime-instructions.jsonl")) : null;
ulong serialPeriod = ulong.Parse(args[5]);
bool submitJob = args[6] == "true";
var configuration = Enum.Parse<MemoryConfiguration>(args[7]);
var speed = args[8] == "original" ? ExecutionSpeed.Original : ExecutionSpeed.Max;
bool stepDiagnostics = args[9] == "true";
var core = new MachineCore(ProcessorProfile.Supervisor, configuration);
var memory = core.MappedMemory!;
var io = core.SupervisorIo!;
var ioDiagnostics = new ProbeIo(io, memory, () => core.Clock.Tick, () => core.Cpu.GetK());
core.Cpu.Supervisor!.Io = ioDiagnostics;
var trace = new Queue<object>();
var delaySnapshots = new Queue<object>();
var console = new List<object>();
var serial = new List<object>();
var operatorText = new StringBuilder();
string operatorSnapshot = "";
int operatorSnapshotLength = 0;
var printerLines = new List<string>();
var errors = new List<object>();
long completed = 0;
ExecutionStatistics? statistics = null;
string termination = "instruction_limit";
bool frontPanelInputRequested = false;
ulong nextJobPoll = ulong.MaxValue;
ulong? guestCompletionTick = null;
bool printerEnabled = false;
int printerEnableTextPosition = 0, lastPrinterQueryPosition = -1;
ulong nextPrinterPoll = ulong.MaxValue;
bool osAlive = false;
string NormalizeLetters(string value) => new(value.Select(ch => ch switch {
    'А' => 'A', 'В' => 'B', 'Е' => 'E', 'К' => 'K', 'М' => 'M', 'Н' => 'H',
    'О' => 'O', 'Р' => 'P', 'С' => 'C', 'Т' => 'T', 'Х' => 'X', 'У' => 'Y', _ => ch }).ToArray());
int[] PrintedPrimes() {
    bool resultSection = false;
    var result = new List<int>();
    foreach (var row in printerLines) {
        string text = NormalizeLetters(row).Trim();
        if (text == "COMPUTING PRIME NUMBERS THE DUMB WAY") { resultSection = true; continue; }
        if (resultSection && text.StartsWith("TIME, SECONDS")) break;
        if (resultSection && Regex.IsMatch(text, @"^\d{4,5}$")) result.Add(int.Parse(text));
    }
    return result.ToArray();
}
ulong? serialIdleSince = null;
var operatorActions = new List<object>();
void PanelRequest(string name, ulong selector, ulong parameter, ulong auxiliary = 0) {
    memory.SetPanel(6, MemoryWord50.Form(new Word48(selector), true, true));
    memory.SetPanel(5, MemoryWord50.Form(new Word48(parameter), true, true));
    memory.SetPanel(4, MemoryWord50.Form(new Word48(auxiliary), true, true));
    io.RaiseInterrupts(1UL << 31);
    operatorActions.Add(new { name, tick = core.Clock.Tick, selector, parameter, auxiliary });
}
if (stepDiagnostics) core.Cpu.TraceInstruction = (k, right, rk, opcode) => {
    if (k == 29464 && !right) { // 071430: documented-in-image nested delay entry.
        uint offset = core.Cpu.GetM(14);
        delaySnapshots.Enqueue(new { tick = core.Clock.Tick, m7 = core.Cpu.GetM(7),
            m15 = core.Cpu.GetM(15), a = core.Cpu.GetA().Value,
            fields = Enumerable.Range(29611,5).Concat(new[] { 29611+(int)offset,29615+(int)offset })
                .Where(value => value < 32768).Distinct().Select(value => new { address = value,
                    physical = memory.PhysicalMemory.ReadRaw((uint)value).Data.Value,
                    forwarded = memory.HostMemory.Read((uint)value).Value }).ToArray(),
            buffered_operands = memory.GetOperandSnapshot().Select(entry => new {
                request = entry.Request, word = entry.Word.Data.Value,
                physical = memory.PhysicalMemory.ReadRaw(memory.Assignment.TranslateRequest(entry.Request)).Data.Value }).ToArray(),
            preceding_trace = trace.ToArray() });
        if (delaySnapshots.Count > 16) delaySnapshots.Dequeue();
    }
    var item = new { address = k, address_octal = Convert.ToString(k, 8), right, rk, opcode,
        tick = core.Clock.Tick, a = core.Cpu.GetA().Value, y = core.Cpu.GetY().Value,
        r = core.Cpu.GetR(), c = core.Cpu.C, m1 = core.Cpu.GetM(1), m2 = core.Cpu.GetM(2),
        m3 = core.Cpu.GetM(3), m14 = core.Cpu.GetM(14), m15 = core.Cpu.GetM(15),
        mode = core.Cpu.Supervisor!.Mode.ToString() };
    trace.Enqueue(item);
    detailed?.WriteLine(JsonSerializer.Serialize(item));
    if (trace.Count > 64) trace.Dequeue();
};
io.ConsoleOutput += (unit, value) => console.Add(new { unit, value });
io.SerialOutput += (unit, value) => {
    serial.Add(new { unit, value });
    if (unit == 1) operatorText.Append(CosyCodec.Koi7ToUnicode(value));
};
io.PrinterOutput += (unit, overprints) => {
    foreach (var row in overprints)
        printerLines.Add(new string(row.Select(code => code == 255 ? ' ' : CosyCodec.GostToUnicode(code)).ToArray()));
};
core.Cpu.InstructionExecuted += _ => completed++;
try {
    foreach (var item in fixture.RootElement.GetProperty("words").EnumerateArray()) {
        uint address = item.GetProperty("address").GetUInt32();
        var word = new MemoryWord50(item.GetProperty("raw50").GetUInt64());
        if (item.GetProperty("panel").GetBoolean()) memory.SetPanel(address, word);
        else memory.PhysicalMemory.WriteRaw(address, word);
    }
    foreach (var (unit, name) in new[] { (0,"sbor2048.bin"), (7,"sbor2053.bin"),
        (5,"krab2063.bin"), (1,"svs2048.bin"), (2,"alt2048.bin") }) {
        string image = Path.Combine(media, name);
        if (File.Exists(image)) io.AttachSimhDiskImage(unit, File.ReadAllBytes(image), false);
    }
    io.CreateScratchDrum(0);
    io.CreateScratchDrum(1);
    io.CreateScratchDisk(6, 2052);
    io.ConfigureSerialTerminal(1);
    io.ConfigurePrinter(0);
    // Selected functional event spacing follows pinned printer_event's ratios;
    // simulation ticks remain completed steps, not microseconds/hardware timing.
    io.PrinterPulseTicks = 1400;
    io.PrinterZeroTicks = 1000;
    if (serialPeriod != 0) io.StartSerialClock(serialPeriod);
    string input = Path.Combine(media, "input.txt");
    if (File.Exists(input)) io.AttachPaperTapeText(0, File.ReadAllText(input, Encoding.UTF8));
    if (timerPeriod != 0) io.StartTimer(timerPeriod, configuration == MemoryConfiguration.Simh512K);
    core.Cpu.StartAt(fixture.RootElement.GetProperty("start_address").GetUInt32());
    core.StepTrace = (_, _) => {
        ioDiagnostics.ObserveCompletedDrums();
        if (operatorText.Length != operatorSnapshotLength) {
            operatorSnapshot = operatorText.ToString();
            operatorSnapshotLength = operatorText.Length;
        }
        if (io.IsSerialIdle) serialIdleSince ??= core.Clock.Tick;
        else serialIdleSince = null;
        // Explicit operator actions from pinned check_initial_setup(). No host write
        // to YEAR or other OS memory: retained image date, fixed guest time12:00.
        if (submitJob && core.Cpu.GetK() == 0x920 &&
            serialIdleSince is ulong idleSince && core.Clock.Tick - idleSince > 10 * serialPeriod &&
            (io.ExternalInterrupts & (1UL << 31)) == 0 &&
            (io.ExternalInterruptMask & (1UL << 31)) != 0) {
            ulong taken = memory.PhysicalMemory.ReadRaw(0x122).Data.Value;
            ulong maskCopy = memory.PhysicalMemory.ReadRaw(0x32D).Data.Value;
            if ((taken & (1UL << 46)) != 0 && (taken & 64) == 0) {
                if (((maskCopy >> 21) & 3) == 0) {
                    PanelRequest("СМЕ", 8, 1UL << 21, 1);
                } else if (((maskCopy >> 21) & 3) != 0) {
                    PanelRequest("ВРЕ", 14, 0x1200);
                }
            }
        }
        if (submitJob && !frontPanelInputRequested && Regex.IsMatch(operatorSnapshot, "ДATA[^\\n]*\\n")) {
            // Same operator front-panel actions as pinned expect.ini, not an OS shortcut.
            PanelRequest("FS8", 1, 8);
            frontPanelInputRequested = true;
            nextJobPoll = core.Clock.Tick + 1000000;
        }
        if (frontPanelInputRequested && guestCompletionTick is null) {
            string text = operatorSnapshot;
            if (text.Contains("KЗ(") || text.Contains("КЗ(")) guestCompletionTick = core.Clock.Tick;
            else if (core.Clock.Tick >= nextJobPoll && io.IsSerialIdle) {
                io.ReceiveSerial(1, new byte[] { 87, 67, 80, 80, 3 }); // WCPP + terminal Enter(ETX).
                nextJobPoll = core.Clock.Tick + 10000000;
            }
        }
        if (guestCompletionTick is ulong finished && !printerEnabled && core.Clock.Tick >= finished + 10000000) {
            PanelRequest("ONL A0", 8, 1);
            printerEnabled = true;
            printerEnableTextPosition = operatorText.Length;
            nextPrinterPoll = core.Clock.Tick + 1000000;
        }
        if (printerEnabled && core.Clock.Tick >= nextPrinterPoll && io.IsSerialIdle &&
            operatorText.ToString(printerEnableTextPosition, operatorText.Length-printerEnableTextPosition).Contains("ECT")) {
            lastPrinterQueryPosition = operatorText.Length;
            io.ReceiveSerial(1, new byte[] { 72, 79, 77, 66, 3 }); // HOMB + ETX.
            nextPrinterPoll = core.Clock.Tick + 1000000;
        }
        if (lastPrinterQueryPosition >= 0 && operatorSnapshotLength > lastPrinterQueryPosition &&
            operatorSnapshot[lastPrinterQueryPosition..].Contains("HET")) osAlive = true;
    };
    var execution = core.RunInstructions(limit, speed, true);
    statistics = execution.Statistics;
    termination = execution.Outcome.Stopped ? "stop" : "instruction_limit";
} catch (Exception error) {
    termination = "exception";
    // Retain diagnostic octal/decimal register numbers, not paths or full messages.
    var ports = Regex.Matches(error.Message, @"\b(?:0x[0-9A-Fa-f]+|[0-9]{1,8})\b")
        .Select(match => match.Value).ToArray();
    var methods = new System.Diagnostics.StackTrace(error, false).GetFrames()?
        .Select(frame => frame.GetMethod()).Where(method => method is not null)
        .Select(method => method!.DeclaringType?.FullName + "." + method.Name).ToArray();
    errors.Add(new { type = error.GetType().Name, numbers = ports, methods,
        parameter = (error as ArgumentException)?.ParamName });
}
string WordsHash(IEnumerable<MemoryWord50> words) {
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] bytes = new byte[8];
    foreach (var word in words) {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, word.RawValue);
        digest.AppendData(bytes);
    }
    return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
}
string physicalHash = WordsHash(Enumerable.Range(0,(int)memory.PhysicalMemory.CapacityWords)
    .Select(address => memory.PhysicalMemory.ReadRaw((uint)address)));
var mediaHashes = new[] { 0,1,2,5,6,7 }.ToDictionary(unit => unit.ToString(), unit => WordsHash(io.SnapshotDisk(unit)));
var drumHashes = new[] { 0,1 }.Select(unit => WordsHash(io.SnapshotDrum(unit))).ToArray();
var guestState = new {
    physicalHash, mediaHashes, drumHashes, clock = core.Clock.Tick, completed,
    a = core.Cpu.GetA().Value, y = core.Cpu.GetY().Value, r = core.Cpu.GetR(), k = core.Cpu.GetK(),
    right = core.Cpu.RightInstruction, c = core.Cpu.C, applyC = core.Cpu.ApplyC,
    modifiers = Enumerable.Range(0,16).Select(core.Cpu.GetM).ToArray(), supervisor = core.Cpu.Supervisor!.Snapshot(),
    panels = Enumerable.Range(1,7).Select(index => memory.GetPanel((uint)index).RawValue).ToArray(),
    assignments = Enumerable.Range(0,32).Select(index => memory.Assignment.GetPhysicalPage((uint)index)).ToArray(),
    protection = memory.Assignment.OperandProtectionMask, operands = memory.GetOperandSnapshot(),
    brzSlots = Enumerable.Range(0,8).Select(index => memory.ReadOperandBufferRegister(index).RawValue).ToArray(),
    instructions = memory.GetInstructionSnapshot(), lastFault = memory.LastFault,
    interrupts = io.ExternalInterrupts, mask = io.ExternalInterruptMask,
    peripheral = io.PeripheralInterrupts, peripheralMask = io.PeripheralInterruptMask,
    nextEvent = core.Scheduler.GetType().GetProperty("NextEventTick", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(core.Scheduler),
    ioCounts = ioDiagnostics.Counts, ioHistory = ioDiagnostics.Fingerprint(),
    operatorActions, serial, console, printerLines
};
string stateSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(guestState))).ToLowerInvariant();
Console.WriteLine(JsonSerializer.Serialize(new {
    physical_words = memory.PhysicalMemory.CapacityWords, memory_configuration = configuration.ToString(),
    hardware_dispatcher = true, hosted_extracodes = false,
    operator_output = operatorText.ToString(), console_bytes = console, serial_bytes = serial,
    printer_lines = printerLines, front_panel_input_requested = frontPanelInputRequested,
    operator_actions = operatorActions, guest_date_policy = "image date retained; no host YEAR patch",
    printed_primes = PrintedPrimes(),
    guest_job_completed = guestCompletionTick is not null && printerLines.Any(row => NormalizeLetters(row).Contains("KOHEЦ ЗAДAЧИ")),
    operating_system_alive = osAlive, state_sha256 = stateSha256, guest_state = guestState,
    same_guest_state_original_max = false,
    termination, timer_period_ticks = timerPeriod, serial_clock_period_ticks = serialPeriod,
    timer_delay_model = "functional; not physical hardware time",
    instruction_attempt_limit = limit, speed = speed.ToString(), statistics,
    completed_instructions = completed, ticks = core.Clock.Tick,
    final_cpu = new { k = core.Cpu.GetK(), k_octal = Convert.ToString(core.Cpu.GetK(),8),
        right = core.Cpu.RightInstruction, a = core.Cpu.GetA().Value, y = core.Cpu.GetY().Value,
        r = core.Cpu.GetR(), c = core.Cpu.C, mode = core.Cpu.Supervisor!.Mode.ToString(),
        status = core.Cpu.Supervisor.Status.EncodedControls,
        modifiers = Enumerable.Range(0,16).Select(index => core.Cpu.GetM(index)).ToArray() },
    interrupt_state = new { grp = io.ExternalInterrupts, mgrp = io.ExternalInterruptMask,
        prp = io.PeripheralInterrupts, mprp = io.PeripheralInterruptMask,
        next_event_tick = core.Scheduler.GetType().GetProperty("NextEventTick", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(core.Scheduler) },
    page_assignments = Enumerable.Range(0,32).Select(page => memory.Assignment.GetPhysicalPage((uint)page)).ToArray(),
    memory_window = Enumerable.Range(Math.Max(8,(int)core.Cpu.GetK()-8),24)
        .Concat(Enumerable.Range(0,16).SelectMany(index => Enumerable.Range(Math.Max(8,(int)core.Cpu.GetM(index)-4),12)))
        .Concat(Enumerable.Range(896,128)) // Startup communication area 01600..01777 octal.
        .Concat(new[] { 0x122, 0x32D, 0x91 }) // TAKEN, MGRP_COPY, YEAR.
        .Concat(Enumerable.Range(29568,128)) // Loaded startup communication area 71600..71777 octal.
        .Where(address => address < 32768).Distinct().Order().Select(address => new { address,
            address_octal = Convert.ToString(address,8), raw = memory.PhysicalMemory.ReadRaw((uint)address).RawValue,
            forwarded = memory.HostMemory.Read((uint)address).Value }).ToArray(),
    operand_buffers = memory.GetOperandSnapshot(), instruction_buffers = memory.GetInstructionSnapshot(),
    io_calls = ioDiagnostics.Counts, io_last256 = ioDiagnostics.Last, delay_entries = delaySnapshots,
    drum_data_comparisons = ioDiagnostics.DrumComparisons,
    transfers = new { disk = io.CompletedTransfers, header = io.CompletedHeaderReads,
        drum = io.CompletedDrumTransfers }, memory_fault = memory.LastFault,
    trace_last64 = trace, errors
}));

sealed class ProbeIo(SupervisorIoController inner, MappedMemoryBackend memory,
    Func<ulong> tick, Func<uint> address) : ISupervisorIo
{
    private readonly IncrementalHash history = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public string Fingerprint() => Convert.ToHexString(history.GetHashAndReset()).ToLowerInvariant();
    public Dictionary<string,long> Counts { get; } = new();
    public Queue<object> Last { get; } = new();
    public Queue<object> DrumComparisons { get; } = new();
    private readonly Dictionary<int,(int Zone,int Sector,uint Address,int Count,bool Read)> pendingDrums = new();
    private readonly Dictionary<(int,int,int,int),ulong[]> writtenDrums = new();
    private int completedDrums;
    public ulong PendingExternalInterrupts => inner.PendingExternalInterrupts;
    private void Record(string operation, uint port, ulong value) {
        Span<byte> record = stackalloc byte[25];
        record[0] = operation switch { "register_read" => 1, "register_write" => 2, "device_read" => 3, _ => 4 };
        BinaryPrimitives.WriteUInt32LittleEndian(record[1..],port);
        BinaryPrimitives.WriteUInt64LittleEndian(record[5..],value);
        BinaryPrimitives.WriteUInt64LittleEndian(record[13..],tick());
        BinaryPrimitives.WriteUInt32LittleEndian(record[21..],address());
        history.AppendData(record);
        string key = operation + ":" + Convert.ToString(port,8);
        Counts[key] = Counts.GetValueOrDefault(key) + 1;
        Last.Enqueue(new { operation, port, port_octal = Convert.ToString(port,8), value,
            value_octal = Convert.ToString((long)value,8), tick = tick(), address = address() });
        if (Last.Count > 256) Last.Dequeue();
    }
    public Word48 ReadRegister(uint register) { var value = inner.ReadRegister(register); Record("register_read",register,value.Value); return value; }
    public void WriteRegister(uint register, Word48 value) { Record("register_write",register,value.Value); inner.WriteRegister(register,value); }
    public Word48 ReadDevice(uint port) { var value = inner.ReadDevice(port); Record("device_read",port,value.Value); return value; }
    public void WriteDevice(uint port, Word48 value) {
        Record("device_write",port,value.Value);
        inner.WriteDevice(port,value);
        uint normalized = port & 0x87F;
        if (normalized is 1 or 2) {
            uint command = (uint)value.Value;
            bool page = (command & 0x40000) != 0;
            uint start = (command & (page ? 0x1F000U : 0x1FC00U)) >> 2 | (command & 0x7800000) >> 8;
            pendingDrums[(int)normalized-1] = ((int)((command&0x3FC)>>2),page?0:(int)(command&3),start,page?1024:256,(command&0x20000)!=0);
        }
    }
    private static string Hash(ulong[] words) {
        byte[] bytes = new byte[words.Length*8];
        for (int i=0;i<words.Length;i++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i*8,8),words[i]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    public void ObserveCompletedDrums() {
        if (inner.CompletedDrumTransfers == completedDrums) return;
        completedDrums = inner.CompletedDrumTransfers;
        foreach (var controller in pendingDrums.Keys.ToArray()) {
            if ((inner.ExternalInterrupts & (1UL << (45-controller))) == 0) continue;
            var request = pendingDrums[controller];
            pendingDrums.Remove(controller);
            ulong[] raw = Enumerable.Range(0,request.Count).Select(i => memory.PhysicalMemory.ReadRaw(request.Address+(uint)i).RawValue).ToArray();
            var key = (controller,request.Zone,request.Sector,request.Count);
            if (!request.Read) { writtenDrums[key] = raw; continue; }
            if (!writtenDrums.TryGetValue(key,out var original)) continue;
            ulong[] forwarded = Enumerable.Range(0,request.Count).Select(i => memory.HostMemory.Read(request.Address+(uint)i).Value).ToArray();
            var mismatches = Enumerable.Range(0,request.Count).Where(i => raw[i] != original[i]).Take(8)
                .Select(i => new { index=i, expected=original[i], actual=raw[i] }).ToArray();
            int forwardedDifferences = Enumerable.Range(0,request.Count).Count(i => forwarded[i] != (raw[i]&Word48.Mask48));
            DrumComparisons.Enqueue(new { tick=tick(),controller,request.Zone,request.Sector,
                request.Address,request.Count,source_raw_sha256=Hash(original),destination_raw_sha256=Hash(raw),
                exact_raw_match=mismatches.Length==0,mismatches,forwarded_differences=forwardedDifferences });
            if (DrumComparisons.Count>64) DrumComparisons.Dequeue();
        }
    }
}
'''


def run_runtime_probe(runtime: pathlib.Path, fixture: pathlib.Path, media: pathlib.Path,
                      limit: int, timeout: float, timer_period: int, detailed_trace: bool,
                      serial_period: int, submit_job: bool, memory_configuration: str,
                      speed: str, light_probe: bool) -> dict:
    """Build against a completed Release snapshot, never against changing projects."""
    if runtime.name != "Besm6.Runtime.dll" or not runtime.is_file():
        raise ValueError("An existing Besm6.Runtime.dll snapshot is required")
    folder = media / "runtime-probe"
    folder.mkdir(exist_ok=True)
    references = "\n".join(
        f'<Reference Include="{html.escape(dll.stem)}"><HintPath>{html.escape(str(dll.resolve()))}</HintPath></Reference>'
        for dll in sorted(runtime.parent.glob("Besm6.*.dll")))
    project = folder / "BootProbe.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                       '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                       '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
                       '</PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>', encoding="utf-8")
    (folder / "Program.cs").write_text(RUNTIME_PROBE, encoding="utf-8")
    build = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo"],
                           capture_output=True, text=True, encoding="utf-8", timeout=timeout)
    if build.returncode:
        # Compiler descriptions are useful; source paths and host names are not.
        descriptions = sorted(set(re.findall(r"error (CS\d+): (.*?) \[", build.stdout)))
        descriptions = [(code, description.replace(str(runtime.parent), "SNAPSHOT").replace(str(folder), "PROBE"))
                        for code, description in descriptions]
        raise ValueError("Runtime probe build failed: " + json.dumps(descriptions))
    run = subprocess.run(["dotnet", str(folder / "bin/Release/net8.0/BootProbe.dll"),
                          str(fixture), str(media), str(limit), str(timer_period), str(detailed_trace).lower(),
                          str(serial_period), str(submit_job).lower(), memory_configuration, speed,
                          str(not light_probe).lower()], capture_output=True,
                         text=True, encoding="utf-8", timeout=timeout)
    if run.returncode:
        raise ValueError(f"Runtime probe failed with exit code {run.returncode}")
    probe = json.loads(run.stdout)
    probe["runtime_sha256"] = hashlib.sha256(runtime.read_bytes()).hexdigest()
    probe["processor_sha256"] = hashlib.sha256((runtime.parent / "Besm6.Processor.dll").read_bytes()).hexdigest()
    probe["architecture_sha256"] = hashlib.sha256((runtime.parent / "Besm6.Architecture.dll").read_bytes()).hexdigest()
    probe["bootstrap_sha256"] = BOOT_HASH
    probe["probe_source_sha256"] = hashlib.sha256(RUNTIME_PROBE.encode("utf-8")).hexdigest()
    return probe


def form_word50(data: int, instruction: bool) -> int:
    """Book control bits, not SIMH's abstract number/instruction markers."""
    if not 0 <= data < 1 << 48:
        raise ValueError("Word exceeds 48 data bits")
    left = (data >> 24).bit_count() & 1
    right = (data & 0xffffff).bit_count() & 1
    return data | ((left ^ (not instruction)) << 49) | ((right ^ instruction) << 48)


def parse_boot(text: str) -> dict:
    """Parse the pinned listing using independently supplied raw instruction words.

    Instruction mnemonics are deliberately not assembled by this tool: every
    instruction must have an address and two 24-bit octal halves in its comment.
    Constants and panel deposits retain their numeric word type.
    """
    address, start = 1, 1
    words: dict[int, dict] = {}
    for line_number, line in enumerate(text.splitlines(), 1):
        code, _, comment = line.partition(";")
        code = code.strip()
        if not code:
            continue
        kind, payload = code[0].lower(), code[1:].strip()
        if kind in "вbпp":
            if not re.fullmatch(r"[0-7]+", payload):
                raise ValueError(f"Invalid octal address on line {line_number}")
            value = int(payload, 8)
            if not 0 <= value < 32768:
                raise ValueError(f"Address exceeds selected 32K memory on line {line_number}")
            if kind in "вb":
                address = value
            else:
                start = value
            continue
        if address >= 32768 or address in words:
            raise ValueError(f"Duplicate or out-of-range deposit on line {line_number}")
        if kind in "кk":
            raw = re.match(r"\s*([0-7]+)\s*-\s*([0-7]{4})\s+([0-7]{4})\s+([0-7]{4})\s+([0-7]{4})(?:\s|$)", comment)
            if raw is None or int(raw[1], 8) != address:
                raise ValueError(f"Missing or mismatched raw instruction on line {line_number}")
            data = int("".join(raw.groups()[1:]), 8)
            instruction = True
        elif kind in "сc":
            octal = "".join(payload.split())
            if not re.fullmatch(r"[0-7]{1,16}", octal):
                raise ValueError(f"Invalid constant on line {line_number}")
            data, instruction = int(octal, 8), False
        else:
            raise ValueError(f"Unsupported boot record on line {line_number}")
        words[address] = {"address": address, "address_octal": f"{address:05o}",
                          "data48": data, "raw50": form_word50(data, instruction),
                          "instruction": instruction, "panel": address < 8}
        address += 1
    return {"schema": "besm6-hardware-boot-v1", "physical_words": 32768,
            "start_address": start, "start_right_half": False,
            "words": list(words.values())}


def download_verified(url: str, destination: pathlib.Path, expected: str) -> dict:
    data = destination.read_bytes() if destination.exists() else urllib.request.urlopen(url, timeout=60).read()
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected:
        raise ValueError(f"Pinned SHA256 mismatch for {destination.name}")
    if not destination.exists():
        destination.write_bytes(data)
    return {"name": destination.name, "bytes": len(data), "sha256": actual, "url": url}


def evaluate_gate(probe: dict) -> dict:
    """Fail closed: a missing readiness field cannot become a passed OS gate."""
    if not isinstance(probe, dict):
        raise ValueError("Hardware probe evidence must be a JSON object")
    operator_output = probe.get("operator_output", "")
    checks = {
        "explicit_supported_memory": probe.get("physical_words") == 32768 or
            (probe.get("physical_words") == 524288 and probe.get("memory_configuration") == "Simh512K"),
        "hardware_dispatcher": probe.get("hardware_dispatcher") is True,
        "hosted_extracodes_disabled": probe.get("hosted_extracodes") is False,
        "operator_ready": isinstance(operator_output, str) and bool(re.search("ДATA[^\n]*\n", operator_output)),
        "fortran_result": probe.get("printed_primes") == EXPECTED_PRIMES,
        "guest_job_completed": probe.get("guest_job_completed") is True,
        "operating_system_alive": probe.get("operating_system_alive") is True,
        "same_guest_state_original_max": probe.get("same_guest_state_original_max") is True,
    }
    return {"checks": checks, "passed": all(checks.values())}


def self_test() -> None:
    fixture = parse_boot("п 2000\nв 2\nс 0\nв 2000\nк рег 101, уиа 2260(2) ; 02000 - 0002 0101 1240 2260\n")
    assert fixture["start_address"] == 0o2000
    assert fixture["words"][0]["panel"]
    assert fixture["words"][1]["data48"] == int("0002010112402260", 8)
    assert form_word50(0, True) == 1 << 48
    assert form_word50(0, False) == 1 << 49
    for invalid in ["в 100000", "в 10\nк стоп ; 00011 - 0330 0000 0330 0000", "в 10\nс 8", "в 10\nк стоп", "в 10\nс 0\nв 10\nс 0"]:
        try:
            parse_boot(invalid)
        except ValueError:
            pass
        else:
            raise AssertionError("Malformed fixture was accepted")
    assert not evaluate_gate({})["passed"]
    assert not evaluate_gate({"operator_output": None})["passed"]
    valid = {"physical_words": 32768, "hardware_dispatcher": True, "hosted_extracodes": False,
             "operator_output": "ДATA 1\n", "printed_primes": EXPECTED_PRIMES,
             "guest_job_completed": True, "operating_system_alive": True,
             "same_guest_state_original_max": True}
    assert evaluate_gate(valid)["passed"]
    valid["hosted_extracodes"] = True
    assert not evaluate_gate(valid)["passed"]
    valid["hosted_extracodes"] = False
    valid["physical_words"] = 524288
    assert not evaluate_gate(valid)["passed"]
    valid["memory_configuration"] = "Simh512K"
    assert evaluate_gate(valid)["passed"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument("--boot", type=pathlib.Path, help="Existing pinned boot_dispak.b6; hash is verified")
    parser.add_argument("--download-media", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--probe-json", type=pathlib.Path,
                        help="Evaluate hardware runner evidence; schema in evaluate_gate()")
    parser.add_argument("--timeout", type=float, default=120)
    parser.add_argument("--runtime-dll", type=pathlib.Path,
                        help="Completed Release Besm6.Runtime.dll; build and execute a diagnostic boot probe")
    parser.add_argument("--instruction-limit", type=int, default=1000000)
    parser.add_argument("--timer-period", type=int, default=10000,
                        help="Functional timer period in simulation ticks; 0 disables; not a hardware delay")
    parser.add_argument("--instruction-trace", action="store_true",
                        help="Write full diagnostic pre-instruction trace as JSONL (slower)")
    parser.add_argument("--serial-period", type=int, default=1000,
                        help="Functional serial clock period; 0 disables; terminal1/printer0 are connected")
    parser.add_argument("--submit-job", action="store_true",
                        help="Request FS8 input from the operator front panel after actual OS readiness")
    parser.add_argument("--memory-configuration", choices=["Classical32K", "Simh512K"], default="Classical32K",
                        help="Explicit geometry; Simh512K is an experimental reference contract")
    parser.add_argument("--speed", choices=["max", "original"], default="max")
    parser.add_argument("--light-probe", action="store_true", help="Skip per-instruction diagnostic snapshots")
    parser.add_argument("--compare-probe", type=pathlib.Path, help="Require state parity with a saved run of the other speed")
    parser.add_argument("--run-command", nargs=argparse.REMAINDER,
                        help="External hardware runner; placeholders {fixture}, {media}; stdout must be JSON")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        if args.output is None:
            print("Boot parser and fail-closed gate checks passed")
            return 0
    if args.output is None:
        parser.error("--output is required for preparation or an OS probe")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    boot = args.boot or output / "boot_dispak.b6"
    boot_url = f"https://raw.githubusercontent.com/simh/simh/{SIMH_REVISION}/BESM6/boot_dispak.b6"
    manifest = {"simh_revision": SIMH_REVISION, "archive_revision": ARCHIVE_REVISION,
                "media_endianness": "little", "zone_words": 1032, "zone_service_words": 8,
                "physical_words": 32768 if args.memory_configuration == "Classical32K" else 524288,
                "memory_configuration": args.memory_configuration, "os_32k_boot_confirmed": False,
                "media_redistribution_license": "not established; do not include images in source control",
                "assets": [download_verified(boot_url, boot, BOOT_HASH)]}
    fixture = parse_boot(boot.read_text(encoding="utf-8"))
    fixture["source_sha256"] = BOOT_HASH
    fixture_path = output / "boot-fixture.json"
    fixture_path.write_text(json.dumps(fixture, indent=2) + "\n", encoding="utf-8")
    for name, digest in SUPPORT_FILES.items():
        if args.download_media or (output / name).exists():
            url = f"https://raw.githubusercontent.com/simh/simh/{SIMH_REVISION}/BESM6/{name}"
            manifest["assets"].append(download_verified(url, output / name, digest))
    for name, digest in MEDIA.items():
        if args.download_media or (output / name).exists():
            url = f"https://raw.githubusercontent.com/besm6/besm6.github.io/{ARCHIVE_REVISION}/download/disks/{name}"
            manifest["assets"].append(download_verified(url, output / name, digest))
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    result = {"fixture_words": len(fixture["words"]), "start_address_octal": f'{fixture["start_address"]:05o}',
              "os_gate": {"passed": False, "status": "not executed"}}
    if sum(bool(value) for value in [args.run_command, args.probe_json, args.runtime_dll]) > 1:
        parser.error("Choose --run-command, --probe-json, or --runtime-dll")
    if args.instruction_limit < 1 or args.timeout <= 0 or args.timer_period < 0 or args.serial_period < 0:
        parser.error("Instruction limit/timeout must be positive; timer period must be nonnegative")
    if args.runtime_dll:
        probe = run_runtime_probe(args.runtime_dll.resolve(), fixture_path, output,
                                  args.instruction_limit, args.timeout, args.timer_period, args.instruction_trace,
                                  args.serial_period, args.submit_job, args.memory_configuration, args.speed, args.light_probe)
        if args.compare_probe:
            reference = json.loads(args.compare_probe.read_text(encoding="utf-8"))
            fields = ["state_sha256", "completed_instructions", "ticks", "physical_words", "memory_configuration",
                      "timer_period_ticks", "serial_clock_period_ticks", "guest_date_policy", "runtime_sha256",
                      "processor_sha256", "architecture_sha256", "probe_source_sha256"]
            probe["same_guest_state_original_max"] = (
                probe.get("speed") != reference.get("speed") and
                all(probe.get(field) is not None and probe[field] == reference.get(field) for field in fields) and
                probe.get("statistics", {}).get("ModelCycles") == reference.get("statistics", {}).get("ModelCycles"))
            probe["comparison_reference_sha256"] = hashlib.sha256(args.compare_probe.read_bytes()).hexdigest()
        (output / "runtime-probe.json").write_text(json.dumps(probe, indent=2) + "\n", encoding="utf-8")
        suffix = f'{args.memory_configuration}-{args.speed}-{args.instruction_limit}-t{args.timer_period}-s{args.serial_period}-{probe["runtime_sha256"][:12]}-{probe["probe_source_sha256"][:8]}'
        (output / f"runtime-probe.{suffix}.json").write_text(json.dumps(probe, indent=2) + "\n", encoding="utf-8")
        result["os_gate"] = evaluate_gate(probe)
        result["diagnostic"] = {key: probe[key] for key in ["termination", "completed_instructions", "ticks", "final_cpu", "transfers", "errors"]}
    elif args.run_command:
        command = [part.replace("{fixture}", str(fixture_path)).replace("{media}", str(output))
                   for part in args.run_command]
        completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", timeout=args.timeout)
        if completed.returncode:
            raise ValueError(f"Hardware probe failed with exit code {completed.returncode}")
        probe = json.loads(completed.stdout)
        result["os_gate"] = evaluate_gate(probe)
    elif args.probe_json:
        result["os_gate"] = evaluate_gate(json.loads(args.probe_json.read_text(encoding="utf-8")))
    (output / "probe-summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if not (args.run_command or args.probe_json or args.runtime_dll) or result["os_gate"]["passed"] else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError, subprocess.TimeoutExpired, json.JSONDecodeError) as error:
        # Exception messages from subprocess/network objects may contain private
        # absolute paths or command lines. Keep portable reports free of those.
        safe_detail = str(error) if isinstance(error, ValueError) else type(error).__name__
        print(f"Stage 4 boot preparation/probe failed: {safe_detail}", file=sys.stderr)
        sys.exit(1)
