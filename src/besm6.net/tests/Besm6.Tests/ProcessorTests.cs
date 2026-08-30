using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    [TestClass]
    public class ProcessorTests
    {
        private sealed class LinearMemory : IMemory
        {
            private readonly Word48[] _words = new Word48[32768];
            public Word48 Read(uint address) => _words[address & 0x7FFF];
            public void Write(uint address, Word48 word) => _words[address & 0x7FFF] = word;
            public int Size => 32768;
        }

        private LinearMemory _memory;
        private Processor _cpu;

        [TestInitialize]
        public void Setup()
        {
            _memory = new LinearMemory();
            _cpu = new Processor(_memory);
        }

        // Ассемблер — делегирование на общий Assembler.
        private static ulong Asm(string s) => Besm6.Asm.Assembler.Asm(s);

        // Восьмеричный литерал.
        private static ulong O(string s) => Convert.ToUInt64(s.Trim(), 8);

        // Принимает восьмеричный литерал C++ буквально (с "'" разделителями,
        // "ul" суффиксом и ведущим "0" префиксом восьмеричной записи),
        // маскирует до 48 бит, как слово БЭСМ-6.
        private static ulong Cw(string cpp)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in cpp)
            {
                if (c == '\'' || c == 'u' || c == 'U' || c == 'l' || c == 'L')
                    continue;
                sb.Append(c);
            }
            ulong v = 0;
            foreach (char c in sb.ToString())
            {
                int d = c - '0';
                if (d < 0 || d > 7)
                    throw new ArgumentException($"bad octal literal: {cpp}");
                v = (v << 3) | (ulong)d;
            }
            return v & 0xFFFFFFFFFFFFUL;
        }

        private void StoreWord(string addr, ulong val) => _memory.Write((uint)(O(addr) & 0x7FFF), new Word48(val));

        private void Run(string startPc = "10")
        {
            _cpu.SetPc((uint)O(startPc));
            for (int i = 0; i < 100000; i++)
            {
                if (_cpu.Step())
                    return;
            }
            Assert.Fail("Processor did not halt within 100000 instructions");
        }

        // Убеждается, что исполнение слова word (по адресу 10) бросает ProcessorException.
        private void ExpectIllegal(string op, ulong word)
        {
            StoreWord("10", word);
            _cpu.SetPc((uint)O("10"));
            try
            {
                _cpu.Step();
            }
            catch (ProcessorException)
            {
                return;
            }
            Assert.Fail($"{op}: ожидалось исключение ProcessorException, но оно не выброшено");
        }

        [TestMethod]
        public void Test_Uj()
        {
            StoreWord("10", Asm("пб 12, сч 0"));
            StoreWord("11", Asm("стоп 76543(2), сч 0"));
            StoreWord("12", Asm("стоп 12345(6), сч 0"));

            Run();

            Assert.AreEqual(O("12"), _cpu.GetPc());
        }

        [TestMethod]
        public void Test_VtmVzmV1m()
        {
            StoreWord("10", Asm("уиа (2), пио 12(2)"));
            StoreWord("11", Asm("пб 15, мода"));
            StoreWord("12", Asm("пино 15(2), пино 15(2)"));
            StoreWord("13", Asm("уиа -1(2), пио 15(2)"));
            StoreWord("14", Asm("пио 15(2), пино 16(2)"));
            StoreWord("15", Asm("стоп 76543(2), мода"));
            StoreWord("16", Asm("стоп 12345(6), мода"));

            Run();

            Assert.AreEqual(O("16"), _cpu.GetPc());
            Assert.AreEqual(O("77777"), _cpu.GetM(2));
        }

        [TestMethod]
        public void Test_JamUtm()
        {
            StoreWord("10", Asm("уиа 1(2), уиа -17(3)"));
            StoreWord("11", Asm("пб 13, мода"));
            StoreWord("12", Asm("сли 2(2), слиа 1(3)"));
            StoreWord("13", Asm("пино 12(2), пино 15(3)"));
            StoreWord("14", Asm("стоп 12345(6), мода"));
            StoreWord("15", Asm("стоп 76543(2), мода"));

            Run();

            Assert.AreEqual(O("14"), _cpu.GetPc());
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
        }

        [TestMethod]
        public void Test_Vlm()
        {
            StoreWord("10", Asm("уиа -11(2), уиа -12(3)"));
            StoreWord("11", Asm("слиа 1(3), цикл 11(2)"));
            StoreWord("12", Asm("пино 105(2), пино 105(3)"));
            StoreWord("13", Asm("цикл 105(2), пино 105(2)"));
            StoreWord("14", Asm("сч 2000, уиа 77401(16)"));
            StoreWord("15", Asm("зп 2400(16), цикл 15(16)"));
            StoreWord("16", Asm("сч, уиа 77401(17)"));
            StoreWord("17", Asm("слц 2400(17), цикл 17(17)"));
            StoreWord("20", Asm("нтж, по 105"));
            StoreWord("21", Asm("нтж 2000, пе 105"));
            StoreWord("22", Asm("уиа 77401(16), уиа 77401(15)"));
            StoreWord("23", Asm("слц 2400(16), цикл 23(16)"));
            StoreWord("24", Asm("нтж, по 105"));
            StoreWord("25", Asm("нтж 2000, пе 105"));
            StoreWord("26", Asm("слц 2400(15), цикл 26(15)"));
            StoreWord("27", Asm("нтж, по 105"));
            StoreWord("30", Asm("нтж 2000, пе 105"));
            StoreWord("31", Asm("уиа 77401(14), уиа 77401(13)"));
            StoreWord("32", Asm("слц 2400(14), цикл 32(14)"));
            StoreWord("33", Asm("нтж, по 105"));
            StoreWord("34", Asm("нтж 2000, пе 105"));
            StoreWord("35", Asm("слц 2400(13), цикл 35(13)"));
            StoreWord("36", Asm("нтж, по 105"));
            StoreWord("37", Asm("нтж 2000, пе 105"));
            StoreWord("40", Asm("уиа 77401(12), уиа 77401(11)"));
            StoreWord("41", Asm("слц 2400(12), цикл 41(12)"));
            StoreWord("42", Asm("нтж, по 105"));
            StoreWord("43", Asm("нтж 2000, пе 105"));
            StoreWord("44", Asm("слц 2400(11), цикл 44(11)"));
            StoreWord("45", Asm("нтж, по 105"));
            StoreWord("46", Asm("нтж 2000, пе 105"));
            StoreWord("47", Asm("уиа 77401(10), уиа 77401(7)"));
            StoreWord("50", Asm("слц 2400(10), цикл 50(10)"));
            StoreWord("51", Asm("нтж, по 105"));
            StoreWord("52", Asm("нтж 2000, пе 105"));
            StoreWord("53", Asm("слц 2400(7), цикл 53(7)"));
            StoreWord("54", Asm("нтж, по 105"));
            StoreWord("55", Asm("нтж 2000, пе 105"));
            StoreWord("56", Asm("уиа 77401(6), уиа 77401(5)"));
            StoreWord("57", Asm("слц 2400(6), цикл 57(6)"));
            StoreWord("60", Asm("нтж, по 105"));
            StoreWord("61", Asm("нтж 2000, пе 105"));
            StoreWord("62", Asm("слц 2400(5), цикл 62(5)"));
            StoreWord("63", Asm("нтж, по 105"));
            StoreWord("64", Asm("нтж 2000, пе 105"));
            StoreWord("65", Asm("уиа 77401(4), уиа 77401(3)"));
            StoreWord("66", Asm("слц 2400(4), цикл 66(4)"));
            StoreWord("67", Asm("нтж, по 105"));
            StoreWord("70", Asm("нтж 2000, пе 105"));
            StoreWord("71", Asm("слц 2400(3), цикл 71(3)"));
            StoreWord("72", Asm("нтж, по 105"));
            StoreWord("73", Asm("нтж 2000, пе 105"));
            StoreWord("74", Asm("уиа 77401(2), мода"));
            StoreWord("75", Asm("слц 2400(2), цикл 75(2)"));
            StoreWord("76", Asm("нтж, по 105"));
            StoreWord("77", Asm("нтж 2000, пе 105"));
            StoreWord("100", Asm("уиа 77401(1), мода"));
            StoreWord("101", Asm("слц 2400(1), цикл 101(1)"));
            StoreWord("102", Asm("нтж, по 105"));
            StoreWord("103", Asm("нтж 2000, пе 105"));
            StoreWord("104", Asm("стоп 12345(6), мода"));
            StoreWord("105", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));

            Run();

            Assert.AreEqual(O("104"), _cpu.GetPc());
            for (int i = 1; i <= 15; i++)
                Assert.AreEqual(0UL, (ulong)_cpu.GetM(i), $"M[{i}]");
        }

        [TestMethod]
        public void Test_UtcWtc()
        {
            StoreWord("10", Asm("мода -1, уиа (3)"));
            StoreWord("11", Asm("пио 40(3), слиа 1(3)"));
            StoreWord("12", Asm("пино 40(3), мода"));
            StoreWord("13", Asm("мода -1, мода"));
            StoreWord("14", Asm("уиа (3), пио 40(3)"));
            StoreWord("15", Asm("слиа 1(3), пино 40(3)"));
            StoreWord("16", Asm("мод 2000, уиа (3)"));
            StoreWord("17", Asm("пио 40(3), слиа 1(3)"));
            StoreWord("20", Asm("пино 40(3), мод 2000"));
            StoreWord("21", Asm("уиа (3), пио 40(3)"));
            StoreWord("22", Asm("слиа 1(3), пино 40(3)"));
            StoreWord("23", Asm("мода -7, мода 10"));
            StoreWord("24", Asm("уиа -2(3), пио 40(3)"));
            StoreWord("25", Asm("слиа 1(3), пино 40(3)"));
            StoreWord("26", Asm("мод 2000, мода 10"));
            StoreWord("27", Asm("уиа -6(3), слиа -1(3)"));
            StoreWord("30", Asm("пино 40(3), уиа -1(3)"));
            StoreWord("31", Asm("мод 2002(3), уиа (4)"));
            StoreWord("32", Asm("уии 5(4), слиа 52526(5)"));
            StoreWord("33", Asm("пино 40(5), слиа 1(3)"));
            StoreWord("34", Asm("мод 2002(3), уиа (4)"));
            StoreWord("35", Asm("уии 5(4), слиа 25253(5)"));
            StoreWord("36", Asm("пино 40(5), мода"));
            StoreWord("37", Asm("стоп 12345(6), мода"));
            StoreWord("40", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("77777"));
            StoreWord("2001", O("5252525252525252"));
            StoreWord("2002", O("2525252525252525"));

            Run();

            Assert.AreEqual(O("37"), _cpu.GetPc());
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
            Assert.AreEqual(O("52525"), _cpu.GetM(4));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(5));
        }

        [TestMethod]
        public void Test_Vjm()
        {
            StoreWord("10", Asm("мода, пв 11(2)"));
            StoreWord("11", Asm("слиа -11(2), пино 23(2)"));
            StoreWord("12", Asm("пв 13(2), мода"));
            StoreWord("13", Asm("слиа -13(2), пино 23(2)"));
            StoreWord("14", Asm("пв 16(2), мода"));
            StoreWord("15", Asm("мода -1, мода"));
            StoreWord("16", Asm("уиа 1(3), пио 23(3)"));
            StoreWord("17", Asm("уиа -1(3), пв 21(2)"));
            StoreWord("20", Asm("уиа -2(3), мода"));
            StoreWord("21", Asm("слиа 1(3), пино 23(3)"));
            StoreWord("22", Asm("стоп 12345(6), мода"));
            StoreWord("23", Asm("стоп 76543(2), мода"));

            Run();

            Assert.AreEqual(O("22"), _cpu.GetPc());
            Assert.AreEqual(O("20"), _cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
        }

        [TestMethod]
        public void Test_Mtj()
        {
            StoreWord("10", Asm("мода -15, уиа 16(2)"));
            StoreWord("11", Asm("слиа -1(2), пино 24(2)"));
            StoreWord("12", Asm("слиа 1(2), уиа 17(2)"));
            StoreWord("13", Asm("слиа -17(2), пино 24(2)"));
            StoreWord("14", Asm("уиа 1(3), сли 2(3)"));
            StoreWord("15", Asm("слиа -1(2), пино 24(2)"));
            StoreWord("16", Asm("слиа 1(2), слиа -1(3)"));
            StoreWord("17", Asm("пино 24(3), слиа 1(3)"));
            StoreWord("20", Asm("уии 2(3), слиа -1(2)"));
            StoreWord("21", Asm("пино 24(2), слиа 1(2)"));
            StoreWord("22", Asm("слиа -1(3), пино 24(3)"));
            StoreWord("23", Asm("стоп 12345(6), мода"));
            StoreWord("24", Asm("стоп 76543(2), мода"));

            Run();

            Assert.AreEqual(O("23"), _cpu.GetPc());
            Assert.AreEqual(1UL, (ulong)_cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
        }

        [TestMethod]
        public void Test_XtaUzaU1a()
        {
            StoreWord("10", Asm("сч 2000, по 12"));
            StoreWord("11", Asm("пб 15, мода"));
            StoreWord("12", Asm("пе 15, пе 15"));
            StoreWord("13", Asm("сч 2001, по 15"));
            StoreWord("14", Asm("по 15, пе 16"));
            StoreWord("15", Asm("стоп 76543(2), мода"));
            StoreWord("16", Asm("стоп 12345(6), мода"));
            StoreWord("2000", O("0"));
            StoreWord("2001", O("1"));

            Run();

            Assert.AreEqual(O("16"), _cpu.GetPc());
            Assert.AreEqual(1UL, _cpu.GetAcc().Value);
            Assert.AreEqual(1UL, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Test_Atx()
        {
            StoreWord("10", Asm("сч, зп 2000"));
            StoreWord("11", Asm("зп 2001, зп 2002"));
            StoreWord("12", Asm("сч 2000, пе 30"));
            StoreWord("13", Asm("сч 2001, пе 30"));
            StoreWord("14", Asm("сч 2002, пе 30"));
            StoreWord("15", Asm("сч 2003, зп 2001"));
            StoreWord("16", Asm("сч 2000, пе 30"));
            StoreWord("17", Asm("сч 2001, по 30"));
            StoreWord("20", Asm("сч 2002, пе 30"));
            StoreWord("21", Asm("сч 2003, зп 2000"));
            StoreWord("22", Asm("зп 2002, сч"));
            StoreWord("23", Asm("зп 2001, сч 2000"));
            StoreWord("24", Asm("по 30, сч 2001"));
            StoreWord("25", Asm("пе 30, сч 2002"));
            StoreWord("26", Asm("по 30, мода"));
            StoreWord("27", Asm("стоп 12345(6), мода"));
            StoreWord("30", Asm("стоп 76543(2), мода"));
            StoreWord("2003", O("1"));

            Run();

            Assert.AreEqual(O("27"), _cpu.GetPc());
            Assert.AreEqual(1UL, _cpu.GetAcc().Value);
            Assert.AreEqual(1UL, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Test_AtiIta()
        {
            StoreWord("10", Asm("сч, уиа -1(2)"));
            StoreWord("11", Asm("уи 2, пино 20(2)"));
            StoreWord("12", Asm("сч 2000, уи 2"));
            StoreWord("13", Asm("пио 20(2), сч"));
            StoreWord("14", Asm("счи 2, уи 3"));
            StoreWord("15", Asm("пио 20(3), слиа 1(3)"));
            StoreWord("16", Asm("пино 20(3), мода"));
            StoreWord("17", Asm("стоп 12345(6), мода"));
            StoreWord("20", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));

            Run();

            Assert.AreEqual(O("17"), _cpu.GetPc());
            Assert.AreEqual((ulong)O("77777"), _cpu.GetAcc().Value);
            Assert.AreEqual(O("77777"), _cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
        }

        [TestMethod]
        public void Test_Addr0()
        {
            StoreWord("10", Asm("уиа -1(2), счи 2"));
            StoreWord("11", Asm("зп, мода"));
            StoreWord("12", Asm("сч, уи 2"));
            StoreWord("13", Asm("пино 27(2), уиа -1(2)"));
            StoreWord("14", Asm("счи 2, мода"));
            StoreWord("15", Asm("зп, сч"));
            StoreWord("16", Asm("уи 2, пино 27(2)"));
            StoreWord("17", Asm("уиа -1(2), счи 2"));
            StoreWord("20", Asm("уи, мода"));
            StoreWord("21", Asm("счи, уи 2"));
            StoreWord("22", Asm("пино 27(2), уиа -1(2)"));
            StoreWord("23", Asm("счи 2, мода"));
            StoreWord("24", Asm("уи, счи"));
            StoreWord("25", Asm("уи 2, пино 27(2)"));
            StoreWord("26", Asm("стоп 12345(6), мода"));
            StoreWord("27", Asm("стоп 76543(2), мода"));

            Run();

            Assert.AreEqual(O("26"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(2));
        }

        [TestMethod]
        public void Test_AaxAoxAex()
        {
            StoreWord("10", Asm("сч 2000, и"));
            StoreWord("11", Asm("пе 22, сч 2000"));
            StoreWord("12", Asm("и 2000, нтж 2000"));
            StoreWord("13", Asm("пе 22, сч 2001"));
            StoreWord("14", Asm("и 2001, нтж 2001"));
            StoreWord("15", Asm("пе 22, сч 2001"));
            StoreWord("16", Asm("и 2002, пе 22"));
            StoreWord("17", Asm("сч 2001, или 2002"));
            StoreWord("20", Asm("нтж 2000, пе 22"));
            StoreWord("21", Asm("стоп 12345(6), мода"));
            StoreWord("22", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));
            StoreWord("2001", O("5252525252525252"));
            StoreWord("2002", O("2525252525252525"));

            Run();

            Assert.AreEqual(O("21"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Test_Arx()
        {
            StoreWord("10", Asm("сч 2002, слц 2001"));
            StoreWord("11", Asm("нтж 2003, пе 17"));
            StoreWord("12", Asm("сч 2000, слц 2001"));
            StoreWord("13", Asm("нтж 2001, пе 17"));
            StoreWord("14", Asm("сч 2000, слц 2000"));
            StoreWord("15", Asm("нтж 2000, пе 17"));
            StoreWord("16", Asm("стоп 12345(6), мода"));
            StoreWord("17", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));
            StoreWord("2001", O("1"));
            StoreWord("2002", O("13"));
            StoreWord("2003", O("14"));

            Run();

            Assert.AreEqual(O("16"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Test_Its()
        {
            StoreWord("10", Asm("уиа 2000(17), счи 17"));
            StoreWord("11", Asm("нтж 2003, уи 16"));
            StoreWord("12", Asm("уиа 11(1), уиа 22(2)"));
            StoreWord("13", Asm("уиа 33(3), счи 1"));
            StoreWord("14", Asm("счим 2, счим 3"));
            StoreWord("15", Asm("счим, сли 17(16)"));
            StoreWord("16", Asm("слиа -2(17), пино 25(17)"));
            StoreWord("17", Asm("сч 2000, нтж 2004"));
            StoreWord("20", Asm("пе 25, сч 2001"));
            StoreWord("21", Asm("нтж 2005, пе 25"));
            StoreWord("22", Asm("сч 2002, нтж 2006"));
            StoreWord("23", Asm("пе 25, мода"));
            StoreWord("24", Asm("стоп 12345(6), мода"));
            StoreWord("25", Asm("стоп 76543(2), мода"));
            StoreWord("2003", O("77777"));
            StoreWord("2004", O("11"));
            StoreWord("2005", O("22"));
            StoreWord("2006", O("33"));

            Run();

            Assert.AreEqual(O("24"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(O("11"), _cpu.GetM(1));
            Assert.AreEqual(O("22"), _cpu.GetM(2));
            Assert.AreEqual(O("33"), _cpu.GetM(3));
        }

        [TestMethod]
        public void Test_Sti()
        {
            StoreWord("10", Asm("уиа 2004(17), счи 17"));
            StoreWord("11", Asm("нтж 2004, уи 16"));
            StoreWord("12", Asm("уим, уим 3"));
            StoreWord("13", Asm("уим 2, уи 1"));
            StoreWord("14", Asm("сли 17(16), слиа 4(17)"));
            StoreWord("15", Asm("пино 31(17), слиа -33(3)"));
            StoreWord("16", Asm("пино 31(3), слиа -22(2)"));
            StoreWord("17", Asm("пино 31(2), слиа -11(1)"));
            StoreWord("20", Asm("пино 31(1), сч 2000"));
            StoreWord("21", Asm("зп 70776, зп 70777"));
            StoreWord("22", Asm("сч, уиа 70776(17)"));
            StoreWord("23", Asm("счм (17), нтж 2000"));
            StoreWord("24", Asm("пе 31, сч 70776"));
            StoreWord("25", Asm("пе 31, уиа 17(17)"));
            StoreWord("26", Asm("сч 2005, уим (17)"));
            StoreWord("27", Asm("нтж 2000, пе 31"));
            StoreWord("30", Asm("стоп 12345(6), мода"));
            StoreWord("31", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));
            StoreWord("2001", O("11"));
            StoreWord("2002", O("22"));
            StoreWord("2003", O("33"));
            StoreWord("2004", O("77777"));
            StoreWord("2005", O("70777"));

            Run();

            Assert.AreEqual(O("30"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(1));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
            Assert.AreEqual(O("70777"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Xts()
        {
            StoreWord("10", Asm("уиа 2000(17), счи 17"));
            StoreWord("11", Asm("нтж 2003, уи 16"));
            StoreWord("12", Asm("сч 2004, счм 2005"));
            StoreWord("13", Asm("счм 2006, счм"));
            StoreWord("14", Asm("сли 17(16), слиа -2(17)"));
            StoreWord("15", Asm("пино 23(17), сч 2000"));
            StoreWord("16", Asm("нтж 2004, пе 23"));
            StoreWord("17", Asm("сч 2001, нтж 2005"));
            StoreWord("20", Asm("пе 23, сч 2002"));
            StoreWord("21", Asm("нтж 2006, пе 23"));
            StoreWord("22", Asm("стоп 12345(6), мода"));
            StoreWord("23", Asm("стоп 76543(2), мода"));
            StoreWord("2003", O("77777"));
            StoreWord("2004", O("11"));
            StoreWord("2005", O("22"));
            StoreWord("2006", O("33"));

            Run();

            Assert.AreEqual(O("22"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Stx()
        {
            StoreWord("10", Asm("уиа 2003(17), счи 17"));
            StoreWord("11", Asm("нтж 2005, уи 16"));
            StoreWord("12", Asm("зпм, уи 3"));
            StoreWord("13", Asm("зпм 2004, уи 2"));
            StoreWord("14", Asm("зпм 2003, уи 1"));
            StoreWord("15", Asm("сли 17(16), слиа 4(17)"));
            StoreWord("16", Asm("пино 27(17), слиа -33(3)"));
            StoreWord("17", Asm("пино 27(3), слиа -22(2)"));
            StoreWord("20", Asm("пино 27(2), слиа -11(1)"));
            StoreWord("21", Asm("пино 27(1), нтж 2000"));
            StoreWord("22", Asm("пе 27, сч 2003"));
            StoreWord("23", Asm("нтж 2001, пе 27"));
            StoreWord("24", Asm("сч 2004, нтж 2002"));
            StoreWord("25", Asm("пе 27, мода"));
            StoreWord("26", Asm("стоп 12345(6), мода"));
            StoreWord("27", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("11"));
            StoreWord("2001", O("22"));
            StoreWord("2002", O("33"));
            StoreWord("2005", O("77777"));

            Run();

            Assert.AreEqual(O("26"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(1));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(15));
        }

        [TestMethod]
        public void Test_AsnAsx()
        {
            StoreWord("10", Asm("уиа -60(14), уиа 60(13)"));
            StoreWord("11", Asm("сч 2003, сда 77(13)"));
            StoreWord("12", Asm("нтж 2004(13), пе 33"));
            StoreWord("13", Asm("слиа -1(13), цикл 11(14)"));
            StoreWord("14", Asm("уиа -60(14), уиа 60(13)"));
            StoreWord("15", Asm("сч 2064, сда 20(13)"));
            StoreWord("16", Asm("нтж 2004(13), пе 33"));
            StoreWord("17", Asm("слиа -1(13), цикл 15(14)"));
            StoreWord("20", Asm("сч 2000, сд 2000"));
            StoreWord("21", Asm("пе 33, сч 2002"));
            StoreWord("22", Asm("сд 2065, нтж 2001"));
            StoreWord("23", Asm("пе 33, сч 2000"));
            StoreWord("24", Asm("сда 64, счмр"));
            StoreWord("25", Asm("нтж 2067, пе 33"));
            StoreWord("26", Asm("сч 2000, сда 104"));
            StoreWord("27", Asm("счмр, нтж 2066"));
            StoreWord("30", Asm("пе 33, сч 2000"));
            StoreWord("31", Asm("сд 2070, пе 33"));
            StoreWord("32", Asm("стоп 12345(6), мода"));
            StoreWord("33", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));
            StoreWord("2001", O("5252525252525252"));
            StoreWord("2002", O("2525252525252525"));
            StoreWord("2003", O("4000000000000000"));
            StoreWord("2004", 0L);
            StoreWord("2005", O("4000000000000000"));
            StoreWord("2006", O("2000000000000000"));
            StoreWord("2007", O("1000000000000000"));
            StoreWord("2010", O("0400000000000000"));
            StoreWord("2011", O("0200000000000000"));
            StoreWord("2012", O("0100000000000000"));
            StoreWord("2013", O("0040000000000000"));
            StoreWord("2014", O("0020000000000000"));
            StoreWord("2015", O("0010000000000000"));
            StoreWord("2016", O("0004000000000000"));
            StoreWord("2017", O("0002000000000000"));
            StoreWord("2020", O("0001000000000000"));
            StoreWord("2021", O("0000400000000000"));
            StoreWord("2022", O("0000200000000000"));
            StoreWord("2023", O("0000100000000000"));
            StoreWord("2024", O("0000040000000000"));
            StoreWord("2025", O("0000020000000000"));
            StoreWord("2026", O("0000010000000000"));
            StoreWord("2027", O("0000004000000000"));
            StoreWord("2030", O("0000002000000000"));
            StoreWord("2031", O("0000001000000000"));
            StoreWord("2032", O("0000000400000000"));
            StoreWord("2033", O("0000000200000000"));
            StoreWord("2034", O("0000000100000000"));
            StoreWord("2035", O("0000000040000000"));
            StoreWord("2036", O("0000000020000000"));
            StoreWord("2037", O("0000000010000000"));
            StoreWord("2040", O("0000000004000000"));
            StoreWord("2041", O("0000000002000000"));
            StoreWord("2042", O("0000000001000000"));
            StoreWord("2043", O("0000000000400000"));
            StoreWord("2044", O("0000000000200000"));
            StoreWord("2045", O("0000000000100000"));
            StoreWord("2046", O("0000000000040000"));
            StoreWord("2047", O("0000000000020000"));
            StoreWord("2050", O("0000000000010000"));
            StoreWord("2051", O("0000000000004000"));
            StoreWord("2052", O("0000000000002000"));
            StoreWord("2053", O("0000000000001000"));
            StoreWord("2054", O("0000000000000400"));
            StoreWord("2055", O("0000000000000200"));
            StoreWord("2056", O("0000000000000100"));
            StoreWord("2057", O("0000000000000040"));
            StoreWord("2060", O("0000000000000020"));
            StoreWord("2061", O("0000000000000010"));
            StoreWord("2062", O("0000000000000004"));
            StoreWord("2063", O("0000000000000002"));
            StoreWord("2064", O("0000000000000001"));
            StoreWord("2065", O("3777777777777777"));
            StoreWord("2066", O("7400000000000000"));
            StoreWord("2067", O("0000000000007777"));
            StoreWord("2070", O("0020000000000000"));

            Run();

            Assert.AreEqual(O("32"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(O("77777"), _cpu.GetM(11));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(12));
        }

        [TestMethod]
        public void Test_AcxAnx()
        {
            StoreWord("10", Asm("счи, чед"));
            StoreWord("11", Asm("пе 35, сч 2000"));
            StoreWord("12", Asm("чед, нтж 2003"));
            StoreWord("13", Asm("пе 35, сч 2007"));
            StoreWord("14", Asm("чед 2004, нтж 2001"));
            StoreWord("15", Asm("пе 35, уиа -60(14)"));
            StoreWord("16", Asm("уиа 60(13), уиа 2011(17)"));
            StoreWord("17", Asm("сч 2001, мода"));
            StoreWord("20", Asm("пино 21(13), сч"));
            StoreWord("21", Asm("зп 2010, нед"));
            StoreWord("22", Asm("счим 13, нтж (17)"));
            StoreWord("23", Asm("пе 35, сч 2010"));
            StoreWord("24", Asm("сда 77, счим 13"));
            StoreWord("25", Asm("и 2002, или (17)"));
            StoreWord("26", Asm("слиа -1(13), цикл 20(14)"));
            StoreWord("27", Asm("сч, нед 2000"));
            StoreWord("30", Asm("нтж 2000, пе 35"));
            StoreWord("31", Asm("уиа 1001(16), счи 16"));
            StoreWord("32", Asm("нед 2000, счмр"));
            StoreWord("33", Asm("нтж 2005, пе 35"));
            StoreWord("34", Asm("стоп 12345(6), мода"));
            StoreWord("35", Asm("стоп 76543(2), мода"));
            StoreWord("2000", Cw("07777777777777777"));
            StoreWord("2001", Cw("1"));
            StoreWord("2002", Cw("7"));
            StoreWord("2003", Cw("60"));
            StoreWord("2004", Cw("07777777777777750"));
            StoreWord("2005", Cw("00010000000000000"));
            StoreWord("2006", Cw("05252525252525252"));
            StoreWord("2007", Cw("02525252525252525"));

            Run();

            Assert.AreEqual(O("34"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(O("77777"), _cpu.GetM(11));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(12));
            Assert.AreEqual(O("1001"), _cpu.GetM(14));
            Assert.AreEqual(O("2011"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_ApxAux()
        {
            StoreWord("10", Asm("сч 2002, сбр 2000"));
            StoreWord("11", Asm("рзб 2001, нтж 2003"));
            StoreWord("12", Asm("пе 22, сч 2002"));
            StoreWord("13", Asm("сбр 2003, пе 22"));
            StoreWord("14", Asm("сч 2002, сбр 2002"));
            StoreWord("15", Asm("рзб 2003, нтж 2003"));
            StoreWord("16", Asm("пе 22, сч 2000"));
            StoreWord("17", Asm("рзб 2003, нтж 2003"));
            StoreWord("20", Asm("пе 22, мода"));
            StoreWord("21", Asm("стоп 12345(6), мода"));
            StoreWord("22", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("7777777777777777"));
            StoreWord("2001", O("3777777777777777"));
            StoreWord("2002", O("5252525252525252"));
            StoreWord("2003", O("2525252525252525"));

            Run();

            Assert.AreEqual(O("21"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Test_NtrRte()
        {
            StoreWord("10", Asm("уиа 2052(17), счи"));
            StoreWord("11", Asm("уиа 77(2), уиа -77(3)"));
            StoreWord("12", Asm("ржа (2), счрж 77"));
            StoreWord("13", Asm("зп 2051, счим 2"));
            StoreWord("14", Asm("сда 27, нтж (17)"));
            StoreWord("15", Asm("пе 65, пе 65"));
            StoreWord("16", Asm("рж, счрж 77"));
            StoreWord("17", Asm("пе 65, рж 2051"));
            StoreWord("20", Asm("счрж 77, счим 2"));
            StoreWord("21", Asm("сда 27, нтж (17)"));
            StoreWord("22", Asm("пе 65, пе 65"));
            StoreWord("23", Asm("уиа 2052(17), рж (17)"));
            StoreWord("24", Asm("уиа 2001(17), счрж 77"));
            StoreWord("25", Asm("счим 2, сда 27"));
            StoreWord("26", Asm("нтж (17), пе 65"));
            StoreWord("27", Asm("слиа -1(2), цикл 12(3)"));
            StoreWord("30", Asm("ржа 77, счрж 41"));
            StoreWord("31", Asm("нтж 4057, пе 65"));
            StoreWord("32", Asm("ржа, по 65"));
            StoreWord("33", Asm("пе 34, пб 65"));
            StoreWord("34", Asm("ржа 7, пе 65"));
            StoreWord("35", Asm("ржа 13, по 65"));
            StoreWord("36", Asm("или, пе 65"));
            StoreWord("37", Asm("ржа 23, пе 65"));
            StoreWord("40", Asm("сч 2000, по 65"));
            StoreWord("41", Asm("ржа 13, пе 65"));
            StoreWord("42", Asm("ржа 23, по 65"));
            StoreWord("43", Asm("ржа 30, по 65"));
            StoreWord("44", Asm("ржа 14, пе 65"));
            StoreWord("45", Asm("сч 4060, ржа 24"));
            StoreWord("46", Asm("пе 65, сч 2000"));
            StoreWord("47", Asm("нтж, сч"));
            StoreWord("50", Asm("счмр, нтж 2000"));
            StoreWord("51", Asm("пе 65, слц"));
            StoreWord("52", Asm("по 65, слц 2000"));
            StoreWord("53", Asm("пе 65, и 2000"));
            StoreWord("54", Asm("по 65, мода"));
            StoreWord("55", Asm("сч, ржа 77"));
            StoreWord("56", Asm("зп 2051, счрж 77"));
            StoreWord("57", Asm("нтж 4061, пе 65"));
            StoreWord("60", Asm("сч 2000, ржа"));
            StoreWord("61", Asm("сч, по 63"));
            StoreWord("62", Asm("пб 65, мода"));
            StoreWord("63", Asm("ржа, сч"));
            StoreWord("64", Asm("по 66, мода"));
            StoreWord("65", Asm("стоп 76543(2), мода"));
            StoreWord("66", Asm("стоп 12345(6), мода"));
            StoreWord("2000", O("7777777777777777"));
            StoreWord("2001", O("5252525252525252"));
            StoreWord("2002", O("2525252525252525"));
            StoreWord("2003", O("1"));
            StoreWord("2004", O("2"));
            StoreWord("2005", O("3"));
            StoreWord("2006", O("60"));
            StoreWord("2007", O("7777777700000001"));
            StoreWord("4057", O("2040000000000000"));
            StoreWord("4060", O("1"));
            StoreWord("4061", O("3740000000000000"));

            Run();

            Assert.AreEqual(O("66"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(4UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("77777"), _cpu.GetM(2));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(3));
            Assert.AreEqual(O("2001"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Yta()
        {
            StoreWord("10", Asm("сч 2000, сда 160"));
            StoreWord("11", Asm("счмр, зп 2002"));
            StoreWord("12", Asm("счмр, нтж 2000"));
            StoreWord("13", Asm("пе 70, сч 2002"));
            StoreWord("14", Asm("нтж 2000, пе 70"));
            StoreWord("15", Asm("сч 2000, сда 160"));
            StoreWord("16", Asm("ржа 23, счмр 123"));
            StoreWord("17", Asm("зп 2002, счмр 65"));
            StoreWord("20", Asm("нтж 2003, пе 70"));
            StoreWord("21", Asm("сч 2002, нтж 2004"));
            StoreWord("22", Asm("пе 70, сч 2000"));
            StoreWord("23", Asm("нтж 2001, сда 160"));
            StoreWord("24", Asm("ржа 13, счмр 123"));
            StoreWord("25", Asm("зп 2002, счмр 65"));
            StoreWord("26", Asm("нтж 2005, пе 70"));
            StoreWord("27", Asm("сч 2002, нтж 2006"));
            StoreWord("30", Asm("пе 70, сч 2000"));
            StoreWord("31", Asm("сда 160, ржа 3"));
            StoreWord("32", Asm("счмр 123, зп 2002"));
            StoreWord("33", Asm("счмр 65, нтж 2003"));
            StoreWord("34", Asm("пе 70, сч 2002"));
            StoreWord("35", Asm("нтж 2004, пе 70"));
            StoreWord("36", Asm("сч 2000, сда 160"));
            StoreWord("37", Asm("и 2001, счмр"));
            StoreWord("40", Asm("пе 70, сч 2000"));
            StoreWord("41", Asm("сда 160, или 2001"));
            StoreWord("42", Asm("счмр, пе 70"));
            StoreWord("43", Asm("сч 2000, сда 160"));
            StoreWord("44", Asm("слц 2001, ржа 7"));
            StoreWord("45", Asm("счмр, пе 70"));
            StoreWord("46", Asm("сч 2000, сда 160"));
            StoreWord("47", Asm("чед 2001, счмр"));
            StoreWord("50", Asm("пе 70, сч 2000"));
            StoreWord("51", Asm("сда 160, сбр 2001"));
            StoreWord("52", Asm("счмр, пе 70"));
            StoreWord("53", Asm("сч 2000, сда 160"));
            StoreWord("54", Asm("рзб 2001, счмр"));
            StoreWord("55", Asm("пе 70, сч 2000"));
            StoreWord("56", Asm("по 70, счмр"));
            StoreWord("57", Asm("нтж 2000, пе 70"));
            StoreWord("60", Asm("и, сч 2000"));
            StoreWord("61", Asm("пе 62, пб 70"));
            StoreWord("62", Asm("счмр, нтж 2000"));
            StoreWord("63", Asm("пе 70, и"));
            StoreWord("64", Asm("сч 2000, нтж"));
            StoreWord("65", Asm("счмр, нтж 2000"));
            StoreWord("66", Asm("пе 70, мода"));
            StoreWord("67", Asm("стоп 12345(6), мода"));
            StoreWord("70", Asm("стоп 76543(2), мода"));
            StoreWord("2000", O("1234567123456712"));
            StoreWord("2001", O("7777777777777777"));
            StoreWord("2002", O("0"));
            StoreWord("2003", O("414567123456712"));
            StoreWord("2004", O("1154567123456712"));
            StoreWord("2005", O("403210654321065"));
            StoreWord("2006", O("1143210654321065"));

            Run();

            Assert.AreEqual(O("67"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Test_EanEsnEaxEsx()
        {
            StoreWord("10", Asm("уиа 77602(14), сч 2000"));
            StoreWord("11", Asm("зп 2003, мода"));
            StoreWord("12", Asm("сч 2003, слпа 77"));
            StoreWord("13", Asm("зп 2003, сда 151"));
            StoreWord("14", Asm("уи 16, сли 16(14)"));
            StoreWord("15", Asm("пино 34(16), цикл 12(14)"));
            StoreWord("16", Asm("сч 2003, вчпа 101"));
            StoreWord("17", Asm("по 34, уиа 77602(14)"));
            StoreWord("20", Asm("сч 2001, зп 2003"));
            StoreWord("21", Asm("уиа -1(13), мода"));
            StoreWord("22", Asm("сч 2003, вчп 2002"));
            StoreWord("23", Asm("зп 2003, сда 151"));
            StoreWord("24", Asm("уи 16, сли 16(13)"));
            StoreWord("25", Asm("пино 34(16), слиа -1(13)"));
            StoreWord("26", Asm("цикл 22(14), сч 2003"));
            StoreWord("27", Asm("слп 2001, нтж 2002"));
            StoreWord("30", Asm("пе 34, сч 2004"));
            StoreWord("31", Asm("слп 2005, нтж 2006"));
            StoreWord("32", Asm("пе 34, мода"));
            StoreWord("33", Asm("стоп 12345(6), мода"));
            StoreWord("34", Asm("стоп 76543(2), мода"));
            StoreWord("2000", Cw("07750000000000000"));
            StoreWord("2001", Cw("00010000000000000"));
            StoreWord("2002", Cw("03750000000000000"));
            StoreWord("2003", Cw("0"));
            StoreWord("2004", Cw("07030000000000000"));
            StoreWord("2005", Cw("04010000000000000"));
            StoreWord("2006", Cw("06760000000000000"));

            Run();

            Assert.AreEqual(O("33"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(4UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("77600"), _cpu.GetM(11));
            Assert.AreEqual(0UL, (ulong)_cpu.GetM(14));
        }

        [TestMethod]
        public void Test_AaxAsxXsa()
        {
            StoreWord("10", Asm("уиа 2000(17), ржа 3"));
            StoreWord("11", Asm("уиа 100(16), счи 16"));
            StoreWord("12", Asm("вч 2012, по 57"));
            StoreWord("13", Asm("сл 2013, пе 57"));
            StoreWord("14", Asm("или, пе 57"));
            StoreWord("15", Asm("сч 2014, вчоб 2013"));
            StoreWord("16", Asm("вч 2015, пе 57"));
            StoreWord("17", Asm("или, пе 57"));
            StoreWord("20", Asm("сч 2014, счм 2013"));
            StoreWord("21", Asm("счм 2014, счм 2016"));
            StoreWord("22", Asm("вч (17), пе 57"));
            StoreWord("23", Asm("сл (17), вчоб (17)"));
            StoreWord("24", Asm("пе 57, или"));
            StoreWord("25", Asm("пе 57, сч 2017"));
            StoreWord("26", Asm("вч 2020, по 57"));
            StoreWord("27", Asm("сл 2021, пе 57"));
            StoreWord("30", Asm("или, по 57"));
            StoreWord("31", Asm("нтж 2022, пе 57"));
            StoreWord("32", Asm("сч 2023, вч 2024"));
            StoreWord("33", Asm("нтж 2025, пе 57"));
            StoreWord("34", Asm("сч 2024, вч 2023"));
            StoreWord("35", Asm("нтж 2026, пе 57"));
            StoreWord("36", Asm("ржа 2, сч 2021"));
            StoreWord("37", Asm("счм 2027, счм 2021"));
            StoreWord("40", Asm("счм 2027, сл (17)"));
            StoreWord("41", Asm("вч (17), вчоб (17)"));
            StoreWord("42", Asm("пе 57, ржа 2"));
            StoreWord("43", Asm("сч 2030, вч 2031"));
            StoreWord("44", Asm("пе 57, нтж 2027"));
            StoreWord("45", Asm("пе 57, ржа 77"));
            StoreWord("46", Asm("сч 2032, сл 2032"));
            StoreWord("47", Asm("ржа, нтж 2033"));
            StoreWord("50", Asm("пе 57, ржа"));
            StoreWord("51", Asm("сч 2034, сл 2035"));
            StoreWord("52", Asm("нтж 2036, пе 57"));
            StoreWord("53", Asm("сч 2032, вчоб 2037"));
            StoreWord("54", Asm("счмр 100, нтж 2040"));
            StoreWord("55", Asm("пе 57, мода"));
            StoreWord("56", Asm("стоп 12345(6), мода"));
            StoreWord("57", Asm("стоп 76543(2), мода"));
            StoreWord("2012", Cw("00000000000000101"));
            StoreWord("2013", Cw("1"));
            StoreWord("2014", Cw("2"));
            StoreWord("2015", Cw("00037777777777777"));
            StoreWord("2016", Cw("3"));
            StoreWord("2017", Cw("06400000000000100"));
            StoreWord("2020", Cw("06400000000000102"));
            StoreWord("2021", Cw("04110000000000000"));
            StoreWord("2022", Cw("06400000000000000"));
            StoreWord("2023", Cw("06420000000000000"));
            StoreWord("2024", Cw("06420000000000001"));
            StoreWord("2025", Cw("06437777777777777"));
            StoreWord("2026", Cw("06400000000000001"));
            StoreWord("2027", Cw("04114000000000000"));
            StoreWord("2030", Cw("04050000000000000"));
            StoreWord("2031", Cw("04060000000000000"));
            StoreWord("2032", Cw("00010000000000000"));
            StoreWord("2033", Cw("00050000000000000"));
            StoreWord("2034", Cw("07700000000001000"));
            StoreWord("2035", Cw("04000000000000001"));
            StoreWord("2036", Cw("06010000000000001"));
            StoreWord("2037", Cw("04010000000000000"));
            StoreWord("2040", Cw("03757777777600000"));

            Run();

            Assert.AreEqual(O("56"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(4UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("2000"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Amx()
        {
            StoreWord("10", Asm("уиа 2001(17), ржа 3"));
            StoreWord("11", Asm("уиа 100(16), счи 16"));
            StoreWord("12", Asm("вчаб 2013, по 36"));
            StoreWord("13", Asm("нтж 2000, пе 36"));
            StoreWord("14", Asm("сч 2014, вчаб 2015"));
            StoreWord("15", Asm("нтж 2016, пе 36"));
            StoreWord("16", Asm("сч 2000, вчаб 2000"));
            StoreWord("17", Asm("пе 36, или"));
            StoreWord("20", Asm("пе 36, сч 2017"));
            StoreWord("21", Asm("счм 2020, вчаб (17)"));
            StoreWord("22", Asm("пе 36, нтж 2021"));
            StoreWord("23", Asm("пе 36, сч 2022"));
            StoreWord("24", Asm("вчаб 2021, нтж 2023"));
            StoreWord("25", Asm("пе 36, сч 2024"));
            StoreWord("26", Asm("счм 2025, вчаб (17)"));
            StoreWord("27", Asm("нтж 2026, пе 36"));
            StoreWord("30", Asm("ржа, сч 2027"));
            StoreWord("31", Asm("счм 2030, вчаб (17)"));
            StoreWord("32", Asm("нтж 2024, пе 36"));
            StoreWord("33", Asm("сч 2031, вчаб 2032"));
            StoreWord("34", Asm("нтж 2033, пе 36"));
            StoreWord("35", Asm("стоп 12345(6), мода"));
            StoreWord("36", Asm("стоп 76543(2), мода"));
            StoreWord("2000", Cw("00037'7777'7777'7777"));
            StoreWord("2013", Cw("00000'0000'0000'0101"));
            StoreWord("2014", Cw("04160'0000'0000'0000"));
            StoreWord("2015", Cw("06400'0000'0000'0000"));
            StoreWord("2016", Cw("06400'0000'0000'0010"));
            StoreWord("2017", Cw("00000'0000'0000'0002"));
            StoreWord("2020", Cw("00000'0000'0000'0003"));
            StoreWord("2021", Cw("00000'0000'0000'0001"));
            StoreWord("2022", Cw("00067'7777'7777'7777"));
            StoreWord("2023", Cw("00050'0000'0000'0000"));
            StoreWord("2024", Cw("04050'0000'0000'0000"));
            StoreWord("2025", Cw("06427'7777'7777'7777"));
            StoreWord("2026", Cw("06410'0000'0000'0000"));
            StoreWord("2027", Cw("06410'0000'0000'0002"));
            StoreWord("2030", Cw("06410'0000'0000'0003"));
            StoreWord("2031", Cw("04060'0000'0000'0000"));
            StoreWord("2032", Cw("04057'7777'7777'7765"));
            StoreWord("2033", Cw("01653'0000'0000'0000"));

            Run();

            Assert.AreEqual(O("35"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(4UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("2001"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Avx()
        {
            StoreWord("10", Asm("уиа 2002(17), ржа 3"));
            StoreWord("11", Asm("уиа 100(16), счи 16"));
            StoreWord("12", Asm("знак 2000, пе 45"));
            StoreWord("13", Asm("нтж 2014, пе 45"));
            StoreWord("14", Asm("счи 16, знак 2001"));
            StoreWord("15", Asm("по 45, нтж 2015"));
            StoreWord("16", Asm("пе 45, сч 2001"));
            StoreWord("17", Asm("знак 2001, пе 45"));
            StoreWord("20", Asm("нтж 2000, пе 45"));
            StoreWord("21", Asm("сч 2000, знак 2001"));
            StoreWord("22", Asm("по 45, нтж 2016"));
            StoreWord("23", Asm("пе 45, сч 2017"));
            StoreWord("24", Asm("счм 2020, знак (17)"));
            StoreWord("25", Asm("пе 45, нтж 2021"));
            StoreWord("26", Asm("пе 45, ржа"));
            StoreWord("27", Asm("сч 2001, знак 2001"));
            StoreWord("30", Asm("пе 45, нтж 2000"));
            StoreWord("31", Asm("пе 45, сч 2000"));
            StoreWord("32", Asm("знак 2001, по 45"));
            StoreWord("33", Asm("нтж 2001, пе 45"));
            StoreWord("34", Asm("сч 2022, знак 2001"));
            StoreWord("35", Asm("по 45, нтж 2023"));
            StoreWord("36", Asm("пе 45, сч 2024"));
            StoreWord("37", Asm("знак 2001, пе 45"));
            StoreWord("40", Asm("нтж, пе 45"));
            StoreWord("41", Asm("сч 2025, знак 2001"));
            StoreWord("42", Asm("пе 45, нтж 2026"));
            StoreWord("43", Asm("пе 45, мода"));
            StoreWord("44", Asm("стоп 12345(6), мода"));
            StoreWord("45", Asm("стоп 76543(2), мода"));
            StoreWord("2000", Cw("04050000000000000"));
            StoreWord("2001", Cw("04020000000000000"));
            StoreWord("2014", Cw("00000000000000100"));
            StoreWord("2015", Cw("00037777777777700"));
            StoreWord("2016", Cw("04070000000000000"));
            StoreWord("2017", Cw("04060000000000000"));
            StoreWord("2020", Cw("04124000000000000"));
            StoreWord("2021", Cw("04114000000000000"));
            StoreWord("2022", Cw("07757777777777777"));
            StoreWord("2023", Cw("07760000000000001"));
            StoreWord("2024", Cw("00010000000000000"));
            StoreWord("2025", Cw("00027777777777777"));
            StoreWord("2026", Cw("00010000000000001"));

            Run();

            Assert.AreEqual(O("44"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(4UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("2002"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Multiply()
        {
            StoreWord("10", Asm("уиа 2001(17), ржа 3"));
            StoreWord("11", Asm("сч 2013, умн 2014"));
            StoreWord("12", Asm("зп (17), счмр 100"));
            StoreWord("13", Asm("зпм 2000, нтж 2015"));
            StoreWord("14", Asm("пе 37, сч 2000"));
            StoreWord("15", Asm("нтж 2016, пе 37"));
            StoreWord("16", Asm("сч 2017, умн 2020"));
            StoreWord("17", Asm("зп (17), счмр 100"));
            StoreWord("20", Asm("зпм 2000, нтж 2021"));
            StoreWord("21", Asm("пе 37, сч 2000"));
            StoreWord("22", Asm("слпа 130, нтж 2022"));
            StoreWord("23", Asm("пе 37, ржа"));
            StoreWord("24", Asm("сч 2023, умн 2024"));
            StoreWord("25", Asm("нтж 2024, пе 37"));
            StoreWord("26", Asm("сч 2024, умн 2023"));
            StoreWord("27", Asm("нтж 2024, пе 37"));
            StoreWord("30", Asm("сч 2024, умн 2024"));
            StoreWord("31", Asm("нтж 2023, пе 37"));
            StoreWord("32", Asm("ржа 2, сч 2025"));
            StoreWord("33", Asm("умн 2026, зп 2000"));
            StoreWord("34", Asm("нтж 2026, нтж 2027"));
            StoreWord("35", Asm("пе 37, мода"));
            StoreWord("36", Asm("стоп 12345(6), мода"));
            StoreWord("37", Asm("стоп 76543(2), мода"));
            StoreWord("2013", Cw("06400000000000005"));
            StoreWord("2014", Cw("02400000000000015"));
            StoreWord("2015", Cw("05000000000000000"));
            StoreWord("2016", Cw("05000000000000101"));
            StoreWord("2017", Cw("02400000000000005"));
            StoreWord("2020", Cw("06437777777777763"));
            StoreWord("2021", Cw("05037777777777777"));
            StoreWord("2022", Cw("06417777777777677"));
            StoreWord("2023", Cw("04050000000000000"));
            StoreWord("2024", Cw("04020000000000000"));
            StoreWord("2025", Cw("04110000000000000"));
            StoreWord("2026", Cw("04114000000000000"));
            StoreWord("2027", Cw("00040000000000000"));

            Run();

            Assert.AreEqual(O("36"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(6UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("2001"), _cpu.GetM(15));
        }

        [TestMethod]
        public void Test_Divide()
        {
            StoreWord("10", Asm("уиа 2000(17), ржа 3"));
            StoreWord("11", Asm("сч 2012, дел 2013"));
            StoreWord("12", Asm("нтж 2014, пе 14"));
            StoreWord("13", Asm("стоп 12345(6), мода"));
            StoreWord("14", Asm("стоп 76543(2), мода"));
            StoreWord("2012", O("4154000000000000"));
            StoreWord("2013", O("4114000000000000"));
            StoreWord("2014", O("4110000000000000"));

            Run();

            Assert.AreEqual(O("13"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(7UL, (ulong)_cpu.GetRau());
            Assert.AreEqual(O("2000"), _cpu.GetM(15));
        }

        // ─── Intercept (перехват арифметической ошибки) ────────────────────
        // Точный порт C++ Processor::intercept (dubna/processor.cpp:68-85).

        [TestMethod]
        public void Intercept_DefaultDisabled_ReturnsFalse()
        {
            // InterceptCount = 0 (default) → перехват отключён.
            Assert.IsFalse(_cpu.Intercept("Arithmetic overflow"));
            Assert.IsFalse(_cpu.Intercept("Division by zero"));
        }

        [TestMethod]
        public void Intercept_Overflow_CountDecremented_PcSetToAddr()
        {
            _cpu.InterceptCount = 1;
            _cpu.InterceptAddr = 0x1234; // произвольный адрес

            // Перед вызовом — PC другой, flags не сброслены.
            _cpu.SetPc(0x5555);
            _cpu.SetRau(0x07);

            bool result = _cpu.Intercept("Arithmetic overflow");

            Assert.IsTrue(result);
            Assert.AreEqual(0, _cpu.InterceptCount);  // count--
            Assert.AreEqual(0x1234UL, (ulong)_cpu.GetPc());     // PC = intercept_addr
        }

        [TestMethod]
        public void Intercept_DivZero_CountDecremented_PcSetToAddr()
        {
            _cpu.InterceptCount = 3;
            _cpu.InterceptAddr = 0x7FFF;

            bool result = _cpu.Intercept("Division by zero");

            Assert.IsTrue(result);
            Assert.AreEqual(2, _cpu.InterceptCount);  // 3 → 2
            Assert.AreEqual(0x7FFFUL, (ulong)_cpu.GetPc());
        }

        [TestMethod]
        public void Intercept_UnknownMessage_ReturnsFalse()
        {
            _cpu.InterceptCount = 1;
            Assert.IsFalse(_cpu.Intercept("Illegal instruction 002 рег/mod"));
            Assert.AreEqual(1, _cpu.InterceptCount);  // не уменьшен
        }

        [TestMethod]
        public void Intercept_OnceOnly_SecondCall_ReturnsFalse()
        {
            _cpu.InterceptCount = 1;
            _cpu.InterceptAddr = 0x2000;

            Assert.IsTrue(_cpu.Intercept("Arithmetic overflow"));
            Assert.AreEqual(0, _cpu.InterceptCount);

            // Второй вызов — count уже 0 → перехват отключён.
            Assert.IsFalse(_cpu.Intercept("Arithmetic overflow"));
        }

        [TestMethod]
        public void Intercept_ResetsFlagsAndMod()
        {
            // Настроим "грязные" флаги и MOD.
            _cpu.InterceptCount = 1;
            _cpu.InterceptAddr = 0x3000;
            _cpu.SetPc(0x6000);
            _cpu.SetRau(0x0F);

            // В C++ intercept: right_instr_flag=false, apply_mod_reg=false, MOD=0.
            // В C# нет прямого доступа к _rightInstrFlag/_applyModReg — проверяем
            // через OnRightInstruction (public property).
            bool wasRight = _cpu.OnRightInstruction;

            _cpu.Intercept("Division by zero");

            Assert.AreEqual(0x3000UL, (ulong)_cpu.GetPc());
            Assert.IsFalse(_cpu.OnRightInstruction); // right_instr_flag = false
        }

        [TestMethod]
        public void StackCorrection_NoOp()
        {
            // StackCorrection() без ожидающей поправки (corr_stack == 0) не меняет состояние.
            // Семантика corr_stack при исключении покрыта
            // ProcessorStateRegressionTests.StackCorrection_RestoresPreparedStackAfterArithmeticException.
            long pcBefore = _cpu.GetPc();
            _cpu.StackCorrection();
            Assert.AreEqual(pcBefore, _cpu.GetPc());
        }

        // ─── Стек (точный порт C++ cpu_test.cpp: stack) ────────────────────
        // Проверяет стек (M[15]) в связке с зп/сч/счм/уим/уи/сда/мод/переходами.

        [TestMethod]
        public void Test_Stack()
        {
            StoreWord("10", Asm("уиа 2010(12), счи 12"));
            StoreWord("11", Asm("нтж 2000, уи 12"));
            StoreWord("12", Asm("сч, зп 2010"));
            StoreWord("13", Asm("зп 2011, зп 2012"));
            StoreWord("14", Asm("уиа 2011(17), сч 2000"));
            StoreWord("15", Asm("зп (17), сли 17(12)"));
            StoreWord("16", Asm("слиа -1(17), пв 102(15)"));
            StoreWord("17", Asm("уиа 2007(17), сч"));
            StoreWord("20", Asm("зп 1(17), зп 3(17)"));
            StoreWord("21", Asm("сч 2000, мода 2"));
            StoreWord("22", Asm("зп (17), сли 17(12)"));
            StoreWord("23", Asm("слиа 2(17), пв 102(15)"));
            StoreWord("24", Asm("сч, зп 2011"));
            StoreWord("25", Asm("уиа 2013(17), сч (17)"));
            StoreWord("26", Asm("уи 2, сда 130"));
            StoreWord("27", Asm("уи 3, сч (17)"));
            StoreWord("30", Asm("уи 4, сда 130"));
            StoreWord("31", Asm("уи 5, сч (17)"));
            StoreWord("32", Asm("уи 6, сда 140"));
            StoreWord("33", Asm("уи 7, пв 117(15)"));
            StoreWord("34", Asm("уиа 2013(17), мода -1"));
            StoreWord("35", Asm("сч (17), уи 6"));
            StoreWord("36", Asm("сда 140, уи 7"));
            StoreWord("37", Asm("сч -2(17), уи 4"));
            StoreWord("40", Asm("сда 140, уи 5"));
            StoreWord("41", Asm("сч -3(17), уи 2"));
            StoreWord("42", Asm("сда 140, уи 3"));
            StoreWord("43", Asm("слиа -3(17), пв 117(15)"));
            StoreWord("44", Asm("уиа 1(4), уиа -1(7)"));
            StoreWord("45", Asm("уиа -1(3), уиа 2013(17)"));
            StoreWord("46", Asm("мод (17), уиа (6)"));
            StoreWord("47", Asm("мод (17), уиа (4)"));
            StoreWord("50", Asm("мод (17), уиа (2)"));
            StoreWord("51", Asm("мода, пв 117(15)"));
            StoreWord("52", Asm("уиа 2010(17), сч 2003"));
            StoreWord("53", Asm("счм, счм 2004"));
            StoreWord("54", Asm("счм 2005, мод -2(17)"));
            StoreWord("55", Asm("уиа (2), пино 101(2)"));
            StoreWord("56", Asm("сли 17(12), слиа -2(17)"));
            StoreWord("57", Asm("пино 101(17), уиа 2010(17)"));
            StoreWord("60", Asm("сч 2001, счм 2002"));
            StoreWord("61", Asm("и (17), пе 101"));
            StoreWord("62", Asm("сч 2001, счм 2002"));
            StoreWord("63", Asm("слц (17), счм 2001"));
            StoreWord("64", Asm("счм 2002, или (17)"));
            StoreWord("65", Asm("нтж (17), пе 101"));
            StoreWord("66", Asm("сч 2001, счм 2002"));
            StoreWord("67", Asm("счм 2000, сбр (17)"));
            StoreWord("70", Asm("рзб (17), нтж 2001"));
            StoreWord("71", Asm("пе 101, счм 2000"));
            StoreWord("72", Asm("чед (17), нтж 2006"));
            StoreWord("73", Asm("пе 101, счм 2000"));
            StoreWord("74", Asm("нед (17), нтж 2003"));
            StoreWord("75", Asm("пе 101, сч 2000"));
            StoreWord("76", Asm("зп (17), сд (17)"));
            StoreWord("77", Asm("пе 101, мода"));
            StoreWord("100", Asm("стоп 12345(6), мода")); // Magic opcode: Pass
            StoreWord("101", Asm("стоп 76543(2), мода")); // Magic opcode: Fail
            StoreWord("102", Asm("пино 101(17), сч 2010"));
            StoreWord("103", Asm("уи 2, пино 101(2)"));
            StoreWord("104", Asm("сда 130, уи 2"));
            StoreWord("105", Asm("пино 101(2), сч 2012"));
            StoreWord("106", Asm("уи 2, пино 101(2)"));
            StoreWord("107", Asm("сда 130, уи 2"));
            StoreWord("110", Asm("пино 101(2), сч 2011"));
            StoreWord("111", Asm("уи 2, сда 130"));
            StoreWord("112", Asm("уи 3, слиа 1(2)"));
            StoreWord("113", Asm("пино 101(2), слиа 1(3)"));
            StoreWord("114", Asm("пино 101(3), сч 2007"));
            StoreWord("115", Asm("зп 2010, зп 2011"));
            StoreWord("116", Asm("зп 2012, пб (15)"));
            StoreWord("117", Asm("сли 17(12), слиа 1(17)"));
            StoreWord("120", Asm("пино 101(17), слиа -1(2)"));
            StoreWord("121", Asm("пино 101(2), слиа 1(3)"));
            StoreWord("122", Asm("пино 101(3), пино 101(4)"));
            StoreWord("123", Asm("пино 101(5), слиа -1(6)"));
            StoreWord("124", Asm("пино 101(6), слиа 1(7)"));
            StoreWord("125", Asm("пино 101(7), пб (15)"));
            StoreWord("2000", Cw("07777777777777777"));
            StoreWord("2001", Cw("05252525252525252"));
            StoreWord("2002", Cw("02525252525252525"));
            StoreWord("2003", Cw("00000000000000001"));
            StoreWord("2004", Cw("00000000000000002"));
            StoreWord("2005", Cw("00000000000000003"));
            StoreWord("2006", Cw("00000000000000060"));
            StoreWord("2007", Cw("07777777700000001"));

            Run("10");

            Assert.AreEqual(O("100"), _cpu.GetPc());
            Assert.AreEqual(0UL, _cpu.GetAcc().Value);
            Assert.AreEqual(0UL, _cpu.GetRmr().Value);
            Assert.AreEqual(O("2010"), _cpu.GetM(15));
        }

        // ─── Нелегальные инструкции (порт исключений dubna/processor.cpp) ──

        [TestMethod]
        public void Test_Illegal_Reg_Mod_Throws()
        {
            // 002 рег/mod — привилегированная, не исполняется.
            ExpectIllegal("002 рег/mod", Asm("рег 0(0)"));
        }

        [TestMethod]
        public void Test_Illegal_Zpp_Throws()
        {
            // 032 зпп — нелегальная.
            ExpectIllegal("032 зпп", Asm("зпп 0(0)"));
        }

        [TestMethod]
        public void Test_Illegal_Schp_Throws()
        {
            // 033 счп — нелегальная.
            ExpectIllegal("033 счп", Asm("счп 0(0)"));
        }

        [TestMethod]
        public void Test_Illegal_Sop_Throws()
        {
            // 046 соп — нелегальная.
            ExpectIllegal("046 соп", Asm("соп 0(0)"));
        }

        [TestMethod]
        public void Test_Illegal_Op47_Throws()
        {
            // 047 — нелегальная.
            ExpectIllegal("047", Asm("э47 0(0)"));
        }

        // ─── Остальные не покрытые коды инструкций ─────────────────────────

        [TestMethod]
        public void Test_E36_BranchWhenMZero()
        {
            // э36 — переход при M[reg]==0 (семантика идентична ПИО).
            _cpu.SetM(2, 0);
            StoreWord("10", Asm("втбрз 12(2), сч 0"));
            StoreWord("11", Asm("стоп 76543(2), сч 0"));
            StoreWord("12", Asm("стоп 12345(6), сч 0"));
            Run();
            Assert.AreEqual(O("12"), _cpu.GetPc());
        }

        [TestMethod]
        public void Test_E36_NoBranchWhenMNonZero()
        {
            // э36 — при M[reg]!=0 переход не происходит, исполняется следующая.
            _cpu.SetM(2, 1);
            StoreWord("10", Asm("втбрз 12(2), сч 0"));
            StoreWord("11", Asm("стоп 76543(2), сч 0"));
            StoreWord("12", Asm("стоп 12345(6), сч 0"));
            Run();
            Assert.AreEqual(O("11"), _cpu.GetPc());
        }

        [TestMethod]
        public void Test_Vypr_Illegal_Throws()
        {
            // 0320 выпр/iret — нелегальная инструкция (как в C++ референсе).
            ExpectIllegal("0320 выпр/iret", Asm("выпр, сч 0"));
        }

        // ─── Экстракоды: диспетчеризация (э50..э77, э20/э21) ───────────────
        // Путь в InstructionExecutor:
        //   aex = addr + M[reg]; M[14] = aex;
        //   if (ExtracodeHandler != null && ExtracodeHandler(op, aex)) break;
        //   else throw "Extracode N not implemented".

        [TestMethod]
        public void Test_Extracode_NoHandler_Throws()
        {
            // э50 без обработчика — инструкция не может быть выполнена.
            ExpectIllegal("э50 (no handler)", Asm("э50 0(0)"));
        }

        [TestMethod]
        public void Test_Extracode_HandlerFalse_Throws()
        {
            // э64: обработчик возвращает false → поведение как без обработчика.
            _cpu.ExtracodeHandler = (op, aex) => false;
            ExpectIllegal("э64 (handler=false)", Asm("э64 0(0)"));
        }

        [TestMethod]
        public void Test_Extracode_HandlerTrue_SetsExchangeReg_AndContinues()
        {
            // э50 100(2), M[2]=5 → aex = 0o100 + 5 = 0o105; M[14] = aex; продолжаем.
            uint seenOp = 0, seenAex = 0;
            _cpu.ExtracodeHandler = (int op, uint aex) => { seenOp = (uint)op; seenAex = aex; return true; };
            _cpu.SetM(2, 5);
            StoreWord("10", Asm("э50 100(2)"));
            StoreWord("11", Asm("стоп, сч 0"));

            _cpu.SetPc((uint)O("10"));
            bool stopped = _cpu.Step();

            Assert.IsFalse(stopped, "extracode не является СТОП");
            Assert.AreEqual(40u, seenOp, "opcode = E50 (0o50)");
            Assert.AreEqual((uint)O("105"), seenAex, "aex = 0o100 + 5");
            Assert.AreEqual((uint)O("105"), _cpu.GetM(14), "M[14] = исполнительный адрес");
        }

        [TestMethod]
        public void Test_Extracode_Long_E20_Handler()
        {
            // э20 — длинный экстракод (opcode 0o200 = 128).
            int seenOp = -1;
            uint seenAex = 0;
            _cpu.ExtracodeHandler = (int op, uint aex) => { seenOp = op; seenAex = aex; return true; };
            StoreWord("10", Asm("э20 200(3)"));
            _cpu.SetM(3, 0);
            _cpu.SetPc((uint)O("10"));
            bool stopped = _cpu.Step();

            Assert.IsFalse(stopped);
            Assert.AreEqual(128, seenOp, "opcode = E20 (0o200)");
            Assert.AreEqual((uint)O("200"), seenAex, "aex = 0o200");
            Assert.AreEqual((uint)O("200"), _cpu.GetM(14));
        }
    }
}
