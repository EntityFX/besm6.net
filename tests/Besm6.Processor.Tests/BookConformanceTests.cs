using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Besm6.Tests;

/// <summary>
/// Expected bits come from printed examples and exact Fraction arithmetic in
/// tools/generate_book_vectors.py, independently of the production ALU/converters.
/// Documented book differences are asserted explicitly, never ignored.
/// </summary>
[TestClass]
[TestCategory("BookConformance")]
public sealed class BookConformanceTests
{
    private sealed class Memory : IMemory
    {
        public ulong[] Words { get; } = new ulong[32768];
        public Word48 Read(uint address) => new(Words[address & 32767]);
        public void Write(uint address, Word48 word) => Words[address & 32767] = word.Value;
        public int Size => Words.Length;
    }

    private static readonly JsonElement Fixture = ReadFixture();
    private static JsonElement ReadFixture()
    {
        using var stream = typeof(BookConformanceTests).Assembly.GetManifestResourceStream(
            "Besm6.Processor.Tests.TestData.book-vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static ulong Hex(JsonElement value) => ulong.Parse(value.GetString()!, NumberStyles.HexNumber);
    public static IEnumerable<object[]> Vectors() =>
        from v in Fixture.GetProperty("vectors").EnumerateArray()
        from path in new[] { "step", "uncached", "trace", "block" }
        select new object[] { v.GetProperty("id").GetString()!, path, v };

    public static IEnumerable<object[]> LoadedVectors() =>
        from v in Fixture.GetProperty("vectors").EnumerateArray()
        where v.GetProperty("steps").GetInt32() == 1
            && v.GetProperty("expected").GetProperty("error").ValueKind == JsonValueKind.Null
            && v.GetProperty("sources").EnumerateArray().Any(s => s.GetString()!.EndsWith("section-5"))
        from speed in new[] { ExecutionSpeed.Max, ExecutionSpeed.Original }
        select new object[] { v.GetProperty("id").GetString()!, speed, v };

    public static string CaseName(MethodInfo method, object[] data) => $"{method.Name} ({data[0]}, {data[1]})";

    public static IEnumerable<object[]> FaultVectors() =>
        from v in Fixture.GetProperty("vectors").EnumerateArray()
        where v.GetProperty("expected").GetProperty("error").ValueKind != JsonValueKind.Null
        from speed in new[] { ExecutionSpeed.Max, ExecutionSpeed.Original }
        from intercept in new[] { false, true }
        select new object[] { v.GetProperty("id").GetString()! + (intercept ? "/intercept" : "/fault"), speed, intercept, v };

    private static void Load(JsonElement v, Processor cpu, IMemory memory)
    {
        var initial = v.GetProperty("initial");
        uint start = initial.GetProperty("k").GetUInt32();
        uint offset = 0;
        foreach (var w in v.GetProperty("program").EnumerateArray())
            memory.Write(start + offset++, new Word48(Hex(w)));
        foreach (var w in v.GetProperty("memory").EnumerateArray())
            memory.Write(w.GetProperty("address").GetUInt32(), new Word48(Hex(w.GetProperty("word"))));
        cpu.StartAt(start);
        cpu.SetA(Hex(initial.GetProperty("a")));
        cpu.SetY(Hex(initial.GetProperty("y")));
        cpu.SetR(initial.GetProperty("r").GetUInt32());
        int index = 0;
        foreach (var m in initial.GetProperty("m").EnumerateArray()) cpu.SetM(index++, m.GetUInt32());
        if (v.TryGetProperty("dispatch", out var dispatch) && dispatch.GetBoolean())
            cpu.ExtracodeDispatch = _ => true;
    }

    [TestMethod]
    [DynamicData(nameof(Vectors), DynamicDataDisplayName = nameof(CaseName))]
    public void DefinedBookBitsAndDubnaContracts(string id, string path, JsonElement v)
    {
        var memory = new Memory();
        var cpu = new Processor(memory) { InstructionCacheEnabled = path != "uncached" };
        Load(v, cpu, memory);
        var expectedMemory = (ulong[])memory.Words.Clone();
        foreach (var w in v.GetProperty("writes").EnumerateArray())
            expectedMemory[w.GetProperty("address").GetUInt32()] = Hex(w.GetProperty("word"));
        int traced = 0;
        if (path == "trace") cpu.InstructionTrace = _ => traced++;
        int steps = v.GetProperty("steps").GetInt32();
        long completed = 0;
        ulong tick = 0;
        bool stop = false;
        string? error = null;
        try
        {
            while (completed < steps && !stop)
            {
                if (path == "block" && cpu.CanExecuteUnobservedBlock)
                {
                    long before = completed;
                    stop = cpu.ExecuteUnobservedBlock(steps - (int)completed, ref completed, ref tick);
                    if (completed != before || stop) continue;
                    // A block ends before an extracode, as MachineCore does.
                }
                stop = cpu.Step();
                completed++;
                tick++;
            }
        }
        catch (ProcessorException ex) { error = ex.Message; }
        var expected = v.GetProperty("expected");
        Assert.AreEqual(expected.GetProperty("error").GetString(), error, id);
        Assert.AreEqual(expected.GetProperty("stop").GetBoolean(), stop, id);
        Assert.AreEqual(expected.GetProperty("ticks").GetUInt64(), tick, id);
        Assert.AreEqual((long)tick, completed, id);
        if (path == "trace") Assert.AreEqual(completed, (long)traced, id);
        AssertState(v, cpu, checkPosition: true);
        CollectionAssert.AreEqual(expectedMemory, memory.Words, id + ": memory including code");
    }

    [TestMethod]
    [DynamicData(nameof(LoadedVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void OriginalAndMaxPreserveBookArithmetic(string id, ExecutionSpeed speed, JsonElement v)
        => CheckLoadedArithmetic(id, speed, v, MemoryModel.Dubna);

    [TestMethod]
    [DynamicData(nameof(LoadedVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void BufferedOriginalAndMaxPreserveBookArithmetic(string id, ExecutionSpeed speed, JsonElement v)
        => CheckLoadedArithmetic(id, speed, v, MemoryModel.Buffered1967);

    private static void CheckLoadedArithmetic(string id, ExecutionSpeed speed, JsonElement v, MemoryModel memoryModel)
    {
        var machine = new MachineCore(memoryModel: memoryModel);
        Load(v, machine.Cpu, machine.Memory);
        var loader = new DubnaLoader(machine) { Speed = speed, InstructionLimit = 10, Output = _ => { } };
        Assert.IsTrue(loader.RunLoaded().Success, id);
        Assert.IsTrue(loader.HaltedByStop, id);
        Assert.AreEqual(2L, loader.InstructionsExecuted, id);
        Assert.AreEqual(2UL, machine.Clock.Tick, id);
        AssertState(v, machine.Cpu, checkPosition: false);
        Assert.AreEqual(9u, machine.Cpu.K, id);
        Assert.IsFalse(machine.Cpu.RightInstruction, id);
    }

    [TestMethod]
    [DynamicData(nameof(FaultVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void AvostStateAndInterceptionMatchBetweenSpeeds(string id, ExecutionSpeed speed, bool intercept, JsonElement v)
        => CheckLoadedFault(id, speed, intercept, v, MemoryModel.Dubna);

    [TestMethod]
    [DynamicData(nameof(FaultVectors), DynamicDataDisplayName = nameof(CaseName))]
    public void BufferedAvostStateAndInterceptionMatchBetweenSpeeds(string id, ExecutionSpeed speed, bool intercept, JsonElement v)
        => CheckLoadedFault(id, speed, intercept, v, MemoryModel.Buffered1967);

    private static void CheckLoadedFault(string id, ExecutionSpeed speed, bool intercept, JsonElement v, MemoryModel memoryModel)
    {
        var machine = new MachineCore(memoryModel: memoryModel);
        Load(v, machine.Cpu, machine.Memory);
        machine.Memory.Write(16, new Word48(0x0D80000D8000)); // STOP in both halves
        machine.Cpu.InterceptCount = intercept ? 1 : 0;
        machine.Cpu.InterceptAddr = 16;
        var loader = new DubnaLoader(machine) { Speed = speed, InstructionLimit = 10, Output = _ => { } };
        var result = loader.RunLoaded();
        Assert.AreEqual(intercept, result.Success, id);
        Assert.AreEqual(intercept, loader.HaltedByStop, id);
        Assert.AreEqual(intercept ? 1L : 0L, loader.InstructionsExecuted, id);
        Assert.AreEqual(intercept ? 1UL : 0UL, machine.Clock.Tick, id);
        Assert.AreEqual(0, machine.Cpu.InterceptCount, id);
        if (intercept)
        {
            AssertState(v, machine.Cpu, checkPosition: false);
            Assert.AreEqual(16u, machine.Cpu.K, id);
            Assert.IsTrue(machine.Cpu.RightInstruction, id);
        }
        else
        {
            Assert.AreEqual(v.GetProperty("expected").GetProperty("error").GetString(), result.ErrorMessage, id);
            AssertState(v, machine.Cpu, checkPosition: true);
        }
    }

    private static void AssertState(JsonElement v, Processor cpu, bool checkPosition)
    {
        string id = v.GetProperty("id").GetString()!;
        var e = v.GetProperty("expected");
        Assert.AreEqual(Hex(e.GetProperty("a")), cpu.A.Value, id + ": A");
        Assert.AreEqual(Hex(e.GetProperty("y")), cpu.Y.Value, id + ": Y (Dubna, including undefined book bits)");
        Assert.AreEqual(e.GetProperty("r").GetUInt32(), cpu.R, id + ": R");
        Assert.AreEqual(e.GetProperty("c").GetUInt32(), cpu.C, id + ": C");
        Assert.AreEqual(e.GetProperty("applyC").GetBoolean(), cpu.ApplyC, id + ": ApplyC");
        int index = 0;
        foreach (var m in e.GetProperty("m").EnumerateArray())
            Assert.AreEqual(m.GetUInt32(), cpu.GetM(index++), id + ": M" + (index - 1));
        if (checkPosition)
        {
            Assert.AreEqual(e.GetProperty("k").GetUInt32(), cpu.K, id + ": K");
            Assert.AreEqual(e.GetProperty("right").GetBoolean(), cpu.RightInstruction, id + ": half");
        }
        foreach (var difference in v.GetProperty("differences").EnumerateArray())
        {
            string field = difference.GetProperty("field").GetString()!;
            Assert.IsTrue(v.GetProperty("bookDefined").EnumerateArray().Any(x => x.GetString() == field));
            Assert.IsFalse(string.IsNullOrWhiteSpace(difference.GetProperty("issue").GetString()));
            if (field == "y") Assert.AreNotEqual(Hex(difference.GetProperty("book")), cpu.Y.Value, id);
            else if (field == "r") Assert.AreNotEqual(difference.GetProperty("book").GetUInt32(), cpu.R, id);
            else Assert.Fail("Unreviewed difference field: " + field);
        }
    }

    [TestMethod]
    public void PrintedConstantsHaveExactBitsAndValues()
    {
        foreach (var c in Fixture.GetProperty("constants").EnumerateArray())
        {
            string id = c.GetProperty("id").GetString()!;
            ulong bits = Convert.ToUInt64(c.GetProperty("octal").GetString(), 8);
            var word = new Word48(bits);
            Assert.AreEqual(bits, word.Value, id);
            if (id.StartsWith("LOG")) Assert.AreEqual(4UL, bits);
            else
            {
                double value = double.Parse(c.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
                Assert.AreEqual(value, word.ToDouble(), id);
                if (id.StartsWith("REAL")) Assert.AreEqual(bits, Word48.FromDouble(value).Value, id);
            }
        }
    }

    [TestMethod]
    public void FixtureIdentitiesAndProvenanceAreComplete()
    {
        Assert.AreEqual(1, Fixture.GetProperty("schemaVersion").GetInt32());
        var ids = new HashSet<string>();
        foreach (var v in Fixture.GetProperty("vectors").EnumerateArray())
        {
            Assert.IsTrue(ids.Add(v.GetProperty("id").GetString()!));
            Assert.IsTrue(v.GetProperty("sources").GetArrayLength() > 0);
            Assert.AreEqual(16, v.GetProperty("initial").GetProperty("m").GetArrayLength());
            Assert.AreEqual(16, v.GetProperty("expected").GetProperty("m").GetArrayLength());
        }
    }
}
