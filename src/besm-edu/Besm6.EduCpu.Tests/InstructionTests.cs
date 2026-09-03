namespace Besm6.EduCpu.Tests;

[TestClass]
public class InstructionTests
{
    [TestMethod]
    public void Short_EncodeDecode_RoundTrip()
    {
        CheckShort(Op.Atx, 0, 0);
        CheckShort(Op.Xta, 1, 64); // 100
        CheckShort(Op.Aax, 15, 4095); // 07777
    }

    [TestMethod]
    public void Long_EncodeDecode_RoundTrip()
    {
        CheckLong(Op.Vtm, 1, 1);
        CheckLong(Op.Uza, 0, 16); // 020
        CheckLong(Op.Uj, 2, 32767); // 0177777
        CheckLong(Op.Stop, 0, 0);
    }

    private static void CheckShort(Op op, byte reg, ushort addr)
    {
        Instruction d = Instruction.Decode(Instruction.EncodeShort(op, reg, addr));
        Assert.AreEqual(InstructionFormat.Short, d.Format);
        Assert.AreEqual(op, d.Opcode);
        Assert.AreEqual(reg, d.Register);
        Assert.AreEqual((ushort)addr, d.BaseAddress);
    }

    private static void CheckLong(Op op, byte reg, ushort addr)
    {
        Instruction d = Instruction.Decode(Instruction.EncodeLong(op, reg, addr));
        Assert.AreEqual(InstructionFormat.Long, d.Format);
        Assert.AreEqual(op, d.Opcode);
        Assert.AreEqual(reg, d.Register);
        Assert.AreEqual((ushort)addr, d.BaseAddress);
    }

    [TestMethod]
    public void Decode_WideValue_Throws()
        => Assert.Throws<InvalidInstructionException>(() => Instruction.Decode(0x100_00000));

    [TestMethod]
    public void Decode_UnsupportedOpcode_Throws()
    {
        // 6-битный код 004 (десятичное 4) отсутствует в учебном наборе.
        uint raw24 = 4u << 12;
        Assert.Throws<UnsupportedOpcodeException>(() => Instruction.Decode(raw24));
    }

    [TestMethod]
    public void Decode_ShortExtendedAddress_Bit18()
    {
        // Признак X дополняет 12-битный адрес тремя старшими битами 111: 070000 | 07777 = 077777.
        uint raw24 = ((uint)Op.Xta << 12) | (1u << 18) | 4095;
        Instruction d = Instruction.Decode(raw24);
        Assert.AreEqual((ushort)32767, d.BaseAddress); // 077777 восьмерично
    }

    [TestMethod]
    public void Encode_RejectsOutOfRangeFields()
    {
        Assert.Throws<InvalidInstructionException>(() => Instruction.EncodeShort(Op.Xta, 0, 4096));
        Assert.Throws<InvalidInstructionException>(() => Instruction.EncodeShort(Op.Vtm, 0, 0));
        Assert.Throws<InvalidInstructionException>(() => Instruction.EncodeLong(Op.Xta, 0, 0));
        Assert.Throws<InvalidInstructionException>(() => Instruction.EncodeShort(Op.Xta, 16, 0));
        Assert.Throws<InvalidInstructionException>(() => Instruction.EncodeLong(Op.Uj, 16, 0));
    }

    [TestMethod]
    public void Disassembly_Strings()
    {
        Assert.AreEqual("vtm 000001(01)", Instruction.Decode(Instruction.EncodeLong(Op.Vtm, 1, 1)).Disassembly);
        Assert.AreEqual("xta 000100(01)", Instruction.Decode(Instruction.EncodeShort(Op.Xta, 1, 64)).Disassembly);
        Assert.AreEqual("stop", Instruction.Decode(Instruction.EncodeLong(Op.Stop, 0, 0)).Disassembly);
        Assert.AreEqual("sub 000012", Instruction.Decode(Instruction.EncodeShort(Op.Sub, 0, 10)).Disassembly);
        Assert.AreEqual("mul 000012", Instruction.Decode(Instruction.EncodeShort(Op.Mul, 0, 10)).Disassembly);
    }
}
