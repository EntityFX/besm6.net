namespace Besm6.Assembler.Tests;

/// <summary>
/// Проверяет критерии приёмки диалектов ассемблера: строгие диалекты принимают
/// свои мнемоники и отклоняют чужие, Auto принимает оба, *32/ext дают EXT.
/// </summary>
[TestClass]
public sealed class AssemblerDialectTests
{
    private static MadlenAssembler Madlen => new();
    private static BemshAssembler Bemsh => new();
    private static AutoDetectingAssembler Auto => new();

    // xta — MADLEN-мнемоника (ShortMadlen[8]); сч — БЭМШ (ShortBemsh[8]).
    private const ulong OpcodeXta = 8UL << 12; // 0x8000

    [TestMethod]
    public void Madlen_AcceptsXta()
    {
        Assert.AreEqual(OpcodeXta, Madlen.AssembleWord("xta"));
    }

    [TestMethod]
    public void Madlen_RejectsBemshMnemonic()
    {
        Assert.ThrowsExactly<AssemblerException>(() => Madlen.AssembleWord("сч"));
    }

    [TestMethod]
    public void Bemsh_AcceptsRussianMnemonic()
    {
        Assert.AreEqual(OpcodeXta, Bemsh.AssembleWord("сч"));
    }

    [TestMethod]
    public void Bemsh_RejectsMadlenMnemonic()
    {
        Assert.ThrowsExactly<AssemblerException>(() => Bemsh.AssembleWord("xta"));
    }

    [TestMethod]
    public void Auto_AcceptsBothDialects()
    {
        Assert.AreEqual(OpcodeXta, Auto.AssembleWord("xta"));
        Assert.AreEqual(OpcodeXta, Auto.AssembleWord("сч"));
    }

    [TestMethod]
    public void Star32_AndExt_BothGiveExtOpcode()
    {
        Assert.AreEqual(Madlen.AssembleWord("*32"), Madlen.AssembleWord("ext"));
        Assert.AreEqual((ulong)Besm6.Architecture.Opcode.Ext << 12, Madlen.AssembleWord("*32"));
    }

    [TestMethod]
    public void Dialect_IsExposedByImplementations()
    {
        Assert.AreEqual(AssemblyDialect.Madlen, Madlen.Dialect);
        Assert.AreEqual(AssemblyDialect.Bemsh, Bemsh.Dialect);
        Assert.AreEqual(AssemblyDialect.Auto, Auto.Dialect);
    }

    [TestMethod]
    public void AssembleProgram_SetsBaseAddrAndWords()
    {
        var result = Madlen.AssembleProgram(new[] { "xta", "stx 10 (2)" }, baseAddress: 512);
        Assert.AreEqual(512, result.BaseAddr);
        Assert.AreEqual(2, result.Words.Count);
        Assert.AreEqual(OpcodeXta, result.Words[0]);
    }
}