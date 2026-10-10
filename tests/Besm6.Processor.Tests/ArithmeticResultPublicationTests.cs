namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticResultPublicationTests
{
    private const long Half = 1L << 39;
    private const ulong Exponent64 = 64UL << 41;

    [TestMethod]
    public void OutputOverlayDoesNotChangeStoredMantissa()
    {
        var result = NormalizationAndRounding.Evaluate(new(64, Half), 5, true, 0, Word48.Zero);
        Assert.AreEqual(Exponent64 | (ulong)Half, result.UnroundedA.Value);
        Assert.AreEqual(Exponent64 | (ulong)Half | 1UL, result.A.Value);
        Assert.AreEqual(5UL, result.Y.Value);
        Assert.IsTrue(result.RoundOnOutput);
        Assert.IsFalse(result.Overflow);
    }

    [TestMethod]
    public void RoundingDisabledKeepsUnroundedOutput()
    {
        var result = NormalizationAndRounding.Evaluate(new(64, Half), 5, true,
            (uint)RFlags.RoundDisable, Word48.Zero);
        Assert.AreEqual(Exponent64 | (ulong)Half, result.A.Value);
        Assert.AreEqual(result.UnroundedA, result.A);
        Assert.IsFalse(result.RoundOnOutput);
    }

    [TestMethod]
    public void NormalizationTransfersLowBitAndCancelsOverlay()
    {
        // One left shift transfers Y's top bit into A and decrements the exponent.
        var result = NormalizationAndRounding.Evaluate(new(64, Half >> 1), (ulong)Half,
            true, 0, Word48.Zero);
        Assert.AreEqual((63UL << 41) | (ulong)Half | 1UL, result.A.Value);
        Assert.AreEqual(result.UnroundedA, result.A);
        Assert.AreEqual(0UL, result.Y.Value);
        Assert.IsFalse(result.RoundOnOutput);
    }

    [TestMethod]
    public void ZeroAndUnderflowPreserveLegacyUpperYBits()
    {
        var previousY = new Word48(0xAB1234567890);
        foreach (var input in new[] { new MantissaExponent(64, 0), new MantissaExponent(uint.MaxValue, Half) })
        {
            var result = NormalizationAndRounding.Evaluate(input, 0, true, 0, previousY);
            Assert.AreEqual(Word48.Zero, result.A);
            Assert.AreEqual(0xAB0000000000UL, result.Y.Value);
            Assert.IsFalse(result.RoundOnOutput);
            Assert.IsFalse(result.Overflow);
        }
    }

    [TestMethod]
    public void EvaluationDefersOverflowAndPublicationWritesRegistersBeforeFault()
    {
        var result = NormalizationAndRounding.Evaluate(new(128, Half), 7, true, 0, Word48.Zero);
        Assert.AreEqual((ulong)Half | 1UL, result.A.Value);
        Assert.AreEqual(7UL, result.Y.Value);
        Assert.IsTrue(result.Overflow);
        var state = new ProcessorState { A = new Word48(123), Y = new Word48(456), R = 0 };
        var normalizer = new NormalizationAndRounding(state);
        Assert.ThrowsExactly<ProcessorException>(() => normalizer.NormalizeAndRound(new(128, Half), 7, true));
        Assert.AreEqual(result.A, state.A);
        Assert.AreEqual(result.Y, state.Y);
    }

    [TestMethod]
    public void OverflowDisabledKeepsWrappedExponentWithoutFault()
    {
        var result = NormalizationAndRounding.Evaluate(new(128, Half), 7, false,
            (uint)RFlags.OvfDisable, Word48.Zero);
        Assert.AreEqual((ulong)Half, result.A.Value);
        Assert.IsFalse(result.Overflow);
    }
}
