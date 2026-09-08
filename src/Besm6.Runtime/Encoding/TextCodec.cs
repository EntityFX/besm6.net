using Besm6.Runtime;

namespace Besm6.Runtime.Encoding
{
    /// <summary>
    /// TEXT codec API (delegates to CosyCodec).
    /// </summary>
    public static class TextCodec
    {
        /// <summary>TEXT-8 code to Unicode char.</summary>
        public static char TextToUnicode(byte ch) => CosyCodec.TextToUnicode(ch);

        /// <summary>Bytes to word.</summary>
        public static long BytesToWord(byte[] data, int offset) => CosyCodec.BytesToWord(data, offset);
    }
}
