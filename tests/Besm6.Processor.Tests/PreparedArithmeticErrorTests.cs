namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class PreparedArithmeticErrorTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void OverflowCauseIsIndependentOfDubnaDeliveryWithoutChangingBits(int choice)
    {
        var large = new Word48((127UL << 41) | (1UL << 39));
        var negativeBoundary = new Word48((127UL << 41) | (1UL << 40));
        var two = new Word48(Convert.ToUInt64("4110000000000000", 8));
        var half = new Word48(Convert.ToUInt64("4010000000000000", 8));
        uint mode = (uint)RFlags.OvfDisable;
        var operation = choice switch
        {
            0 => PreparedArithmeticOperation.Add(large, mode),
            1 => PreparedArithmeticOperation.Multiply(two, mode),
            2 => PreparedArithmeticOperation.Divide(half, mode),
            3 => PreparedArithmeticOperation.AddExponent(1, mode),
            _ => PreparedArithmeticOperation.ChangeSign(true, mode)
        };
        var accumulator = choice == 4 ? negativeBoundary : large;
        var legacy = operation.Evaluate(accumulator, Word48.Zero);
        var physicalCause = operation.EvaluateForErrorControl(accumulator, Word48.Zero);
        Assert.IsFalse(legacy.Overflow);
        Assert.IsTrue(physicalCause.Overflow);
        Assert.AreEqual(1UL << 39, physicalCause.A.Value); // Order128 wraps to0 in the output word.
        Assert.AreEqual(Word48.Zero, physicalCause.Y);
        Assert.AreEqual(legacy.A, physicalCause.A);
        Assert.AreEqual(legacy.UnroundedA, physicalCause.UnroundedA);
        Assert.AreEqual(legacy.Y, physicalCause.Y);
        Assert.AreEqual(legacy.RoundOnOutput, physicalCause.RoundOnOutput);
    }

    [TestMethod]
    public void ErrorEvaluationDoesNotDisableRoundingOrNormalization()
    {
        var one = new Word48(Convert.ToUInt64("4050000000000000", 8));
        var small = new Word48((24UL << 41) | (1UL << 39));
        var rounded = PreparedArithmeticOperation.Add(small, (uint)RFlags.OvfDisable)
            .EvaluateForErrorControl(one, Word48.Zero);
        var unrounded = PreparedArithmeticOperation.Add(small, (uint)(RFlags.OvfDisable | RFlags.RoundDisable))
            .EvaluateForErrorControl(one, Word48.Zero);
        Assert.AreEqual(one.Value | 1UL, rounded.A.Value);
        Assert.IsTrue(rounded.RoundOnOutput);
        Assert.AreEqual(one, unrounded.A);
        Assert.IsFalse(unrounded.RoundOnOutput);
        var denormal = new Word48((65UL << 41) | 1UL);
        var preserved = PreparedArithmeticOperation.AddExponent(0, (uint)(RFlags.NormDisable | RFlags.OvfDisable))
            .EvaluateForErrorControl(denormal, Word48.Zero);
        Assert.AreEqual(denormal, preserved.A);
        var underflow = PreparedArithmeticOperation.AddExponent(-256, (uint)RFlags.OvfDisable)
            .EvaluateForErrorControl(one, Word48.Zero);
        Assert.AreEqual(Word48.Zero, underflow.A);
        Assert.IsFalse(underflow.Overflow);
    }
}
