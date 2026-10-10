namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class PreparedArithmeticTests
{
    // Independently encoded constants: Fortran book §2 and MADLEN constants.
    private static Word48 Octal(string value) => new(Convert.ToUInt64(value, 8));
    private static Word48 One => Octal("4050000000000000");
    private static Word48 Two => Octal("4110000000000000");
    private static Word48 NegativeOne => Octal("4020000000000000");

    [TestMethod]
    public void AdditionVariantsMatchIndependentlyEncodedResults()
    {
        Assert.AreEqual(Two, AdditiveOperations.EvaluateAdd(One, Word48.Zero, 0, One, false, false).A);
        Assert.AreEqual(One, AdditiveOperations.EvaluateAdd(Two, Word48.Zero, 0, One, false, true).A);
        Assert.AreEqual(NegativeOne, AdditiveOperations.EvaluateAdd(One, Word48.Zero, 0, Two, false, true).A);
        Assert.AreEqual(One, AdditiveOperations.EvaluateAdd(One, Word48.Zero, 0, Two, true, false).A);
        Assert.AreEqual(NegativeOne, AdditiveOperations.EvaluateAdd(NegativeOne, Word48.Zero, 0, Two, true, true).A);
    }

    [TestMethod]
    public void ExponentAndSignOperationsMatchIndependentlyEncodedResults()
    {
        Assert.AreEqual(Two, AdditiveOperations.EvaluateAddExponent(One, 0, 1).A);
        Assert.AreEqual(Octal("4010000000000000"), AdditiveOperations.EvaluateAddExponent(One, 0, -1).A);
        Assert.AreEqual(NegativeOne, AdditiveOperations.EvaluateChangeSign(One, 0, true).A);
        Assert.AreEqual(One, AdditiveOperations.EvaluateChangeSign(One, 0, false).A);
    }

    [TestMethod]
    public void MultiplyAndDivideMatchIndependentlyEncodedResults()
    {
        var multiply = MultiplicativeOperations.EvaluateMultiply(One, Word48.Zero, 0, Two);
        var divide = MultiplicativeOperations.EvaluateDivide(One, Word48.Zero, 0, Two);
        Assert.AreEqual(Two, multiply.A);
        Assert.AreEqual(Word48.Zero, multiply.Y);
        Assert.AreEqual(Octal("4010000000000000"), divide.A);
        Assert.AreEqual(Word48.Zero, divide.Y);
        Assert.AreEqual(Octal("4060000000000000"),
            MultiplicativeOperations.EvaluateMultiply(NegativeOne, Word48.Zero, 0, Two).A);
        Assert.AreEqual(Octal("3760000000000000"),
            MultiplicativeOperations.EvaluateDivide(NegativeOne, Word48.Zero, 0, Two).A);
    }

    [TestMethod]
    public void PreparationDoesNotPublishAndLaterPublicationUsesPreparedInputs()
    {
        var state = new ProcessorState { A = One, Y = new(123), R = 0, K = 7, C = 9 };
        state.M[1] = 11;
        var prepared = AdditiveOperations.EvaluateAdd(state.A, state.Y, state.R, One, false, false);
        Assert.AreEqual(One, state.A);
        Assert.AreEqual(123UL, state.Y.Value);
        state.A = NegativeOne;
        state.R = (uint)RFlags.RoundDisable;
        new NormalizationAndRounding(state).Publish(prepared);
        Assert.AreEqual(Two, state.A);
        Assert.AreEqual(Word48.Zero, state.Y);
        Assert.AreEqual((uint)RFlags.RoundDisable, state.R);
        Assert.AreEqual(7U, state.K);
        Assert.AreEqual(9U, state.C);
        Assert.AreEqual(11U, state.M[1]);
    }

    [TestMethod]
    public void MultiplyByZeroPreservesLegacyUpperYWhileSignAndExponentClearIt()
    {
        var previousY = new Word48(0xAB1234567890);
        Assert.AreEqual(0xAB0000000000UL,
            MultiplicativeOperations.EvaluateMultiply(One, previousY, 0, Word48.Zero).Y.Value);
        Assert.AreEqual(Word48.Zero, AdditiveOperations.EvaluateAddExponent(Word48.Zero, 0, -1).Y);
        Assert.AreEqual(Word48.Zero, AdditiveOperations.EvaluateChangeSign(Word48.Zero, 0, true).Y);
    }

    [TestMethod]
    public void InvalidDivisorDoesNotPublishButOverflowPublishesBeforeThrowing()
    {
        var state = new ProcessorState { A = One, Y = new(123), R = 0 };
        var operations = new MultiplicativeOperations(state, new(state));
        Assert.ThrowsExactly<ProcessorException>(() => operations.Divide(Word48.Zero));
        Assert.AreEqual(One, state.A);
        Assert.AreEqual(123UL, state.Y.Value);
        var large = new Word48((127UL << 41) | (1UL << 39));
        var prepared = MultiplicativeOperations.EvaluateMultiply(large, state.Y, 0, large);
        Assert.IsTrue(prepared.Overflow);
        Assert.AreEqual(One, state.A);
        Assert.ThrowsExactly<ProcessorException>(() => new NormalizationAndRounding(state).Publish(prepared));
        Assert.AreEqual(prepared.A, state.A);
        Assert.AreEqual(prepared.Y, state.Y);
    }

    [TestMethod]
    [DataRow(0)] // add
    [DataRow(1)] // subtract
    [DataRow(2)] // reverse subtract
    [DataRow(3)] // subtract magnitudes
    [DataRow(4)] // multiply
    [DataRow(5)] // divide
    [DataRow(6)] // exponent
    [DataRow(7)] // sign
    public void PreparedAndImmediatePathsAgreeForBoundaryAndSeededOperands(int operation)
    {
        // Differential coverage of the two publication paths, including exceptions.
        // The independent expected-bit vectors above remain the semantic oracle.
        ulong seed = 0x93A51D67;
        ulong Next()
        {
            seed ^= seed << 13;
            seed ^= seed >> 7;
            seed ^= seed << 17;
            return seed & 0xFFFFFFFFFFFFUL;
        }

        ulong[] boundaries = [0, 1, 0xFFFFFFFFFFFF, One.Value, NegativeOne.Value,
            1UL << 39, 1UL << 40, 127UL << 41, (127UL << 41) | (1UL << 39)];
        for (int sample = 0; sample < 512; sample++)
        {
            var a = new Word48(sample < boundaries.Length ? boundaries[sample] : Next());
            var y = new Word48(Next());
            var operand = new Word48(sample < boundaries.Length ? boundaries[boundaries.Length - sample - 1] : Next());
            uint mode = (uint)sample & 0x3F;
            int exponentDelta = sample % 128 - 64;
            bool negate = (sample & 1) != 0;
            var state = new ProcessorState { A = a, Y = y, R = mode, K = 7, C = 9 };
            state.M[1] = 11;
            NormalizedArithmeticResult prepared = default;
            bool invalidDivisor = false;
            try
            {
                prepared = operation switch
                {
                    0 => AdditiveOperations.EvaluateAdd(a, y, mode, operand, false, false),
                    1 => AdditiveOperations.EvaluateAdd(a, y, mode, operand, false, true),
                    2 => AdditiveOperations.EvaluateAdd(a, y, mode, operand, true, false),
                    3 => AdditiveOperations.EvaluateAdd(a, y, mode, operand, true, true),
                    4 => MultiplicativeOperations.EvaluateMultiply(a, y, mode, operand),
                    5 => MultiplicativeOperations.EvaluateDivide(a, y, mode, operand),
                    6 => AdditiveOperations.EvaluateAddExponent(a, mode, exponentDelta),
                    _ => AdditiveOperations.EvaluateChangeSign(a, mode, negate)
                };
            }
            catch (ProcessorException)
            {
                Assert.AreEqual(5, operation);
                invalidDivisor = true;
            }
            Assert.AreEqual(a, state.A);
            Assert.AreEqual(y, state.Y);
            var alu = new Alu(state);
            bool fault = false;
            try
            {
                switch (operation)
                {
                    case 0: alu.Add(operand, false, false); break;
                    case 1: alu.Add(operand, false, true); break;
                    case 2: alu.Add(operand, true, false); break;
                    case 3: alu.Add(operand, true, true); break;
                    case 4: alu.Multiply(operand); break;
                    case 5: alu.Divide(operand); break;
                    case 6: alu.AddExponent(exponentDelta); break;
                    default: alu.ChangeSign(negate); break;
                }
            }
            catch (ProcessorException) { fault = true; }
            Assert.AreEqual(invalidDivisor || prepared.Overflow, fault, $"sample {sample}");
            Assert.AreEqual(invalidDivisor ? a : prepared.A, state.A, $"A sample {sample}");
            Assert.AreEqual(invalidDivisor ? y : prepared.Y, state.Y, $"Y sample {sample}");
            Assert.AreEqual(mode, state.R);
            Assert.AreEqual(7U, state.K);
            Assert.AreEqual(9U, state.C);
            Assert.AreEqual(11U, state.M[1]);
        }
    }
}
