using Besm6.Runtime.Devices;
using Besm6.Processor.Memory;
using System.Numerics;
using System.Text;

namespace Besm6.Tests;

[TestClass]
public class PunchCardTests
{
    [TestMethod]
    public void Ibm_FortranExpression_HasBookColumnPatterns()
    {
        var card = PunchCardCodec.Encode("      A=B+3", PunchCardEncoding.Ibm);
        ushort[] expected = { 0, 0, 0, 0, 0, 0, 0x009, 0x500, 0x011, 0x501, 0x020 };
        for (int col = 0; col < 80; col++)
            for (int row = 0; row < 12; row++)
                Assert.AreEqual(col < expected.Length && (expected[col] & (1 << row)) != 0,
                    card.IsPunched(row, col), $"row={row}, col={col}");
    }

    [TestMethod]
    public void Upp_FortranExpression_HasKnownBytesAndPreservesSpaces()
    {
        var card = PunchCardCodec.Encode("      A=B+3", PunchCardEncoding.Upp);
        byte[] expected = { 0x8F, 0x8F, 0x8F, 0x8F, 0x8F, 0x8F, 0x20, 0x15, 0xA2, 0x8A, 0x83 };
        for (int i = 0; i < 120; i++)
            Assert.AreEqual(i < expected.Length ? expected[i] : (byte)0, ReadUppByte(card, i));
    }

    [TestMethod]
    public void Upp_AllSupportedSymbols_HaveOddParity()
    {
        const string symbols = "0123456789+-/, .⏨↑()×=;[]*‘’≠<>:AБBГДEЖЗИЙKЛMHOПPCТYФXЦЧШЩЫЬЭЮЯDFGIJLNQRSUVWZ";
        var card = PunchCardCodec.Encode(symbols, PunchCardEncoding.Upp);
        for (int i = 0; i < symbols.Length; i++)
            Assert.AreEqual(1, BitOperations.PopCount((uint)ReadUppByte(card, i)) % 2, $"{symbols[i]}");
    }

    [TestMethod]
    public void RequestedSpecialSymbolsUseHistoricalColumnAndGostCodes()
    {
        const string text = "⑩◊¬␣";
        var ibm = PunchCardCodec.Encode(text, PunchCardEncoding.Ibm);
        ushort[] columns = [0x604, 0x422, 0x602, 0x414];
        for (int column = 0; column < columns.Length; column++)
            for (int row = 0; row < 12; row++)
                Assert.AreEqual((columns[column] & (1 << row)) != 0, ibm.IsPunched(row, column));
        var upp = PunchCardCodec.Encode(text, PunchCardEncoding.Upp);
        byte[] bytes = [0x10, 0x57, 0xD3, 0x8F];
        for (int i = 0; i < bytes.Length; i++) Assert.AreEqual(bytes[i], ReadUppByte(upp, i));
    }

    [TestMethod]
    public void CyrillicAndLowercase_AreEncodedWithoutAsciiReplacement()
    {
        // А/В are shared glyphs in UPP; Б is a separate character.
        CollectionAssert.AreEqual(
            PunchCardCodec.Encode("ABБ", PunchCardEncoding.Upp).ToMemoryImage(),
            PunchCardCodec.Encode("авб", PunchCardEncoding.Upp).ToMemoryImage());
        var ibm = PunchCardCodec.Encode("Ю", PunchCardEncoding.Ibm);
        for (int row = 0; row < 12; row++)
            Assert.AreEqual(row is 0 or 1 or 2 or 10, ibm.IsPunched(row, 0));
    }

    [TestMethod]
    public void Capacity_UsesLastColumnAndLastUppByte_RejectsOverflow()
    {
        Assert.IsTrue(PunchCardCodec.Encode(new string('1', 80), PunchCardEncoding.Ibm).IsPunched(3, 79));
        Assert.AreEqual((byte)0x83, ReadUppByte(PunchCardCodec.Encode(new string('3', 120), PunchCardEncoding.Upp), 119));
        Assert.ThrowsExactly<ArgumentException>(() => PunchCardCodec.Encode(new string('A', 81), PunchCardEncoding.Ibm));
        Assert.ThrowsExactly<ArgumentException>(() => PunchCardCodec.Encode(new string('A', 121), PunchCardEncoding.Upp));
    }

    [TestMethod]
    public void UnsupportedCharacters_AreRejectedRatherThanReplaced()
    {
        foreach (var encoding in Enum.GetValues<PunchCardEncoding>())
            foreach (string text in new[] { "?", "A\tB", "A\nB", "🙂" })
                Assert.ThrowsExactly<ArgumentException>(() => PunchCardCodec.Encode(text, encoding));
    }

