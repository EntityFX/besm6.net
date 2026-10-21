using Besm6.Architecture.Numerics;
using Besm6.Processor.Memory;
using Besm6.Runtime.Devices;

namespace Besm6.Runtime.Tests;

[TestClass]
public sealed class PuncherFormatTests
{
    [TestMethod]
    [DataRow(0x280, "stdarray.out", "`77761 ")]
    [DataRow(0xA00, "cosy.out", "HELLO\n")]
    public void ArrayCardsAreRecognizedByTheirTwelveBitType(int type, string fileName, string expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), "besm6-punch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var memory = new CoreMemory();
        var columns = new ushort[80];
        columns[0] = (ushort)type;
        columns[1] = 1;
        ulong text = 0x48454C4C4F0A; // HELLO followed by newline, one complete COSY word.
        for (int i = 0; i < 4; i++) columns[4 + i] = (ushort)((text >> (36 - i * 12)) & 0xFFF);
        var bytes = new byte[144];
        for (int column = 0; column < 80; column++)
            for (int row = 0; row < 12; row++)
                if ((columns[column] & (1 << row)) != 0)
                    bytes[row * 12 + 1 + column / 8 + (column >= 40 ? 1 : 0)] |= (byte)(0x80 >> (column % 8));
        for (int i = 0; i < 24; i++)
        {
            ulong word = 0;
            for (int j = 0; j < 6; j++) word = (word << 8) | bytes[i * 6 + j];
            memory.Write((uint)(100 + i), new Word48(word));
        }
        var puncher = new Puncher(memory, directory);
        try
        {
            puncher.Punch(100, 124);
            puncher.Finish();
            Assert.IsTrue(File.ReadAllText(Path.Combine(directory, fileName)).StartsWith(expected, StringComparison.Ordinal));
            Assert.IsTrue(File.ReadAllText(Path.Combine(directory, "punch.out")).Length > 0);
        }
        finally
        {
            puncher.Finish();
            Directory.Delete(directory, true);
        }
    }
}
