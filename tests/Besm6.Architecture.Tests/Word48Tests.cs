using Besm6.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// РўРµСЃС‚С‹ РєРѕРЅРІРµСЂСЃРёРё Word48 <-> double.
    /// Р§РёСЃР»Рѕ Р‘Р­РЎРњ-6: Р±РёС‚С‹ 47..41 вЂ” РїРѕСЂСЏРґРѕРє (bias=64), Р±РёС‚С‹ 40..0 вЂ” РјР°РЅС‚РёСЃСЃР°
    /// РІ РґРѕРїРѕР»РЅРёС‚РµР»СЊРЅРѕРј РєРѕРґРµ (Р·РЅР°Рє РІ Р±РёС‚Рµ 40). Р­С‚Рѕ РєР°РЅРѕРЅРёС‡РµСЃРєРёР№ С„РѕСЂРјР°С‚,
    /// СЃРѕРІРїР°РґР°СЋС‰РёР№ СЃ Besm6Math (РїРѕСЂС‚ besm6_arch.cpp).
    /// </summary>
    [TestClass]
    public class Word48FloatTests
    {
        [TestMethod]
        public void ToDouble_One_IsOne()
        {
            // 1.0 = (65 << 41) | (1 << 39) = 0x828000000000
            ulong one = (65UL << 41) | (1UL << 39);
            Assert.AreEqual(1.0, new Word48(one).ToDouble(), 1e-9);
        }

        [TestMethod]
        public void FromDouble_One_IsCanonicalWord()
        {
            ulong expected = (65UL << 41) | (1UL << 39);
            Assert.AreEqual(expected, Word48.FromDouble(1.0).Value);
        }

        [TestMethod]
        public void RoundTrip_PreservesValue()
        {
            double[] values = { 0.25, 0.5, 1.0, -1.0, 2.0, -3.75, 100.0 };
            foreach (double v in values)
            {
                double back = Word48.FromDouble(v).ToDouble();
                Assert.AreEqual(v, back, System.Math.Abs(v) * 1e-9 + 1e-12, $"round-trip {v:R}");
            }
        }

        [TestMethod]
        public void ToDouble_MatchesBesm6Math()
        {
            // Word48.ToDouble Рё Besm6Math.Besm6ToDouble РґРѕР»Р¶РЅС‹ РѕРїРёСЃС‹РІР°С‚СЊ РћР”РќРћ Р РўРћ Р–Р•
            // С‡РёСЃР»Рѕ Р‘Р­РЎРњ-6 (РѕР±Р° РїСЂРµС‚РµРЅРґСѓСЋС‚ РЅР° РєР°РЅРѕРЅРёС‡РµСЃРєРёР№ С„РѕСЂРјР°С‚).
            ulong[] words = {
                0,
                (65UL << 41) | (1UL << 39),       // 1.0
                (65UL << 41) | (3UL << 39),       // 1.5 (mantissa = 3*2^39)
            };
            foreach (ulong w in words)
            {
                Assert.AreEqual(
                    Besm6Math.Besm6ToDouble(w),
                    new Word48(w).ToDouble(),
                    1e-9,
                    $"word 0x{w:X12}");
            }
        }
    }

    [TestClass]
    public class Word48OctalTests
    {
        [TestMethod]
        public void ToOctal_FromOctal_RoundTrip()
        {
            ulong value = 0x123456789ABCUL;
            string oct = new Word48(value).ToOctal();
            Assert.AreEqual(16, oct.Length, "48 Р±РёС‚ = 16 РІРѕСЃСЊРјРµСЂРёС‡РЅС‹С… С†РёС„СЂ");
            Assert.AreEqual(value, Word48.FromOctal(oct).Value);
        }

        [TestMethod]
        public void FromOctal_Parses17DigitLeadingZero()
        {
            // РљР°РЅРѕРЅРёС‡РµСЃРєР°СЏ 17-СЃРёРјРІРѕР»СЊРЅР°СЏ Р·Р°РїРёСЃСЊ СЃ РІРµРґСѓС‰РёРј РЅСѓР»С‘Рј.
            // "04050000000000000" = 1.0; РІРµРґСѓС‰РёР№ 0 РґР°С‘С‚ Р±РёС‚ 48, РєРѕС‚РѕСЂС‹Р№ РѕС‚Р±СЂР°СЃС‹РІР°РµС‚СЃСЏ.
            ulong one = (65UL << 41) | (1UL << 39);
            Assert.AreEqual(one, Word48.FromOctal("04050000000000000").Value);
        }
    }
}