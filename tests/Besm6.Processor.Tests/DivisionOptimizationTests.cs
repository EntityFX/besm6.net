namespace Besm6.Tests;

[TestClass]
public sealed class DivisionOptimizationTests
{
    private static MantissaExponent LegacyDivide(MantissaExponent n, MantissaExponent d)
    {
        var q = new MantissaExponent(0, 0);
        if (d.Mantissa == MantissaExponent.BIT40)
            return new MantissaExponent(n.Exponent - d.Exponent + 65, n.Mantissa);
        n.Mantissa <<= 1;
        d.Mantissa <<= 1;
        if (Math.Abs(n.Mantissa) >= Math.Abs(d.Mantissa)) n.NormalizeToTheRight();
        q.Exponent = n.Exponent - d.Exponent + 64;
        for (long bit = MantissaExponent.BIT40; bit > 0; bit >>= 1)
        {
            if (n.Mantissa == 0) break;
            if (Math.Abs(n.Mantissa) < MantissaExponent.BIT40) n.Mantissa *= 2;
            else if ((n.Mantissa > 0) == (d.Mantissa > 0))
            { q.Mantissa += bit; n.Mantissa = n.Mantissa * 2 - d.Mantissa; }
            else { q.Mantissa -= bit; n.Mantissa = n.Mantissa * 2 + d.Mantissa; }
        }
        return q;
    }

    [TestMethod]
    public void Division_MatchesLegacyRecurrenceAndExponent()
    {
        var random = new Random(4140);
        long[] edges = [0, 1, -1, MantissaExponent.BIT40 - 1, MantissaExponent.BIT40,
            -MantissaExponent.BIT40, MantissaExponent.BIT41 - 1, -MantissaExponent.BIT41];
        void Check(long a, long b)
        {
            var n = new MantissaExponent((uint)random.Next(128), a);
            var d = new MantissaExponent((uint)random.Next(128), b);
            MantissaExponent expected = LegacyDivide(n, d);
            MantissaExponent actual = MultiplicativeOperations.NrDiv(n, d);
            Assert.AreEqual(expected.Mantissa, actual.Mantissa, $"{a} / {b}");
            Assert.AreEqual(expected.Exponent, actual.Exponent);
        }
        foreach (long a in edges)
            foreach (long b in edges.Where(b => Math.Abs(b) >= MantissaExponent.BIT40)) Check(a, b);
        for (int i = 0; i < 50_000; i++)
        {
            long b = random.NextInt64(MantissaExponent.BIT40, MantissaExponent.BIT41);
            if (i % 2 != 0) b = -b;
            Check(random.NextInt64(-MantissaExponent.BIT41, MantissaExponent.BIT41), b);
        }
    }

    [TestMethod]
    public void Division_PreservesRegistersAndOverflowInAllArithmeticModes()
    {
        var random = new Random(6480);
        var state = new ProcessorState();
        var normalizer = new NormalizationAndRounding(state);
        var operations = new MultiplicativeOperations(state, normalizer);
        for (uint flags = 0; flags < 8; flags++)
        for (int i = 0; i < 5000; i++)
        {
            uint mode = (flags & 3) | ((flags & 4) << 3);
            var a = new Word48((ulong)random.NextInt64(1L << 48));
            var operand = new Word48((ulong)random.NextInt64(1L << 48));
            var y = new Word48((ulong)random.NextInt64(1L << 48));
            state.A = a; state.Y = y; state.R = mode;
            string? expectedException = null;
            try
            {
                if (((operand.Value ^ (operand.Value << 1)) & ArchitectureConstants.BIT41) == 0)
                    throw new ProcessorException("Division by zero");
                normalizer.NormalizeAndRound(LegacyDivide(new MantissaExponent(a), new MantissaExponent(operand)), 0, false);
            }
            catch (ProcessorException ex) { expectedException = ex.Message; }
            Word48 expectedA = state.A, expectedY = state.Y;
            state.A = a; state.Y = y; state.R = mode;
            string? actualException = null;
            try { operations.Divide(operand); }
            catch (ProcessorException ex) { actualException = ex.Message; }
            Assert.AreEqual(expectedException, actualException);
            Assert.AreEqual(expectedA, state.A);
            Assert.AreEqual(expectedY, state.Y);
            Assert.AreEqual(mode, state.R);
        }
    }
}
