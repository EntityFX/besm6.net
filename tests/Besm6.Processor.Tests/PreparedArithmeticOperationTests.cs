namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class PreparedArithmeticOperationTests
{
    [TestMethod]
    [DataRow(0, "4110000000000000")]
    [DataRow(1, "0000000000000000")]
    [DataRow(2, "0000000000000000")]
    [DataRow(3, "0000000000000000")]
    [DataRow(4, "4110000000000000")]
    [DataRow(5, "4010000000000000")]
    [DataRow(6, "4110000000000000")]
    [DataRow(7, "4020000000000000")]
    public void AcceptedOperationUsesTheSharedArithmeticAndIndependentBits(int choice, string expected)
    {
        Word48 one = new(Convert.ToUInt64("4050000000000000", 8));
        Word48 two = new(Convert.ToUInt64("4110000000000000", 8));
        var operation = choice switch
        {
            0 => PreparedArithmeticOperation.Add(one, 0),
            1 => PreparedArithmeticOperation.Add(one, 0, negateOperand: true),
            2 => PreparedArithmeticOperation.Add(one, 0, negateAccumulator: true),
            3 => PreparedArithmeticOperation.Add(one, 0, true, true),
            4 => PreparedArithmeticOperation.Multiply(two, 0),
            5 => PreparedArithmeticOperation.Divide(two, 0),
            6 => PreparedArithmeticOperation.AddExponent(1, 0),
            _ => PreparedArithmeticOperation.ChangeSign(true, 0)
        };
        var result = operation.Evaluate(one, Word48.Zero);
        Assert.AreEqual(new Word48(Convert.ToUInt64(expected, 8)), result.A);
        Assert.AreEqual(Word48.Zero, result.Y);
        Assert.IsFalse(result.Overflow);
        Assert.AreEqual(choice == 5, operation.IsDivision);
    }
}
