using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Loader;

namespace Besm6.Tests
{
    /// <summary>
    /// Дополнительные юнит-тесты:
    ///  1) канонические tape-id константы (порт machine.h);
    ///  2) разрешение каталога лент;
    ///  3) точная конверсия числа БЭСМ-6 <-> IEEE double (two's complement mantissa).
    /// </summary>
    [TestClass]
    public class TapeIdTests
    {
        [TestMethod]
        public void TapeIds_MatchCanonicalTextEncodingConstants()
        {
            // в TEXT-кодировке + номер). Проверяем, что константы не искажены.
            Assert.AreEqual(0xB6FBB3E73009L, TapeImage.TapeMonsys, "TapeMonsys");
            Assert.AreEqual(0xB298B2872012L, TapeImage.TapeLibrar12, "TapeLibrar12");
            Assert.AreEqual(0xB298B2872037L, TapeImage.TapeLibrar37, "TapeLibrar37");
            Assert.AreEqual(0x929CF08636D9L, TapeImage.TapeBemsh, "TapeBemsh");
            Assert.AreEqual(0x880000000007L, TapeImage.TapeB, "TapeB");
        }

        [TestMethod]
        public void TapeIdByName_ResolvesKnownTapes()
        {
            Assert.AreEqual(TapeImage.TapeMonsys, TapeImage.TapeIdByName("monsys"));
            Assert.AreEqual(TapeImage.TapeMonsys, TapeImage.TapeIdByName("9"));
            Assert.AreEqual(TapeImage.TapeLibrar12, TapeImage.TapeIdByName("librar12"));
            Assert.AreEqual(TapeImage.TapeLibrar37, TapeImage.TapeIdByName("librar37"));
            Assert.AreEqual(TapeImage.TapeBemsh, TapeImage.TapeIdByName("bemsh"));
            Assert.AreEqual(TapeImage.TapeB, TapeImage.TapeIdByName("b"));
        }

        [TestMethod]
        public void TapeIdByName_ChannelOverridesName()
        {
            // Канал — десятичное значение восьмеричного номера из карты '*tape:NN/...'.
            // Ключевой случай CERN: '*tape:12/librar,32' → LIBRAR 12 (не 37!).
            Assert.AreEqual(TapeImage.TapeLibrar12, TapeImage.TapeIdByName("librar", 10), "012 oct");
            Assert.AreEqual(TapeImage.TapeLibrar37, TapeImage.TapeIdByName("librar", 31), "037 oct");
            Assert.AreEqual(TapeImage.TapeMonsys, TapeImage.TapeIdByName("monsys", 9), "011 oct");
            Assert.AreEqual(TapeImage.TapeB, TapeImage.TapeIdByName("b", 7), "007 oct");
            Assert.AreEqual(TapeImage.TapeBemsh, TapeImage.TapeIdByName("bemsh", 217), "0331 oct");

            // Канал 0 (не задан) — legacy-поведение по имени.
            Assert.AreEqual(TapeImage.TapeLibrar37, TapeImage.TapeIdByName("librar", 0));
            Assert.AreEqual(TapeImage.TapeLibrar12, TapeImage.TapeIdByName("librar12", 0));
        }

        [TestMethod]
        public void TapeIdByName_UnknownChannelFallsBackToName()
        {
            // Неизвестный канал → fallback по имени (старое поведение).
            Assert.AreEqual(TapeImage.TapeB, TapeImage.TapeIdByName("b", 5));
            Assert.AreEqual(0, TapeImage.TapeIdByName("no-such-tape", 5));
        }

