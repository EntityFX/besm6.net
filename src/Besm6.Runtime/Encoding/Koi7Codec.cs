using Besm6.Runtime;

namespace Besm6.Runtime.Encoding
{
    /// <summary>
    /// KOI-7 codec API (delegates to CosyCodec).
    /// </summary>
    public static class Koi7Codec
    {
        /// <summary>Convert UTF-8 to KOI-7.</summary>
        public static string Utf8ToKoi7(string input, int maxLen = int.MaxValue) => CosyCodec.Utf8ToKoi7(input, maxLen);

        /// <summary>KOI-7 code to Unicode char.</summary>
        public static char Koi7ToUnicode(byte ch) => CosyCodec.Koi7ToUnicode(ch);
    }
}
