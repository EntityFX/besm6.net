using System.Numerics;

namespace Besm6.BitVisualizer;

/// <summary>Результат перевода целого числа между системами счисления.</summary>
public sealed record RadixConversionResult(
    BigInteger Value,
    string Output,
    CalculationReport Report);

/// <summary>Переводит целые числа между основаниями 2–36 с пошаговым объяснением.</summary>
public static class RadixConverter
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static RadixConversionResult Convert(string input, int sourceBase, int targetBase)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Введите число: строка не может быть пустой.");
        ValidateBase(sourceBase, nameof(sourceBase));
        ValidateBase(targetBase, nameof(targetBase));

        string trimmed = input.Trim();
        var steps = new List<string>();
        bool negative = trimmed.StartsWith('-');
        string magnitude = negative ? trimmed[1..] : trimmed;
        if (magnitude.Length == 0)
            throw new FormatException("После знака «-» должна идти хотя бы одна цифра.");

        if (negative)
            steps.Add("Знак «-» запоминается отдельно и восстанавливается над готовым результатом.");

        if (magnitude.Length == 1 && magnitude[0] == '0')
        {
            steps.Add("Единственная цифра числа равна 0, поэтому его значение — ноль.");
            steps.Add("Ноль записывается одинаково в любой системе счисления: 0.");
            return new RadixConversionResult(
                BigInteger.Zero,
                "0",
                BuildReport(trimmed, sourceBase, targetBase, "0", steps));
        }

        BigInteger value = BigInteger.Zero;
        for (int index = 0; index < magnitude.Length; index++)
        {
            char symbol = magnitude[index];
            int digit = DigitValue(symbol);
            if (digit >= sourceBase)
                throw new FormatException(
                    $"Цифра «{symbol}» (значение {digit}) не существует в системе счисления с основанием {sourceBase}.");

            BigInteger next = value * sourceBase + digit;
            steps.Add($"Разряд {index}: значение = {value} × {sourceBase} + {digit} = {next}.");
            value = next;
        }

        BigInteger remaining = value;
        var outputDigits = new List<char>();
        while (remaining > BigInteger.Zero)
        {
            BigInteger dividend = remaining;
            remaining = BigInteger.DivRem(remaining, targetBase, out BigInteger remainder);
            char outputDigit = Alphabet[(int)remainder];
            outputDigits.Add(outputDigit);
            steps.Add($"Деление {dividend} на {targetBase}: частное {remaining}, остаток {remainder} → цифра «{outputDigit}».");
        }

        outputDigits.Reverse();
        string output = negative
            ? '-' + new string(outputDigits.ToArray())
            : new string(outputDigits.ToArray());

        BigInteger resultValue = negative ? -value : value;
        return new RadixConversionResult(
            resultValue,
            output,
            BuildReport(trimmed, sourceBase, targetBase, output, steps));
    }

    private static CalculationReport BuildReport(string trimmed, int sourceBase, int targetBase, string output, List<string> steps)
    {
        return new CalculationReport(
            $"Системы счисления — перевод из основания {sourceBase} в {targetBase}",
            $@"«{trimmed}» (основание {sourceBase})",
            output,
            steps);
    }

    private static void ValidateBase(int radix, string name)
    {
        if (radix is < 2 or > 36)
            throw new FormatException($"Основание {name} должно быть целым числом от 2 до 36 (получено {radix}).");
    }

    private static int DigitValue(char symbol)
    {
        if (symbol is >= '0' and <= '9')
            return symbol - '0';
        if (symbol is >= 'a' and <= 'z')
            return symbol - 'a' + 10;
        if (symbol is >= 'A' and <= 'Z')
            return symbol - 'A' + 10;
        throw new FormatException($"Символ «{symbol}» не является цифрой числа.");
    }
}