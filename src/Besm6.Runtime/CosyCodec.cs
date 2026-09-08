using System;
using Besm6.Runtime.Encoding;
using System.Collections.Generic;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// Кодировка COSY и таблицы ГОСТ-10859 / KOI-7 / TEXT.
    /// Порт dubna/cosy.cpp + dubna/encoding.cpp + dubna/gost10859.h.
    /// </summary>
    public static class CosyCodec
    {
        //
        // COSY специальные карты.
        // Символ пробела в них НЕ пакован (иначе получить регулярным encode_cosy нельзя).
        //
        // '*READ OLD' + (0xCA) + '\n\n'
        public static readonly byte[] CosyReadOld = EncodingTables.CosyReadOld;
        // '*END' + (0x81) + 'FILE' + (0xCA) + '\n\n'
        public static readonly byte[] CosyEndFileRegular = EncodingTables.CosyEndFileRegular;
        // '*END FILE ' + (0xC9) + '\n'
        public static readonly byte[] CosyEndFileLegacy = EncodingTables.CosyEndFileLegacy;

        // ISO упаковка карты '*read old' и '*end file' (пробел не пакован).
        private static byte[] EncBytes(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        /// <summary>
        /// Кодирование строки в формат COSY (порт encode_cosy из dubna/cosy.cpp).
        /// Входная строка уже должна быть в KOI-7. Выход — последовательность байт,
        /// кратно 6 (одно слово = 6 байт).
        /// </summary>
        public static byte[] EncodeCosy(string koi7Line)
        {
            // Расширить до 83 символов и добавить перевод строки.
            var line = new List<byte>();
            int n = koi7Line.Length;
            int pad = 83 - n;
            for (int i = 0; i < n; i++) line.Add((byte)koi7Line[i]);
            for (int i = 0; i < pad; i++) line.Add((byte)' ');
            line.Add((byte)'\n');

            // Пакование пробелов.
            int numSpaces = 0;
            int firstSpaceIndex = 0;
            int idx = 0;
            while (idx < line.Count)
            {
                if (line[idx] == (byte)' ')
                {
                    if (numSpaces == 0) firstSpaceIndex = idx;
                    numSpaces++;
                    idx++;
                }
                else
                {
                    if (numSpaces > 0)
                    {
                        line[firstSpaceIndex] = (byte)(0x80 + numSpaces);
                        if (numSpaces > 1)
                        {
                            line.RemoveRange(firstSpaceIndex + 1, numSpaces - 1);
                            idx = firstSpaceIndex + 1;
                        }
                        numSpaces = 0;
                        firstSpaceIndex = 0;
                    }
                    idx++;
                }
            }

            // Выравнивание до 6 байт.
            int rem = line.Count % 6;
            switch (rem)
            {
                case 1: line.AddRange(New("    \n")); break;
                case 2: line.AddRange(New("   \n")); break;
                case 3: line.AddRange(New("  \n")); break;
                case 4: line.AddRange(New(" \n")); break;
                case 5: line.AddRange(New("\n")); break;
            }
            return line.ToArray();
        }

        private static byte[] New(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        /// <summary>
        /// Проверка карты '*read old'.
        /// </summary>
        public static bool IsReadOldCosy(byte[] line)
        {
            return ByteEquals(line, CosyReadOld);
        }

        /// <summary>
        /// Проверка карты '*end file' (два варианта представления).
        /// </summary>
        public static bool IsEndFileCosy(byte[] line)
        {
            return ByteEquals(line, CosyEndFileRegular) || ByteEquals(line, CosyEndFileLegacy);
        }

        private static bool ByteEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Декодирование строки из формата COSY. Возвращает null при ошибке.
        /// </summary>
        public static string? DecodeCosy(byte[] line)
        {
            var result = new StringBuilder();
            foreach (byte ch in line)
            {
                if (ch == (byte)'\n') break;
                if (ch >= 0x81 && ch <= 0xD3)
                {
                    int cnt = ch - 0x80;
                    while (cnt-- > 0) result.Append(' ');
                    continue;
                }
                if (ch < 0x20 || ch > 0x7F) return null;
                result.Append(Koi7ToUnicode(ch));
            }
            string s = result.ToString().TrimEnd(' ');
            return s;
        }

        /// <summary>
        /// Конвертация строки UTF-8 в KOI-7 (порт utf8_to_koi7 из dubna/encoding.cpp).
        /// </summary>
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

        //
        // KOI-7 -> Unicode (таблица koi7_to_unicode из dubna/encoding.cpp).
        //
        private static readonly ushort[] Koi7ToUnicodeTable = EncodingTables.Koi7ToUnicodeTable;

        /// <summary>
        /// KOI-7 -> Unicode символ.
        /// </summary>
        public static char Koi7ToUnicode(byte ch)
        {
            if (ch >= Koi7ToUnicodeTable.Length) return (char)ch;
            return (char)Koi7ToUnicodeTable[ch];
        }

        //
        // GOST-10859 (latin default) -> Unicode, таблица gost_to_unicode_lat.
        // Используется для TEXT-кодировки имён лент и вывода ГОСТ.
        //
        // ГОСТ-10859 (latin) -> Unicode, таблица gost_to_unicode_lat из dubna/encoding.cpp.
        //
        // static const unsigned short gost_to_unicode_lat[256] = {
        //     000-007: 0x30,0x31,0x32,0x33,0x34,0x35,0x36,0x37,
        //     010-017: 0x38,0x39,0x2b,0x2d,0x2f,0x2c,0x2e,0x20,
        //     020-027: 0x23e8,0x2191,0x28,0x29,0xd7,0x3d,0x3b,0x5b,
        //     030-037: 0x5d,0x2a,0x2018,0x2019,0x2260,0x3c,0x3e,0x3a,
        //     040-047: 0x41,0x0411,0x42,0x0413,0x0414,0x45,0x0416,0x0417,
        //     050-057: 0x0418,0x0419,0x4b,0x041b,0x4d,0x48,0x4f,0x041f,
        //     060-067: 0x50,0x43,0x54,0x59,0x0424,0x58,0x0426,0x0427,
        //     070-077: 0x0428,0x0429,0x042b,0x042c,0x042d,0x042e,0x042f,0x44,
        //     100-107: 0x46,0x47,0x49,0x4a,0x4c,0x4e,0x51,0x52,
        //     110-117: 0x53,0x55,0x56,0x57,0x5a,0x203e,0x2a7d,0x2a7e,
        //     120-127: 0x2228,0x2227,0x2283,0xac,0xf7,0x2261,0x25,0x25c7,
        //     130-137: 0x7c,0x2015,0x5f,0x21,0x22,0x042a,0xb0,0x2032,
        // };
        private static readonly ushort[] GostToUnicodeLat = EncodingTables.GostToUnicodeLat;

        /// <summary>
        /// GOST-10859 -> Unicode символ.
        /// </summary>
        public static char GostToUnicode(byte ch)
        {
            if (ch >= GostToUnicodeLat.Length) return (char)ch;
            return (char)GostToUnicodeLat[ch];
        }

        //
        // TEXT -> GOST, таблица text_to_gost из dubna/encoding.cpp.
        // 6-битная TEXT-кодировка монитора Dubna.
        //
        private static readonly byte[] TextToGost = EncodingTables.TextToGost;

        /// <summary>
        /// TEXT-символ (6 бит) -> Unicode.
        /// </summary>
        public static char TextToUnicode(byte ch)
        {
            if (ch >= TextToGost.Length) return '?';
            // В C# нет octal-литералов, поэтому пишем hex-маску 6 бит.
            return GostToUnicode(TextToGost[ch & 0x3F]);
        }

        //
        // Основная таблица tab0 (порт dubna/encoding.cpp): ASCII + специальные символы.
        // Строчные латинские буквы преобразуются в прописные (как в KOI-7).
        //
        private static readonly byte[] Tab0 = EncodingTables.Tab0;

        //
        // Unicode -> KOI-7 (порт unicode_to_koi7). Реализована основная часть:
        // таблица для ASCII и switch для кириллицы. Возвращает 0 при отсутствии символа.
        //
        private static byte UnicodeToKoi7(uint val)
        {
            switch (val >> 8)
            {
                case 0x00:
                    // Основная таблица таб0: ASCII и специальные.
                    if (val < 0x80) return Tab0[val];
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

        /// <summary>
        /// Упаковка 6 байт в 48-битное слово (как в drum_write_cosy).
        /// </summary>
        public static long BytesToWord(byte[] data, int offset)
        {
            long w = 0;
            for (int i = 0; i < 6; i++)
                w = (w << 8) | data[offset + i];
            return w & 0xFFFFFFFFFFFFL;
        }

        /// <summary>
        /// Таблица ALLTOISO: 128 записей по 48 бит (порт all_to_iso из dubna/encoding.cpp).
        /// Используется экстракодом *65 для адресов 06000..06000+127.
        /// </summary>
        public static readonly long[] AllToIso = EncodingTables.AllToIso;
    }
}