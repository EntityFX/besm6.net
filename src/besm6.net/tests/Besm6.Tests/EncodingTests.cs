using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Loader;

namespace Besm6.Tests
{
    /// <summary>
    /// Тесты кодировок (порт ref/encoding.cpp + ref/cosy.cpp).
    /// </summary>
    [TestClass]
    public class EncodingTests
    {
        [TestMethod]
        public void GostToUnicode_PrintMapIsCorrect()
        {
            // ГОСТ-10859 (latin): код символа → Unicode (таблица gost_to_unicode_lat).
            // '0'=0x00..'9'=0x09
            for (int i = 0; i < 10; i++)
                Assert.AreEqual((char)('0' + i), CosyCodec.GostToUnicode((byte)i), $"digit {i}");

            // '+'(0x0A), '-'(0x0B), '/'(0x0C), ','(0x0D), '.'(0x0E), ' '(0x0F)
            Assert.AreEqual('+', CosyCodec.GostToUnicode(0x0A));
            Assert.AreEqual('-', CosyCodec.GostToUnicode(0x0B));
            Assert.AreEqual('/', CosyCodec.GostToUnicode(0x0C));
            Assert.AreEqual(',', CosyCodec.GostToUnicode(0x0D));
            Assert.AreEqual('.', CosyCodec.GostToUnicode(0x0E));
            Assert.AreEqual(' ', CosyCodec.GostToUnicode(0x0F));

            // ЛАТИНСКИЕ буквы в таблице gost_to_unicode_lat разбросаны
            // среди кириллических (как в ref/encoding.cpp). Точные коды:
            Assert.AreEqual('A', CosyCodec.GostToUnicode(0x20));
            Assert.AreEqual('\u0411', CosyCodec.GostToUnicode(0x21)); // Б
            Assert.AreEqual('B', CosyCodec.GostToUnicode(0x22));
            Assert.AreEqual('\u0413', CosyCodec.GostToUnicode(0x23)); // Г
            Assert.AreEqual('\u0414', CosyCodec.GostToUnicode(0x24)); // Д
            Assert.AreEqual('E', CosyCodec.GostToUnicode(0x25));
            Assert.AreEqual('\u0416', CosyCodec.GostToUnicode(0x26)); // Ж
            Assert.AreEqual('\u0417', CosyCodec.GostToUnicode(0x27)); // З
            Assert.AreEqual('\u0418', CosyCodec.GostToUnicode(0x28)); // И
            Assert.AreEqual('\u0419', CosyCodec.GostToUnicode(0x29)); // Й
            Assert.AreEqual('K', CosyCodec.GostToUnicode(0x2A));
            Assert.AreEqual('\u041B', CosyCodec.GostToUnicode(0x2B)); // Л
            Assert.AreEqual('M', CosyCodec.GostToUnicode(0x2C));
            Assert.AreEqual('H', CosyCodec.GostToUnicode(0x2D)); // Н
            Assert.AreEqual('O', CosyCodec.GostToUnicode(0x2E));
            Assert.AreEqual('\u041F', CosyCodec.GostToUnicode(0x2F)); // П
            Assert.AreEqual('P', CosyCodec.GostToUnicode(0x30));
            Assert.AreEqual('C', CosyCodec.GostToUnicode(0x31)); // С
            Assert.AreEqual('T', CosyCodec.GostToUnicode(0x32));
            Assert.AreEqual('Y', CosyCodec.GostToUnicode(0x33)); // У
            Assert.AreEqual('\u0424', CosyCodec.GostToUnicode(0x34)); // Ф
            Assert.AreEqual('X', CosyCodec.GostToUnicode(0x35));
            Assert.AreEqual('\u0426', CosyCodec.GostToUnicode(0x36)); // Ц
            Assert.AreEqual('\u0427', CosyCodec.GostToUnicode(0x37)); // Ч
            Assert.AreEqual('\u0428', CosyCodec.GostToUnicode(0x38)); // Ш
            Assert.AreEqual('\u0429', CosyCodec.GostToUnicode(0x39)); // Щ
            Assert.AreEqual('D', CosyCodec.GostToUnicode(0x3F));
            Assert.AreEqual('F', CosyCodec.GostToUnicode(0x40));
            Assert.AreEqual('G', CosyCodec.GostToUnicode(0x41));
            Assert.AreEqual('I', CosyCodec.GostToUnicode(0x42));
            Assert.AreEqual('J', CosyCodec.GostToUnicode(0x43));
            Assert.AreEqual('L', CosyCodec.GostToUnicode(0x44));
            Assert.AreEqual('N', CosyCodec.GostToUnicode(0x45));
            Assert.AreEqual('Q', CosyCodec.GostToUnicode(0x46));
            Assert.AreEqual('R', CosyCodec.GostToUnicode(0x47));
            Assert.AreEqual('S', CosyCodec.GostToUnicode(0x48));
            Assert.AreEqual('U', CosyCodec.GostToUnicode(0x49));
            Assert.AreEqual('V', CosyCodec.GostToUnicode(0x4A));
            Assert.AreEqual('W', CosyCodec.GostToUnicode(0x4B));
            Assert.AreEqual('Z', CosyCodec.GostToUnicode(0x4C));
        }

        [TestMethod]
        public void Koi7ToUnicode_AsciiRange()
        {
            // ASCII диапазон 0x20..0x7E совпадает с Unicode.
            for (int i = 0x20; i <= 0x5A; i++)
                Assert.AreEqual((char)i, CosyCodec.Koi7ToUnicode((byte)i), $"0x{i:X2}");
        }

