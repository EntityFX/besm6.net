using Besm6.Assembler.Disassembly;

namespace Besm6.Tests;

[TestClass]
public sealed class InstructionEditorTests
{
    [TestMethod]
    public void EveryOperationRoundTripsAllThreePresentations()
    {
        foreach (var operation in InstructionEditor.Operations)
        foreach (int register in new[] { 0, 15 })
        foreach (int address in new[] { 0, 4095, 28672, 32767 })
        {
            uint encoded = InstructionEditor.Encode(operation.Opcode, register, address);
            var decoded = InstructionEditor.Decode(encoded);
            Assert.AreEqual(register, decoded.Register); Assert.AreEqual(address, decoded.Address);
            Assert.AreEqual(encoded, InstructionEditor.EncodeText(decoded.Autocode, "autocode"));
            Assert.AreEqual(encoded, InstructionEditor.EncodeText(decoded.Bemsh, "bemsh"));
            Assert.AreEqual(encoded, InstructionEditor.EncodeText(decoded.Madlen, "madlen"));
        }
    }

    [TestMethod]
    public void InvalidShortAddressesAndOverflowAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InstructionEditor.Encode(8, 0, 4096));
        Assert.Throws<ArgumentException>(() => InstructionEditor.Encode(8, 16, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstructionEditor.Decode(0x1000000));
        var decoded = InstructionEditor.Decode(InstructionEditor.EncodeText("СЧ M15 -1", "autocode"));
        Assert.AreEqual(32767, decoded.Address); Assert.IsTrue(decoded.Extension);
    }
}
