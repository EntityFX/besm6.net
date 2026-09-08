namespace Besm6.Runtime.Encoding
{
    internal static class TextCodec
    {
        /// <summary>TEXT-8 code to Unicode char.</summary>
        public static char TextToUnicode(byte ch)
        {
            var table = EncodingTables.TextToGost;
            if (ch >= table.Length) return '?';
            return Gost10859Codec.GostToUnicode(table[ch & 0x3F]);
        }
    }
}
