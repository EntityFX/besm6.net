using Besm6.Core;

public static class Word48Extensions
{
    public static Word48 FromOctal(string oct)
    {
        ulong val = 0;
        for (int i = 0; i < oct.Length; i++)
        {
            val = (val << 3) | (uint)(oct[i] - '0');
        }
        return new Word48(val);
    }

    public static string ToOctal(this Word48 word)
    {
        ulong val = word.Value;
        char[] digits = new char[16];
        for (int i = 15; i >= 0; i--)
        {
            digits[i] = (char)((val & 7) + '0');
            val >>= 3;
        }
        return new string(digits);
    }
}