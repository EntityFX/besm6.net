namespace Besm6.EduCpu.Tests;

[TestClass]
public class Word48Tests
{
    [TestMethod]
    public void Constructor_MasksTo48Bits()
        => Assert.AreEqual((ulong)0xFFFF_FFFF_FFFF, new Word48(0x1_FFFF_FFFF_FFFF).Raw);

    [TestMethod]
    public void Zero_Has16ZeroOctalDigits()
    {
        Word48 w = new(0);
        Assert.AreEqual((ulong)0, w.Raw);
        Assert.AreEqual("0000000000000000", w.ToOctal());
    }

    [TestMethod]
    public void Max_Has16SevenOctalDigits()
    {
        Word48 w = new(Word48.Mask);
        Assert.AreEqual((ulong)Word48.Mask, w.Raw);
        Assert.AreEqual("7777777777777777", w.ToOctal());
    }

    [TestMethod]
    public void Octal_RoundTrips()
    {
        foreach (ulong v in new ulong[] { 0, 1, 7, 8, 65535, 65536, Word48.Mask })
        {
            Word48 w = new(v);
            Assert.AreEqual(w, Word48.FromOctal(w.ToOctal()));
        }
    }

    [TestMethod]
    public void FromOctal_RejectsNonOctalDigit()
        => Assert.Throws<Exception>(() => Word48.FromOctal("8"));

    [TestMethod]
    public void FromOctal_RejectsEmpty()
        => Assert.Throws<Exception>(() => Word48.FromOctal(""));

    [TestMethod]
    public void Pack_SplitsIntoTwoHalves()
    {
        Word48 w = Word48.Pack(0x123456, 0x654321);
        Assert.AreEqual((uint)0x123456, w.LeftHalf);
        Assert.AreEqual((uint)0x654321, w.RightHalf);
    }

    [TestMethod]
    public void Pack_RejectsWideHalves()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Word48.Pack(0x100_00000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Word48.Pack(0, 0x100_00000));
    }

    [TestMethod]
    public void Bitwise_OperationsStayIn48Bits()
    {
        Word48 a = new(0xFF);
        Word48 b = new(0x0F);
        Assert.AreEqual((ulong)0x0F, a.And(b).Raw);
        Assert.AreEqual((ulong)0xFF, a.Or(b).Raw);
        Assert.AreEqual((ulong)0xF0, a.Xor(b).Raw);
        Assert.AreEqual(0xFFFFFFFFFF00UL, a.Not().Raw);
        Assert.AreEqual(Word48.Mask, a.Not().Xor(a).Raw);
    }

    [TestMethod]
    public void CyclicAdd_WithoutCarry_IsSum()
        => Assert.AreEqual((ulong)8, new Word48(3).CyclicAdd(new Word48(5)).Raw);

    [TestMethod]
    public void CyclicAdd_CarryFoldsIntoLowBit()
    {
        Word48 max = new(Word48.Mask);
        // Максимум + k: перенос из 49-го разряда прибавляется к младшему -> k.
        Assert.AreEqual((ulong)1, max.CyclicAdd(new Word48(1)).Raw);
        Assert.AreEqual((ulong)2, max.CyclicAdd(new Word48(2)).Raw);
        Assert.AreEqual(max, max.CyclicAdd(max));
    }

    [TestMethod]
    public void Subtract_Modular48Bits()
    {
        Assert.AreEqual((ulong)5, new Word48(7).Subtract(new Word48(2)).Raw);
        Assert.AreEqual(0xFFFF_FFFF_FFF9UL, new Word48(0).Subtract(new Word48(7)).Raw); // 0 - 7 = 2^48 - 7
    }

    [TestMethod]
    public void Multiply_UsesLow24Bits()
    {
        Assert.AreEqual((ulong)45, new Word48(5).Multiply(new Word48(9)).Raw);
        Assert.AreEqual(((ulong)0x234567 * 9) & Word48.Mask, new Word48(0x1234567).Multiply(new Word48(9)).Raw);
        ulong max24 = 0xFF_FFFF;
        Assert.AreEqual((max24 * max24) & Word48.Mask, new Word48(max24).Multiply(new Word48(max24)).Raw);
    }
}
