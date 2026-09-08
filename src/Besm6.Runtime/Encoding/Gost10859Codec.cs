using Besm6.Runtime;

namespace Besm6.Runtime.Encoding
{
    /// <summary>
    /// GOST-10859 codec API (delegates to CosyCodec).
    /// </summary>
    public static class Gost10859Codec
    {
        /// <summary>GOST-10859 code to Unicode char.</summary>
        public static char GostToUnicode(byte ch) => CosyCodec.GostToUnicode(ch);
    }
}
