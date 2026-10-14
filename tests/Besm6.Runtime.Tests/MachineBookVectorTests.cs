using Besm6.Runtime.Timing.Calendar;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.State;
using Besm6.Architecture.Supervisor;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;
using Besm6.Runtime.Machine;
using Besm6.Runtime.Timing;
using Besm6.Runtime.Timing.Memory;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;


/// <summary>
/// The existing fixture derives bits from printed examples and exact Fraction
/// arithmetic, not the production ALU. Its classified compatibility differences
/// remain explicit. Supervisor extracode/fault entry has a separate contract.
/// </summary>
[TestClass]
[TestCategory("BookConformance")]
public sealed class MachineBookVectorTests
{
    private static readonly JsonElement Fixture = ReadFixture();
    private static JsonElement ReadFixture()
    {
        using var stream = typeof(MachineBookVectorTests).Assembly.GetManifestResourceStream(
            "Besm6.Runtime.Tests.TestData.book-vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    // Pass JSON text across test discovery; parse it in the execution process.
    private static JsonElement ParseVector(string vectorJson)
    {
        using var document = JsonDocument.Parse(vectorJson);
        return document.RootElement.Clone();
    }

    private static ulong Hex(JsonElement value) => ulong.Parse(value.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static IEnumerable<(bool Hardware, ExecutionSpeed Speed)> Owners() =>
        from hardware in new[] { false, true }
        from speed in new[] { ExecutionSpeed.Max, ExecutionSpeed.Original }
        select (hardware, speed);

    public static IEnumerable<object[]> OrdinaryVectors() =>
        from v in Fixture.GetProperty("vectors").EnumerateArray()
        where v.GetProperty("expected").GetProperty("error").ValueKind == JsonValueKind.Null
            && !v.TryGetProperty("dispatch", out _)
        from owner in Owners()
        select new object[] { v.GetProperty("id").GetString()!, owner.Hardware, owner.Speed, v.GetRawText() };

    public static IEnumerable<object[]> FaultVectors() =>
        from v in Fixture.GetProperty("vectors").EnumerateArray()
        where v.GetProperty("expected").GetProperty("error").ValueKind != JsonValueKind.Null
        from owner in Owners()
        select new object[] { v.GetProperty("id").GetString()!, owner.Hardware, owner.Speed, v.GetRawText() };

    public static IEnumerable<object[]> ExtracodeVectors() =>
        from v in Fixture.GetProperty("vectors").EnumerateArray()
        where v.TryGetProperty("dispatch", out var dispatch) && dispatch.GetBoolean()
        from owner in Owners()
        select new object[] { v.GetProperty("id").GetString()!, owner.Hardware, owner.Speed, v.GetRawText() };

    public static string CaseName(MethodInfo method, object[] data) =>
        $"{method.Name} ({data[0]}, {((bool)data[1] ? "hardware" : "functional")}, {data[2]})";

    private static HardwareMachineConfiguration Timing() => new(
        new(new(2000), new(900), new(1500), MramReadSampling.AtCycleStart, MramArbitration.FifoPerBank),
        new(100), new(100), Enum.GetValues<Opcode>()
            .Concat(Enumerable.Range(0x28, 0x18).Select(value => (Opcode)value))
            .Distinct().ToDictionary(x => x, _ => new HardwareDuration(300)),
        new(new(20000), new(700), new(900), new(100), new(50), new(500), new(30), new(0)),
        Enum.GetValues<Opcode>().ToDictionary(x => x, _ => new HardwareDuration(1200)));

    private static (MachineCore Machine, ulong[] Memory) Load(JsonElement vector, bool hardware)
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        machine.Cpu.Supervisor!.Status = new((ControlUnitFlags)0x403);
        var memory = machine.MappedMemory!.PhysicalMemory;
        var initial = vector.GetProperty("initial");
        var expected = new ulong[ArchitectureConstants.WordCount];
        uint address = initial.GetProperty("k").GetUInt32();
        foreach (var word in vector.GetProperty("program").EnumerateArray())
        {
            ulong data = Hex(word); expected[address] = data;
            memory.WriteRaw(address++, MemoryWord50.Form(new(data), false, false));
        }
        foreach (var word in vector.GetProperty("memory").EnumerateArray())
        {
            address = word.GetProperty("address").GetUInt32();
            ulong data = Hex(word.GetProperty("word")); expected[address] = data;
            memory.WriteRaw(address, MemoryWord50.Form(new(data), true, true));
        }
        machine.Cpu.StartAt(initial.GetProperty("k").GetUInt32());
        machine.Cpu.SetA(Hex(initial.GetProperty("a"))); machine.Cpu.SetY(Hex(initial.GetProperty("y")));
        machine.Cpu.SetR(initial.GetProperty("r").GetUInt32());
        int index = 0;
        foreach (var modifier in initial.GetProperty("m").EnumerateArray()) machine.Cpu.SetM(index++, modifier.GetUInt32());
        foreach (var word in vector.GetProperty("writes").EnumerateArray())
            expected[word.GetProperty("address").GetUInt32()] = Hex(word.GetProperty("word"));
        if (hardware) machine.UseHardwareExecution(Timing());
        return (machine, expected);
    }

    private sealed class FakeTime : IExecutionTimeSource
    {
        public double ElapsedSeconds { get; private set; } = 7;
        public void Sleep(int milliseconds) => ElapsedSeconds += milliseconds / 1000.0;
        public void Spin() => ElapsedSeconds += .000001;
    }

    private static MachineExecutionResult Run(MachineCore machine, int steps, bool hardware, ExecutionSpeed speed) =>
        hardware ? machine.RunHardwareInstructions(steps, speed, false, new FakeTime()) :
        machine.RunInstructions(steps, speed);

    private static void AssertRegisters(JsonElement e, Processor cpu, string id)
    {
        Assert.AreEqual(Hex(e.GetProperty("a")), cpu.A.Value, id + ": A");
        Assert.AreEqual(Hex(e.GetProperty("y")), cpu.Y.Value, id + ": Y");
        Assert.AreEqual(e.GetProperty("r").GetUInt32(), cpu.R, id + ": R");
        Assert.AreEqual(e.GetProperty("c").GetUInt32(), cpu.C, id + ": C");
        Assert.AreEqual(e.GetProperty("applyC").GetBoolean(), cpu.ApplyC, id + ": ApplyC");
        int index = 0;
        foreach (var value in e.GetProperty("m").EnumerateArray())
            Assert.AreEqual(value.GetUInt32(), cpu.GetM(index++), id + ": M" + (index - 1));
    }

    [TestMethod]
    [DynamicData(nameof(OrdinaryVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void SharedOwnersMatchIndependentBitsAndVisibleWrites(string id, bool hardware, ExecutionSpeed speed, string vectorJson)
    {
        var vector = ParseVector(vectorJson);
        var (machine, expectedMemory) = Load(vector, hardware); long notifications = 0;
        machine.Cpu.InstructionExecuted += _ => notifications++;
        var result = Run(machine, vector.GetProperty("steps").GetInt32(), hardware, speed);
        var expected = vector.GetProperty("expected");
        AssertRegisters(expected, machine.Cpu, id);
        Assert.AreEqual(expected.GetProperty("k").GetUInt32(), machine.Cpu.K, id + ": K");
        Assert.AreEqual(expected.GetProperty("right").GetBoolean(), machine.Cpu.RightInstruction, id + ": half");
        Assert.AreEqual(expected.GetProperty("stop").GetBoolean(), result.Outcome.Stopped, id);
        long count = expected.GetProperty("ticks").GetInt64();
        Assert.AreEqual(count, result.Outcome.Instructions); Assert.AreEqual(count, notifications);
        Assert.AreEqual(hardware ? 0UL : (ulong)count, machine.Clock.Tick);
        var mapped = machine.MappedMemory!;
        var visible = Enumerable.Range(0, (int)ArchitectureConstants.WordCount)
            .Select(address => mapped.PhysicalMemory.ReadRaw((uint)address).Data.Value).ToArray();
        // Observe forwarded values without issuing reads or changing buffer recency.
        foreach (var entry in mapped.GetOperandSnapshot())
            if (entry.Request.IsPhysical) visible[entry.Request.Address] = entry.Word.Data.Value;
        CollectionAssert.AreEqual(expectedMemory, visible, id + ": visible memory including code");
        foreach (var difference in vector.GetProperty("differences").EnumerateArray())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(difference.GetProperty("issue").GetString()), id);
            string field = difference.GetProperty("field").GetString()!;
            if (field == "y") Assert.AreNotEqual(Hex(difference.GetProperty("book")), machine.Cpu.Y.Value, id);
            else if (field == "r") Assert.AreNotEqual(difference.GetProperty("book").GetUInt32(), machine.Cpu.R, id);
            else Assert.Fail("Unclassified book difference: " + field);
        }
    }

    [TestMethod]
    [DynamicData(nameof(FaultVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void ArithmeticFaultsEnterSupervisorVectorWithoutCountingCommand(string id, bool hardware, ExecutionSpeed speed, string vectorJson)
    {
        var vector = ParseVector(vectorJson);
        var (machine, _) = Load(vector, hardware); long notifications = 0;
        machine.Cpu.InstructionExecuted += _ => notifications++;
        var result = Run(machine, 1, hardware, speed);
        Assert.IsTrue(result.Outcome.LimitExceeded); Assert.AreEqual(0L, result.Outcome.Instructions);
        Assert.AreEqual(0L, notifications); Assert.AreEqual(0UL, machine.Clock.Tick);
        AssertRegisters(vector.GetProperty("expected"), machine.Cpu, id);
        Assert.AreEqual(0x140u, machine.Cpu.K); Assert.IsFalse(machine.Cpu.RightInstruction);
        var snapshot = machine.Cpu.Supervisor!.Snapshot();
        Assert.AreEqual(SupervisorMode.Interrupt, snapshot.Mode);
        Assert.AreEqual(vector.GetProperty("initial").GetProperty("k").GetUInt32(), snapshot.SpecialRegisters[11]);
        // TO-8 guest avost encoding: division 7<<20, overflow 3<<20.
        Assert.AreEqual(id.StartsWith("division-", StringComparison.Ordinal) ? 7UL << 20 : 3UL << 20, snapshot.InternalInterrupts);
    }

    [TestMethod]
    [DynamicData(nameof(ExtracodeVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void SupervisorExtracodePreservesRAndSavesNextWord(string id, bool hardware, ExecutionSpeed speed, string vectorJson)
    {
        var vector = ParseVector(vectorJson);
        var (machine, _) = Load(vector, hardware);
        int steps = vector.GetProperty("steps").GetInt32(); var result = Run(machine, steps, hardware, speed);
        var initial = vector.GetProperty("initial");
        Assert.AreEqual((long)steps, result.Outcome.Instructions); Assert.IsFalse(result.Outcome.Stopped);
        Assert.AreEqual(Hex(initial.GetProperty("a")), machine.Cpu.A.Value);
        Assert.AreEqual(Hex(initial.GetProperty("y")), machine.Cpu.Y.Value);
        Assert.AreEqual(51u, machine.Cpu.R); Assert.AreEqual(17u, machine.Cpu.GetM(14));
        Assert.AreEqual(0x168u, machine.Cpu.K); Assert.IsFalse(machine.Cpu.RightInstruction);
        var snapshot = machine.Cpu.Supervisor!.Snapshot();
        Assert.AreEqual(SupervisorMode.Extracode, snapshot.Mode); Assert.AreEqual(9u, snapshot.SpecialRegisters[10]);
        Assert.AreEqual(hardware ? 0UL : (ulong)steps, machine.Clock.Tick);
    }

    [TestMethod]
    public void VectorArgumentsSurviveDiscoverySerialization()
    {
        var serializer = new System.Runtime.Serialization.DataContractSerializer(
            typeof(object[]), new[] { typeof(ExecutionSpeed) });
        foreach (var row in OrdinaryVectors().Concat(FaultVectors()).Concat(ExtracodeVectors()))
        {
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, row);
            stream.Position = 0;
            var restored = (object[])serializer.ReadObject(stream)!;
            CollectionAssert.AreEqual(row, restored);
            Assert.AreEqual(restored[0], ParseVector((string)restored[3]).GetProperty("id").GetString());
        }
    }

    [TestMethod]
    public void EveryExistingVectorHasAnExplicitProfileClassification()
    {
        Assert.AreEqual(100, Fixture.GetProperty("vectors").GetArrayLength());
        Assert.AreEqual(400, OrdinaryVectors().Count() + FaultVectors().Count() + ExtracodeVectors().Count());
        Assert.AreEqual(12, FaultVectors().Count()); Assert.AreEqual(8, ExtracodeVectors().Count());
    }
}
