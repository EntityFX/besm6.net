using System;
using System.IO;
using Besm6.Core;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Пограничные случаи парсера .dub (дополнение к JobParserTests в LoaderTests.cs).
    /// </summary>
    [TestClass]
    public class JobParserEdgeTests
    {
        [TestMethod]
        public void ParseOctalWord_InvalidDigit_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(
                () => JobParser.ParseOctalWord("0123456701234568", "`0123456701234568"));
        }

        [TestMethod]
        public void ParseOctalWord_MoreThan16Digits_KeepsLow16()
        {
            // 24 восьмеричные цифры — берутся только последние 16 ("0123456701234567").
            long val = JobParser.ParseOctalWord("012345670123456701234567", "`012345670123456701234567");
            Assert.AreEqual(0x053977053977L, val);
        }

        [TestMethod]
        public void ParseOctalWord_AllZeros_ReturnsZero()
        {
            Assert.AreEqual(0L, JobParser.ParseOctalWord("0000000000000000", "`0000000000000000"));
        }

        [TestMethod]
        public void ParseControlCard_DoubleAsteriskAndColon_Parsed()
        {
            var card = JobParser.ParseControlCard("**name:hello world");
            Assert.AreEqual("name", card.Directive);
            Assert.AreEqual("hello world", card.Argument);
        }

        [TestMethod]
        public void Parse_Name_LastWins_DirectiveCaseInsensitive()
        {
            var job = JobParser.Parse(new[] { "*name first", "*NAME second" });
            Assert.AreEqual("second", job.Name);
        }

        [TestMethod]
        public void Parse_TapeMount_NameOnly_DefaultsApplied()
        {
            // Без '/': канал = 0, имя всё остальное, zone = null.
            var job = JobParser.Parse(new[] { "*tape:monsys" });
            var m = job.TapeMounts[0];
            Assert.AreEqual(0, m.Channel);
            Assert.AreEqual("monsys", m.Name);
            Assert.IsNull(m.Zone);
        }

        [TestMethod]
        public void Parse_TapeMount_NoZone_ZoneIsNull()
        {
            var job = JobParser.Parse(new[] { "*tape:7/monsys" });
            var m = job.TapeMounts[0];
            Assert.AreEqual(7, m.Channel);
            Assert.AreEqual("monsys", m.Name);
            Assert.IsNull(m.Zone);
        }

        [TestMethod]
        public void Parse_TapeMount_NonOctalChannel_ChannelStaysZero()
        {
            var job = JobParser.Parse(new[] { "*tape:xx/b,5" });
            var m = job.TapeMounts[0];
            Assert.AreEqual(0, m.Channel); // TryParseOctal провалился — поле по умолчанию 0
            Assert.AreEqual("b", m.Name);
            Assert.AreEqual(5, m.Zone);
        }

        [TestMethod]
        public void Parse_TransMain_WithParameters_TakesAddressOnly()
        {
            var job = JobParser.Parse(new[] { "*trans-main:40020, 1000" });
            Assert.AreEqual(Convert.ToInt32("40020", 8), job.TransMain);
        }

        [TestMethod]
        public void Parse_TransMain_NonOctal_RemainsNull()
        {
            var job = JobParser.Parse(new[] { "*trans-main:12ab" });
            Assert.IsNull(job.TransMain);
        }

        [TestMethod]
        public void Parse_UnknownDirective_SavedAsControlCardOnly()
        {
            var job = JobParser.Parse(new[] { "*bogus foo" });
            Assert.AreEqual(1, job.ControlCards.Count);
            Assert.AreEqual("bogus", job.ControlCards[0].Directive);
            Assert.AreEqual("foo", job.ControlCards[0].Argument);
            Assert.IsNull(job.Name);
            Assert.AreEqual(0, job.SourceLines.Count);
        }

        [TestMethod]
        public void Parse_Execute_ArgumentCaptured()
        {
            var job = JobParser.Parse(new[] { "*execute monsys, 400" });
            Assert.AreEqual("monsys, 400", job.Execute);
        }

        [TestMethod]
        public void Parse_EmptyLineInAssem_AddsZeroRawWord()
        {
            var job = JobParser.Parse(new[] { "*assem", "", "*execute" });
            Assert.AreEqual(1, job.AssemProgram.Count);
            Assert.IsTrue(job.AssemProgram[0].IsRaw);
            Assert.AreEqual(0L, job.AssemProgram[0].Value);
        }

        [TestMethod]
        public void Parse_KnownControlDirective_ClosesAssemSection()
        {
            var job = JobParser.Parse(new[]
            {
                "*assem",
                "xta 1003",
                "*read 5",   // известная карта управления — закрывает секцию
                "src line",  // теперь исходный текст
            });
            Assert.AreEqual(1, job.AssemProgram.Count);
            Assert.AreEqual("xta 1003", job.AssemProgram[0].Text);
            Assert.AreEqual("read", job.ControlCards[1].Directive); // [0] = "assem"
            Assert.AreEqual(1, job.SourceLines.Count);
            Assert.AreEqual("src line", job.SourceLines[0]);
        }

        [TestMethod]
        public void ParseFile_MissingFile_ThrowsFileNotFoundException()
        {
            Assert.Throws<FileNotFoundException>(
                () => JobParser.ParseFile("nonexistent_besm6_job_xyz.dub"));
        }
    }

    /// <summary>
    /// Превращения COSY/ГОСТ/KOI-7 (дополнение к CosyCodecTests в LoaderTests.cs).
    /// </summary>
    [TestClass]
    public class CosyCodecTransformTests
    {
        [TestMethod]
        public void IsReadOldCosy_MatchesCanonicalOnly()
        {
            Assert.IsTrue(CosyCodec.IsReadOldCosy(CosyCodec.CosyReadOld));
            var changed = (byte[])CosyCodec.CosyReadOld.Clone();
            changed[9] = 0x0A;
            Assert.IsFalse(CosyCodec.IsReadOldCosy(changed));
            Assert.IsFalse(CosyCodec.IsReadOldCosy(new byte[] { 0x2A }));
        }

        [TestMethod]
        public void IsEndFileCosy_RecognizesBothVariants()
        {
            Assert.IsTrue(CosyCodec.IsEndFileCosy(CosyCodec.CosyEndFileRegular));
            Assert.IsTrue(CosyCodec.IsEndFileCosy(CosyCodec.CosyEndFileLegacy));
            Assert.IsFalse(CosyCodec.IsEndFileCosy(CosyCodec.CosyReadOld));
        }

        [TestMethod]
        public void DecodeCosy_CanonicalMaps_DecodeToText()
        {
            // 0x81..0xD3 = упакованные пробелы (cnt = b - 0x80).
            Assert.AreEqual("*READ OLD", CosyCodec.DecodeCosy(CosyCodec.CosyReadOld));
            Assert.AreEqual("*END FILE", CosyCodec.DecodeCosy(CosyCodec.CosyEndFileRegular));
            Assert.AreEqual("*END FILE", CosyCodec.DecodeCosy(CosyCodec.CosyEndFileLegacy));
        }

        [TestMethod]
        public void DecodeCosy_UnpacksInternalSpaceRun()
        {
            byte[] line = { 0x2A, 0x83, 0x42, 0x0A }; // '*' + 3 пробела + 'B'
            Assert.AreEqual("*   B", CosyCodec.DecodeCosy(line));
        }

        [TestMethod]
        public void DecodeCosy_InvalidBytes_ReturnsNull()
        {
            Assert.IsNull(CosyCodec.DecodeCosy(new byte[] { 0x20, 0x10, 0x0A })); // 0x10 < 0x20
            Assert.IsNull(CosyCodec.DecodeCosy(new byte[] { 0x41, 0xE0 }));       // 0xE0 > 0x7F
        }

        [TestMethod]
        public void Utf8ToKoi7_MaxLen_Truncates()
        {
            Assert.AreEqual("ABC", CosyCodec.Utf8ToKoi7("ABCD", 3));
        }

        [TestMethod]
        public void Utf8ToKoi7_UnknownCharacters_Skipped()
        {
            // π (U+03C0) нет в KOI-7 — пропускается.
            Assert.AreEqual("AB", CosyCodec.Utf8ToKoi7("A\u03C0B"));
        }

        [TestMethod]
        public void Koi7ToUnicode_CyrillicMapping()
        {
            // Таблица на 128 элементов: кириллица в 0x60..0x7F (стандартный KOI-7).
            Assert.AreEqual('\u042E', CosyCodec.Koi7ToUnicode(0x60)); // Ю
            Assert.AreEqual('\u0410', CosyCodec.Koi7ToUnicode(0x61)); // А (кириллица!)
            Assert.AreEqual('\u0411', CosyCodec.Koi7ToUnicode(0x62)); // Б
            Assert.AreEqual('\u0426', CosyCodec.Koi7ToUnicode(0x63)); // Ц
            Assert.AreEqual('\u041F', CosyCodec.Koi7ToUnicode(0x70)); // П
            Assert.AreEqual('A', CosyCodec.Koi7ToUnicode(0x41));      // латинская A
            // Выход за таблицу (128) — прозрачный возврат (char)ch.
            Assert.AreEqual((char)0xA0, CosyCodec.Koi7ToUnicode(0xA0));
        }

        [TestMethod]
        public void GostToUnicode_UnmappedCode_ReturnsZeroChar()
        {
            // Коды 140-255 не заданы в C++ оригинале → 0.
            Assert.AreEqual('\0', CosyCodec.GostToUnicode(0xFF));
            Assert.AreEqual(' ', CosyCodec.GostToUnicode(0x0F));
        }

        [TestMethod]
        public void BytesToWord_BigEndian_SixBytes()
        {
            Assert.AreEqual(0x010203040506L,
                CosyCodec.BytesToWord(new byte[] { 1, 2, 3, 4, 5, 6 }, 0));
            byte[] data = { 0x00, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
            Assert.AreEqual(0x0A0B0C0D0E0FL, CosyCodec.BytesToWord(data, 1));
        }

        [TestMethod]
        public void EncodeCosy_MiddleSpaceRun_RoundTrip()
        {
            byte[] enc = CosyCodec.EncodeCosy("AB  CD");
            Assert.AreEqual(0, enc.Length % 6);
            Assert.AreEqual("AB  CD", CosyCodec.DecodeCosy(enc));
        }

        [TestMethod]
        public void EncodeCosy_OnlySpaces_DecodesEmpty()
        {
            byte[] enc = CosyCodec.EncodeCosy(" ");
            Assert.AreEqual(0, enc.Length % 6);
            Assert.AreEqual("", CosyCodec.DecodeCosy(enc));
        }

        [TestMethod]
        public void EncodeCosy_AlwaysMultipleOfSix()
        {
            for (int len = 1; len <= 90; len++)
            {
                byte[] enc = CosyCodec.EncodeCosy(new string('A', len));
                Assert.AreEqual(0, enc.Length % 6, $"len={len}");
            }
        }
    }

    /// <summary>
    /// Дополнительные пути ExtracodeHandler (дополнение к ExtracodeHandlerTests
    /// в LoaderTests.cs): E50-сервисы, E71, E72, E75-intercept, E57-floor,
    /// E70 disk-ветка и phys_io-редирект.
    /// </summary>
    [TestClass]
    public class ExtracodeEdgeTests
    {
        private static ExtracodeHandler MakeHandler(MachineCore machine) =>
            new ExtracodeHandler(machine, id => null, u => null, d => null, output: s => { });

        [TestMethod]
        public void E50_Date_SetsExpectedAcc()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // 067 oct (55 dec) = DATE*. Раскладка union E50_Date_Time (ref/extracode.h):
            //   decisec b0-3, sec_lo b4-7, sec_hi b8-11, min_lo b12-15, min_hi b16-19,
            //   hour_lo b20-23, hour_hi b24-25, year_lo b26-29, year_hi b30-33,
            //   month_lo b34-37, month_hi b38-41, day_lo b42-45, day_hi b46-47.
            // Фиксированное значение C++: 04/07/24 23:45:56 = 0'101C'9234'5560 hex.
            machine.Cpu.SetM(14, 55);
            handler.Handle(40, 0); // *50
            Assert.AreEqual(0x101C92345560UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E50_ZeroCase_066_ClearsAcc()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 54); // 066 oct
            machine.Cpu.SetAcc(123);
            handler.Handle(40, 0);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E50_Floor_007()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 7); // 007 oct = floor
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(3.75));
            handler.Handle(40, 0);
            Assert.AreEqual(3.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc().Value), 1e-9);
        }

        [TestMethod]
        public void E50_UnimplementedAddr_Throws()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 9); // 011 oct — нет case'а → default → ProcessorException
            bool threw = false;
            try { handler.Handle(40, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, "E50 addr=9 должен бросить ProcessorException");
        }

        [TestMethod]
        public void E72_AllVariants_NoOp()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetAcc(12345);
            machine.Cpu.SetM(14, 5);
            handler.Handle(58, 0); // 072 oct
            Assert.AreEqual(12345UL, machine.Cpu.GetAcc().Value, "E72 — no-op, ACC не меняется");
        }

        [TestMethod]
        public void E75_Addr16_SetsInterceptCount()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 16); // 020 oct → включить intercept
            machine.Cpu.SetAcc(42);
            handler.Handle(61, 0); // 075 oct
            Assert.AreEqual(42UL, machine.Memory.Read(16).Value);
            Assert.AreEqual(1, machine.Cpu.InterceptCount);
        }

        [TestMethod]
        public void E61_WatanabePlotter_OutputsBytes()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // Записать байты "AB" + NUL в память по адресу 100.
            machine.Memory.Write(100, new Word48(0x414200000000L));

            // E61 addr=077777 (32767): ACC = адрес 100 (младшие 15 бит), target=0 (Watanabe).
            machine.Cpu.SetM(14, 32767);
            machine.Cpu.SetAcc(100L); // target = 0 (Watanabe), addr = 100
            handler.Handle(49, 0); // 061 oct = 49 dec

            Assert.AreEqual("AB", machine.Plotter.Watanabe);
            Assert.AreEqual("", machine.Plotter.Tektronix);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value, "E61 должен сбросить ACC");
        }

        [TestMethod]
        public void E61_TektronixPlotter_OutputsBytes()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // Записать байты "XY" + NUL в память по адресу 200.
            machine.Memory.Write(200, new Word48(0x585900000000L));

            // E61 addr=077777, target = 01400 oct = 0x300, addr = 200.
            machine.Cpu.SetM(14, 32767);
            machine.Cpu.SetAcc((0x300L << 36) | 200L);
            handler.Handle(49, 0);

            Assert.AreEqual("XY", machine.Plotter.Tektronix);
            Assert.AreEqual("", machine.Plotter.Watanabe);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E61_UnknownTarget_Throws()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // target = 0x500 (не Watanabe и не Tektronix).
            machine.Cpu.SetM(14, 32767);
            machine.Cpu.SetAcc((0x500L << 36) | 100L);
            Assert.Throws<ProcessorException>(() => handler.Handle(49, 0));
        }

        [TestMethod]
        public void E61_NonSpecialAddr_ClearsAcc()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 5); // не 077777
            machine.Cpu.SetAcc(12345);
            handler.Handle(49, 0);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E51_Addr1_ReturnsCos()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // E51 addr=1 → cos(0) = 1.
            machine.Cpu.SetM(14, 1);
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(0.0));
            handler.Handle(41, 0); // 051 oct = 41 dec
            Assert.AreEqual(1.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc().Value), 1e-6);
        }

        [TestMethod]
        public void E52_NonZeroAddr_Throws()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 3);
            Assert.Throws<ProcessorException>(() => handler.Handle(42, 0)); // 052 oct
        }

        [TestMethod]
        public void E50_Case066_ChangesPlotterPage()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 54); // 066 oct
            machine.Cpu.SetAcc(123);
            handler.Handle(40, 0); // *50

            Assert.AreEqual(1, machine.Plotter.PageNumber);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E57_FloorAcc()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            machine.Cpu.SetM(14, 0); // E57 addr=0 → floor(ACC)
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(3.75));
            handler.Handle(47, 0); // 057 oct
            Assert.AreEqual(3.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc().Value), 1e-9);
        }

        [TestMethod]
        public void E70_DiskUnit_WriteThenRead_RoundTrip()
        {
            var machine = new MachineCore();
            var disk = new TapeImage(1, new byte[4 * 1024 * 6], readOnly: false);
            var handler = new ExtracodeHandler(
                machine, id => null,
                diskByUnit: u => u == 30 ? disk : null,
                drumByUnit: u => null,
                output: s => { });

            for (int i = 0; i < 1024; i++)
                machine.Memory.Write((uint)i, new Word48((ulong)i * 3UL));

            // unit=30 (биты 12-17), zone=2 (биты 0-11), запись (бит 39 = 0), page=0.
            ulong ctrl = (30UL << 12) | 2UL;
            machine.Cpu.SetM(14, 0); // execAddr = 0 → ctrl берётся из ACC
            machine.Cpu.SetAcc(ctrl);
            handler.Handle(56, 0); // 070 oct

            Assert.AreEqual(0L, disk.ReadWord(2 * 1024));
            Assert.AreEqual(3L, disk.ReadWord(2 * 1024 + 1));
            Assert.AreEqual(3L * 1023, disk.ReadWord(2 * 1024 + 1023));

            // Стереть память и считать обратно с диска.
            for (int i = 0; i < 1024; i++)
                machine.Memory.Write((uint)i, new Word48(0));
            machine.Cpu.SetAcc(ctrl | (1UL << 39)); // read
            handler.Handle(56, 0);
            Assert.AreEqual(3UL, machine.Memory.Read(1).Value);
            Assert.AreEqual(3UL * 1023, machine.Memory.Read(1023).Value);
        }

        [TestMethod]
        public void E70_DiskUnit_AutoMountFails_Throws()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null,
                diskByUnit: u => null, // диск не смонтирован, mountTape по умолчанию false
                drumByUnit: u => null,
                output: s => { });

            machine.Cpu.SetM(14, 0);
            machine.Cpu.SetAcc((30L << 12) | 1);
            bool threw = false;
            try { handler.Handle(56, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, "E70: несмонтированный диск-унит должен бросать ProcessorException");
        }

        [TestMethod]
        public void E70_DiskUnit_Bit40_EarlyReturn_NoData()
        {
            var machine = new MachineCore();
            var disk = new TapeImage(1, new byte[4 * 1024 * 6], readOnly: false);
            var handler = new ExtracodeHandler(
                machine, id => null,
                diskByUnit: u => u == 30 ? disk : null,
                drumByUnit: u => null,
                output: s => { });

            machine.Memory.Write(0, new Word48(77));
            machine.Cpu.SetM(14, 0);
            machine.Cpu.SetAcc((30L << 12) | 1 | (1L << 40)); // бит 40 → ранний выход
            handler.Handle(56, 0); // не бросает
            Assert.AreEqual(0L, disk.ReadWord(1024), "бит 40 — early return, данные не пишутся");
        }

        [TestMethod]
        public void E70_MapDrumToDisk_PhysIo_RedirectsWrite()
        {
            var machine = new MachineCore();
            var disk = new TapeImage(1, new byte[4 * 1024 * 6], readOnly: false);
            var handler = new ExtracodeHandler(
                machine, id => null,
                diskByUnit: u => null,
                drumByUnit: u => { throw new InvalidOperationException("барабан не должен трогаться"); },
                output: s => { });
            handler.MapDrumToDisk(2, 30, disk);

            for (int i = 0; i < 1024; i++)
                machine.Memory.Write((uint)i, new Word48((ulong)i * 5UL));

            // unit=2 (thisDrum=2 >= _mappedDrum=2), phys_io бит 38, tract=1, запись.
            ulong ctrl = (2UL << 12) | 1UL | (1UL << 38);
            machine.Cpu.SetM(14, 0);
            machine.Cpu.SetAcc(ctrl);
            handler.Handle(56, 0);

            // diskZone = tract + (2 - 2) * 32 = 1 → слово[1024 + 5] = memory[5] = 25.
            Assert.AreEqual(25L, disk.ReadWord(1 * 1024 + 5));
            Assert.AreEqual(0L, disk.ReadWord(5), "зона 0 диска не должна затронуться");
        }
    }
}


