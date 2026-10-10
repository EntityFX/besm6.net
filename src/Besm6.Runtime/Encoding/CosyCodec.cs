using System;
using Besm6.Runtime.Encoding;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// Кодировка COSY и таблицы ГОСТ-10859 / KOI-7 / TEXT.
    /// Порт dubna/cosy.cpp + dubna/encoding.cpp + dubna/gost10859.h.
    /// Реализация делегирует в namespace Besm6.Runtime.Encoding.
    /// </summary>
    public static class CosyCodec
    {
        //
        // COSY специальные карты.
        //
        public static readonly byte[] CosyReadOld = EncodingTables.CosyReadOld;
        public static readonly byte[] CosyEndFileRegular = EncodingTables.CosyEndFileRegular;
        public static readonly byte[] CosyEndFileLegacy = EncodingTables.CosyEndFileLegacy;

        /// <summary>
        /// Кодирование строки в формат COSY (порт encode_cosy из dubna/cosy.cpp).
        /// Входная строка уже должна быть в KOI-7. Выход — последовательность байт,
        /// кратно 6 (одно слово = 6 байт).
        /// </summary>
        public static byte[] EncodeCosy(string koi7Line) => CosyEncoder.Encode(koi7Line);

        /// <summary>
        /// Проверка карты '*read old'.
        /// </summary>
        public static bool IsReadOldCosy(byte[] line) => ByteEquals(line, CosyReadOld);

        /// <summary>
        /// Проверка карты '*end file' (два варианта представления).
        /// </summary>
        public static bool IsEndFileCosy(byte[] line) =>
            ByteEquals(line, CosyEndFileRegular) || ByteEquals(line, CosyEndFileLegacy);

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
            return result.ToString().TrimEnd(' ');
        }

        /// <summary>
        /// Конвертация строки UTF-8 в KOI-7 (порт utf8_to_koi7 из dubna/encoding.cpp).
        /// </summary>
        public static string Utf8ToKoi7(string input, int maxLen = int.MaxValue) =>
            Koi7Codec.Utf8ToKoi7(input, maxLen);

        /// <summary>KOI-7 → Unicode символ.</summary>
        public static char Koi7ToUnicode(byte ch) => Koi7Codec.Koi7ToUnicode(ch);

        /// <summary>GOST-10859 → Unicode символ.</summary>
        public static char GostToUnicode(byte ch) => Gost10859Codec.GostToUnicode(ch);

        /// <summary>TEXT-символ (6 бит) → Unicode.</summary>
        public static char TextToUnicode(byte ch) => TextCodec.TextToUnicode(ch);

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
