using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Besm6.Core;

namespace Besm6.Tests;

/// <summary>
/// Architectural regression tests for the CPU <-> extracode boundary.
///
/// These tests intentionally exercise the processor at the instruction level,
/// not through MONSYS/CERNLIB.  Every bug found by a high-level integration
/// test should eventually have a small regression here.
///
/// IMPORTANT:
/// The small adapter methods at the bottom are the only places that may need
/// renaming if the public/internal Processor API changes.
/// </summary>
[TestClass]
[TestCategory("Architecture")]
[TestCategory("Extracode")]
public sealed class ProcessorExtracodeTests
{
    // BESM-6 addresses are 15-bit.
    private const int TestK = 02000;
    private const int NextK = TestK + 1;

    // Pick a harmless extracode number handled by the test callback.
    // The handler short-circuits real system handling, so the exact service
    // implementation is irrelevant to these contract tests.
    private const int TestExtracode = 050;

    [TestMethod]
    public void ExtracodeInLeftHalf_SkipsRightHalf()
    {
        var cpu = CreateProcessor();

        int handlerCalls = 0;

        cpu.ExtracodeDispatch = _ =>
        {
            handlerCalls++;
            return true;
        };

        //
        // LEFT  = extracode
        // RIGHT = VTM 01234(1)
        //
        // If RIGHT is executed by mistake, M[1] becomes 01234.
        //
        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode),
            right: EncodeVtm(register: 1, address: 01234));

        SetM(cpu, 1, 0);
        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.AreEqual(
            1,
            handlerCalls,
            "Extracode handler must be invoked exactly once.");

        Assert.AreEqual(
            0,
            GetM(cpu, 1),
            "The RIGHT half of a word containing an extracode in LEFT must not execute.");

        Assert.AreEqual(
            NextK,
            GetK(cpu),
            "After an extracode in LEFT, execution must continue at the next 48-bit word.");

        Assert.IsFalse(
            IsRightHalfNext(cpu),
            "After an extracode, the next instruction must be the LEFT half of the next word.");
    }

    [TestMethod]
    public void ExtracodeInRightHalf_AdvancesToNextWordLeftHalf()
    {
        var cpu = CreateProcessor();

        int handlerCalls = 0;

        cpu.ExtracodeDispatch = _ =>
        {
            handlerCalls++;
            return true;
        };

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeNopLikeInstruction(),
            right: EncodeExtracode(TestExtracode));

        SetExecutionPoint(cpu, TestK, rightHalf: true);

        Step(cpu);

        Assert.AreEqual(
            1,
            handlerCalls,
            "Extracode handler must be invoked exactly once.");

        Assert.AreEqual(
            NextK,
            GetK(cpu),
            "After an extracode in RIGHT, K must advance to the next word.");

        Assert.IsFalse(
            IsRightHalfNext(cpu),
            "After an extracode in RIGHT, the next instruction must be LEFT.");
    }

    [TestMethod]
    public void Extracode_HandlerAccumulatorResult_IsPreserved()
    {
        var cpu = CreateProcessor();

        const ulong expected = 0x1234_5678_9ABCUL & 0xFFFF_FFFF_FFFFUL;

        cpu.SetA(0x111UL);

        cpu.ExtracodeDispatch = _ =>
        {
            cpu.SetA(expected);
            return true;
        };

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode),
            right: EncodeNopLikeInstruction());

        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.AreEqual(
            expected,
            GetA(cpu),
            "A written by ExtracodeHandler must not be overwritten by the cached pre-extracode A.");
    }

    [TestMethod]
    public void Extracode_HandlerYResult_IsPreserved()
    {
        var cpu = CreateProcessor();

        const ulong expected = 0x0ABC_DEF0_1234UL & 0xFFFF_FFFF_FFFFUL;

        SetY(cpu, 0x222UL);

        cpu.ExtracodeDispatch = _ =>
        {
            SetY(cpu, expected);
            return true;
        };

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode),
            right: EncodeNopLikeInstruction());

        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.AreEqual(
            expected,
            GetY(cpu),
            "Y written by ExtracodeHandler must not be overwritten by the cached pre-extracode Y.");
    }

    [TestMethod]
    public void Extracode_SetsRToLogical()
    {
        var cpu = CreateProcessor();

        //
        // Put R into a definitely non-logical state first.
        // The helper should use the same public/internal mechanism as the
        // existing arithmetic tests.
        //
        SetRAdditive(cpu);

        cpu.ExtracodeDispatch = _ => true;

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode),
            right: EncodeNopLikeInstruction());

        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.IsTrue(
            IsRLogical(cpu),
            "Every successfully handled extracode must leave R in Logical mode, matching dubna core.set_logical().");
    }

    [TestMethod]
    public void Extracode_SetsM14ToEffectiveAddress()
    {
        var cpu = CreateProcessor();

        const int reg = 3;
        const int baseAddress = 01234;
        const int modifier = 00007;
        // 15-bit ADDR-маска — явно в hex (0x7FFF = 077777 oct), чтобы не зависеть
        // от трактовки литералов с ведущим нулём в разных версиях компилятора.
        int expectedEffectiveAddress = (baseAddress + modifier) & 0x7FFF;

        int? handlerAddress = null;

        cpu.ExtracodeDispatch = call =>
        {
            handlerAddress = (int)call.EffectiveAddress;
            return true;
        };

        SetM(cpu, reg, modifier);

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode, reg, baseAddress),
            right: EncodeNopLikeInstruction());

        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.AreEqual(
            expectedEffectiveAddress,
            GetM(cpu, 14),
            "M[14] must contain Aex for an extracode.");

        Assert.AreEqual(
            expectedEffectiveAddress,
            handlerAddress,
            "ExtracodeHandler must receive the same effective address that is stored in M[14].");
    }

    [TestMethod]
    public void Extracode_HandlerReceivesOpcode()
    {
        var cpu = CreateProcessor();

        int? receivedOpcode = null;

        cpu.ExtracodeDispatch = call =>
        {
            receivedOpcode = (int)call.Code;
            return true;
        };

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode),
            right: EncodeNopLikeInstruction());

        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.AreEqual(
            TestExtracode,
            receivedOpcode,
            "The decoded extracode opcode passed to the handler must be exact.");
    }

    [TestMethod]
    public void Extracode_MetadataReportsLeftHalfForLeftExtracode()
    {
        //
        // Metadata (reg/rawAddr/half) must describe the EXECUTED instruction,
        // the K/half advance in step()).
        //
        var cpu = CreateProcessor();

        ExtracodeCall? observedCall = null;
        cpu.ExtracodeDispatch = call =>
        {
            observedCall = call;
            return true;
        };

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeExtracode(TestExtracode, register: 2, address: 01234),
            right: EncodeNopLikeInstruction());

        SetExecutionPoint(cpu, TestK, rightHalf: false);

        Step(cpu);

        Assert.AreEqual(
            2,
            observedCall!.Value.Register,
            "Extracode metadata must hold the decoded register.");

        Assert.AreEqual(
            01234u,
            observedCall.Value.RawAddress,
            "Extracode metadata must hold the RAW (pre-indexing) address.");

        Assert.IsFalse(
            observedCall.Value.IsRightHalf,
            "An extracode executed in the LEFT half must be reported as LEFT.");
    }

    [TestMethod]
    public void Extracode_MetadataReportsRightHalfForRightExtracode()
    {
        //
        // REGRESSION: extracode half metadata used to be stored AFTER the extracode
        // advance (k += 1; rightFlag = false), so a RIGHT-half extracode was
        // reported as LEFT.  The stored half must be the one that EXECUTED
        //
        var cpu = CreateProcessor();

        ExtracodeCall? observedCall = null;
        cpu.ExtracodeDispatch = call =>
        {
            observedCall = call;
            return true;
        };

        WriteInstructionWord(
            cpu,
            TestK,
            left: EncodeNopLikeInstruction(),
            right: EncodeExtracode(TestExtracode));

        SetExecutionPoint(cpu, TestK, rightHalf: true);

        Step(cpu);

        Assert.AreEqual(
            0,
            observedCall!.Value.Register,
            "Extracode metadata must hold the decoded register.");

        Assert.AreEqual(
            0u,
            observedCall.Value.RawAddress,
            "Extracode metadata must hold the RAW (pre-indexing) address.");

        Assert.IsTrue(
            observedCall.Value.IsRightHalf,
            "A RIGHT-half extracode must be reported as RIGHT (executed half, not post-advance state).");
    }

    // =====================================================================
    // Test adapter
    // =====================================================================
    //
    // Keep all coupling to the concrete besm6.net Processor API here.
    //
    // If your current branch exposes differently named helpers (Memory,
    // SetK, RightInstrFlag, Y, R, etc.), change ONLY this section.
    // The tests above describe the architectural contract and should stay
    // unchanged.
    // =====================================================================

    private sealed class LinearMemory : IMemory
    {
        private readonly Word48[] _words = new Word48[32768];
        public Word48 Read(uint address) => _words[address & 0x7FFF];
        public void Write(uint address, Word48 word) => _words[address & 0x7FFF] = word;
        public int Size => _words.Length;
    }

    // Память последнего созданного процессора (тесты класса исполняются последовательно).
    private static IMemory _memory = null!;

    private static Processor CreateProcessor()
    {
        _memory = new LinearMemory();
        return new Processor(_memory);
    }

    private static void Step(Processor cpu) => cpu.Step();

    private static void WriteInstructionWord(
        Processor cpu,
        int address,
        uint left,
        uint right)
    {
        ulong word =
            (((ulong)left & 0xFF_FFFFUL) << 24) |
             ((ulong)right & 0xFF_FFFFUL);

        SetMemoryWord(cpu, address, word);
    }

    private static uint EncodeExtracode(
        int opcode,
        int register = 0,
        int address = 0)
    {
        return EncodeInstruction(opcode, register, address);
    }

    private static uint EncodeVtm(int register, int address)
    {
        return EncodeInstruction((int)Opcode.Vtm, register, address);
    }

    private static uint EncodeNopLikeInstruction()
    {
        // UTC 0 — архитектурно безвреден: next_c = 0 → apply_c_reg не ставится.
        return EncodeInstruction((int)Opcode.Utc, 0, 0);
    }

    private static uint EncodeInstruction(int opcode, int register, int address)
    {
        // Реальный ассемблер проекта (порт dubna/assembler.cpp) — второго
        // энкодера в тестах нет. Мнемоника берётся из OpcodeTable (Bemsh):
        string mnemonic = Besm6.Asm.OpcodeTable.GetOpNameBemsh(opcode);
        string oct = Convert.ToString(address, 8);
        ulong word = Besm6.Asm.Assembler.Asm($"{mnemonic} {oct}({register})");
        return (uint)((word >> 24) & 0xFF_FFFFUL); // левое полу-слово = инструкция
    }

    private static void SetMemoryWord(Processor cpu, int address, ulong value)
    {
        _memory.Write((uint)address, new Word48(value));
    }

    private static ulong ReadMemoryWord(int address)
    {
        return _memory.Read((uint)address).Value;
    }

    private static void SetExecutionPoint(
        Processor cpu,
        int k,
        bool rightHalf)
    {
        cpu.SetK((uint)k);
        if (!rightHalf)
            return;

        // Публичный API не имеет setter'а right_instr_flag: чтобы оказаться на
        // RIGHT-половине слова, исполняем безвредную LEFT-половину (UTC 0)
        // ТОГО ЖЕ слова — машина сама делает переход L → R. Правая половина
        // слова при этом не изменяется.
        ulong word = ReadMemoryWord(k);
        ulong newWord = (((ulong)EncodeNopLikeInstruction()) << 24) | (word & 0xFF_FFFFUL);
        _memory.Write((uint)k, new Word48(newWord));
        cpu.SetK((uint)k);
        cpu.Step();
        // Теперь: K = k, right_instr_flag = true — следующая Step() возьмёт RIGHT.
    }

    private static int GetK(Processor cpu) => (int)cpu.GetK();

    private static bool IsRightHalfNext(Processor cpu) => cpu.OnRightInstruction;

    private static void SetM(Processor cpu, int register, int value) => cpu.SetM(register, (uint)value);

    private static int GetM(Processor cpu, int register) => (int)cpu.GetM(register);

    private static ulong GetA(Processor cpu) => cpu.GetA().Value;

    private static void SetY(Processor cpu, ulong value) => cpu.SetY(value);

    private static ulong GetY(Processor cpu) => cpu.GetY().Value;

    private static void SetRAdditive(Processor cpu)
    {
        // Аддитивный режим + сохранение non-mode флагов (как в арифметических тестах).
        cpu.SetR((ulong)(RFlags.OvfDisable | RFlags.RoundDisable | RFlags.Add));
    }

    private static bool IsRLogical(Processor cpu)
    {
        return (cpu.GetR() & (uint)RFlags.Mode) == (uint)RFlags.Log;
    }
}