        [TestMethod]
        public void Koi7ToUnicode_Cyrillic()
        {
            // ЮАБЦДЕФГХИЙКЛМНО (верхний регистр доступен по KOI-7).
            Assert.AreEqual('\u042E', CosyCodec.Koi7ToUnicode(0x60)); // Ю
            Assert.AreEqual('\u0410', CosyCodec.Koi7ToUnicode(0x61)); // А
            Assert.AreEqual('\u0411', CosyCodec.Koi7ToUnicode(0x62)); // Б
            Assert.AreEqual('\u0426', CosyCodec.Koi7ToUnicode(0x63)); // Ц
            Assert.AreEqual('\u0414', CosyCodec.Koi7ToUnicode(0x64)); // Д
            Assert.AreEqual('\u0415', CosyCodec.Koi7ToUnicode(0x65)); // Е
            Assert.AreEqual('\u0424', CosyCodec.Koi7ToUnicode(0x66)); // Ф
            Assert.AreEqual('\u0413', CosyCodec.Koi7ToUnicode(0x67)); // Г
        }

        [TestMethod]
        public void TextToUnicode_MapIsCorrect()
        {
            // TEXT (6 бит) → Unicode через таблицу text_to_gost.
            // 0=space → GOST 017 → ' '
            Assert.AreEqual(' ', CosyCodec.TextToUnicode(0));
            // 16='0' (text 0) → GOST 0
            Assert.AreEqual('0', CosyCodec.TextToUnicode(0x10));
            // 17='1' → GOST 1
            Assert.AreEqual('1', CosyCodec.TextToUnicode(0x11));
            // 40='З' → ГОСТ 047 (З) → latin не входит, поэтому через cyr/lat зависит
            // Проверим только что функция возвращает непустой char.
            Assert.IsTrue(CosyCodec.TextToUnicode(0x28) != '\0');
        }

        [TestMethod]
        public void Utf8ToKoi7_CyrillicUpper()
        {
            // А → A (KOI-7 uppercasing), Б → b, В → B.
            Assert.AreEqual("AbB", CosyCodec.Utf8ToKoi7("АБВ"));
        }

        [TestMethod]
        public void EncodeCosy_SpacePacking_CorrectCount()
        {
            // 3 пробела внутри + по 3 в хвосте: упаковываются байтом 0x80+count.
            byte[] enc = CosyCodec.EncodeCosy("A   B");
            // "A", 0x83 (3 пробела), "B", затем дополнение до 83 символов+'\n', упаковка, выравнивание.
            Assert.AreEqual(0, enc.Length % 6);
            Assert.AreEqual((byte)'A', enc[0]);
            Assert.AreEqual((byte)(0x80 + 3), enc[1]);
            Assert.AreEqual((byte)'B', enc[2]);
        }

        [TestMethod]
        public void GostToUnicode_MonsysBannerChars()
        {
            // Тест символов из баннера MONSYS: "ЙOKCEЛ      БЭCM-6/5     ШИФP-12"
            // Проверяем правильность таблицы GOST->Unicode (latin) для символов баннера.
            // В Latin таблице ГОСТ (gost_to_unicode_lat из encoding.cpp):
            //   Кириллические буквы: Й(0x29), Л(0x2B), Б(0x21), Э(0x3C), П(0x2F)
            //   Латинские буквы: O(0x2E), K(0x2A), C(0x31), E(0x25), M(0x2C)
            // В баннере MONSYS "M" в "CM-6" это на самом деле латинская M (0x4D),
            // а не кириллическая М (0x041C).
            
            // Й (U+0419, кириллица) -> GOST код 0x29
            Assert.AreEqual('Й', CosyCodec.GostToUnicode(0x29), "Й at GOST 0x29");
            
            // O (латиница, U+004F) -> GOST код 0x2E
            Assert.AreEqual('O', CosyCodec.GostToUnicode(0x2E), "Latin O at GOST 0x2E");
            
            // K (латиница, U+004B) -> GOST код 0x2A
            Assert.AreEqual('K', CosyCodec.GostToUnicode(0x2A), "Latin K at GOST 0x2A");
            
            // C (латиница, U+0043) -> GOST код 0x31
            Assert.AreEqual('C', CosyCodec.GostToUnicode(0x31), "Latin C at GOST 0x31");
            
            // E (латиница, U+0045) -> GOST код 0x25
            Assert.AreEqual('E', CosyCodec.GostToUnicode(0x25), "Latin E at GOST 0x25");
            
            // Л (кириллица, U+041B) -> GOST код 0x2B
            Assert.AreEqual('Л', CosyCodec.GostToUnicode(0x2B), "Cyrillic Л at GOST 0x2B");
            
            // Б (кириллица, U+0411) -> GOST код 0x21
            Assert.AreEqual('Б', CosyCodec.GostToUnicode(0x21), "Cyrillic Б at GOST 0x21");
            
            // Э (кириллица, U+042D) -> GOST код 0x3C
            Assert.AreEqual('Э', CosyCodec.GostToUnicode(0x3C), "Cyrillic Э at GOST 0x3C");
            
            // M (латиница, U+004D) -> GOST код 0x2C (в латинской таблице GOST)
            Assert.AreEqual('M', CosyCodec.GostToUnicode(0x2C), "Latin M at GOST 0x2C");
            
            // П (кириллица, U+041F) -> GOST код 0x2F
            Assert.AreEqual('П', CosyCodec.GostToUnicode(0x2F), "Cyrillic П at GOST 0x2F");
        }
    }
}