namespace Besm6.Runtime.Encoding
{
    internal static class Gost10859Codec
    {
        /// <summary>GOST-10859 code to Unicode char.</summary>
        public static char GostToUnicode(byte ch)
        {
            var table = EncodingTables.GostToUnicodeLat;
            if (ch >= table.Length) return (char)ch;
            return (char)table[ch];
        }
    }
}