        [TestMethod]
        public void FindImagePath_ReturnsNullForUnknownTapeAndEmptyDir()
        {
            // Неизвестный tape-id → null.
            Assert.IsNull(TapeImage.FindImagePath(0x1234, "nonesuch-dir"));

            // Известный tape-id, но каталог пуст → null.
            string temp = Path.Combine(Path.GetTempPath(), "besm6_no_tapes_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                Assert.IsNull(TapeImage.FindImagePath(TapeImage.TapeMonsys, temp));
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        [TestMethod]
        public void TapeImage_WordReadWrite_RoundTrip()
        {
            var tape = new TapeImage(1, new byte[6 * 4], readOnly: false);
            long[] words = { 0, 1, 0xFFFFFFFFFFFFL, 0x123456789ABCL };
            for (int i = 0; i < words.Length; i++)
                tape.WriteWord(i, words[i]);
            for (int i = 0; i < words.Length; i++)
                Assert.AreEqual(words[i], tape.ReadWord(i), $"word[{i}]");
        }
    }

    [TestClass]
    public class NumberConversionTests
    {
        [TestMethod]
        public void DoubleToBesm6_One_IsCanonicalWord()
        {
            // 1.0 = 2^47 + 2^41 + 2^39 (oct: 0405 0000 0000 0000 0)
            ulong expected = (1UL << 47) | (1UL << 41) | (1UL << 39);
            Assert.AreEqual(expected, Besm6Math.DoubleToBesm6(1.0));
        }

        [TestMethod]
        public void DoubleToBesm6_NegativeOne_IsCanonicalWord()
        {
            // -1.0 = 2^47 + 2^40 (oct: 0402 0000 0000 0000 0)
            ulong expected = (1UL << 47) | (1UL << 40);
            Assert.AreEqual(expected, Besm6Math.DoubleToBesm6(-1.0));
        }

        [TestMethod]
        public void DoubleToBesm6_Two_IsCanonicalWord()
        {
            // 2.0 = 2^47 + 2^42 + 2^39 (oct: 0411 0000 0000 0000 0)
            ulong expected = (1UL << 47) | (1UL << 42) | (1UL << 39);
            Assert.AreEqual(expected, Besm6Math.DoubleToBesm6(2.0));
        }

        [TestMethod]
        public void DoubleToBesm6_Zero_IsZero()
        {
            Assert.AreEqual(0UL, Besm6Math.DoubleToBesm6(0.0));
        }

        [TestMethod]
        public void RoundTrip_PreservesValueWithinMantissaPrecision()
        {
            double[] values = { 0.25, 0.5, 1.0, 1.5, 2.0, -0.5, -3.75, 100.0, 0.001 };
            foreach (double val in values)
            {
                ulong word = Besm6Math.DoubleToBesm6(val);
                double back = Besm6Math.Besm6ToDouble(word);
                Assert.AreEqual(val, back, Math.Abs(val) * 1e-9 + 1e-12,
                    $"Round-trip {val:R} -> {back:R} (word=0x{word:X12})");
            }
        }

        [TestMethod]
        public void Log_IsNaturalLogarithm()
        {
            // ln(e) = 1.0; используем exp(1.0) как вход и проверяем, что log даёт ~1.0.
            ulong eWord = Besm6Math.Exp(Besm6Math.DoubleToBesm6(1.0));
            double eVal = Besm6Math.Besm6ToDouble(eWord);
            ulong logWord = Besm6Math.Log(Besm6Math.DoubleToBesm6(eVal));
            double logVal = Besm6Math.Besm6ToDouble(logWord);
            Assert.AreEqual(1.0, logVal, 1e-6, $"log(exp(1)) должно быть 1.0, получено {logVal:R}");
        }

        [TestMethod]
        public void Exp_IsNaturalExponential()
        {
            // exp(0) = 1.0.
            ulong word = Besm6Math.Exp(Besm6Math.DoubleToBesm6(0.0));
            Assert.AreEqual(1.0, Besm6Math.Besm6ToDouble(word), 1e-9);
        }

        [TestMethod]
        public void Sqrt_And_Trig_ProduceExpectedValues()
        {
            Assert.AreEqual(2.0, Besm6Math.Besm6ToDouble(Besm6Math.Sqrt(Besm6Math.DoubleToBesm6(4.0))), 1e-8);
            Assert.AreEqual(0.0, Besm6Math.Besm6ToDouble(Besm6Math.Sin(Besm6Math.DoubleToBesm6(0.0))), 1e-10);
            Assert.AreEqual(1.0, Besm6Math.Besm6ToDouble(Besm6Math.Cos(Besm6Math.DoubleToBesm6(0.0))), 1e-10);
        }
    }
}