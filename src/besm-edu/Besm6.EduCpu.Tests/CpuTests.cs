namespace Besm6.EduCpu.Tests;

[TestClass]
public class CpuTests
{
    private const ushort DataAddr = 64; // 0100

    private static (Cpu Cpu, Memory Mem) New(Func<Memory, ushort> fill)
    {
        Memory mem = new();
        ushort entry = fill(mem);
        return (new Cpu(mem, entry), mem);
    }

    private static void Put(Memory mem, ushort addr, Half half, uint raw24)
    {
        Word48 w = mem.Read(addr);
        mem.Write(addr, half == Half.Left
            ? Word48.Pack(raw24, w.RightHalf)
            : Word48.Pack(w.LeftHalf, raw24));
    }

    private static void AssertPos((ushort A, Half H) expected, (ushort A, Half H) actual)
    {
        Assert.AreEqual(expected.A, actual.A);
        Assert.AreEqual(expected.H, actual.H);
    }

    [TestMethod]
    public void Half_Alternation_FlipsLeftRight()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 0));
            Put(mem, 0, Half.Right, Instruction.EncodeShort(Op.Xta, 0, 0));
            Put(mem, 1, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 0));
            Put(mem, 1, Half.Right, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        Trace t1 = cpu.Step();
        AssertPos((0, Half.Left), (t1.FromAddress, t1.FromHalf));
        AssertPos((0, Half.Right), (t1.NextAddress, t1.NextHalf));
        Trace t2 = cpu.Step();
        AssertPos((0, Half.Right), (t2.FromAddress, t2.FromHalf));
        AssertPos((1, Half.Left), (t2.NextAddress, t2.NextHalf));
        Trace t3 = cpu.Step();
        AssertPos((1, Half.Left), (t3.FromAddress, t3.FromHalf));
        AssertPos((1, Half.Right), (t3.NextAddress, t3.NextHalf));
        cpu.Step();
        Assert.IsTrue(cpu.Stopped);
    }

    [TestMethod]
    public void Half_WrapsAround2To15()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 32767, Half.Right, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 32767;
        });

        cpu.Step(); // (0177777, L)
        Trace t2 = cpu.Step();
        AssertPos((32767, Half.Right), (t2.FromAddress, t2.FromHalf));
        AssertPos((0, Half.Left), (t2.NextAddress, t2.NextHalf));
        Assert.IsTrue(cpu.Stopped);
    }

    [TestMethod]
    public void M0_AlwaysReadsZero_AndVtmSetsOtherRegisters()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 0, Half.Left, Instruction.EncodeLong(Op.Vtm, 1, 5));
            Put(mem, 0, Half.Right, Instruction.EncodeLong(Op.Vtm, 2, 32767));
            return 0;
        });

        Assert.AreEqual((ushort)0, cpu.ReadM(0));
        cpu.Step();
        Assert.AreEqual((ushort)5, cpu.ReadM(1));
        Assert.AreEqual((ushort)0, cpu.ReadM(0));
        cpu.Step();
        Assert.AreEqual((ushort)32767, cpu.ReadM(2));
        Assert.AreEqual((ushort)0, cpu.ReadM(0));
    }

    [TestMethod]
    public void Indexing_AddsMRegister()
    {
        (Cpu cpu, _) = New(m =>
        {
            m.Write(DataAddr + 1, new Word48(45)); // 55 восьмерично
            Put(m, 0, Half.Left, Instruction.EncodeLong(Op.Vtm, 1, 1));
            Put(m, 0, Half.Right, Instruction.EncodeShort(Op.Xta, 1, DataAddr));
            Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Run(3);
        Assert.AreEqual((ulong)45, cpu.Acc.Raw);
    }

    [TestMethod]
    public void Atx_WritesAccumulator()
    {
        (Cpu cpu, Memory mem) = New(m =>
        {
            m.Write(10, new Word48(77));
            Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 10));
            Put(m, 0, Half.Right, Instruction.EncodeShort(Op.Atx, 0, 20));
            Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Run(3);
        Assert.AreEqual((ulong)77, mem.Read(20).Raw);
    }

    [TestMethod]
    public void Aax_Aex_Aox_Semantics()
    {
        foreach ((Op op, ulong expect) in new[]
        {
            (Op.Aax, 8u),  // 1100 & 1010 = 1000
            (Op.Aex, 6u),  // 1100 ^ 1010 = 0110
            (Op.Aox, 14u), // 1100 | 1010 = 1110
        })
        {
            (Cpu cpu, Memory mem) = New(m =>
            {
                m.Write(10, new Word48(12)); // 1100
                Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 10));
                Put(m, 0, Half.Right, Instruction.EncodeShort(op, 0, 10));
                Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
                return 0;
            });

            cpu.Step();                       // ACC = 1100
            mem.Write(10, new Word48(10));    // 1010
            cpu.Step();                       // ACC = f(1100, 1010)
            Assert.AreEqual((ulong)expect, cpu.Acc.Raw);
        }
    }

    [TestMethod]
    public void Arx_CyclicCarry()
    {
        (Cpu cpu, Memory mem) = New(m =>
        {
            m.Write(10, new Word48(Word48.Mask));
            Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 10));
            Put(m, 0, Half.Right, Instruction.EncodeShort(Op.Arx, 0, 10));
            Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Step();                   // ACC = 7777777777777777
        mem.Write(10, new Word48(1)); // 1
        cpu.Step();                   // перенос из 49-го разряда: результат 1
        Assert.AreEqual((ulong)1, cpu.Acc.Raw);
    }

    [TestMethod]
    public void Vtm_Loads15BitValue()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 0, Half.Left, Instruction.EncodeLong(Op.Vtm, 3, 32767));
            Put(mem, 0, Half.Right, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Run(2);
        Assert.AreEqual((ushort)32767, cpu.ReadM(3));
    }

    [TestMethod]
    public void Uza_TakenWhenAccIsZero()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 0, Half.Left, Instruction.EncodeLong(Op.Uza, 0, 16));
            Put(mem, 16, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        Trace t = cpu.Step();
        AssertPos((16, Half.Left), (t.NextAddress, t.NextHalf));
        cpu.Step();
        Assert.IsTrue(cpu.Stopped);
    }

    [TestMethod]
    public void Uza_NotTakenWhenAccNonZero()
    {
        (Cpu cpu, _) = New(m =>
        {
            m.Write(10, new Word48(5));
            Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 10));
            Put(m, 0, Half.Right, Instruction.EncodeLong(Op.Uza, 0, 16));
            Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Step(); // ACC = 5
        Trace t = cpu.Step();
        AssertPos((1, Half.Left), (t.NextAddress, t.NextHalf));
        cpu.Step();
        Assert.IsTrue(cpu.Stopped);
    }

    [TestMethod]
    public void Uj_BranchesUnconditionally()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 0, Half.Left, Instruction.EncodeLong(Op.Uj, 0, 24));
            Put(mem, 24, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Step();
        Assert.AreEqual((ushort)24, cpu.Pc);
        Assert.AreEqual(Half.Left, cpu.Half);
        cpu.Step();
        Assert.IsTrue(cpu.Stopped);
    }

    [TestMethod]
    public void Stop_PreventsFurtherSteps()
    {
        (Cpu cpu, _) = New(mem =>
        {
            Put(mem, 0, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Step();
        Assert.IsTrue(cpu.Stopped);
        Assert.Throws<StepAfterStopException>(() => cpu.Step());
    }

    [TestMethod]
    public void Run_RespectsStepLimit()
    {
        (Cpu cpu, _) = New(mem =>
        {
            // Пустые половины декодируются как ATX: программа не останавливается.
            return 0;
        });

        Assert.Throws<StepLimitExceededException>(() => cpu.Run(3));
        Assert.AreEqual((int)3, cpu.Steps);
    }

    [TestMethod]
    public void State_UnchangedAfterDecodeError()
    {
        (Cpu cpu, _) = New(mem =>
        {
            // 6-битный код 004 (десятичное 4) не поддерживается.
            Put(mem, 0, Half.Left, 4u << 12);
            return 0;
        });

        Word48 accBefore = cpu.Acc;
        ushort pcBefore = cpu.Pc;
        int stepsBefore = cpu.Steps;

        Assert.Throws<UnsupportedOpcodeException>(() => cpu.Step());

        Assert.AreEqual(accBefore, cpu.Acc);
        Assert.AreEqual((ushort)pcBefore, cpu.Pc);
        Assert.AreEqual((int)stepsBefore, cpu.Steps);
        Assert.AreEqual(Half.Left, cpu.Half);
    }

    [TestMethod]
    public void Sub_TractandsMemoryFromAcc()
    {
        (Cpu cpu, _) = New(m =>
        {
            m.Write(10, new Word48(9));
            Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 10));
            Put(m, 0, Half.Right, Instruction.EncodeShort(Op.Sub, 0, 10));
            Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Run(3);
        Assert.AreEqual((ulong)0, cpu.Acc.Raw);
    }

    [TestMethod]
    public void Sub_WrapsAround48Bits()
    {
        (Cpu cpu, _) = New(m =>
        {
            m.Write(10, new Word48(1));
            Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Sub, 0, 10));
            Put(m, 0, Half.Right, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Run(2);
        Assert.AreEqual(Word48.Mask, cpu.Acc.Raw); // 0 - 1 = 2^48 - 1
    }

    [TestMethod]
    public void Mul_MultipliesLow24Bits()
    {
        (Cpu cpu, _) = New(m =>
        {
            m.Write(10, new Word48(6));
            Put(m, 0, Half.Left, Instruction.EncodeShort(Op.Xta, 0, 10));
            Put(m, 0, Half.Right, Instruction.EncodeShort(Op.Mul, 0, 10));
            Put(m, 1, Half.Left, Instruction.EncodeLong(Op.Stop, 0, 0));
            return 0;
        });

        cpu.Run(3);
        Assert.AreEqual((ulong)36, cpu.Acc.Raw); // 6 * 6
    }
}