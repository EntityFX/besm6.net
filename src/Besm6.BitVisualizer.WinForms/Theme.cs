using Besm6.Architecture.Visualization;

namespace Besm6.BitVisualizer.WinForms;

/// <summary>Палитра семантических категорий битов с читаемым контрастом текста.</summary>
public static class Theme
{
    /// <summary>Фон ячейки битовой сетки для категории поля.</summary>
    public static Color ColorFor(BitFieldKind kind) => kind switch
    {
        BitFieldKind.Sign => Color.FromArgb(0xFA, 0xDE, 0xDE),
        BitFieldKind.Exponent or BitFieldKind.Format => Color.FromArgb(0xFB, 0xF0, 0xC8),
        BitFieldKind.Fraction => Color.FromArgb(0xD9, 0xEC, 0xFB),
        BitFieldKind.Opcode => Color.FromArgb(0xEA, 0xDF, 0xFB),
        BitFieldKind.Register => Color.FromArgb(0xD7, 0xF5, 0xDF),
        _ => Color.FromArgb(0xE8, 0xEB, 0xF2),
    };

    /// <summary>Цвет подписи для категории поля.</summary>
    public static Color ForegroundFor(BitFieldKind kind) => kind switch
    {
        BitFieldKind.Sign => Color.FromArgb(0x8B, 0x1A, 0x1A),
        BitFieldKind.Exponent or BitFieldKind.Format => Color.FromArgb(0x7A, 0x5A, 0x00),
        BitFieldKind.Fraction => Color.FromArgb(0x0B, 0x3D, 0x6E),
        BitFieldKind.Opcode => Color.FromArgb(0x4B, 0x2E, 0x83),
        BitFieldKind.Register => Color.FromArgb(0x1E, 0x6B, 0x2E),
        _ => Color.FromArgb(0x33, 0x3A, 0x47),
    };
}