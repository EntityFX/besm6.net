using Besm6.Architecture;

namespace Besm6.BitVisualizer;

public sealed record Besm6InstructionDecodeResult(
    DecodedInstruction Instruction,
    string Mnemonic,
    string Description,
    IReadOnlyList<BitField> Fields,
    CalculationReport Report);

/// <summary>Разбирает одно 24-битное полуслово команды БЭСМ-6.</summary>
public static class Besm6InstructionDecoder
{
    public static Besm6InstructionDecodeResult Decode(BitPattern bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Width != 24)
            throw new ArgumentException("Команда БЭСМ-6 должна содержать ровно 24 бита.", nameof(bits));

        uint raw = (uint)bits.UnsignedValue;
        DecodedInstruction instruction = InstructionCodec.DecodeHalf(raw);
        (string mnemonic, string description) = OpcodeInfoCatalog.Get(instruction.Opcode, instruction.Format);
        bool isLong = instruction.Format == InstructionFormat.Long;
        IReadOnlyList<BitField> fields = isLong
            ?
            [
                new BitField("Индекс-регистр", 23, 20, BitFieldKind.Register),
                new BitField("Формат", 19, 19, BitFieldKind.Format),
                new BitField("Код операции", 18, 15, BitFieldKind.Opcode),
                new BitField("Адрес", 14, 0, BitFieldKind.Address),
            ]
            :
            [
                new BitField("Индекс-регистр", 23, 20, BitFieldKind.Register),
                new BitField("Формат", 19, 19, BitFieldKind.Format),
                new BitField("Расширение адреса", 18, 18, BitFieldKind.Format),
                new BitField("Код операции", 17, 12, BitFieldKind.Opcode),
                new BitField("Адрес", 11, 0, BitFieldKind.Address),
            ];

        var steps = new List<string>
        {
            $"Биты 23–20: (команда >> 20) & 0xF = {instruction.Register}; выбран M{instruction.Register}.",
            $"Форматный бит 19 равен {(isLong ? 1 : 0)}: команда {(isLong ? "длинная" : "короткая")}.",
        };

        if (isLong)
        {
            steps.Add($"Код операции: (команда >> 15) & 0xF, затем историческое выравнивание; получаем {ToOctal((int)instruction.Opcode, 3)}₈ — {mnemonic}.");
            steps.Add($"Адрес: команда & маска 0x7FFF = {ToOctal(instruction.Address, 5)}₈ = {instruction.Address}₁₀.");
        }
        else
        {
            bool extension = (raw & (1u << 18)) != 0;
            steps.Add($"Бит 18 расширения адреса равен {(extension ? 1 : 0)}; старшие три бита адреса заполняются {(extension ? "единицами" : "нулями")}.");
            steps.Add($"Код операции: (команда >> 12) & маска 0x3F = {ToOctal((int)instruction.Opcode, 2)}₈ — {mnemonic}.");
            steps.Add($"Младший адрес: команда & маска 0xFFF; после расширения получаем {ToOctal(instruction.Address, 5)}₈ = {instruction.Address}₁₀.");
        }

        string summary = $"M{instruction.Register} {mnemonic} {ToOctal(instruction.Address, 5)}₈ — {description}";
        var report = new CalculationReport(
            "БЭСМ-6 — декодирование 24-битной команды",
            $"{bits.ToBinaryString()}₂ ({ToOctal((int)raw, 8)}₈)",
            summary,
            steps);

        return new Besm6InstructionDecodeResult(instruction, mnemonic, description, fields, report);
    }

    private static string ToOctal(int value, int width) => Convert.ToString(value, 8).PadLeft(width, '0');
}
