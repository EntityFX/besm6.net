using System;
using System.Collections.Generic;
using System.Text;

namespace Besm6.Loader
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
        public static readonly byte[] CosyReadOld = { 0x2A, 0x52, 0x45, 0x41, 0x44, 0x20, 0x4F, 0x4C, 0x44, 0xCA, 0x0A, 0x0A };
        // '*END' + (0x81) + 'FILE' + (0xCA) + '\n\n'
        public static readonly byte[] CosyEndFileRegular = { 0x2A, 0x45, 0x4E, 0x44, 0x81, 0x46, 0x49, 0x4C, 0x45, 0xCA, 0x0A, 0x0A };
        // '*END FILE ' + (0xC9) + '\n'
        public static readonly byte[] CosyEndFileLegacy = { 0x2A, 0x45, 0x4E, 0x44, 0x20, 0x46, 0x49, 0x4C, 0x45, 0x20, 0xC9, 0x0A };

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
        private static readonly ushort[] Koi7ToUnicodeTable =
        {
            0x0000,0x0001,0x0002,0x0003,0x0004,0x042a,0x0006,0x00d7, // Ъ×
            0x0008,0x0009,0x000a,0x000b,0x000c,0x000d,0x2a7d,0x2a7e, //   ⩽⩾
            0x2018,0x0011,0x0012,0x0013,0x0014,0x2015,0x2191,0x23e8, // ‘    ―↑⏨
            0x2260,0x00b0,0x00f7,0x2019,0x2283,0x2261,0x2228,0x00ac, // ≠°÷’⊃≡∨¬
            0x0020,0x0021,0x0022,0x0023,0x0024,0x0025,0x0026,0x0027, //  !"#$%&'
            0x0028,0x0029,0x002a,0x002b,0x002c,0x002d,0x002e,0x002f, // ()*+,-./
            0x0030,0x0031,0x0032,0x0033,0x0034,0x0035,0x0036,0x0037, // 01234567
            0x0038,0x0039,0x003a,0x003b,0x003c,0x003d,0x003e,0x003f, // 89:;<=>?
            0x0040,0x0041,0x0042,0x0043,0x0044,0x0045,0x0046,0x0047, // @ABCDEFG
            0x0048,0x0049,0x004a,0x004b,0x004c,0x004d,0x004e,0x004f, // HIJKLMNO
            0x0050,0x0051,0x0052,0x0053,0x0054,0x0055,0x0056,0x0057, // PQRSTUVW
            0x0058,0x0059,0x005a,0x005b,0x203e,0x005d,0x007c,0x005f, // XYZ[‾]|_
            0x042e,0x0410,0x0411,0x0426,0x0414,0x0415,0x0424,0x0413, // ЮAБЦДEФГ
            0x0425,0x0418,0x0419,0x041a,0x041b,0x041c,0x041d,0x041e, // XИЙKЛMHO
            0x041f,0x042f,0x0420,0x0421,0x0422,0x0423,0x0416,0x0412, // ПЯPCTYЖB
            0x042c,0x042b,0x0417,0x0428,0x042d,0x0429,0x0427,0x007f, // ЬЫЗШЭЩЧ
        };

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
        // Полная таблица 256 элементов. Элементы, не указанные в C++ оригинале, равны 0.
        //
        // C++ оригинал:
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
        private static readonly ushort[] GostToUnicodeLat = BuildGostToUnicodeLatTable();

        private static ushort[] BuildGostToUnicodeLatTable()
        {
            var table = new ushort[256];
            
            // 000-007
            table[0x00] = 0x0030; table[0x01] = 0x0031; table[0x02] = 0x0032; table[0x03] = 0x0033;
            table[0x04] = 0x0034; table[0x05] = 0x0035; table[0x06] = 0x0036; table[0x07] = 0x0037;
            // 010-017
            table[0x08] = 0x0038; table[0x09] = 0x0039; table[0x0A] = 0x002b; table[0x0B] = 0x002d;
            table[0x0C] = 0x002f; table[0x0D] = 0x002c; table[0x0E] = 0x002e; table[0x0F] = 0x0020;
            // 020-027
            table[0x10] = 0x23e8; table[0x11] = 0x2191; table[0x12] = 0x0028; table[0x13] = 0x0029;
            table[0x14] = 0x00d7; table[0x15] = 0x003d; table[0x16] = 0x003b; table[0x17] = 0x005b;
            // 030-037
            table[0x18] = 0x005d; table[0x19] = 0x002a; table[0x1A] = 0x2018; table[0x1B] = 0x2019;
            table[0x1C] = 0x2260; table[0x1D] = 0x003c; table[0x1E] = 0x003e; table[0x1F] = 0x003a;
            // 040-047
            table[0x20] = 0x0041; table[0x21] = 0x0411; table[0x22] = 0x0042; table[0x23] = 0x0413;
            table[0x24] = 0x0414; table[0x25] = 0x0045; table[0x26] = 0x0416; table[0x27] = 0x0417;
            // 050-057
            table[0x28] = 0x0418; table[0x29] = 0x0419; table[0x2A] = 0x004b; table[0x2B] = 0x041b;
            table[0x2C] = 0x004d; table[0x2D] = 0x0048; table[0x2E] = 0x004f; table[0x2F] = 0x041f;
            // 060-067
            table[0x30] = 0x0050; table[0x31] = 0x0043; table[0x32] = 0x0054; table[0x33] = 0x0059;
            table[0x34] = 0x0424; table[0x35] = 0x0058; table[0x36] = 0x0426; table[0x37] = 0x0427;
            // 070-077
            table[0x38] = 0x0428; table[0x39] = 0x0429; table[0x3A] = 0x042b; table[0x3B] = 0x042c;
            table[0x3C] = 0x042d; table[0x3D] = 0x042e; table[0x3E] = 0x042f; table[0x3F] = 0x0044;
            // 100-107
            table[0x40] = 0x0046; table[0x41] = 0x0047; table[0x42] = 0x0049; table[0x43] = 0x004a;
            table[0x44] = 0x004c; table[0x45] = 0x004e; table[0x46] = 0x0051; table[0x47] = 0x0052;
            // 110-117
            table[0x48] = 0x0053; table[0x49] = 0x0055; table[0x4A] = 0x0056; table[0x4B] = 0x0057;
            table[0x4C] = 0x005a; table[0x4D] = 0x203e; table[0x4E] = 0x2a7d; table[0x4F] = 0x2a7e;
            // 120-127
            table[0x50] = 0x2228; table[0x51] = 0x2227; table[0x52] = 0x2283; table[0x53] = 0x00ac;
            table[0x54] = 0x00f7; table[0x55] = 0x2261; table[0x56] = 0x0025; table[0x57] = 0x25c7;
            // 130-137
            table[0x58] = 0x007c; table[0x59] = 0x2015; table[0x5A] = 0x005f; table[0x5B] = 0x0021;
            table[0x5C] = 0x0022; table[0x5D] = 0x042a; table[0x5E] = 0x00b0; table[0x5F] = 0x2032;
            // 140-255: остальные 0 (как в C++ оригинале)
            // Остальные уже инициализированы 0 по умолчанию
            
            return table;
        }

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
        private static readonly byte[] TextToGost =
        {
            0x0F, // space (017)
            0x0E, // . (016)
            0x21, // Б (041)
            0x36, // Ц (066)
            0x24, // Д (044)
            0x34, // Ф (064)
            0x23, // Г (043)
            0x28, // И (050)
            0x12, // ( (022)
            0x13, // ) (023)
            0x19, // * (031)
            0x29, // Й (051)
            0x2B, // Л (053)
            0x3E, // Я (076)
            0x26, // Ж (046)
            0x0C, // / (014)
            0x00, // 0 (020)
            0x01, // 1
            0x02, // 2
            0x03, // 3
            0x04, // 4
            0x05, // 5
            0x06, // 6
            0x07, // 7
            0x08, // 8
            0x09, // 9
            0x3B, // Ь (073)
            0x0D, // , (015)
            0x2F, // П (057)
            0x0B, // - (013)
            0x0A, // + (012)
            0x3A, // Ы (072)
            0x27, // З (047)
            0x20, // А (040)
            0x22, // В (042)
            0x31, // С (061)
            0x3F, // D (077)
            0x25, // Е (045)
            0x40, // F (100)
            0x41, // G (101)
            0x2D, // Н (055)
            0x42, // I (102)
            0x43, // J (103)
            0x2A, // К (052)
            0x44, // L (104)
            0x2C, // М (054)
            0x45, // N (105)
            0x2E, // О (056)
            0x30, // Р (060)
            0x46, // Q (106)
            0x47, // R (107)
            0x48, // S (110)
            0x32, // Т (062)
            0x49, // U (111)
            0x4A, // V (112)
            0x4B, // W (113)
            0x35, // Х (065)
            0x33, // У (063)
            0x4C, // Z (114)
            0x38, // Ш (070)
            0x3C, // Э (074)
            0x39, // Щ (071)
            0x37, // Ч (067)
            0x3D, // Ю (075)
        };

        /// <summary>
        /// TEXT-символ (6 бит) -> Unicode.
        /// </summary>
        public static char TextToUnicode(byte ch)
        {
            if (ch >= TextToGost.Length) return '?';
            // C++ `text_to_gost[ch & 077]`: 077 — ВОСЬМЕРИЧНОЕ (== 0x3F).
            // В C# нет octal-литералов, поэтому пишем hex-маску 6 бит.
            return GostToUnicode(TextToGost[ch & 0x3F]);
        }

        //
        // Основная таблица tab0 (порт dubna/encoding.cpp): ASCII + специальные символы.
        // Строчные латинские буквы преобразуются в прописные (как в KOI-7).
        //
        private static readonly byte[] Tab0 = BuildTab0();

        private static byte[] BuildTab0()
        {
            byte[] t = new byte[256];
            for (int i = 0; i < 256; i++) t[i] = (byte)i;
            // Специальные символы.
            t[0x5C] = 0x1D; // '['? в KOI-7
            t[0x5E] = 0x5C;
            t[0x5F] = 0x5F;
            t[0x60] = 0;
            t[0x7B] = 0x0E; // {
            t[0x7C] = 0x5E; // |
            t[0x7D] = 0x0F; // }
            t[0x7E] = 0x1F; // ~
            t[0x7F] = 0;
            // Строчные латинские -> прописные.
            for (int i = 0; i < 26; i++)
                t[0x61 + i] = (byte)(0x41 + i);
            return t;
        }

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
            // Возвращает KOI-7 для кириллических символов (аналог case 0x04 в C++).
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
    }
}