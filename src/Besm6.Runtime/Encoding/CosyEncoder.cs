using System;
using System.Collections.Generic;

namespace Besm6.Runtime.Encoding
{
    internal static class CosyEncoder
    {
        /// <summary>
        /// Encode a KOI-7 string into COSY format (space packing + 6-byte alignment).
        /// Port of encode_cosy from dubna/cosy.cpp.
        /// </summary>
        public static byte[] Encode(string koi7Line)
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
    }
}
