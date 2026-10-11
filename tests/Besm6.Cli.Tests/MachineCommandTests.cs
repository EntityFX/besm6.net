using System.Text.Json;
using Besm6.Cli;
using Besm6.Core;
using Besm6.Architecture;
using Besm6.Runtime;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MachineCommandTests
{
    private static MachineJobConfiguration Job(string model = "functional", string speed = "max") => new()
    {
        ExecutionModel = model, Speed = speed, InstructionLimit = 10,
        MemoryWords = new() { new() { Address = 8, Raw50 = MemoryWord50.Form(new(Besm6.Asm.Assembler.Asm("vtm 1(1), stop")), false, false).RawValue } },
        Timing = new()
        {
            BankCycleNs = 2000, ReadLatencyNs = 900, WriteVisibilityNs = 1500, BufferHitNs = 100, ArithmeticCycleNs = 100,
            ControlDurationsNs = new() { ["Vtm"] = 300, ["Stop"] = 300 },
            Devices = new() { ExchangeReadyNs = 20000, ConsoleReplyNs = 700, PaperFeedNs = 900,
                PrinterCharacterNs = 100, PrinterZeroNs = 50, TimerPeriodNs = 500, SerialPeriodNs = 30 }
        }
    };
    private static (int Code, string Output, string Error) Run(MachineJobConfiguration job, params string[] arguments)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(job)); var output = new StringWriter(); var error = new StringWriter();
            int code = CliApplication.Run(new[] { "machine", path }.Concat(arguments).ToArray(), output, error);
            return (code, output.ToString(), error.ToString());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void JsonSelectionAndCliOverridesKeepGuestStateIdentical()
    {
        var functional = Run(Job("functional", "original"), "--execution", "functional", "--speed", "max", "--stats");
        var hardware = Run(Job("functional", "original"), "--execution", "hardware", "--speed", "max", "--stats");
        Assert.AreEqual(0, functional.Code, functional.Error); Assert.AreEqual(0, hardware.Code, hardware.Error);
        using var f = JsonDocument.Parse(functional.Output); using var h = JsonDocument.Parse(hardware.Output);
        Assert.AreEqual(f.RootElement.GetProperty("state").GetRawText(), h.RootElement.GetProperty("state").GetRawText());
        Assert.AreEqual("Max", h.RootElement.GetProperty("speed").GetString());
        Assert.AreEqual(2UL, f.RootElement.GetProperty("functionalTicks").GetUInt64());
        Assert.AreEqual(0UL, h.RootElement.GetProperty("functionalTicks").GetUInt64());
        Assert.IsTrue(h.RootElement.GetProperty("hardwareNanoseconds").GetUInt64() > 0);
        Assert.AreEqual(JsonValueKind.Null, h.RootElement.GetProperty("statistics").GetProperty("modelCycles").ValueKind);
        Assert.IsTrue(h.RootElement.GetProperty("statistics").GetProperty("hardwareModelSeconds").GetDouble() > 0);
    }

    [TestMethod]
    [DataRow("functional", "original")] [DataRow("functional", "max")]
    [DataRow("hardware", "original")] [DataRow("hardware", "max")]
    public void BothOwnerAndSpeedSelectionsReachStop(string model, string speed)
    {
        var result = Run(Job(model, speed)); Assert.AreEqual(0, result.Code, result.Error);
        using var json = JsonDocument.Parse(result.Output); Assert.IsTrue(json.RootElement.GetProperty("stopped").GetBoolean());
        Assert.AreEqual(2L, json.RootElement.GetProperty("instructions").GetInt64());
        if (speed == "max") Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("statistics").ValueKind);
        else Assert.IsTrue(json.RootElement.GetProperty("statistics").GetProperty("originalTempoMet").GetBoolean());
    }

    [TestMethod]
    [DataRow("--execution", "fast")] [DataRow("--speed", "fast")] [DataRow("--limit", "0")]
    public void InvalidOptionsAreRejectedBeforeReadingMissingConfiguration(string option, string value)
    {
        var output = new StringWriter(); var error = new StringWriter();
        Assert.AreEqual(1, CliApplication.Run(new[] { "machine", "missing-machine.json", option, value }, output, error));
        Assert.AreEqual(string.Empty, output.ToString()); Assert.IsFalse(error.ToString().Contains("Could not find", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingTimingInvalidImageAndExpandedDefaultTimingFailBeforeExecution()
    {
        var missing = Job("hardware"); missing.Timing = null; Assert.AreEqual(1, Run(missing).Code);
        var duplicate = Job(); duplicate.MemoryWords.Add(duplicate.MemoryWords[0]); Assert.AreEqual(1, Run(duplicate).Code);
        var bounds = Job(); bounds.MemoryWords[0].Address = 32768; Assert.AreEqual(1, Run(bounds).Code);
        var controls = Job(); controls.MemoryWords[0].Raw50 = 1UL << 50; Assert.AreEqual(1, Run(controls).Code);
        var expanded = Job("hardware"); expanded.MemoryConfiguration = "simh512k"; Assert.AreEqual(1, Run(expanded).Code);
        expanded.Timing!.Banking = "logicalLowThreeBits"; var success = Run(expanded); Assert.AreEqual(0, success.Code, success.Error);
    }

    [TestMethod]
    public void InstructionLimitAndMissingOpcodeTimingReturnFailure()
    {
        var limit = Run(Job("hardware"), "--limit", "1"); Assert.AreEqual(1, limit.Code);
        using var json = JsonDocument.Parse(limit.Output); Assert.IsTrue(json.RootElement.GetProperty("limitExceeded").GetBoolean());
        var missing = Job("hardware"); missing.Timing!.ControlDurationsNs.Remove("Stop");
        var failure = Run(missing); Assert.AreEqual(1, failure.Code); StringAssert.Contains(failure.Error, "No explicit UU duration");
    }

    [TestMethod]
    public void UnknownJsonFieldsAndInvalidConfigModesAreNotSilentlyIgnored()
    {
        var mode = Job(); mode.ExecutionModel = "fast"; Assert.AreEqual(1, Run(mode).Code);
        var speed = Job(); speed.Speed = "fast"; Assert.AreEqual(1, Run(speed).Code);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"unknownField\":1}"); var error = new StringWriter();
            Assert.AreEqual(1, CliApplication.Run(new[] { "machine", path }, new StringWriter(), error));
            Assert.IsTrue(error.ToString().Length > 0);
        }
        finally { File.Delete(path); }
    }
}