    [TestMethod]
    public void MemoryImage_IgnoresPaddingAndCopiesInputAndOutput()
    {
        var raw = new byte[144];
        for (int i = 0; i < 144; i += 6) raw[i] = 255;
        raw[1] = 128; raw[5] = 1; raw[7] = 128; raw[143] = 1;
        var card = PunchCardImage.FromMemoryImage(raw);
        raw[1] = 0;
        var copy = card.ToMemoryImage(); copy[1] = 0;
        for (int row = 0; row < 12; row++)
            for (int col = 0; col < 80; col++)
                Assert.AreEqual((row == 0 && col is 0 or 39 or 40) || (row == 11 && col == 79), card.IsPunched(row, col));
        Assert.ThrowsExactly<ArgumentException>(() => PunchCardImage.FromMemoryImage(new byte[120]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => card.IsPunched(12, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => card.IsPunched(0, -1));
    }

    [TestMethod]
    public void Grid_HasCompleteNumberedColumnsAndEncodingSpecificRowLabels()
    {
        var card = PunchCardCodec.Encode("A", PunchCardEncoding.Ibm);
        var lines = PunchCardRenderer.RenderGrid(card, PunchCardEncoding.Ibm).Split('\n');
        Assert.AreEqual("00000000011111111112222222222333333333344444444445555555555666666666677777777778", lines[0].AsSpan(4, 80).ToString());
        Assert.AreEqual(string.Concat(Enumerable.Repeat("1234567890", 8)), lines[1].AsSpan(4, 80).ToString());
        int[] labels = { 12, 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        for (int row = 0; row < 12; row++)
        {
            StringAssert.StartsWith(lines[row + 4], $"{labels[row]:00}{(row == 0 ? "/" : "│")} ");
            var glyphs = lines[row + 4].AsSpan(4, 80);
            Assert.AreEqual(80, glyphs.Length);
            Assert.IsTrue(glyphs.ToArray().All(ch => ch is '□' or '.'));
        }
        var ascii = PunchCardRenderer.RenderGrid(card, PunchCardEncoding.Upp, ascii: true);
        Assert.IsTrue(ascii.All(ch => ch < 128));
        StringAssert.Contains(ascii, "01/ O");
        StringAssert.Contains(ascii, "04| O");
        StringAssert.Contains(ascii, "12| ");
    }

    [TestMethod]
    public void Braille_MapsEveryPhysicalHoleToItsDot()
    {
        int[,] dots = { { 1, 8 }, { 2, 16 }, { 4, 32 }, { 64, 128 } };
        for (int row = 0; row < 12; row++)
            for (int col = 0; col < 80; col++)
            {
                var raw = new byte[144];
                raw[row * 12 + (col < 40 ? 1 + col / 8 : 7 + (col - 40) / 8)] = (byte)(128 >> (col % 8));
                var lines = PunchCardRenderer.RenderBraille(PunchCardImage.FromMemoryImage(raw)).Split('\n');
                for (int block = 0; block < 3; block++)
                    for (int cell = 0; cell < 40; cell++)
                        Assert.AreEqual((char)(0x2800 + (block == row / 4 && cell == col / 2 ? dots[row % 4, col % 2] : 0)), lines[block][cell]);
            }
    }

    [TestMethod]
    public void Puncher_OutputMatchesLegacyBrailleForDenseMemoryImage()
    {
        var raw = Enumerable.Range(0, 144).Select(i => (byte)(i * 71 + 19)).ToArray();
        var expected = new StringBuilder();
        int[] dots = { 1, 8, 2, 16, 4, 32, 64, 128 };
        for (int block = 0; block < 3; block++)
        {
            for (int cell = 0; cell < 40; cell++)
            {
                int value = 0;
                for (int dot = 0; dot < 8; dot++)
                {
                    int row = block * 4 + dot / 2, col = cell * 2 + dot % 2;
                    if ((raw[1 + 12 * row + (col >= 40 ? 1 : 0) + col / 8] & (128 >> (col % 8))) != 0)
                        value |= dots[dot];
                }
                expected.Append((char)(0x2800 + value));
            }
            expected.Append('\n');
        }
        expected.Append('\n');
        string dir = Path.Combine(Path.GetTempPath(), "besm6-card-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var memory = new CoreMemory(4096);
            var pointer = new BytePointer(memory, 0);
            foreach (byte b in raw) pointer.Put(b);
            var puncher = new Puncher(memory, dir);
            try { puncher.Punch(0, 24); }
            finally { puncher.Finish(); }
            Assert.AreEqual(expected.ToString(), File.ReadAllText(Path.Combine(dir, "punch.out")));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static byte ReadUppByte(PunchCardImage card, int index)
    {
        int value = 0;
        for (int bit = 0; bit < 8; bit++)
            value = (value << 1) | (card.IsPunched(index / 10, index % 10 * 8 + bit) ? 1 : 0);
        return (byte)value;
    }
}
