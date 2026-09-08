using Besm6.Runtime;

namespace Besm6.Runtime.Encoding
{
    /// <summary>
    /// COSY encoder API (delegates to CosyCodec).
    /// </summary>
    public static class CosyEncoder
    {
        /// <summary>Encode KOI-7 line to COSY bytes.</summary>
        public static byte[] Encode(string koi7Line) => CosyCodec.EncodeCosy(koi7Line);
    }
}
