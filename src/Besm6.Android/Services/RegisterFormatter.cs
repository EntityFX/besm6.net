using System.Text;
using Besm6.Runtime;

namespace Besm6.Android.Services;

/// <summary>Text presentation for the register dialog, including unavailable-model notes.</summary>
public static class RegisterFormatter
{
    public static string Format(RegisterSnapshot? snapshot)
    {
        if (snapshot is null) return "Снимок регистров ещё не получен.";

        var text = new StringBuilder(4096);
        text.Append("СОСТОЯНИЕ\n");
        text.Append("  память: ").Append(snapshot.MemoryModel)
            .Append("; исполнение: ").Append(snapshot.ExecutionModel).Append('\n');
        text.Append("  профиль: ").Append(snapshot.ProcessorProfile)
            .Append("; команд: ").Append(snapshot.Instructions.ToString("N0")).Append("\n\n");

        text.Append("ПРОЦЕССОР\n");
        Row(text, "K", Address(snapshot.K));
        text.Append("  H   ").Append(snapshot.RightHalf ? "правое полуслово" : "левое полуслово").Append('\n');
        WordRow(text, "A", snapshot.A);
        WordRow(text, "Y", snapshot.Y);
        Row(text, "R", snapshot.R.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        Row(text, "C", Address(snapshot.C));
        text.Append("  ApplyC  ").Append(snapshot.ApplyC ? "да" : "нет").Append("\n\n");

        text.Append("ИНДЕКСНЫЕ РЕГИСТРЫ (M0–M15)\n");
        for (int index = 0; index < 16; index++)
        {
            text.Append("  M")
                .Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(2))
                .Append("  ")
                .Append(index < snapshot.M.Length ? Address(snapshot.M[index]) : "—");
            if (index % 2 == 1) text.Append('\n');
        }
        if (snapshot.M.Length > 0 && snapshot.M.Length % 2 == 1) text.Append('\n');
        text.Append('\n');

        text.Append("БУФЕРНЫЕ РЕГИСТРЫ\n");
        AppendGroup(text, snapshot.Buffers, "БРЧ", "БРЧ — недоступны в текущей модели (нужен аппаратный supervisor).");
        AppendGroup(text, snapshot.Buffers, "БРС", "БРС — недоступны в текущей модели памяти.");
        AppendGroup(text, snapshot.Buffers, "БРЗ", "БРЗ — недоступны в текущей модели памяти.");

        return text.ToString();
    }

    public static string FormatMemory(MemorySnapshot? snapshot,
        NumberBase numberBase = NumberBase.Octal)
    {
        if (snapshot is null) return "Снимок памяти ещё не получен.";
        var text = new StringBuilder(snapshot.Words.Length * 64);
        text.Append("ПАМЯТЬ, адрес ").Append(Address(numberBase, (uint)snapshot.Address)).Append('\n');
        foreach (var word in snapshot.Words)
        {
            text.Append(Address(numberBase, (uint)word.Address)).Append("  ")
                .Append(Word(numberBase, word.Value)).Append("  ")
                .Append(word.Disassembly).Append('\n');
        }
        return text.ToString();
    }

    private static string Address(NumberBase numberBase, uint value) => numberBase switch
    {
        NumberBase.Decimal => value.ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
        NumberBase.Hexadecimal => value.ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, 8).PadLeft(5, '0'),
    };

    private static string Word(NumberBase numberBase, ulong value) => numberBase switch
    {
        NumberBase.Decimal => value.ToString("D15", System.Globalization.CultureInfo.InvariantCulture),
        NumberBase.Hexadecimal => value.ToString("X12", System.Globalization.CultureInfo.InvariantCulture),
        _ => Word(value),
    };

    private static void AppendGroup(StringBuilder text, BufferRegisterView[] buffers,
        string marker, string unavailableMessage)
    {
        var group = buffers.Where(buffer => buffer.Name.Contains(marker, StringComparison.Ordinal)).ToArray();
        text.Append(marker).Append('\n');
        if (group.Length == 0)
        {
            text.Append("  ").Append(unavailableMessage).Append('\n');
            return;
        }

        foreach (var buffer in group.OrderBy(buffer => buffer.Name, StringComparer.Ordinal)
                     .ThenBy(buffer => buffer.Slot))
        {
            text.Append("  ").Append(buffer.Name).Append('[').Append(buffer.Slot).Append("]  ")
                .Append(Word(buffer.Value));
            if (buffer.Raw50 is { } raw)
                text.Append("  КР49/50=").Append(((raw >> 48) & 3).ToString("00",
                    System.Globalization.CultureInfo.InvariantCulture));
            if (buffer.Address is { } address)
                text.Append("  адрес=").Append(Address(address));
            if (buffer.Active is { } active) text.Append(active ? "  active" : "  —");
            if (buffer.Ready is { } ready) text.Append(ready ? "  ready" : "  не готов");
            if (buffer.PendingWrite is true) text.Append("  ожидает записи");
            text.Append('\n');
        }
    }

    private static void Row(StringBuilder text, string name, string value) =>
        text.Append("  ").Append(name.PadRight(3)).Append(' ').Append(value).Append('\n');

    private static void WordRow(StringBuilder text, string name, ulong value) =>
        text.Append("  ").Append(name.PadRight(3)).Append(' ').Append(Word(value))
            .Append("  0x").Append(value.ToString("X12", System.Globalization.CultureInfo.InvariantCulture))
            .Append('\n');

    private static string Address(uint value) =>
        Convert.ToString(value, 8).PadLeft(5, '0');

    private static string Address(ulong value) =>
        Convert.ToString((long)value, 8).PadLeft(5, '0');

    private static string Word(ulong value)
    {
        string octal = Convert.ToString((long)value, 8).PadLeft(16, '0');
        return $"{octal[..4]} {octal[4..8]} {octal[8..12]} {octal[12..16]}";
    }
}
