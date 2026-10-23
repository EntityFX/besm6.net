using Besm6.Assembler.Disassembly;

namespace Besm6.AspNetCore.Web.Hosting;

public sealed record InstructionRequest(string Value = "0", int Radix = 8, int Width = 24,
    string? Text = null, string Syntax = "autocode", int Opcode = 0, int Register = 0, int Address = 0);

public static class InstructionEndpoints
{
    public static IResult Convert(InstructionRequest request, bool encode)
    {
        try
        {
            if (request.Value is null || request.Value.Length > 64 || request.Text?.Length > 256) throw new ArgumentException("Instruction input is too long.");
            if (request.Radix is not (2 or 8 or 10 or 16) || request.Width is not (24 or 48)) throw new ArgumentException("Select 24/48 bits and BIN/OCT/DEC/HEX.");
            ulong value = encode ? request.Text is not null ? InstructionEditor.EncodeText(request.Text, request.Syntax)
                : InstructionEditor.Encode(request.Opcode, request.Register, request.Address) : System.Convert.ToUInt64(request.Value.Trim(), request.Radix);
            ulong maximum = request.Width == 24 ? 0xFFFFFFUL : 0xFFFFFFFFFFFFUL;
            if (value > maximum) throw new ArgumentException("Value exceeds the selected bit width.");
            return Results.Ok(new { octal = System.Convert.ToString((long)value, 8).PadLeft(request.Width / 3, '0'),
                hex = value.ToString(request.Width == 24 ? "X6" : "X12"), decimalValue = value.ToString(),
                binary = System.Convert.ToString((long)value, 2).PadLeft(request.Width, '0'),
                halves = request.Width == 48 ? new[] { InstructionEditor.Decode((uint)(value >> 24)), InstructionEditor.Decode((uint)(value & 0xFFFFFF)) }
                    : new[] { InstructionEditor.Decode((uint)value) } });
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { return Results.BadRequest(new { error = ex.Message }); }
    }
}
