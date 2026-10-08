namespace Besm6.Tests;

[TestClass]
public sealed class BitOperationOptimizationTests
{
    [TestMethod]
    public void IntrinsicsMatchReference_IncludingZeroAndUpperBits()
    {
        static int Count(ulong value)
        {
            int count = 0;
            while (value != 0) { value &= value - 1; count++; }
            return count;
        }
        static int Highest(ulong value)
        {
            int n = 32, count = 0;
            do
            {
                ulong temp = value;
                if ((temp >>= n) != 0) { count += n; value = temp; }
            } while ((n >>= 1) != 0);
            return 48 - count;
        }
        var words = new List<ulong> { 0, ulong.MaxValue, ArchitectureConstants.BITS48 };
        for (int i = 0; i < 64; i++) words.Add(1UL << i);
        var random = new Random(1234);
        byte[] bytes = new byte[8];
        for (int i = 0; i < 10_000; i++) { random.NextBytes(bytes); words.Add(BitConverter.ToUInt64(bytes)); }
        foreach (ulong word in words)
        {
            Assert.AreEqual(Count(word), ProcessorBitOperations.Besm6CountOnes(word));
            Assert.AreEqual(Highest(word), ProcessorBitOperations.Besm6HighestBit(word));
        }
    }
}
