using System;
using System.Text;

namespace Besm6.Runtime.Encoding
{
    internal static class Koi7Codec
    {
        /// <summary>KOI-7 byte to Unicode char.</summary>
        public static char Koi7ToUnicode(byte ch)
        {
            var table = EncodingTables.Koi7ToUnicodeTable;
            if (ch >= table.Length) return (char)ch;
            return (char)table[ch];
        }

        /// <summary>UTF-8 string to KOI-7 (unmapped chars dropped).</summary>
        public static string Utf8ToKoi7(string input, int maxLen = int.MaxValue)
        {
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (sb.Length >= maxLen) break;
                byte ch = UnicodeToKoi7(c);
                if (ch == 0) continue;
                sb.Append((char)ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Unicode => KOI-7 (port unicode_to_koi7 from dubna/encoding.cpp).
        /// Returns 0 if unmapped.
        /// </summary>
        internal static byte UnicodeToKoi7(uint val)
{
            switch (val >> 8)
            {
                case 0x00:
                    // Основная таблица таб0: ASCII и специальные.
                    if (val < 0x80) return EncodingTables.Tab0[val];
                    switch (val)
                    {
                        case 0x0a: return 0x0a;
                        case 0x0e: return 0x0e;
                        case 0x0f: return 0x0f;
                        case 0x1f: return 0x1f;
                        case 0x19: return 0x19;
                    }
                    return 0;
                case 0x04:
                    // Кириллица.
                    return CyrillicToKoi7((byte)val);
                case 0x20:
                    switch (val)
                    {
                        case 0x2015: return 0x25; // ―
                        case 0x2018: return 0x20; // ‘
                        case 0x2019: return 0x33; // ’
                        case 0x2228: return 0x0a; // ∨
                        case 0x2032: return 0x32; // ′
                        case 0x203e: return 0x5c; // ‾
                    }
                    return 0;
                case 0x22:
                    switch (val)
                    {
                        case 0x2227: return 0x27; // ∧
                        case 0x2228: return 0x36; // ∨
                        case 0x2260: return 0x30; // ≠
                        case 0x2261: return 0x35; // ≡
                        case 0x2a7d: return 0x16; // ⩽
                        case 0x2a7e: return 0x17; // ⩾
                        case 0x2283: return 0x34; // ⊃
                    }
                    return 0;
                case 0x21:
                    if (val == 0x2191) return 0x26; // ↑
                    return 0;
                case 0x23:
                    if (val == 0x23e8) return 0x27; // ⏨
                    return 0;
                default:
                    break;
            }
            if (val == 0x00b0) return 0x31; // °
            if (val == 0x00d7) return 0x06; // ×
            if (val == 0x00f7) return 0x3a; // ÷
            if (val == 0x2260) return 0x30; // ≠
            if (val == 0x25c7) return 0x37; // ◆
            return 0;
        }

        private static byte CyrillicToKoi7(byte b)
{
            switch (b)
            {
                case 0x01: return (byte)'E'; // Ё
                case 0x04: return (byte)'E'; // Є
                case 0x06: return (byte)'I'; // І
                case 0x07: return (byte)'I'; // Ї
                case 0x10: return (byte)'A'; // А
                case 0x11: return 0x62;      // Б
                case 0x12: return (byte)'B'; // В
                case 0x13: return 0x67;      // Г
                case 0x14: return 0x64;      // Д
                case 0x15: return (byte)'E'; // Е
                case 0x16: return 0x76;      // Ж
                case 0x17: return 0x7a;      // З
                case 0x18: return 0x69;      // И
                case 0x19: return 0x6a;      // Й
                case 0x1a: return (byte)'K'; // К
                case 0x1b: return 0x6c;      // Л
                case 0x1c: return (byte)'M'; // М
                case 0x1d: return (byte)'H'; // Н
                case 0x1e: return (byte)'O'; // О
                case 0x1f: return 0x70;      // П
                case 0x20: return (byte)'P'; // Р
                case 0x21: return (byte)'C'; // С
                case 0x22: return (byte)'T'; // Т
                case 0x23: return (byte)'Y'; // У
                case 0x24: return 0x66;      // Ф
                case 0x25: return (byte)'X'; // Х
                case 0x26: return 0x63;      // Ц
                case 0x27: return 0x7e;      // Ч
                case 0x28: return 0x7b;      // Ш
                case 0x29: return 0x7d;      // Щ
                case 0x2a: return 0x05;      // Ъ
                case 0x2b: return 0x79;      // Ы
                case 0x2c: return 0x78;      // Ь
                case 0x2d: return 0x7c;      // Э
                case 0x2e: return 0x60;      // Ю
                case 0x2f: return 0x71;      // Я
                case 0x30: return (byte)'A'; // а
                case 0x31: return 0x62;      // б
                case 0x32: return (byte)'B'; // в
                case 0x33: return 0x67;      // г
                case 0x34: return 0x64;      // д
                case 0x35: return (byte)'E'; // е
                case 0x36: return 0x76;      // ж
                case 0x37: return 0x7a;      // з
                case 0x38: return 0x69;      // и
                case 0x39: return 0x6a;      // й
                case 0x3a: return (byte)'K'; // к
                case 0x3b: return 0x6c;      // л
                case 0x3c: return (byte)'M'; // м
                case 0x3d: return (byte)'H'; // н
                case 0x3e: return (byte)'O'; // о
                case 0x3f: return 0x70;      // п
                case 0x40: return (byte)'P'; // р
                case 0x41: return (byte)'C'; // с
                case 0x42: return (byte)'T'; // т
                case 0x43: return (byte)'Y'; // у
                case 0x44: return 0x66;      // ф
                case 0x45: return (byte)'X'; // х
                case 0x46: return 0x63;      // ц
                case 0x47: return 0x7e;      // ч
                case 0x48: return 0x7b;      // ш
                case 0x49: return 0x7d;      // щ
                case 0x4a: return 0x05;      // ъ
                case 0x4b: return 0x79;      // ы
                case 0x4c: return 0x78;      // ь
                case 0x4d: return 0x7c;      // э
                case 0x4e: return 0x60;      // ю
                case 0x4f: return 0x71;      // я
                case 0x51: return (byte)'E'; // ё
                case 0x54: return (byte)'E'; // є
                case 0x56: return (byte)'I'; // і
                case 0x57: return (byte)'I'; // ї
                default: return 0;
            }
        }
    }
}
