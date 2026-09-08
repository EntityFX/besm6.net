using Besm6.Runtime;

namespace Besm6.Runtime.Encoding
{
    /// <summary>
    /// COSY decoder API (delegates to CosyCodec).
    /// </summary>
    public static class CosyDecoder
    {
        /// <summary>Check *READ OLD marker.</summary>
        public static bool IsReadOld(byte[] line) => CosyCodec.IsReadOldCosy(line);

        /// <summary>Check *END FILE marker.</summary>
        public static bool IsEndFile(byte[] line) => CosyCodec.IsEndFileCosy(line);

        /// <summary>Decode COSY line.</summary>
        public static string? Decode(byte[] line) => CosyCodec.DecodeCosy(line);
    }
}
