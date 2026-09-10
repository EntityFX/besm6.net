using Besm6.Architecture;
using Besm6.BitVisualizer;

namespace Besm6.BitVisualizer.Tests;

[TestClass]
public sealed class Besm6InstructionDecoderTests
{
    [TestMethod]
    public void Decode_ShortCommandUsesInstructionCodecAndExplainsFields()
    {
        var source = new DecodedInstruction(3, Opcode.APlusX, 0x123, InstructionFormat.Short);
        uint raw = InstructionCodec.EncodeHalf(source);

        Besm6InstructionDecodeResult result = Besm6InstructionDecoder.Decode(new BitPattern(24, raw));

        Assert.AreEqual(source, result.Instruction);
        Assert.AreEqual("СЛ", result.Mnemonic);
        StringAssert.Contains(result.Description, "сложение");
        StringAssert.Contains(result.Report.Render(), "маска");
        Assert.IsTrue(result.Fields.Any(field => field.Name == "Расширение адреса"));
    }

    [TestMethod]
    public void Decode_LongCommandReturnsLongFieldLayout()
    {
        var source = new DecodedInstruction(2, Opcode.Stop, 0x4567, InstructionFormat.Long);
        uint raw = InstructionCodec.EncodeHalf(source);

        Besm6InstructionDecodeResult result = Besm6InstructionDecoder.Decode(new BitPattern(24, raw));

        Assert.AreEqual(source, result.Instruction);
        Assert.AreEqual("СТОП", result.Mnemonic);
        Assert.IsFalse(result.Fields.Any(field => field.Name == "Расширение адреса"));
        BitField address = result.Fields.Single(field => field.Kind == BitFieldKind.Address);
        Assert.AreEqual(14, address.MostSignificantBit);
        Assert.AreEqual(0, address.LeastSignificantBit);
    }

    [TestMethod]
    public void Decode_UnknownShortOpcodeIsDescribedAsExtracode()
    {
        const uint raw = 0x28u << 12;

        Besm6InstructionDecodeResult result = Besm6InstructionDecoder.Decode(new BitPattern(24, raw));

        Assert.AreEqual("Э50", result.Mnemonic);
        StringAssert.Contains(result.Description, "Экстракод");
    }

    [TestMethod]
    public void Decode_RejectsNonInstructionWidth()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Besm6InstructionDecoder.Decode(new BitPattern(23, 0)));
    }
}
