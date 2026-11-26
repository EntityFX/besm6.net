namespace Besm6.Android.Services;

public enum NumberBase
{
    Octal,
    Decimal,
    Hexadecimal,
}

/// <summary>Presentation settings shared by the editor and output tabs.</summary>
public sealed record DisplaySettings(
    string OutputFontFamily,
    double OutputFontSize,
    double EditorFontSize,
    int MemoryWords,
    NumberBase NumberBase)
{
    public static DisplaySettings Default { get; } = new(
        "monospace", 10.5, 11.0, 32, NumberBase.Octal);

    public bool TryValidate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(OutputFontFamily))
        {
            error = "Выберите шрифт вывода.";
            return false;
        }
        if (OutputFontSize is < 8 or > 24 || EditorFontSize is < 8 or > 24)
        {
            error = "Размер шрифта должен быть от 8 до 24.";
            return false;
        }
        if (MemoryWords is < 1 or > 128)
        {
            error = "Количество слов памяти должно быть от 1 до 128.";
            return false;
        }
        if (!Enum.IsDefined(NumberBase))
        {
            error = "Неизвестная система счисления.";
            return false;
        }
        error = null;
        return true;
    }
}
