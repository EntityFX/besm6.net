namespace Besm6.EduCpu;

/// <summary>Восьмеричное форматирование чисел (адресов, кодов, полей команд).</summary>
public static class Oct
{
    private const string Digits = "01234567";

    /// <summary>Восьмеричная запись без ведущих нулей.</summary>
    public static string Of(ulong value)
    {
        if (value == 0)
        {
            return "0";
        }

        char[] buf = new char[26];
        int n = 0;
        while (value > 0)
        {
            buf[n++] = Digits[(int)(value & 7)];
            value >>= 3;
        }

        for (int i = 0; i < n / 2; ++i)
        {
            (buf[i], buf[n - 1 - i]) = (buf[n - 1 - i], buf[i]);
        }

        return new string(buf, 0, n);
    }

    /// <summary>Восьмеричная запись, дополненная нулями до <paramref name="width"/> цифр.</summary>
    public static string Pad(ulong value, int width)
    {
        string s = Of(value);
        return s.Length >= width ? s : new string('0', width - s.Length) + s;
    }
}