using System.Text.RegularExpressions;
using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Assembler.Parsing;

namespace Besm6.Assembler.Disassembly;

public sealed record InstructionOperation(int Opcode, string Madlen, string Bemsh, string Autocode, bool Long);
public sealed record InstructionView(uint Value, int Register, int Opcode, bool Long, bool Extension, int Address,
    string Octal, string Hex, string Binary, string Madlen, string Bemsh, string Autocode);

/// <summary>Pure instruction presentation. Uses the processor's codec; never executes a command.</summary>
public static class InstructionEditor
{
    public static IReadOnlyList<InstructionOperation> Operations { get; } = Enumerable.Range(0, 80).Select(index =>
    {
        bool wide = index >= 64; int op = wide ? 0x80 + (index - 64) * 8 : index;
        string madlen = wide ? OpcodeTable.LongMadlen[index - 64] : OpcodeTable.ShortMadlen[index];
        string bemsh = wide ? OpcodeTable.LongBemsh[index - 64] : OpcodeTable.ShortBemsh[index];
        return new InstructionOperation(op, madlen, bemsh, bemsh.ToUpperInvariant(), wide);
    }).ToArray();

    public static InstructionView Decode(uint value)
    {
        if (value > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(value), "An instruction contains 24 bits.");
        var decoded = InstructionCodec.DecodeHalf(value);
        var operation = Operations.Single(op => op.Opcode == (int)decoded.Opcode);
        string address = Convert.ToString(decoded.Address, 8).PadLeft(5, '0');
        return new(value, decoded.Register, (int)decoded.Opcode, decoded.Format == InstructionFormat.Long,
            decoded.Format == InstructionFormat.Short && (value & 0x40000) != 0, decoded.Address,
            Convert.ToString(value, 8).PadLeft(8, '0'), value.ToString("X6"), Convert.ToString(value, 2).PadLeft(24, '0'),
            $",{operation.Madlen},{decoded.Register} {address},", $"{operation.Bemsh} {decoded.Register},{address}",
            $"{operation.Autocode} M{decoded.Register} {address}");
    }

    public static uint Encode(int opcode, int register, int address)
    {
        if (register is < 0 or > 15 || address is < -32768 or > 32767 || !Operations.Any(op => op.Opcode == opcode))
            throw new ArgumentException("Invalid opcode, register (0–15), or 15-bit address.");
        return InstructionCodec.EncodeHalf(new((byte)register, (Opcode)opcode, (ushort)(address & 0x7FFF),
            opcode >= 0x80 ? InstructionFormat.Long : InstructionFormat.Short));
    }

    public static uint EncodeText(string text, string syntax)
    {
        string pattern = syntax switch
        {
            "autocode" => @"^\s*(?<op>\S+)\s+M(?<r>\d+)\s+(?<a>-?[0-7]+)\s*$",
            "bemsh" => @"^\s*(?<op>\S+)\s+(?<r>\d+)\s*,\s*(?<a>-?[0-7]+)\s*$",
            "madlen" => @"^\s*,(?<op>[^,]+),(?<r>\d+)\s+(?<a>-?[0-7]+),\s*$",
            _ => throw new ArgumentException("Select autocode, bemsh or madlen.")
        };
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new ArgumentException("Use MNEMONIC M3 00123 / mnemonic 3,00123 / ,mnemonic,3 00123,. Addresses are octal; register numbers are decimal.");
        string name = match.Groups["op"].Value;
        var operation = Operations.FirstOrDefault(op => string.Equals(syntax == "madlen" ? op.Madlen : op.Bemsh, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown instruction mnemonic.");
        string rawAddress = match.Groups["a"].Value;
        int address = Convert.ToInt32(rawAddress.TrimStart('-'), 8) * (rawAddress.StartsWith('-') ? -1 : 1);
        return Encode(operation.Opcode, int.Parse(match.Groups["r"].Value), address);
    }
}
